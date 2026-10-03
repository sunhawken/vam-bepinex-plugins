namespace ProceduralSlaps;

public sealed class Settings
{
	public const int MapSize = 256;

	public const float RenderInterval = 0.1f;

	public const string CompositeBundle = "data/skin-composite.assetbundle";

	public const string CompositeShader = "assets/proceduralslaps/decal.shader";

	public const float AppearanceSeconds = 1f;

	public const float PainFadeSeconds = 60f;

	public const float DetailSensorSpacing = 0.06f;

	public const int DirtyTileSize = 8;

	public const int SnapshotVerticesPerFrame = 4096;

	public const int SnapshotBlocksPerFrame = 128;

	public const float MaxContactAge = 2f;

	public const float SurfaceDistance = 0.12f;

	public const float AnchoredSurfaceDistance = 0.2f;

	public const float MinimumRadius = 0.008f;

	public const float MaximumRadius = 0.12f;

	public const float MinimumSlideSpeed = 0.03f;

	public const float MinimumSourceSpeed = 0.03f;

	public const float MotionWindowSeconds = 0.1f;

	public const float MinimumSlideTravel = 0.003f;

	public const float ImpactQuietTime = 0.15f;

	public const float SurfaceUpdateInterval = 0.2f;

	public const float ContactMergeDistance = 0.09f;

	public const float ContactMergeDepth = 0.025f;

	public const float ContactMergeWindow = 0.05f;

	public const int MaxStampsPerFrame = 2;

	public const float IrritationContactDelay = 0.15f;

	public const float ImpactCooldown = 0.18f;

	public const float ReferenceArea = 0.003f;

	public const float ReferencePressure = 5000f;

	public const float ReferenceSkinMass = 0.1f;

	public const float DefaultFriction = 0.6f;

	public const int MaxQueuedContacts = 64;

	public const string DataDirectory = "Saves/PluginData/RidgeRock/ProceduralSlaps";

	public const string DataPath = "Saves/PluginData/RidgeRock/ProceduralSlaps/settings.json";

	public readonly Parameter ImpactSensitivity = new Parameter("impactSensitivity", "Impact sensitivity", 1f, 0f, 5f);

	public readonly Parameter ImpactThreshold = new Parameter("impactThreshold", "Impact speed threshold (m/s)", 0.35f, 0.05f, 3f);

	public readonly Parameter ImpulseThreshold = new Parameter("impulseThreshold", "Impact impulse threshold (N s)", 0.015f, 0f, 1f);

	public readonly Parameter ImpactRadius = new Parameter("impactRadius", "Contact radius multiplier", 1f, 0.25f, 3f);

	public readonly Parameter ImpactStrength = new Parameter("impactStrength", "Impact strength per N s", 1.2f, 0f, 10f);

	public readonly Parameter ImpactDecay = new Parameter("impactDecay", "Impact fade time (seconds)", 10f, 10f, 300f);

	public readonly Parameter PainSensitivity = new Parameter("painSensitivity", "Pain sensitivity", 0.1f, 0f, 5f);

	public readonly Parameter PainDecay = new Parameter("painDecay", "Pain decay", 1f, 0f, 5f);

	public readonly Parameter IrritationSensitivity = new Parameter("irritationSensitivity", "Irritation sensitivity", 1f, 0f, 5f);

	public readonly Parameter PressureSensitivity = new Parameter("pressureSensitivity", "Pressure sensitivity", 1f, 0f, 5f);

	public readonly Parameter FrictionSensitivity = new Parameter("frictionSensitivity", "Friction sensitivity", 1f, 0f, 5f);

	public readonly Parameter AccumulationRate = new Parameter("accumulationRate", "Irritation accumulation rate", 0.8f, 0f, 5f);

	public readonly Parameter IrritationDecay = new Parameter("irritationDecay", "Irritation fade time (seconds)", 90f, 10f, 300f);

	public readonly Parameter MaxIrritation = new Parameter("maxIrritation", "Maximum irritation", 1f, 0f, 1f);

	public readonly Parameter Redness = new Parameter("redness", "Visible redness", 0.65f, 0f, 1f);

	public readonly Parameter Red = new Parameter("red", "Redness color R", 0.65f, 0f, 1f);

	public readonly Parameter Green = new Parameter("green", "Redness color G", 0.055f, 0f, 1f);

	public readonly Parameter Blue = new Parameter("blue", "Redness color B", 0.045f, 0f, 1f);

	public readonly Parameter[] Parameters;

	public bool SelfContacts;

	public Settings()
	{
		Parameters = new Parameter[18]
		{
			ImpactSensitivity, ImpactThreshold, ImpulseThreshold, ImpactRadius, ImpactStrength, ImpactDecay, PainSensitivity, PainDecay, IrritationSensitivity, PressureSensitivity,
			FrictionSensitivity, AccumulationRate, IrritationDecay, MaxIrritation, Redness, Red, Green, Blue
		};
	}

	public void Validate()
	{
		for (int i = 0; i < Parameters.Length; i++)
		{
			Parameters[i].Set(Parameters[i].Value);
		}
	}
}
