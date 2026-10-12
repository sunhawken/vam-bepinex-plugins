#!/usr/bin/env python
"""lookview - quick software render of a VaM person look (the sim has no 3D pipeline, this is its mesh viewer).

  python zerot/tools/lookview.py <package.var | scene.json> [--atom ".Girl"] [--out look.png] [--views front,three,side] [--list]
                                 [--no-morphs] [--no-clothes] [--no-hair]

What it draws: VaM's female base mesh (Genesis2Female, read from the install's asset bundles and cached), the look's morphs that exist as
.vmi/.vmb files, the skin textures tinted with the skin colour, the clothing items (their meshes wrapped onto the morphed body exactly the way
VaM's skin wrap does it), and the hair (the stored strand geometry as lines), all in the base rest pose.
What it does not draw (and says so): VaM's built-in named morphs, the pose, hair simulation, normal / specular / gloss maps, decals, lights.
"""
import argparse
import colorsys
import io
import json
import os
import re
import struct
import sys
import zipfile

import numpy as np
from numba import njit
from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import vabparse as VP  # noqa: E402

VAM = os.environ.get('VAM_DIR', r'T:\New folder')
HERE = os.path.dirname(os.path.abspath(__file__))
CACHE = os.path.join(HERE, '..', 'UiSim', 'cache', 'g2f.npz')
BUNDLE = os.path.join(VAM, 'VaM_Data', 'StreamingAssets', 'a_per_anim')
BUNDLE_PER = os.path.join(VAM, 'VaM_Data', 'StreamingAssets', 'a_per')
MANAGED = os.path.join(VAM, 'VaM_Data', 'Managed')
MESH_ID = 7775492944566153113           # Genesis2Female.Shape in a_per_anim (Unity mesh: 24119 vertices, 28 materials)
DAZ_MESH_ID = 219940491292851741        # DAZMesh "GenesisFemale" in a_per: base vertices, UV vertices, seam map
MERGED_MESH_ID = 6308178841185996234    # DAZMergedMesh "GenesisFemale:Genitalia-default": the body clothes are wrapped to
MATS = ['Legs', 'EyeReflection', 'Nostrils', 'Lacrimals', 'Pupils', 'Lips', 'Tear', 'Gums', 'Irises', 'Teeth', 'Cornea', 'Face', 'Toenails', 'Sclera',
        'Fingernails', 'Head', 'Ears', 'Hands', 'Shoulders', 'Neck', 'Hips', 'Torso', 'Nipples', 'Forearms', 'Feet', 'Eyelashes', 'Tongue', 'InnerMouth']
GROUP = {'Face': 'face', 'Head': 'face', 'Ears': 'face', 'Lips': 'face', 'Nostrils': 'face',
         'Torso': 'torso', 'Neck': 'torso', 'Hips': 'torso', 'Nipples': 'torso',
         'Legs': 'limbs', 'Forearms': 'limbs', 'Hands': 'limbs', 'Feet': 'limbs', 'Shoulders': 'limbs', 'Fingernails': 'limbs', 'Toenails': 'limbs'}
FLAT = {'Sclera': (0.93, 0.92, 0.9), 'Teeth': (0.9, 0.87, 0.8), 'Gums': (0.72, 0.32, 0.34), 'Tongue': (0.75, 0.38, 0.4), 'InnerMouth': (0.35, 0.1, 0.1),
        'Pupils': (0.02, 0.02, 0.02), 'Eyelashes': (0.08, 0.06, 0.06)}
SKIP = {'Cornea', 'EyeReflection', 'Tear', 'Lacrimals'}


# ---------------------------------------------------------------- base mesh
def triangulate(pmat, pverts, nmat):
    """VaM's PolyListToTriangleIndexes: per material in order, polygons in order; (v2,v1,v0) and for quads (v0,v3,v2)"""
    subs = [[] for _ in range(nmat)]
    for m, v in zip(pmat, pverts):
        s = subs[m]
        s.append((v[2], v[1], v[0]))
        if len(v) == 4:
            s.append((v[0], v[3], v[2]))
    return [np.array(s, dtype=np.int32).reshape(-1, 3) for s in subs]


