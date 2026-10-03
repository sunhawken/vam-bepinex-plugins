using System;
using System.Collections.Generic;
using ProceduralSlaps.Core;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace ProceduralSlaps;

public sealed class SkinSurface
{
	public static readonly string[] RegionNames = new string[14]
	{
		"Head / face", "Neck", "Torso / back", "Chest", "Hips / buttocks", "Left arm", "Right arm", "Left hand", "Right hand", "Left leg",
		"Right leg", "Left foot", "Right foot", "Other skin"
	};

	public readonly DAZSkinV2 Skin;

	private readonly DAZMesh mesh;

	private readonly Vector3[] positions;

	private readonly V3[] vertices;

	private readonly List<SkinTriangle> triangles = new List<SkinTriangle>();

	private readonly List<int>[] adjacency;

	private readonly int[] visited;

	private readonly int[] sensorRegions;

	private readonly List<int> patch;

	private int stampId;

	private readonly int[] topology;

	private readonly Vector2[] uv;

	private readonly TriangleIndex index;

	private AsyncGPUReadbackRequest readback;

	private bool reading;

	private bool preparing;

	private bool snapshotReady;

	private int vertexOffset;

	private Vector3 readOffset;

	public bool Reading => reading;

	public bool Valid => (Object)(object)Skin != (Object)null && (Object)(object)Skin.dazMesh == (Object)(object)mesh && mesh.UVTriangles == topology && mesh.UV == uv;

	public SkinSurface(DAZSkinV2 skin, int[] atlasByMaterial)
	{
		Skin = skin;
		mesh = skin.dazMesh;
		topology = mesh.UVTriangles;
		uv = mesh.UV;
		positions = new Vector3[mesh.numBaseVertices];
		vertices = new V3[positions.Length];
		adjacency = new List<int>[positions.Length];
		sensorRegions = new int[positions.Length];
		for (int i = 0; i < adjacency.Length; i++)
		{
			adjacency[i] = new List<int>();
			sensorRegions[i] = 13;
		}
		Mesh uvMappedMesh = mesh.uvMappedMesh;
		for (int j = 0; j < atlasByMaterial.Length && j < uvMappedMesh.subMeshCount; j++)
		{
			if (atlasByMaterial[j] < 0)
			{
				continue;
			}
			int[] array = uvMappedMesh.GetTriangles(j);
			for (int k = 0; k + 2 < array.Length; k += 3)
			{
				int num = BaseIndex(array[k]);
				int num2 = BaseIndex(array[k + 1]);
				int num3 = BaseIndex(array[k + 2]);
				if (num >= 0 && num2 >= 0 && num3 >= 0 && num < positions.Length && num2 < positions.Length && num3 < positions.Length)
				{
					int count = triangles.Count;
					triangles.Add(new SkinTriangle
					{
						A = num,
						B = num2,
						C = num3,
						Atlas = atlasByMaterial[j],
						U = Texcoord(uv[array[k]]),
						V = Texcoord(uv[array[k + 1]]),
						W = Texcoord(uv[array[k + 2]]),
						Region = RegionFor(num, j)
					});
					adjacency[num].Add(count);
					adjacency[num2].Add(count);
					adjacency[num3].Add(count);
					int region = ClassifyRegion(string.Empty, mesh.materialNames[j]);
					if (!SensorDensity.Dense(region))
					{
						region = RegionFor(num, j);
					}
					SetSensorRegion(num, region);
					SetSensorRegion(num2, region);
					SetSensorRegion(num3, region);
				}
			}
		}
		if (triangles.Count == 0)
		{
			throw new InvalidOperationException("No supported skin triangles.");
		}
		visited = new int[triangles.Count];
		patch = new List<int>(triangles.Count);
		int[] array2 = new int[triangles.Count * 3];
		for (int l = 0; l < triangles.Count; l++)
		{
			array2[l * 3] = triangles[l].A;
			array2[l * 3 + 1] = triangles[l].B;
			array2[l * 3 + 2] = triangles[l].C;
		}
		index = new TriangleIndex(array2);
	}

