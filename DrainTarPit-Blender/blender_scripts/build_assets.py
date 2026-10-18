"""DrainTarPit - builds hose pieces + manual pump in Blender (5.x). Re-run safe.
Run inside Blender (Scripting tab) or via the Blender MCP execute_blender_code tool."""
import bpy, bmesh, math
from mathutils import Vector, Matrix

PREFIX = "DTP_"
AXIS_Z = 0.11   # hose axis height above pivot (pieces lie on the ground)

# ---------------- cleanup (re-run safe) ----------------
if bpy.context.object and bpy.context.object.mode != 'OBJECT':
    bpy.ops.object.mode_set(mode='OBJECT')
for o in list(bpy.data.objects):
    if o.name.startswith(PREFIX) or o.name == "Cube":
        bpy.data.objects.remove(o, do_unlink=True)
for me in list(bpy.data.meshes):
    if me.users == 0:
        bpy.data.meshes.remove(me)

# ---------------- materials ----------------
def get_mat(name, color, rough, metal):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    b = next((n for n in m.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'), None)
    if b:
        b.inputs['Base Color'].default_value = (*color, 1)
        b.inputs['Roughness'].default_value = rough
        b.inputs['Metallic'].default_value = metal
    m.diffuse_color = (*color, 1)
    return m

MATS = [get_mat("DTP_LeatherTarred", (0.045, 0.035, 0.03), 0.75, 0.0),
        get_mat("DTP_IronDark",      (0.09, 0.09, 0.10), 0.55, 0.85),
        get_mat("DTP_WoodDark",      (0.16, 0.10, 0.055), 0.85, 0.0),
        get_mat("DTP_Rope",          (0.36, 0.28, 0.15), 0.95, 0.0)]
LEATHER, IRON, WOOD, ROPE = 0, 1, 2, 3

def get_col(name):
    c = bpy.data.collections.get(name)
    if not c:
        c = bpy.data.collections.new(name)
        bpy.context.scene.collection.children.link(c)
    return c

# ---------------- mesh builder ----------------
class MB:
    def __init__(self):
        self.bm = bmesh.new()
        self.uv = self.bm.loops.layers.uv.new("UVMap")

    def _uv(self, f, uvs):
        for l, uv in zip(f.loops, uvs):
            l[self.uv].uv = uv

    def box(self, c, s, mat, rot=None):
        n0 = len(self.bm.faces)
        M = Matrix.Translation(Vector(c))
        if rot is not None:
            M = M @ rot.to_4x4()
        M = M @ Matrix.Diagonal((s[0], s[1], s[2], 1.0))
        bmesh.ops.create_cube(self.bm, size=1.0, matrix=M, calc_uvs=True)
        for f in list(self.bm.faces)[n0:]:
            f.material_index = mat
            f.smooth = False

    def beam(self, p0, p1, w, h, mat):
        p0, p1 = Vector(p0), Vector(p1)
        d = p1 - p0
        rot = Vector((0, 0, 1)).rotation_difference(d.normalized()).to_matrix()
        self.box((p0 + p1) / 2, (w, h, d.length), mat, rot)

    def cyl(self, p0, p1, r0, r1, mat, seg=12, caps=True):
        p0, p1 = Vector(p0), Vector(p1)
        d = p1 - p0
        dn = d.normalized()
        rot = Vector((0, 0, 1)).rotation_difference(dn).to_matrix().to_4x4()
        M = Matrix.Translation((p0 + p1) / 2) @ rot
        n0 = len(self.bm.faces)
        try:
            bmesh.ops.create_cone(self.bm, cap_ends=caps, cap_tris=False, segments=seg,
                                  radius1=r0, radius2=r1, depth=d.length, matrix=M, calc_uvs=True)
        except TypeError:
            bmesh.ops.create_cone(self.bm, cap_ends=caps, cap_tris=False, segments=seg,
                                  diameter1=r0, diameter2=r1, depth=d.length, matrix=M, calc_uvs=True)
        for f in list(self.bm.faces)[n0:]:
            f.normal_update()
            f.material_index = mat
            f.smooth = abs(f.normal.dot(dn)) < 0.5

    def revolve(self, origin, axis, profile, mat, seg=14):
        origin = Vector(origin)
        axis = Vector(axis).normalized()
        u = axis.orthogonal().normalized()
        v = axis.cross(u)
        rings = []
        for (r, t) in profile:
            rings.append([self.bm.verts.new(origin + axis * t +
                          (u * math.cos(2 * math.pi * j / seg) + v * math.sin(2 * math.pi * j / seg)) * r)
                          for j in range(seg)])
        n = len(profile)
        for i in range(n):
            i2 = (i + 1) % n
            for j in range(seg):
                j2 = (j + 1) % seg
                f = self.bm.faces.new((rings[i][j], rings[i][j2], rings[i2][j2], rings[i2][j]))
                f.material_index = mat
                f.normal_update()
                f.smooth = abs(f.normal.dot(axis)) < 0.5
                self._uv(f, [(j / seg, i / n), ((j + 1) / seg, i / n), ((j + 1) / seg, (i + 1) / n), (j / seg, (i + 1) / n)])

    def tube(self, pts, tans, radii, mat, seg=12):
        up = Vector((0, 0, 1))
        rings, cum = [], [0.0]
        for i, (P, T, r) in enumerate(zip(pts, tans, radii)):
            n = T.cross(up).normalized()
            rings.append([self.bm.verts.new(P + (n * math.cos(2 * math.pi * j / seg) + up * math.sin(2 * math.pi * j / seg)) * r)
                          for j in range(seg)])
            if i:
                cum.append(cum[-1] + (P - pts[i - 1]).length)
        for i in range(len(rings) - 1):
            for j in range(seg):
                j2 = (j + 1) % seg
                f = self.bm.faces.new((rings[i][j], rings[i][j2], rings[i + 1][j2], rings[i + 1][j]))
                f.material_index = mat
                f.smooth = True
                self._uv(f, [(j / seg, cum[i] / 0.5), ((j + 1) / seg, cum[i] / 0.5),
                             ((j + 1) / seg, cum[i + 1] / 0.5), (j / seg, cum[i + 1] / 0.5)])
        for ring in (rings[0], rings[-1]):
            f = self.bm.faces.new(ring)
            f.material_index = mat
            f.smooth = False
            self._uv(f, [(0.5 + 0.5 * math.cos(2 * math.pi * j / seg), 0.5 + 0.5 * math.sin(2 * math.pi * j / seg)) for j in range(seg)])

    def shift(self, vec):
        bmesh.ops.translate(self.bm, vec=Vector(vec), verts=list(self.bm.verts))

    def finish(self, name, col):
        bm = self.bm
        bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
        me = bpy.data.meshes.new(name)
        bm.to_mesh(me)
        bm.free()
        for m in MATS:
            me.materials.append(m)
        ob = bpy.data.objects.new(name, me)
        col.objects.link(ob)
        return ob

def empty(name, loc, parent, col, size=0.08, kind='ARROWS'):
    e = bpy.data.objects.new(name, None)
    e.empty_display_type = kind
    e.empty_display_size = size
    e.location = loc
    col.objects.link(e)
    e.parent = parent
    return e

def collar(mb, origin, axis, mat=IRON):
    # flange face at t=0 (connection plane), body extends along +axis
    prof = [(0.056, 0.0), (0.11, 0.0), (0.11, 0.03), (0.086, 0.03), (0.086, 0.10), (0.097, 0.10),
            (0.097, 0.125), (0.086, 0.125), (0.086, 0.16), (0.056, 0.16)]
    mb.revolve(origin, axis, prof, mat, seg=14)

BASE_R, AMP = 0.062, 0.012
def ridge_r(phase):  # phase in ridges; valley at integer phase
    return BASE_R + AMP * (0.5 - 0.5 * math.cos(2 * math.pi * phase))

# ---------------- HOSES ----------------
col_h = get_col("DTP_Hoses")
col_p = get_col("DTP_Pump")

def build_straight(L, name):
    mb = MB()
    n = max(1, int((L - 0.3) / 0.12))
    pitch = (L - 0.3) / n
    ss = [0.004, 0.15] + [0.15 + pitch * (i + k / 4) for i in range(n) for k in range(1, 5)] + [L - 0.004]
    pts, tans, rad = [], [], []
    for s in ss:
        pts.append(Vector((0, s, 0)))
        tans.append(Vector((0, 1, 0)))
        rad.append(ridge_r((s - 0.15) / pitch) if 0.15 <= s <= L - 0.15 else BASE_R)
    mb.tube(pts, tans, rad, LEATHER, seg=12)
    collar(mb, (0, 0, 0), (0, 1, 0))
    collar(mb, (0, L, 0), (0, -1, 0))
    mb.shift((0, -L / 2, AXIS_Z))
    ob = mb.finish(name, col_h)
    empty(name + "_snapA", (0, -L / 2, AXIS_Z), ob, col_h)
    empty(name + "_snapB", (0, L / 2, AXIS_Z), ob, col_h)
    return ob

def build_elbow(name, R=0.45, stub=0.17):
    mb = MB()
    n = int((math.pi * R / 2) / 0.1)
    steps = 4 * n
    pts = [Vector((0, 0.004, 0)), Vector((0, stub, 0))]
    tans = [Vector((0, 1, 0)), Vector((0, 1, 0))]
    rad = [BASE_R, BASE_R]
    C = Vector((R, stub, 0))
    for k in range(1, steps + 1):
        th = math.pi - (math.pi / 2) * k / steps
        pts.append(C + Vector((R * math.cos(th), R * math.sin(th), 0)))
        tans.append(Vector((math.sin(th), -math.cos(th), 0)))
        rad.append(ridge_r(k / 4.0))
    pts.append(Vector((R + stub - 0.004, stub + R, 0)))
    tans.append(Vector((1, 0, 0)))
    rad.append(BASE_R)
    mb.tube(pts, tans, rad, LEATHER, seg=12)
    A = Vector((0, 0, 0))
    B = Vector((R + stub, stub + R, 0))
    collar(mb, A, (0, 1, 0))
    collar(mb, B, (-1, 0, 0))
    ctr = (A + B) / 2
    mb.shift((-ctr.x, -ctr.y, AXIS_Z))
    ob = mb.finish(name, col_h)
    empty(name + "_snapA", (A.x - ctr.x, A.y - ctr.y, AXIS_Z), ob, col_h)
    empty(name + "_snapB", (B.x - ctr.x, B.y - ctr.y, AXIS_Z), ob, col_h)
    return ob

def build_intake(name):
    mb = MB()
    collar(mb, (0, 0, 0), (0, 1, 0))
    mb.cyl((0, 0.15, 0), (0, 0.33, 0), 0.062, 0.062, IRON, 10)
    mb.cyl((0, 0.33, 0), (0, 0.53, 0), 0.07, 0.185, IRON, 12)           # funnel
    mb.cyl((0, 0.53, 0), (0, 0.95, 0), 0.12, 0.12, LEATHER, 12)         # filter core
    mb.cyl((0, 0.53, 0), (0, 0.57, 0), 0.19, 0.19, IRON, 12)            # top ring
    mb.cyl((0, 0.91, 0), (0, 0.97, 0), 0.19, 0.19, IRON, 12)            # bottom ring
    for i in range(8):                                                   # cage bars
        phi = 2 * math.pi * i / 8
        mb.box((0.17 * math.sin(phi), 0.74, 0.17 * math.cos(phi)), (0.03, 0.36, 0.03), IRON,
               Matrix.Rotation(phi, 3, 'Y'))
    mb.cyl((0, 0.97, 0), (0, 1.09, 0), 0.19, 0.05, IRON, 12)            # weighted nose
    ob = mb.finish(name, col_h)
    empty(name + "_snapA", (0, 0, 0), ob, col_h)
    return ob

def build_outlet(name):
    mb = MB()
    collar(mb, (0, 0, 0), (0, 1, 0))
    mb.cyl((0, 0.16, 0), (0, 0.42, 0), 0.078, 0.046, IRON, 12)          # spout
    mb.cyl((0, 0.42, 0), (0, 0.46, 0), 0.062, 0.062, IRON, 12)          # lip
    mb.cyl((0, 0.18, 0), (0, 0.27, 0), 0.092, 0.092, ROPE, 12)          # rope grip wrap
    ob = mb.finish(name, col_h)
    empty(name + "_snapA", (0, 0, 0), ob, col_h)
    return ob

hose2 = build_straight(2.0, "DTP_Hose_Straight_2m")
hose1 = build_straight(1.0, "DTP_Hose_Straight_1m")
elbow = build_elbow("DTP_Hose_Elbow90")
intake = build_intake("DTP_Hose_IntakeStrainer")
outlet = build_outlet("DTP_Hose_OutletNozzle")

# ---------------- MANUAL PUMP ----------------
def build_pump(name="DTP_Pump_Manual"):
    mb = MB()
    # base: 5 planks + 2 battens
    for i in range(5):
        mb.box((-0.36 + 0.18 * i, 0.15, 0.09), (0.175, 1.0, 0.06), WOOD)
    for y in (-0.25, 0.5):
        mb.box((0, y, 0.03), (0.9, 0.08, 0.06), WOOD)
    # body barrel + iron bands + cap
    mb.cyl((0, 0, 0.12), (0, 0, 1.0), 0.2, 0.2, WOOD, 12)
    for z in (0.2, 0.55, 0.9):
        mb.cyl((0, 0, z), (0, 0, z + 0.06), 0.213, 0.213, IRON, 12)
    mb.cyl((0, 0, 1.0), (0, 0, 1.07), 0.235, 0.235, IRON, 12)
    mb.cyl((0, 0, 1.07), (0, 0, 1.12), 0.10, 0.10, IRON, 10)
    # intake (-X) and outlet (+X) ports
    mb.cyl((-0.19, 0, 0.38), (-0.30, 0, 0.38), 0.07, 0.07, IRON, 10)
    collar(mb, (-0.46, 0, 0.38), (1, 0, 0))
    mb.cyl((0.19, 0, 0.76), (0.30, 0, 0.76), 0.085, 0.085, IRON, 10)   # valve bulge
    collar(mb, (0.46, 0, 0.76), (-1, 0, 0))
    mb.cyl((0.28, 0, 0.76), (0.34, 0, 0.76), 0.066, 0.066, IRON, 10)
    # fulcrum posts, braces, axle
    for x in (-0.0875, 0.0875):
        mb.box((x, 0.30, 0.76), (0.05, 0.07, 1.28), WOOD)
        mb.beam((x, 0.62, 0.12), (x, 0.30, 0.95), 0.045, 0.045, WOOD)
    mb.cyl((-0.14, 0.30, 1.30), (0.14, 0.30, 1.30), 0.018, 0.018, IRON, 8)
    root = mb.finish(name, col_p)

    # lever (origin = pivot)
    lv = MB()
    lv.box((0, 0.35, 0), (0.07, 1.30, 0.07), WOOD)
    lv.box((0, 0, 0), (0.10, 0.10, 0.11), IRON)               # pivot bracket
    lv.box((0, -0.27, 0), (0.09, 0.07, 0.09), IRON)           # short end band
    lv.cyl((-0.22, 0.95, 0), (0.22, 0.95, 0), 0.03, 0.03, WOOD, 8)   # cross handle
    lv.cyl((-0.12, 0.95, 0), (0.12, 0.95, 0), 0.036, 0.036, ROPE, 8) # rope grip
    lever = lv.finish(name + "_Lever", col_p)

    # piston rod (origin = top attach point)
    rd = MB()
    rd.cyl((0, 0, 0), (0, 0, -0.52), 0.02, 0.02, IRON, 8)
    rd.box((0, 0, -0.02), (0.05, 0.03, 0.06), IRON)
    rod = rd.finish(name + "_PistonRod", col_p)

    lever.parent = root; lever.location = (0, 0.30, 1.30)
    rod.parent = root;   rod.location = (0, 0.0, 1.30)
    empty(name + "_snapIntake", (-0.46, 0, 0.38), root, col_p)
    empty(name + "_snapOutlet", (0.46, 0, 0.76), root, col_p)
    ip = bpy.data.objects.new(name + "_interactPoint", None)
    ip.empty_display_type = 'SPHERE'; ip.empty_display_size = 0.08
    ip.location = (0, 0.95, 0); col_p.objects.link(ip); ip.parent = lever
    return root, lever, rod

pump, lever, rod = build_pump()

# ---------------- layout (display only; export resets to origin) ----------------
layout = {hose2: (-2.4, 0, 0), hose1: (-1.6, 0, 0), elbow: (-0.6, 0, 0),
          intake: (0.6, 0, 0), outlet: (1.4, 0, 0), pump: (3.0, 0, 0)}
for o, p in layout.items():
    o.location = p