def build_cache():
    import UnityPy
    from UnityPy.helpers.MeshHelper import MeshHandler
    from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator
    from scipy.spatial import cKDTree
    UnityPy.config.FALLBACK_UNITY_VERSION = '2018.1.0f2'
    print('reading the base mesh from VaM (once) ...')
    env = UnityPy.load(BUNDLE)
    mesh = None
    for o in env.objects:
        if o.type.name == 'Mesh' and o.path_id == MESH_ID:
            mesh = o.read()
    if mesh is None:
        sys.exit('Genesis2Female mesh not found in ' + BUNDLE)
    h = MeshHandler(mesh)
    h.process()
    tris = [np.array(t, dtype=np.int32).reshape(-1, 3) for t in h.get_triangles()]
    uv = np.array(h.m_UV0, dtype=np.float32)
    env2 = UnityPy.load(BUNDLE_PER)
    gen = TypeTreeGenerator('2018.1.0f2')
    for f in ('Assembly-CSharp.dll', 'UnityEngine.dll', 'mscorlib.dll'):
        gen.load_dll(open(os.path.join(MANAGED, f), 'rb').read())
    env2.typetree_generator = gen
    dz = mg = None
    for o in env2.objects:
        if o.path_id == DAZ_MESH_ID:
            dz = o.read_typetree()
        elif o.path_id == MERGED_MESH_ID:
            mg = o.read_typetree()
    if dz is None or mg is None:
        sys.exit('DAZ meshes not found in ' + BUNDLE_PER)

    def v3(a):
        return np.array([[p['x'], p['y'], p['z']] for p in a], dtype=np.float32)

    nbase = int(dz['_numBaseVertices'])
    uvv = v3(dz['_UVVertices'])
    ouv = np.array([[p['x'], p['y']] for p in dz['_OrigUV']], dtype=np.float32)
    base_of_uv = np.arange(len(uvv), dtype=np.int32)
    for m in dz['_baseVerticesToUVVertices']:
        base_of_uv[m['tovert']] = m['fromvert']
    dist, ix = cKDTree(ouv).query(uv)
    if dist.max() > 1e-5:
        sys.exit('UV match failed (%g)' % dist.max())
    # the merged mesh (body + genitalia graft) that clothes are wrapped to
    mbase = v3(mg['_baseVertices'])
    if np.abs(mbase[:nbase] - uvv[:nbase]).max() > 1e-5:
        sys.exit('merged mesh does not start with the body vertices')
    polys = mg['_basePolyList']
    mtris = triangulate([p['materialNum'] for p in polys], [p['vertices'] for p in polys], int(mg['_numMaterials']))
    gnn = cKDTree(mbase[:nbase]).query(mbase[nbase:])[1].astype(np.int32)
    os.makedirs(os.path.dirname(CACHE), exist_ok=True)
    np.savez_compressed(CACHE, verts=uvv[ix], baseid=base_of_uv[ix], nbase=np.int32(nbase), uv=uv, mbase=mbase, mtris=np.concatenate(mtris), gnn=gnn,
                        **{'t%d' % i: t for i, t in enumerate(tris)})


def load_base():
    ok = os.path.exists(CACHE)
    if ok:
        ok = 'mtris' in np.load(CACHE).files
    if not ok:
        build_cache()
    d = np.load(CACHE)
    tris = [d['t%d' % i] for i in range(len(MATS))]
    body = {'mbase': d['mbase'], 'mtris': d['mtris'], 'gnn': d['gnn']}
    return d['verts'].copy(), d['uv'], tris, d['baseid'], int(d['nbase']), body


# ---------------------------------------------------------------- packages
_pkgs = None


def packages():
    global _pkgs
    if _pkgs is None:
        _pkgs = {}
        for root, _, files in os.walk(os.path.join(VAM, 'AddonPackages')):
            for f in files:
                m = re.match(r'(.+)\.(\d+)\.var$', f)
                if m:
                    key = m.group(1)
                    n = int(m.group(2))
                    _pkgs.setdefault(key, {})[n] = os.path.join(root, f)
    return _pkgs


