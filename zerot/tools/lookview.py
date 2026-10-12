#!/usr/bin/env python
"""lookview - quick software render of a VaM person look (the sim has no 3D pipeline, this is its mesh viewer).

  python zerot/tools/lookview.py <package.var | scene.json> [--atom ".Girl"] [--out look.png] [--views front,three,side] [--list]

What it draws: VaM's own female base mesh (Genesis2Female, read from the install's asset bundles and cached), the look's morphs that
exist as .vmi/.vmb files (from the package itself, other installed packages, or Custom/ in the install), the skin textures of the look
(face / torso / limbs), eyes, teeth and so on as flat colours, in the base rest pose.
What it does not draw (and says so): VaM's built-in named morphs (they live in bundle data this reader does not decode), the person's
pose, hair, clothing, normal / specular / gloss maps, decals, lighting from the scene.
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
from PIL import Image

VAM = os.environ.get('VAM_DIR', r'T:\New folder')
HERE = os.path.dirname(os.path.abspath(__file__))
CACHE = os.path.join(HERE, '..', 'UiSim', 'cache', 'g2f.npz')
BUNDLE = os.path.join(VAM, 'VaM_Data', 'StreamingAssets', 'a_per_anim')
MESH_ID = 7775492944566153113
MATS = ['Legs', 'EyeReflection', 'Nostrils', 'Lacrimals', 'Pupils', 'Lips', 'Tear', 'Gums', 'Irises', 'Teeth', 'Cornea', 'Face', 'Toenails', 'Sclera',
        'Fingernails', 'Head', 'Ears', 'Hands', 'Shoulders', 'Neck', 'Hips', 'Torso', 'Nipples', 'Forearms', 'Feet', 'Eyelashes', 'Tongue', 'InnerMouth']
GROUP = {'Face': 'face', 'Head': 'face', 'Ears': 'face', 'Lips': 'face', 'Nostrils': 'face',
         'Torso': 'torso', 'Neck': 'torso', 'Hips': 'torso', 'Nipples': 'torso',
         'Legs': 'limbs', 'Forearms': 'limbs', 'Hands': 'limbs', 'Feet': 'limbs', 'Shoulders': 'limbs', 'Fingernails': 'limbs', 'Toenails': 'limbs'}
FLAT = {'Sclera': (0.93, 0.92, 0.9), 'Teeth': (0.9, 0.87, 0.8), 'Gums': (0.72, 0.32, 0.34), 'Tongue': (0.75, 0.38, 0.4), 'InnerMouth': (0.35, 0.1, 0.1),
        'Pupils': (0.02, 0.02, 0.02), 'Eyelashes': (0.08, 0.06, 0.06)}
SKIP = {'Cornea', 'EyeReflection', 'Tear', 'Lacrimals'}


# ---------------------------------------------------------------- base mesh
def load_base():
    if not os.path.exists(CACHE):
        import UnityPy
        from UnityPy.helpers.MeshHelper import MeshHandler
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
        os.makedirs(os.path.dirname(CACHE), exist_ok=True)
        np.savez_compressed(CACHE, verts=np.array(h.m_Vertices, dtype=np.float32), uv=np.array(h.m_UV0, dtype=np.float32),
                            **{'t%d' % i: t for i, t in enumerate(tris)})
    d = np.load(CACHE)
    tris = [d['t%d' % i] for i in range(len(MATS))]
    return d['verts'].copy(), d['uv'], tris


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
                    if key not in _pkgs or n > _pkgs[key][0]:
                        _pkgs[key] = (n, os.path.join(root, f))
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
        if ':/' in uid:
            pkg, rel = uid.split(':/', 1)
            if pkg == 'SELF':
                if self.selfvar:
                    return self._from(self.selfvar, rel)
            else:
                m = re.match(r'(.+)\.(\d+|latest)$', pkg)
                if m and m.group(1) in packages():
                    path = packages()[m.group(1)][1]
                    if m.group(2) != 'latest':
                        cand = [p for _, p in [(0, os.path.join(r, m.group(1) + '.' + m.group(2) + '.var')) for r in [os.path.dirname(path)]]]
                        path = cand[0] if os.path.exists(cand[0]) else path
                    return self._from(path, rel)
            return None
        p = os.path.join(VAM, uid)
        if os.path.exists(p):
            return open(p, 'rb').read()
        # an unqualified path may also be inside the package itself
        if self.selfvar:
            return self._from(self.selfvar, uid)
        return None

    def _from(self, var, rel):
        z = self.zip(var)
        names = {n.lower(): n for n in z.namelist()}
        n = names.get(rel.lower())
        return z.read(n) if n else None


# ---------------------------------------------------------------- morphs
def seam_groups(v):
    """vertices at the same position are copies of one DAZ vertex (UV seams); DAZ ids follow the order of first appearance"""
    key = np.round(v * 1e5).astype(np.int64)
    _, first, inv = np.unique(key, axis=0, return_index=True, return_inverse=True)
    order = np.argsort(first)               # unique-group numbers in order of first appearance
    rank = np.empty_like(order)
    rank[order] = np.arange(len(order))
    group_of = rank[inv]                    # for every vertex: its DAZ id
    first_idx = first[order]                # vertex index of the first copy of DAZ id k
    # delta lands on the first copy, then spreads to every copy through group_of
    return first_idx, first_idx[group_of]

def read_vmb(b):
    n = len(b)
    if (n - 4) % 16 == 0 and struct.unpack('<i', b[:4])[0] == (n - 4) // 16:
        b = b[4:]
    arr = np.frombuffer(b, dtype=np.dtype([('i', '<i4'), ('d', '<f4', 3)]))
    return arr['i'].astype(np.int64), arr['d']


def apply_morphs(verts, person, src, report):
    groups_first, group_of = seam_groups(verts)
    geo = [s for s in person['storables'] if s['id'] == 'geometry'][0]
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
        vmi = src.read(uid)
        vmb = src.read(uid[:-4] + '.vmb')
        if vmb is None:
            report['missing'].append((m.get('name', uid), val))
            continue
        idx, d = read_vmb(vmb)
        # morph ids are DAZ vertex ids; the mesh splits vertices at UV seams, so each id moves every copy of that vertex
        ok = idx < len(groups_first)
        sel = idx[ok]
        delta = np.zeros_like(verts)
        np.add.at(delta, groups_first[sel], d[ok])
        verts += (delta[group_of] * val)
        report['applied'].append((m.get('name', uid), val, int(ok.sum())))
    return verts


# ---------------------------------------------------------------- textures
def load_tex(src, uid, size=1024):
    if not uid:
        return None
    b = src.read(uid)
    if b is None:
        return None
    im = Image.open(io.BytesIO(b))
    im.draft('RGB', (size, size))
    im = im.convert('RGB')
    im.thumbnail((size, size))
    return np.asarray(im, dtype=np.float32) / 255.0


def sample(tex, uv):
    h, w = tex.shape[:2]
    x = np.clip((uv[..., 0] % 1.0) * (w - 1), 0, w - 1)
    y = np.clip((1.0 - (uv[..., 1] % 1.0)) * (h - 1), 0, h - 1)
    x0 = x.astype(int)
    y0 = y.astype(int)
    x1 = np.minimum(x0 + 1, w - 1)
    y1 = np.minimum(y0 + 1, h - 1)
    fx = (x - x0)[..., None]
    fy = (y - y0)[..., None]
    return (tex[y0, x0] * (1 - fx) * (1 - fy) + tex[y0, x1] * fx * (1 - fy) + tex[y1, x0] * (1 - fx) * fy + tex[y1, x1] * fx * fy)


# ---------------------------------------------------------------- rasteriser
def normals(v, t):
    n = np.zeros_like(v)
    f = np.cross(v[t[:, 1]] - v[t[:, 0]], v[t[:, 2]] - v[t[:, 0]])
    for k in range(3):
        np.add.at(n, t[:, k], f)
    l = np.linalg.norm(n, axis=1, keepdims=True)
    return n / np.maximum(l, 1e-9)


def render(verts, uv, tris, albedo, yaw_deg, W, H, ss=2):
    """albedo: per submesh either an (h,w,3) texture or an rgb tuple"""
    allt = np.concatenate(tris)
    nrm = normals(verts, allt)
    a = np.radians(yaw_deg)
    R = np.array([[np.cos(a), 0, -np.sin(a)], [0, 1, 0], [np.sin(a), 0, np.cos(a)]], dtype=np.float32)
    pv = (verts - np.array([0, 0.9, 0], dtype=np.float32)) @ R.T
    pn = nrm @ R.T
    # camera on +z looking at -z; pv.z toward camera is larger
    ext = 1.95
    fov_dist = 5.0
    w, h = W * ss, H * ss
    scale = h / ext
    z = fov_dist - pv[:, 2]
    persp = fov_dist / z
    sx = w / 2 + pv[:, 0] * persp * scale
    sy = h / 2 - pv[:, 1] * persp * scale
    img = np.zeros((h, w, 3), dtype=np.float32)
    img[:] = (0.16, 0.17, 0.2)
    zbuf = np.full((h, w), np.inf, dtype=np.float32)
    light = np.array([0.35, 0.55, 0.75], dtype=np.float32)
    light /= np.linalg.norm(light)
    for si, t in enumerate(tris):
        mat = MATS[si]
        if mat in SKIP:
            continue
        al = albedo[si]
        for tri in t:
            i0, i1, i2 = tri
            x0, y0, x1, y1, x2, y2 = sx[i0], sy[i0], sx[i1], sy[i1], sx[i2], sy[i2]
            area = (x1 - x0) * (y2 - y0) - (x2 - x0) * (y1 - y0)
            if area >= 0:   # back face (screen y is flipped)
                continue
            minx = max(int(np.floor(min(x0, x1, x2))), 0)
            maxx = min(int(np.ceil(max(x0, x1, x2))), w - 1)
            miny = max(int(np.floor(min(y0, y1, y2))), 0)
            maxy = min(int(np.ceil(max(y0, y1, y2))), h - 1)
            if minx > maxx or miny > maxy:
                continue
            px, py = np.meshgrid(np.arange(minx, maxx + 1) + 0.5, np.arange(miny, maxy + 1) + 0.5)
            w0 = ((x1 - px) * (y2 - py) - (x2 - px) * (y1 - py)) / area
            w1 = ((x2 - px) * (y0 - py) - (x0 - px) * (y2 - py)) / area
            w2 = 1 - w0 - w1
            m = (w0 >= 0) & (w1 >= 0) & (w2 >= 0)
            if not m.any():
                continue
            zz = w0 * z[i0] + w1 * z[i1] + w2 * z[i2]
            zb = zbuf[miny:maxy + 1, minx:maxx + 1]
            m &= zz < zb
            if not m.any():
                continue
            zb[m] = zz[m]
            nn = w0[..., None] * pn[i0] + w1[..., None] * pn[i1] + w2[..., None] * pn[i2]
            nn /= np.maximum(np.linalg.norm(nn, axis=-1, keepdims=True), 1e-9)
            lam = np.clip((nn * light).sum(-1), 0, 1)
            shade = 0.38 + 0.62 * lam
            if isinstance(al, np.ndarray):
                u = w0[..., None] * uv[i0] + w1[..., None] * uv[i1] + w2[..., None] * uv[i2]
                col = sample(al, u)
            else:
                col = np.broadcast_to(np.array(al, dtype=np.float32), shade.shape + (3,))
            spec = (np.clip((nn * (light + np.array([0, 0, 1.0], dtype=np.float32)) / 1.7).sum(-1), 0, 1) ** 24 * 0.12)[..., None]
            out = col * shade[..., None] + spec
            sub = img[miny:maxy + 1, minx:maxx + 1]
            sub[m] = out[m]
    im = Image.fromarray((np.clip(img, 0, 1) * 255).astype(np.uint8))
    return im.resize((W, H), Image.LANCZOS)


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


def iris_color(person):
    for s in person['storables']:
        if s['id'] == 'irises' and 'Diffuse Color' in s:
            c = s['Diffuse Color']
            try:
                r, g, b = colorsys.hsv_to_rgb(float(c['h']), float(c['s']), float(c['v']))
                return (r, g, b)
            except Exception:
                pass
    return (0.35, 0.25, 0.15)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('source')
    ap.add_argument('--atom')
    ap.add_argument('--out')
    ap.add_argument('--views', default='front,three,side')
    ap.add_argument('--size', default='480x900')
    ap.add_argument('--list', action='store_true')
    ap.add_argument('--morphs', action='store_true', help='EXPERIMENTAL: apply the look .vmb morphs. The vertex-id mapping is not solved, so the shape comes out torn.')
    a = ap.parse_args()
    j, var, name = load_scene(a.source)
    ps = persons(j)
    if a.list or not ps:
        print('persons:', [p['id'] for p in ps])
        return
    person = [p for p in ps if p['id'] == a.atom][0] if a.atom else ps[0]
    W, H = [int(x) for x in a.size.split('x')]
    src = Source(var)
    verts, uv, tris = load_base()
    report = {'applied': [], 'builtin': [], 'missing': [], 'otherMesh': []}
    if a.morphs:
        verts = apply_morphs(verts, person, src, report)
    else:
        report['note'] = True
    tex = {}
    ts = [s for s in person['storables'] if s['id'] == 'textures']
    ts = ts[0] if ts else {}
    for g in ('face', 'torso', 'limbs'):
        tex[g] = load_tex(src, ts.get(g + 'DiffuseUrl'))
    flat = dict(FLAT)
    flat['Irises'] = iris_color(person)
    albedo = []
    for mname in MATS:
        g = GROUP.get(mname)
        if g:
            albedo.append(tex[g] if tex.get(g) is not None else (0.78, 0.6, 0.52))
        else:
            albedo.append(flat.get(mname, (0.5, 0.5, 0.5)))
    yaws = {'front': 0, 'three': -35, 'side': -90}
    imgs = [render(verts, uv, tris, albedo, yaws[v], W, H) for v in a.views.split(',')]
    sheet = Image.new('RGB', (W * len(imgs), H))
    for k, im in enumerate(imgs):
        sheet.paste(im, (k * W, 0))
    out = a.out or ('look_%s.png' % re.sub(r'[^A-Za-z0-9]+', '_', person['id']).strip('_'))
    sheet.save(out)
    print('wrote', out)
    if report.get('note'):
        print("NOTE: this is VaM's default female body with the look's skin textures, NOT her shape (morphs are off, see --morphs).")
    else:
        print('morphs applied: %d (%s)' % (len(report['applied']), ', '.join('%s %.2f' % (n, v) for n, v, _ in report['applied'][:8])))
    for k, lbl in (('builtin', "VaM built-in morphs (not decoded, left out)"), ('missing', 'morph files not found'), ('otherMesh', 'morphs for the genitalia mesh (not drawn)')):
        if report[k]:
            print('%s: %d' % (lbl, len(report[k])))
    print('not drawn: pose (rest pose shown), hair, clothing, normal/specular maps, decals')


if __name__ == '__main__':
    main()
