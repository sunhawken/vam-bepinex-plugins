"""Reader for VaM's .vab binary stores (clothing / hair): DAZMesh, DAZSkinWrap, RuntimeHairGeometryCreator.
The layouts follow VaM's own LoadFromBinaryReader code (decompiled from Assembly-CSharp)."""
import struct

import numpy as np


class BR:
    def __init__(self, b):
        self.b = b
        self.p = 0

    def i32(self):
        v = struct.unpack_from('<i', self.b, self.p)[0]
        self.p += 4
        return v

    def f32(self):
        v = struct.unpack_from('<f', self.b, self.p)[0]
        self.p += 4
        return v

    def boolean(self):
        v = self.b[self.p] != 0
        self.p += 1
        return v

    def string(self):
        n = 0
        shift = 0
        while True:
            c = self.b[self.p]
            self.p += 1
            n |= (c & 0x7F) << shift
            if not c & 0x80:
                break
            shift += 7
        s = self.b[self.p:self.p + n].decode('utf-8', 'replace')
        self.p += n
        return s

    def arr(self, dtype, count, width=1):
        size = np.dtype(dtype).itemsize * count * width
        a = np.frombuffer(self.b, dtype=dtype, count=count * width, offset=self.p)
        self.p += size
        return a.reshape(count, width) if width > 1 else a


def read_mesh(r):
    if r.string() != 'DAZMesh':
        raise ValueError('not a DAZMesh section')
    if r.string() != '1.0':
        raise ValueError('DAZMesh schema')
    m = {'nodeId': r.string(), 'sceneNodeId': r.string(), 'geometryId': r.string(), 'sceneGeometryId': r.string()}
    nb = r.i32()
    m['base'] = r.arr('<f4', nb, 3).astype(np.float32)
    nm = r.i32()
    m['mats'] = [r.string() for _ in range(nm)]
    npoly = r.i32()

    def polys():
        mat = np.empty(npoly, dtype=np.int32)
        verts = []
        for k in range(npoly):
            mat[k] = r.i32()
            n = r.i32()
            verts.append(r.arr('<i4', n))
        return mat, verts

    m['pmat'], m['pverts'] = polys()
    m['uvpmat'], m['uvpverts'] = polys()
    nuv = r.i32()
    m['nuv'] = nuv
    m['origuv'] = r.arr('<f4', nuv, 2).astype(np.float32)
    nmap = r.i32()
    mp = r.arr('<i4', nmap, 3) if nmap else np.zeros((0, 3), dtype=np.int32)
    m['map'] = mp
    return m


def read_wrap(r):
    if r.string() != 'DAZSkinWrap':
        raise ValueError('not a DAZSkinWrap section')
    if r.string() != '1.0':
        raise ValueError('DAZSkinWrap schema')
    name = r.string()
    if r.string() != 'DAZSkinWrapStore':
        raise ValueError('not a DAZSkinWrapStore section')
    if r.string() != '1.0':
        raise ValueError('DAZSkinWrapStore schema')
    n = r.i32()
    dt = np.dtype([('tri', '<i4'), ('v1', '<i4'), ('v2', '<i4'), ('v3', '<i4'), ('nproj', '<f4'), ('t1proj', '<f4'), ('t2proj', '<f4'),
                   ('ndot', '<f4'), ('t1dot', '<f4'), ('t2dot', '<f4')])
    a = np.frombuffer(r.b, dtype=dt, count=n, offset=r.p)
    r.p += n * dt.itemsize
    return name, a


def read_hair(r):
    if r.string() != 'RuntimeHairGeometryCreator':
        raise ValueError('not a RuntimeHairGeometryCreator section')
    ver = r.string()
    if ver not in ('1.0', '1.1'):
        raise ValueError('hair schema ' + ver)
    h = {'scalp': r.string()}
    h['segments'] = r.i32()
    h['seglen'] = r.f32()
    r.string()                      # mask name
    nmask = r.i32()
    r.p += nmask                    # bools
    nstr = r.i32()
    for _ in range(nstr):
        r.i32()
        c = r.i32()
        r.p += 12 * c
    ni = r.i32()
    h['indices'] = r.arr('<i4', ni)
    nv = r.i32()
    h['verts'] = r.arr('<f4', nv, 3).astype(np.float32)
    return h


def read_store(data, components, is_hair):
    """components: type names from the .vaj in order. Returns dict with mesh / wrap / hair (whatever the file holds)."""
    r = BR(data)
    if r.string() != 'DynamicStore':
        raise ValueError('not a DynamicStore file')
    if r.string() != '1.0':
        raise ValueError('DynamicStore schema')
    out = {}
    for c in components:
        if c == 'DAZMesh':
            out['mesh'] = read_mesh(r)
        elif c == 'DAZSkinWrap':
            out['wrapname'], out['wrap'] = read_wrap(r)
        else:
            break                   # material option sections follow; nothing we need is behind them for clothing
    else:
        pass
    if is_hair and 'wrap' not in out or (is_hair and components == []):
        pass
    return out, r


def read_hair_store(data, components):
    """hair: the components (if any), then a flag and the hair geometry; material option sections sit between, so look for the section name"""
    key = b'\x1aRuntimeHairGeometryCreator'
    i = data.find(key)
    if i < 0:
        return None
    r = BR(data)
    r.p = i
    return read_hair(r)