class Source:
    """reads files the way VaM resolves uids: SELF:/x (the package), Name.N:/x or Name.latest:/x (installed var), plain paths (the install)."""

    def __init__(self, selfvar):
        self.selfvar = selfvar
        self.zips = {}

    def zip(self, path):
        if path not in self.zips:
            self.zips[path] = zipfile.ZipFile(path)
        return self.zips[path]

    def read(self, uid):
        uid = uid.replace('\\', '/')
        if ':/' in uid:
            pkg, rel = uid.split(':/', 1)
            if pkg == 'SELF':
                return self._from(self.selfvar, rel) if self.selfvar else None
            m = re.match(r'(.+)\.(\d+|latest)$', pkg)
            low = {k.lower(): k for k in packages()}
            if m and m.group(1).lower() in low:
                vers = packages()[low[m.group(1).lower()]]
                path = vers.get(int(m.group(2))) if m.group(2) != 'latest' else None
                path = path or vers[max(vers)]
                return self._from(path, rel)
            return None
        p = os.path.join(VAM, uid)
        if os.path.exists(p):
            return open(p, 'rb').read()
        return self._from(self.selfvar, uid) if self.selfvar else None

    def _from(self, var, rel):
        z = self.zip(var)
        names = {n.lower(): n for n in z.namelist()}
        n = names.get(rel.lower())
        return z.read(n) if n else None


# ---------------------------------------------------------------- morphs
def read_vmb(b):
    n = len(b)
    if (n - 4) % 16 == 0 and struct.unpack('<i', b[:4])[0] == (n - 4) // 16:
        b = b[4:]
    arr = np.frombuffer(b, dtype=np.dtype([('i', '<i4'), ('d', '<f4', 3)]))
    return arr['i'].astype(np.int64), arr['d']


def apply_morphs(verts, person, src, report, baseid, nbase):
    """returns the vertices and the per-base-vertex displacement (clothes follow it)"""
    geo = [s for s in person['storables'] if s['id'] == 'geometry'][0]
    total = np.zeros((nbase, 3), dtype=np.float32)
    for m in geo.get('morphs', []):
        val = float(m.get('value', 0))
        uid = m['uid']
        if val == 0:
            continue
        if not uid.lower().endswith('.vmi'):
            report['builtin'].append((m.get('name', uid), val))
            continue
        low = uid.lower()
        if '/female_genitalia/' in low or '/male' in low:
            report['otherMesh'].append((m.get('name', uid), val))
            continue
        vmb = src.read(uid[:-4] + '.vmb')
        if vmb is None:
            report['missing'].append((m.get('name', uid), val))
            continue
        idx, d = read_vmb(vmb)
        ok = idx < nbase
        delta = np.zeros((nbase, 3), dtype=np.float32)
        np.add.at(delta, idx[ok], d[ok])
        total += delta * val
        report['applied'].append((m.get('name', uid), val, int(ok.sum())))
    verts += total[baseid]
    return verts, total


# ---------------------------------------------------------------- textures and colours
def load_img(src, uid, size=1024, keep_alpha=False):
    if not uid:
        return None
    b = src.read(uid)
    if b is None:
        return None
    im = Image.open(io.BytesIO(b))
    im.draft('RGB', (size, size))
    im = im.convert('RGBA' if keep_alpha else 'RGB')
    im.thumbnail((size, size))
    return np.ascontiguousarray(np.asarray(im, dtype=np.float32) / 255.0)


def hsv(c, default=(1.0, 1.0, 1.0)):
    try:
        return colorsys.hsv_to_rgb(float(c['h']), float(c['s']), float(c['v']))
    except Exception:
        return default


# ---------------------------------------------------------------- rasteriser (numba)
@njit(cache=True)
def _sample(tex, u, v):
    h, w = tex.shape[0], tex.shape[1]
    u = u - np.floor(u)
    v = v - np.floor(v)
    x = u * (w - 1)
    y = (1.0 - v) * (h - 1)
    x0 = int(x)
    y0 = int(y)
    x1 = min(x0 + 1, w - 1)
    y1 = min(y0 + 1, h - 1)
    fx = x - x0
    fy = y - y0
    out = np.empty(tex.shape[2], dtype=np.float32)
    for c in range(tex.shape[2]):
        out[c] = (tex[y0, x0, c] * (1 - fx) * (1 - fy) + tex[y0, x1, c] * fx * (1 - fy) + tex[y1, x0, c] * (1 - fx) * fy + tex[y1, x1, c] * fx * fy)
    return out