	private void SetSensorRegion(int vertex, int region)
	{
		if (!SensorDensity.Dense(sensorRegions[vertex]))
		{
			sensorRegions[vertex] = region;
		}
	}

	public int SensorRegion(int vertex)
	{
		return (vertex < 0 || vertex >= sensorRegions.Length) ? 13 : sensorRegions[vertex];
	}

	public bool BeginReadback()
	{
		if (reading || preparing)
		{
			return false;
		}
		snapshotReady = false;
		readOffset = Skin.drawOffset;
		ComputeBuffer val = ((!Skin.useSmoothing) ? Skin.rawVertsBuffer : Skin.smoothedVertsBuffer);
		if (val != null && val.count >= positions.Length)
		{
			if (!SystemInfo.supportsAsyncGPUReadback)
			{
				throw new InvalidOperationException("This GPU does not support asynchronous skin readback.");
			}
			readback = AsyncGPUReadback.Request(val, positions.Length * 12, 0);
			reading = true;
		}
		else
		{
			if ((int)Skin.skinMethod != 0 || Skin.rawSkinnedVerts == null || Skin.rawSkinnedVerts.Length < positions.Length)
			{
				return false;
			}
			Array.Copy(Skin.rawSkinnedVerts, positions, positions.Length);
			BeginSnapshot();
		}
		return true;
	}

	public bool PollReadback()
	{
		if (reading)
		{
			if (!readback.done)
			{
				return false;
			}
			reading = false;
			if (readback.hasError)
			{
				throw new InvalidOperationException("Asynchronous skin readback failed. Use Retry / refresh skin.");
			}
			readback.GetData<Vector3>(0).CopyTo(positions);
			BeginSnapshot();
			return false;
		}
		if (!preparing)
		{
			return snapshotReady;
		}
		if (vertexOffset < positions.Length)
		{
			int num = Math.Min(positions.Length, vertexOffset + 4096);
			while (vertexOffset < num)
			{
				ref V3 reference = ref vertices[vertexOffset];
				reference = Core(positions[vertexOffset] + readOffset);
				vertexOffset++;
			}
			if (vertexOffset == positions.Length)
			{
				index.BeginUpdate(vertices);
			}
			return false;
		}
		if (!index.ContinueUpdate(128))
		{
			return false;
		}
		preparing = false;
		snapshotReady = true;
		return true;
	}

	private void BeginSnapshot()
	{
		vertexOffset = 0;
		preparing = true;
	}

	public int ResolveVertex(int vertex)
	{
		return BaseIndex(vertex);
	}

	private int BaseIndex(int i)
	{
		int value;
		return (mesh.uvVertToBaseVert == null || !mesh.uvVertToBaseVert.TryGetValue(i, out value)) ? i : value;
	}

	private static V3 Texcoord(Vector2 p)
	{
		return new V3(p.x, p.y, 0f);
	}

	public static V3 Core(Vector3 p)
	{
		return new V3(p.x, p.y, p.z);
	}

	private int RegionFor(int vertex, int material)
	{
		string bone = ((Skin.strongestDAZBone == null || vertex >= Skin.strongestDAZBone.Length || !((Object)(object)Skin.strongestDAZBone[vertex] != (Object)null)) ? string.Empty : ((Object)Skin.strongestDAZBone[vertex]).name);
		return ClassifyRegion(bone, mesh.materialNames[material]);
	}

