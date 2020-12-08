# A script to fix the normals from the exported button.obj.
# This script mainly exists because I have more experience with Python than I do with Blender. :-p

# Note: the top of the button is in the +Y direction.

import collections
import functools
import math

import pywavefront

Point3 = collections.namedtuple('Point3', ['x', 'y', 'z'])

FILE_NAME = 'button.obj'

button = pywavefront.Wavefront(FILE_NAME, collect_faces=True, parse=True)

V = [Point3(*xyz) for xyz in button.vertices]

# Tests corresponding to what types of vertices we have

SHAFT = 1 << 0
BOTTOM_CASE = 1 << 1
TOP_CASE = 1 << 2
OUTER_LIP = 1 << 3
INNER_LIP = 1 << 4
DOME = 1 << 5

tests = {
    SHAFT: lambda p: 0.24 < (p.x**2 + p.z**2) < 0.26,
    BOTTOM_CASE: lambda p: 1.24 < p.y < 1.26,
    TOP_CASE: lambda p: 1.34 < p.y < 1.36,
    OUTER_LIP: lambda p: 3.99 < (p.x**2 + p.z**2) < 4.01,
    INNER_LIP: lambda p: 3.79 < (p.x**2 + p.z**2) < 3.81,
    DOME: lambda p: 3.79 < (p.x**2 + p.z**2 + (p.y - 1.35)**2) < 3.81,
}

classes = [sum((k for k in tests if tests[k](v))) for v in V]

expected_vertex_classes = [
    SHAFT,
    SHAFT | BOTTOM_CASE,
    BOTTOM_CASE | OUTER_LIP,
    TOP_CASE | OUTER_LIP,
    TOP_CASE | INNER_LIP | DOME,
    DOME
]
assert set(classes) == set(expected_vertex_classes)

tris = button.mesh_list[0].faces

tri_classes = [functools.reduce(lambda x, y: x & y, [classes[v] for v in tri]) for tri in tris]

expected_tri_classes = [
    SHAFT,
    BOTTOM_CASE,
    OUTER_LIP,
    TOP_CASE,
    TOP_CASE | INNER_LIP | DOME,
    DOME
]

def normalize(p):
    m = math.sqrt(p.x**2 + p.y**2 + p.z**2)
    return Point3(p.x/m, p.y/m, p.z/m)

# normal_calculations: class -> (vertex -> normal)
normal_calculations = {
    SHAFT: lambda p: normalize(Point3(p.x, 0, p.z)),
    BOTTOM_CASE: lambda p: Point3(0, -1, 0),
    OUTER_LIP: lambda p: normalize(Point3(p.x, 0, p.z)),
    TOP_CASE: lambda p: Point3(0, 1, 0),
    TOP_CASE | INNER_LIP | DOME: lambda p: Point3(0, 1, 0),
    DOME: lambda p: normalize(Point3(p.x, p.y - 1.35, p.z))
}

# Now, get ready to replace the normals
f = open(FILE_NAME)
lines = list(f)
f.close()

# This inefficient, but whatever
vn_in_lines = [(i, line) for i, line in enumerate(lines) if line.startswith('vn')] # These will be contiguous
f_in_lines = {i: line for i, line in enumerate(lines) if line.startswith('f')} # These will all be after vn_lines

normals_to_new_vn_indices = {}

f_out_lines = {}

for i in f_in_lines:
    f_line = f_in_lines[i]
    vtn_indices = map(lambda vtn: map(int, vtn.split('/')), f_line.split()[1:])
    v, vt, vn = tuple(zip(*vtn_indices))
    face_class = functools.reduce(lambda x, y: x & y, [classes[i - 1] for i in v])
    normals = [normal_calculations[face_class](V[i - 1]) for i in v]
    vn_new = []
    for n in normals:
        if n not in normals_to_new_vn_indices:
            normals_to_new_vn_indices[n] = len(normals_to_new_vn_indices) + 1
        vn_new.append(normals_to_new_vn_indices[n])
    out_line = 'f ' + ' '.join(['%d/%d/%d' % items for items in zip(v, vt, vn_new)])
    f_out_lines[i] = out_line

for i in range(vn_in_lines[0][0]):
    print(lines[i])

normals = {normals_to_new_vn_indices[k]: k for k in normals_to_new_vn_indices}

for i in range(len(normals_to_new_vn_indices)):
    point = normals[i + 1]
    print('vn %f %f %f' % point)

for i in range(vn_in_lines[-1][0], len(lines)):
    if lines[i].startswith('f '):
        print(f_out_lines[i])
    else:
        print(lines[i])