@njit(cache=True)
def raster(img, zbuf, sx, sy, z, nrm, uv, tris, tex, has_tex, tex_alpha_ch, alpha, has_alpha, color, tint, cutoff, two_sided, light):
    h, w = zbuf.shape
    for t in range(tris.shape[0]):
        i0, i1, i2 = tris[t, 0], tris[t, 1], tris[t, 2]
        x0, y0, x1, y1, x2, y2 = sx[i0], sy[i0], sx[i1], sy[i1], sx[i2], sy[i2]
        area = (x1 - x0) * (y2 - y0) - (x2 - x0) * (y1 - y0)
        if area == 0:
            continue
        back = area >= 0
        if back and not two_sided:
            continue
        minx = max(int(np.floor(min(x0, min(x1, x2)))), 0)
        maxx = min(int(np.ceil(max(x0, max(x1, x2)))), w - 1)
        miny = max(int(np.floor(min(y0, min(y1, y2)))), 0)
        maxy = min(int(np.ceil(max(y0, max(y1, y2)))), h - 1)
        for py in range(miny, maxy + 1):
            for px in range(minx, maxx + 1):
                fx = px + 0.5
                fy = py + 0.5
                w0 = ((x1 - fx) * (y2 - fy) - (x2 - fx) * (y1 - fy)) / area
                w1 = ((x2 - fx) * (y0 - fy) - (x0 - fx) * (y2 - fy)) / area
                w2 = 1.0 - w0 - w1
                if w0 < 0 or w1 < 0 or w2 < 0:
                    continue
                zz = w0 * z[i0] + w1 * z[i1] + w2 * z[i2]
                if zz >= zbuf[py, px]:
                    continue
                u = w0 * uv[i0, 0] + w1 * uv[i1, 0] + w2 * uv[i2, 0]
                v = w0 * uv[i0, 1] + w1 * uv[i1, 1] + w2 * uv[i2, 1]
                if has_alpha:
                    a = _sample(alpha, u, v)[0]
                    if a < cutoff:
                        continue
                if has_tex:
                    tc = _sample(tex, u, v)
                    if tex_alpha_ch and tc[3] < cutoff:
                        continue
                    r = tc[0] * tint[0]
                    g = tc[1] * tint[1]
                    b = tc[2] * tint[2]
                else:
                    r = color[0]
                    g = color[1]
                    b = color[2]
                nx = w0 * nrm[i0, 0] + w1 * nrm[i1, 0] + w2 * nrm[i2, 0]
                ny = w0 * nrm[i0, 1] + w1 * nrm[i1, 1] + w2 * nrm[i2, 1]
                nz = w0 * nrm[i0, 2] + w1 * nrm[i1, 2] + w2 * nrm[i2, 2]
                ln = np.sqrt(nx * nx + ny * ny + nz * nz) + 1e-9
                nx /= ln
                ny /= ln
                nz /= ln
                if back:
                    nx = -nx
                    ny = -ny
                    nz = -nz
                lam = max(nx * light[0] + ny * light[1] + nz * light[2], 0.0)
                shade = 0.38 + 0.62 * lam
                hx = light[0]
                hy = light[1]
                hz = light[2] + 1.0
                hl = np.sqrt(hx * hx + hy * hy + hz * hz)
                sp = max((nx * hx + ny * hy + nz * hz) / hl, 0.0) ** 24 * 0.12
                zbuf[py, px] = zz
                img[py, px, 0] = r * shade + sp
                img[py, px, 1] = g * shade + sp
                img[py, px, 2] = b * shade + sp


@njit(cache=True)
def raster_lines(img, zbuf, sx, sy, z, seg, col, width, bias):
    h, w = zbuf.shape
    for s in range(seg.shape[0]):
        a, b = seg[s, 0], seg[s, 1]
        dx = sx[b] - sx[a]
        dy = sy[b] - sy[a]
        n = int(max(abs(dx), abs(dy))) + 1
        for k in range(n + 1):
            t = k / n
            x = sx[a] + dx * t
            y = sy[a] + dy * t
            zz = z[a] + (z[b] - z[a]) * t - bias
            for ox in range(-width, width + 1):
                for oy in range(-width, width + 1):
                    px = int(x) + ox
                    py = int(y) + oy
                    if px < 0 or py < 0 or px >= w or py >= h:
                        continue
                    if zz < zbuf[py, px]:
                        zbuf[py, px] = zz
                        for c in range(3):
                            img[py, px, c] = col[a, c] + (col[b, c] - col[a, c]) * t