	public static int ClassifyRegion(string bone, string material)
	{
		string text = bone.ToLowerInvariant();
		string text2 = material.ToLowerInvariant();
		bool flag = text.StartsWith("l");
		if (text.Contains("hand") || text.Contains("thumb") || text.Contains("index") || text.Contains("mid") || text.Contains("ring") || text.Contains("pinky"))
		{
			return (!flag) ? 8 : 7;
		}
		if (text.Contains("foot") || text.Contains("toe"))
		{
			return (!flag) ? 12 : 11;
		}
		if (text.Contains("thigh") || text.Contains("shin"))
		{
			return (!flag) ? 10 : 9;
		}
		if (text.Contains("shldr") || text.Contains("shoulder") || text.Contains("forearm") || text.Contains("collar"))
		{
			return (!flag) ? 6 : 5;
		}
		if (text.Contains("head") || text.Contains("jaw") || text2.Contains("face") || text2.Contains("lip") || text2.Contains("ear") || text2.Contains("head"))
		{
			return 0;
		}
		if (text.Contains("neck"))
		{
			return 1;
		}
		if (text.Contains("pectoral") || text2.Contains("nipple"))
		{
			return 3;
		}
		if (text.Contains("pelvis") || text.Contains("hip") || text.Contains("glute") || text2.Contains("genital") || text2.Contains("hip") || text2.Contains("pelvis") || text2.Contains("glute") || text2.Contains("anus") || text2 == "defaultmat")
		{
			return 4;
		}
		if (text.Contains("chest") || text.Contains("abdomen") || text2.Contains("torso"))
		{
			return 2;
		}
		return 13;
	}

	private float Distance(int id, V3 p, out V3 point, out V3 normal)
	{
		SkinTriangle skinTriangle = triangles[id];
		V3 v = vertices[skinTriangle.A];
		V3 v2 = vertices[skinTriangle.B];
		V3 v3 = vertices[skinTriangle.C];
		V3 v4 = MathEx.ClosestWeights(p, v, v2, v3);
		point = v * v4.X + v2 * v4.Y + v3 * v4.Z;
		normal = V3.Cross(v2 - v, v3 - v).Normalized;
		return (p - point).LengthSquared;
	}

	public bool Stamp(ContactSample sample, Response response, IList<SkinAtlas> atlases, Settings settings)
	{
		V3 p = Core(sample.Point);
		V3 normal = Core(sample.Normal);
		int surfaceVertex = sample.SurfaceVertex;
		int num = index.Closest(p, normal, (surfaceVertex < 0) ? 0.12f : 0.2f, surfaceVertex, out var point, out var direction);
		if (num < 0)
		{
			return false;
		}
		if (stampId == int.MaxValue)
		{
			Array.Clear(visited, 0, visited.Length);
			stampId = 0;
		}
		stampId++;
		patch.Clear();
		patch.Add(num);
		visited[num] = stampId;
		float num2 = response.Radius * response.Radius;
		for (int i = 0; i < patch.Count; i++)
		{
			int id = patch[i];
			SkinTriangle skinTriangle = triangles[id];
			if (!(Distance(id, point, out var _, out var normal2) > num2) && !(V3.Dot(normal2, direction) < 0.2f))
			{
				atlases[skinTriangle.Atlas].Map.Triangle(vertices[skinTriangle.A], vertices[skinTriangle.B], vertices[skinTriangle.C], skinTriangle.U, skinTriangle.V, skinTriangle.W, point, response.Radius, skinTriangle.Region);
				AddNeighbours(skinTriangle.A);
				AddNeighbours(skinTriangle.B);
				AddNeighbours(skinTriangle.C);
			}
		}
		SkinTriangle skinTriangle2 = triangles[num];
		V3 v = MathEx.ClosestWeights(point, vertices[skinTriangle2.A], vertices[skinTriangle2.B], vertices[skinTriangle2.C]);
		V3 v2 = skinTriangle2.U * v.X + skinTriangle2.V * v.Y + skinTriangle2.W * v.Z;
		atlases[skinTriangle2.Atlas].Map.Seed(v2.X, v2.Y, skinTriangle2.Region);
		for (int j = 0; j < atlases.Count; j++)
		{
			if (atlases[j].Map.PendingTexels > 0)
			{
				atlases[j].DecayTo(Time.time, settings);
				atlases[j].Map.Commit(response.Impact, response.Irritation, settings.MaxIrritation.Value);
			}
		}
		return true;
	}

	private void AddNeighbours(int vertex)
	{
		List<int> list = adjacency[vertex];
		for (int i = 0; i < list.Count; i++)
		{
			if (visited[list[i]] != stampId)
			{
				visited[list[i]] = stampId;
				patch.Add(list[i]);
			}
		}
	}
}