def vertex_normals(pos, tris):
    n = np.zeros_like(pos)
    f = np.cross(pos[tris[:, 1]] - pos[tris[:, 0]], pos[tris[:, 2]] - pos[tris[:, 0]])
    for k in range(3):
        np.add.at(n, tris[:, k], f)
    l = np.linalg.norm(n, axis=1, keepdims=True)
    return (n / np.maximum(l, 1e-12)).astype(np.float32)


class Camera:
    def __init__(self, yaw_deg, W, H, ss, cy=0.9, height=1.95):
        a = np.radians(yaw_deg)
        self.R = np.array([[np.cos(a), 0, -np.sin(a)], [0, 1, 0], [np.sin(a), 0, np.cos(a)]], dtype=np.float32)
        self.w, self.h = W * ss, H * ss
        self.scale = self.h / height
        self.cy = cy
        self.dist = 5.0
        self.ss = ss
        self.img = np.zeros((self.h, self.w, 3), dtype=np.float32)
        self.img[:] = (0.16, 0.17, 0.2)
        self.zbuf = np.full((self.h, self.w), np.inf, dtype=np.float32)
        self.light = np.array([0.35, 0.55, 0.75], dtype=np.float32)
        self.light /= np.linalg.norm(self.light)

    def project(self, pos):
        pv = (pos - np.array([0, self.cy, 0], dtype=np.float32)) @ self.R.T
        z = (self.dist - pv[:, 2]).astype(np.float32)
        persp = self.dist / z
        sx = (self.w / 2 + pv[:, 0] * persp * self.scale).astype(np.float32)
        sy = (self.h / 2 - pv[:, 1] * persp * self.scale).astype(np.float32)
        return sx, sy, z, pv

    def draw_mesh(self, pos, uv, tris, nrm, tex=None, alpha=None, color=(0.6, 0.6, 0.6), tint=(1, 1, 1), cutoff=0.5, two_sided=False):
        if len(tris) == 0:
            return
        sx, sy, z, _ = self.project(pos)
        n = (nrm @ self.R.T).astype(np.float32)
        has_tex = tex is not None
        has_alpha = alpha is not None
        dummy = np.zeros((1, 1, 3), dtype=np.float32)
        dummy1 = np.zeros((1, 1, 1), dtype=np.float32)
        raster(self.img, self.zbuf, sx, sy, z, n, uv.astype(np.float32), np.ascontiguousarray(tris, dtype=np.int32),
               tex if has_tex else dummy, has_tex, bool(has_tex and tex.shape[2] == 4), alpha if has_alpha else dummy1, has_alpha,
               np.array(color, dtype=np.float32), np.array(tint, dtype=np.float32), np.float32(cutoff), two_sided, self.light)

    def draw_lines(self, pos, seg, col, width=1):
        sx, sy, z, _ = self.project(pos)
        raster_lines(self.img, self.zbuf, sx, sy, z, np.ascontiguousarray(seg, dtype=np.int32), np.ascontiguousarray(col, dtype=np.float32), width, 0.004)

    def result(self, W, H):
        im = Image.fromarray((np.clip(self.img, 0, 1) * 255).astype(np.uint8))
        return im.resize((W, H), Image.LANCZOS)


# ---------------------------------------------------------------- clothing
def tri_normals(pos, tris):
    f = np.cross(pos[tris[:, 1]] - pos[tris[:, 0]], pos[tris[:, 2]] - pos[tris[:, 0]])
    l = np.linalg.norm(f, axis=1, keepdims=True)
    l[l == 0] = 1e-3
    return (f / l).astype(np.float32)


def wrap_positions(cloth, mv, mtris, addthick, offset):
    """DAZSkinWrap's CPU path: every clothing vertex is placed relative to the body triangle it was wrapped to"""
    nb = len(cloth['mesh']['base'])
    w = cloth['wrap'][:nb]
    tn = tri_normals(mv, mtris)
    v1 = mv[w['v1']]
    cen = (mv[w['v1']] + mv[w['v2']] + mv[w['v3']]) * 0.33333
    t1 = cen - v1
    n = tn[w['tri']]
    t2 = np.cross(t1, n)
    pos = (v1 + t1 * (w['t1proj'] + w['t1dot'] * addthick)[:, None] + t2 * (w['t2proj'] + w['t2dot'] * addthick)[:, None]
           + n * (w['nproj'] + offset + w['ndot'] * addthick)[:, None])
    return pos.astype(np.float32)


def item_paths(uid):
    base = uid[:-4] if uid.lower().endswith('.vam') else uid
    return base + '.vam', base + '.vaj', base + '.vab'


def tex_uid(item_uid, name):
    if not name:
        return None
    if ':/' in name:
        return name
    pkg, rel = item_uid.split(':/', 1) if ':/' in item_uid else ('', item_uid)
    folder = rel.rsplit('/', 1)[0]
    return (pkg + ':/' if pkg else '') + folder + '/' + name


def load_item(src, uid, person_store, body, mv, report):
    vam, vaj, vab = item_paths(uid)
    try:
        j = json.loads(src.read(vaj).decode('utf-8-sig'))
        data = src.read(vab)
    except Exception:
        report['itemsMissing'].append(uid)
        return []
    if data is None:
        report['itemsMissing'].append(uid)
        return []
    comps = [c['type'] for c in j.get('components', [])]
    meta = {}
    try:
        meta = json.loads(src.read(vam).decode('utf-8-sig'))
    except Exception:
        pass
    sto = {s['id']: dict(s) for s in j.get('storables', [])}
    for sid, s in person_store.items():          # the scene's own settings for this item win
        if sid in sto:
            sto[sid].update(s)
    out = []
    if 'DAZMesh' not in comps or 'DAZSkinWrap' not in comps:
        return out
    cloth, _ = VP.read_store(data, comps, False)
    mesh = cloth['mesh']
    addthick = offset = 0.0
    for sid, s in sto.items():
        if sid.endswith('WrapControl'):
            offset = float(s.get('surfaceOffset', 0))
            addthick = float(s.get('additionalThicknessMultiplier', 0))
    pos_base = wrap_positions(cloth, mv, body['mtris'], addthick, offset)
    nuv = mesh['nuv']
    base_of_uv = np.arange(nuv, dtype=np.int32)
    nbase_c = len(mesh['base'])
    for m in mesh['map']:
        base_of_uv[m[1]] = m[0]
    base_of_uv = np.minimum(base_of_uv, nbase_c - 1)
    upos = pos_base[base_of_uv]
    nm = len(mesh['mats'])
    subs = triangulate(mesh['uvpmat'], mesh['uvpverts'], nm)
    bsubs = [b for b in triangulate(mesh['pmat'], mesh['pverts'], nm) if len(b)]
    bn = vertex_normals(pos_base, np.concatenate(bsubs)) if bsubs else np.zeros_like(pos_base)
    nrm = bn[base_of_uv]
    for mi, mname in enumerate(mesh['mats']):
        st = None
        for sid, s in sto.items():
            if sid.endswith('Material' + mname):
                st = s
        if st is None:
            for sid, s in sto.items():
                if sid.endswith('MaterialCombined') or sid.endswith('MaterialDefault') or re.search(r'Material\w*Combined\d*$', sid):
                    st = s
        st = st or {}
        if st.get('hideMaterial', 'false') == 'true':
            continue
        col = hsv(st.get('Diffuse Color'), (0.7, 0.7, 0.7))
        tex = load_img(src, tex_uid(uid, st.get('customTexture_MainTex')), 1024, keep_alpha=True)
        alpha = None
        if st.get('customTexture_AlphaTex'):
            a = load_img(src, tex_uid(uid, st['customTexture_AlphaTex']), 1024)
            if a is not None:
                alpha = np.ascontiguousarray(a[..., :1])
        if tex is not None and tex.shape[2] == 4 and tex[..., 3].min() > 0.99:
            tex = np.ascontiguousarray(tex[..., :3])
        out.append({'pos': upos, 'uv': mesh['origuv'], 'tris': subs[mi], 'nrm': nrm, 'tex': tex, 'alpha': alpha, 'color': col,
                    'tint': col if tex is not None else (1, 1, 1), 'name': '%s/%s' % (meta.get('displayName') or uid.rsplit('/', 1)[-1], mname)})
    return out


def load_hair(src, uid, person_store, report):
    vam, vaj, vab = item_paths(uid)
    try:
        data = src.read(vab)
        j = json.loads(src.read(vaj).decode('utf-8-sig'))
    except Exception:
        data = None
    if data is None:
        report['itemsMissing'].append(uid)
        return None
    h = VP.read_hair_store(data, [])
    if h is None or len(h['verts']) == 0:
        return None
    sto = {s['id']: dict(s) for s in j.get('storables', [])}
    for sid, s in person_store.items():
        if sid in sto:
            sto[sid].update(s)
    sim = [s for sid, s in sto.items() if sid.endswith('Sim')]
    s = sim[0] if sim else {}
    return {'verts': h['verts'], 'indices': h['indices'], 'segments': h['segments'], 'root': hsv(s.get('rootColor'), (0.1, 0.07, 0.05)),
            'tip': hsv(s.get('tipColor'), (0.3, 0.2, 0.1)), 'rolloff': float(s.get('colorRolloff', 2.0)), 'mult': float(s.get('hairMultiplier', 4)), 'name': uid.rsplit('/', 1)[-1]}


def hair_strands(h):
    """the stored guide strands (one run of `segments` vertices each) plus extra strands blended between the three guides of every root triangle
    (VaM's hair multiplier does the same at runtime), coloured root -> tip"""
    n = max(h['segments'], 2)
    ns = len(h['verts']) // n
    S = h['verts'][:ns * n].reshape(ns, n, 3)
    rng = np.random.RandomState(7)
    tri = h['indices'][:len(h['indices']) // 3 * 3].reshape(-1, 3)
    tri = tri[(tri < ns).all(axis=1)]
    mult = int(max(0, min(h['mult'], 12)))
    parts = [S]
    if len(tri) and mult:
        k = min(mult, max(1, 60000 // max(len(tri), 1)))
        r = rng.random_sample((len(tri), k, 2)).astype(np.float32)
        flip = r.sum(-1) > 1
        r[flip] = 1 - r[flip]
        u = r[..., 0, None, None]
        v = r[..., 1, None, None]
        a = S[tri[:, 0]][:, None]
        b = S[tri[:, 1]][:, None]
        c = S[tri[:, 2]][:, None]
        parts.append((a * (1 - u - v) + b * u + c * v).reshape(-1, n, 3))
    S = np.concatenate(parts).astype(np.float32)
    total = len(S)
    verts = S.reshape(-1, 3)
    k = np.arange(n - 1)
    seg = (np.arange(total)[:, None] * n + k[None, :])
    seg = np.stack([seg.ravel(), seg.ravel() + 1], axis=1)
    t = (np.arange(len(verts)) % n) / (n - 1.0)
    t = t ** max(h['rolloff'], 0.1)
    col = (np.array(h['root'], dtype=np.float32)[None, :] * (1 - t[:, None]) + np.array(h['tip'], dtype=np.float32)[None, :] * t[:, None]).astype(np.float32)
    return verts, seg, col


# ---------------------------------------------------------------- main
def load_scene(path):
    if path.lower().endswith('.var'):
        z = zipfile.ZipFile(path)
        scenes = [n for n in z.namelist() if n.lower().startswith('saves/scene/') and n.lower().endswith('.json')]
        looks = [n for n in z.namelist() if n.lower().startswith('saves/person/') and n.lower().endswith('.vap')]
        if not scenes and not looks:
            sys.exit('no scene or person preset in the package')
        n = (scenes or looks)[0]
        return json.loads(z.read(n).decode('utf-8-sig')), path, n
    return json.loads(open(path, encoding='utf-8-sig').read()), None, path


def persons(j):
    return [a for a in j.get('atoms', []) if a.get('type') == 'Person']


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('source')
    ap.add_argument('--atom')
    ap.add_argument('--out')
    ap.add_argument('--views', default='front,three,side')
    ap.add_argument('--size', default='480x900')
    ap.add_argument('--list', action='store_true')
    ap.add_argument('--no-morphs', action='store_true', help='show the default body without the look morphs')
    ap.add_argument('--no-clothes', action='store_true')
    ap.add_argument('--no-hair', action='store_true')
    a = ap.parse_args()
    j, var, name = load_scene(a.source)
    ps = persons(j)
    if a.list or not ps:
        print('persons:', [p['id'] for p in ps])
        return
    person = [p for p in ps if p['id'] == a.atom][0] if a.atom else ps[0]
    store = {s['id']: s for s in person['storables']}
    W, H = [int(x) for x in a.size.split('x')]
    src = Source(var)
    verts, uv, tris, baseid, nbase, body = load_base()
    report = {'applied': [], 'builtin': [], 'missing': [], 'otherMesh': [], 'itemsMissing': []}
    total = np.zeros((nbase, 3), dtype=np.float32)
    if not a.no_morphs:
        verts, total = apply_morphs(verts, person, src, report, baseid, nbase)
    mv = body['mbase'].copy()
    mv[:nbase] += total
    mv[nbase:] += total[body['gnn']]
    ts = store.get('textures', {})
    skin = store.get('skin', {})
    tint = hsv(skin.get('Skin Color'), (1, 1, 1))
    tex = {g: load_img(src, ts.get(g + 'DiffuseUrl')) for g in ('face', 'torso', 'limbs')}
    flat = dict(FLAT)
    flat['Irises'] = hsv(store.get('irises', {}).get('Diffuse Color'), (0.35, 0.25, 0.15))
    geo = store.get('geometry', {})
    items = []
    if not a.no_clothes:
        for c in geo.get('clothing', []):
            if c.get('enabled', 'true') != 'false':
                items += load_item(src, c['id'], store, body, mv, report)
    hairs = []
    if not a.no_hair:
        for c in geo.get('hair', []):
            if c.get('enabled', 'true') != 'false':
                h = load_hair(src, c['id'], store, report)
                if h is not None:
                    hairs.append(h)
    nrm_body = vertex_normals(verts, np.concatenate(tris))
    views = {'front': (0, 0.9, 1.95), 'three': (-35, 0.9, 1.95), 'side': (-90, 0.9, 1.95), 'head': (0, 1.62, 0.5), 'head3': (-35, 1.62, 0.5), 'torso': (-20, 1.3, 0.8)}
    imgs = []
    for v in a.views.split(','):
        cam = Camera(views[v][0], W, H, 2, views[v][1], views[v][2])
        for si, t in enumerate(tris):
            mname = MATS[si]
            if mname in SKIP:
                continue
            g = GROUP.get(mname)
            if g:
                cam.draw_mesh(verts, uv, t, nrm_body, tex=tex.get(g), color=(0.78, 0.6, 0.52), tint=tint)
            else:
                cam.draw_mesh(verts, uv, t, nrm_body, color=flat.get(mname, (0.5, 0.5, 0.5)))
        for it in items:
            cam.draw_mesh(it['pos'], it['uv'], it['tris'], it['nrm'], tex=it['tex'], alpha=it['alpha'], color=it['color'], tint=it['tint'], two_sided=True)
        for h in hairs:
            vv, seg, col = hair_strands(h)
            cam.draw_lines(vv, seg, col, 1)
        imgs.append(cam.result(W, H))
    sheet = Image.new('RGB', (W * len(imgs), H))
    for k, im in enumerate(imgs):
        sheet.paste(im, (k * W, 0))
    out = a.out or ('look_%s.png' % re.sub(r'[^A-Za-z0-9]+', '_', person['id']).strip('_'))
    sheet.save(out)
    print('wrote', out)
    print('morphs applied: %d (%s)' % (len(report['applied']), ', '.join('%s %.2f' % (n, v) for n, v, _ in report['applied'][:6])))
    print('clothing meshes drawn: %d, hair items drawn: %d' % (len(items), len(hairs)))
    for k, lbl in (('builtin', 'VaM built-in morphs (not decoded, left out)'), ('missing', 'morph files not found'), ('otherMesh', 'morphs for the genitalia mesh (not drawn)'),
                   ('itemsMissing', 'clothing / hair files not found (package not installed?)')):
        if report[k]:
            print('%s: %d %s' % (lbl, len(report[k]), report[k][:3] if k == 'itemsMissing' else ''))
    print('not drawn: pose (rest pose shown), hair simulation, normal/specular maps, decals')


if __name__ == '__main__':
    main()
