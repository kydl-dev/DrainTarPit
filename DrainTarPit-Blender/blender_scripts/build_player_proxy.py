"""DrainTarPit - Unity-Humanoid-compatible proxy character + IK rig for authoring the pump animations.
Blender 5.x. Re-run safe (removes every DTP_Proxy* object first).  Part 1: rig, mannequin, IK/constraints.
Conventions: character faces Blender -Y (-> Unity +Z), character LEFT = Blender +X. Feet origin S = pump-local (0, 1.80, 0)."""
import bpy, bmesh, math
from mathutils import Vector, Matrix

PX = "DTP_Proxy"
STAND_Y = 1.80          # pump-local Y of the feet origin (standing behind the handle)
GRIP_Y = 0.95           # lever-local Y of the grip bar
GRIP_X = 0.15           # each hand's offset from the bar centre
WRIST_OFF = Vector((0.0, 0.06, 0.04))   # wrist target relative to grip centre (lever-local)

# ---------------- cleanup ----------------
if bpy.context.object and bpy.context.object.mode != 'OBJECT':
    bpy.ops.object.mode_set(mode='OBJECT')
for o in list(bpy.data.objects):
    if o.name.startswith(PX) or o.name == "DTP_Pump_Manual_standPoint":
        bpy.data.objects.remove(o, do_unlink=True)
for c in list(bpy.data.collections):
    if c.name == PX + "_Anim": bpy.data.collections.remove(c)
for blk in (bpy.data.meshes, bpy.data.armatures, bpy.data.actions, bpy.data.materials):
    for d in list(blk):
        if d.name.startswith(PX) and d.users == 0: blk.remove(d)

coll = bpy.data.collections.new(PX + "_Anim")
bpy.context.scene.collection.children.link(coll)
pump = bpy.data.objects["DTP_Pump_Manual"]
lever = bpy.data.objects["DTP_Pump_Manual_Lever"]
P = pump.matrix_world.translation.copy()
PIVOT = lever.matrix_world.translation.copy()
S = P + Vector((0, STAND_Y, 0))
def V(x, y, z): return Vector((x, y, z)) + S

# stand point (where the plugin places the player), child of the pump; Blender -Y (toward pump) == Unity +Z
sp = bpy.data.objects.new("DTP_Pump_Manual_standPoint", None)
sp.empty_display_type = 'ARROWS'; sp.empty_display_size = 0.25
bpy.data.collections["DTP_Pump"].objects.link(sp)
sp.parent = pump; sp.location = (0, STAND_Y, 0)

# ---------------- armature ----------------
arm_data = bpy.data.armatures.new(PX + "_RigData")
arm = bpy.data.objects.new(PX + "_Rig", arm_data)
coll.objects.link(arm)
bpy.context.view_layer.objects.active = arm
arm.select_set(True)
bpy.ops.object.mode_set(mode='EDIT')
eb = arm_data.edit_bones
B = {}
def add(name, head, tail, parent=None, connect=False):
    b = eb.new(name); b.head = V(*head); b.tail = V(*tail)
    if parent: b.parent = B[parent]; b.use_connect = connect
    B[name] = b
add("Hips",       (0,0,0.95), (0,0,1.03))
add("Spine",      (0,0,1.03), (0,0,1.18), "Hips", True)
add("Chest",      (0,0,1.18), (0,0,1.32), "Spine", True)
add("UpperChest", (0,0,1.32), (0,0,1.45), "Chest", True)
add("Neck",       (0,0,1.45), (0,0,1.56), "UpperChest", True)
add("Head",       (0,0,1.56), (0,0,1.78), "Neck", True)
for side, sx in (("Left", 1.0), ("Right", -1.0)):
    add(f"{side}Shoulder", (0.03*sx,0,1.40), (0.17*sx,0,1.40), "UpperChest")
    add(f"{side}UpperArm", (0.17*sx,0,1.40), (0.46*sx,0,1.40), f"{side}Shoulder", True)
    add(f"{side}LowerArm", (0.46*sx,0,1.40), (0.72*sx,0,1.40), f"{side}UpperArm", True)
    add(f"{side}Hand",     (0.72*sx,0,1.40), (0.81*sx,0,1.40), f"{side}LowerArm", True)
    add(f"{side}UpperLeg", (0.09*sx,0,0.93), (0.09*sx,0,0.51), "Hips")
    add(f"{side}LowerLeg", (0.09*sx,0,0.51), (0.09*sx,0,0.09), f"{side}UpperLeg", True)
    add(f"{side}Foot",     (0.09*sx,0,0.09), (0.09*sx,-0.13,0.04), f"{side}LowerLeg", True)
    add(f"{side}Toes",     (0.09*sx,-0.13,0.04), (0.09*sx,-0.21,0.02), f"{side}Foot", True)
bpy.ops.object.mode_set(mode='OBJECT')

# ---------------- mannequin (rigid segments, one vertex group per bone) ----------------
bm = bmesh.new()
groups = []
def cyl(bone, p0, p1, r0, r1, sxy=(1.0, 1.0), seg=12):
    a, b = V(*p0), V(*p1); d = b - a
    M = Matrix.Translation((a + b) / 2) @ d.to_track_quat('Z', 'Y').to_matrix().to_4x4() @ Matrix.Diagonal((sxy[0], sxy[1], 1.0, 1.0))
    r = bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=seg, radius1=r0, radius2=r1, depth=d.length, matrix=M)
    groups.append((bone, r['verts']))
cyl("Hips",       (0,0,0.90), (0,0,1.07), 0.10, 0.10, (1.55, 1.0))
cyl("Spine",      (0,0,1.03), (0,0,1.18), 0.10, 0.11, (1.5, 0.9))
cyl("Chest",      (0,0,1.18), (0,0,1.32), 0.11, 0.125, (1.55, 0.9))
cyl("UpperChest", (0,0,1.32), (0,0,1.45), 0.125, 0.12, (1.6, 0.85))
cyl("Neck",       (0,0,1.45), (0,0,1.56), 0.045, 0.04)
r = bmesh.ops.create_uvsphere(bm, u_segments=14, v_segments=10, radius=0.11,
                              matrix=Matrix.Translation(V(0,0,1.67)) @ Matrix.Diagonal((1.0, 1.05, 1.25, 1.0)))
groups.append(("Head", r['verts']))
for side, sx in (("Left", 1.0), ("Right", -1.0)):
    cyl(f"{side}Shoulder", (0.03*sx,0,1.40), (0.17*sx,0,1.40), 0.05, 0.05)
    cyl(f"{side}UpperArm", (0.17*sx,0,1.40), (0.46*sx,0,1.40), 0.05, 0.042)
    cyl(f"{side}LowerArm", (0.46*sx,0,1.40), (0.72*sx,0,1.40), 0.042, 0.032)
    cyl(f"{side}Hand",     (0.72*sx,0,1.40), (0.81*sx,0,1.40), 0.032, 0.03, (1.0, 0.55))
    cyl(f"{side}UpperLeg", (0.09*sx,0,0.93), (0.09*sx,0,0.51), 0.075, 0.058)
    cyl(f"{side}LowerLeg", (0.09*sx,0,0.51), (0.09*sx,0,0.09), 0.055, 0.04)
    cyl(f"{side}Foot",     (0.09*sx,0.03,0.07), (0.09*sx,-0.13,0.04), 0.05, 0.045, (1.0, 0.8))
    cyl(f"{side}Toes",     (0.09*sx,-0.13,0.04), (0.09*sx,-0.21,0.02), 0.04, 0.035, (1.0, 0.6))
bm.verts.index_update()
idx = [(bone, [v.index for v in verts]) for bone, verts in groups]
me = bpy.data.meshes.new(PX + "_BodyMesh"); bm.to_mesh(me); bm.free()
for p in me.polygons: p.use_smooth = True
body = bpy.data.objects.new(PX + "_Body", me); coll.objects.link(body)
for bone, ids in idx:
    vg = body.vertex_groups.get(bone) or body.vertex_groups.new(name=bone)
    vg.add(ids, 1.0, 'REPLACE')
mod = body.modifiers.new("Armature", 'ARMATURE'); mod.object = arm
body.parent = arm
mat = bpy.data.materials.new(PX + "_Mat"); mat.diffuse_color = (0.55, 0.62, 0.72, 1.0)
me.materials.append(mat)

# ---------------- control empties ----------------
def empty(name, loc=None, parent=None, size=0.05, kind='SPHERE'):
    e = bpy.data.objects.new(name, None); e.empty_display_type = kind; e.empty_display_size = size
    coll.objects.link(e)
    if parent: e.parent = parent
    if loc is not None: e.location = loc
    return e
lever_ctrl = empty(PX + "_LeverCtrl", PIVOT, size=0.12, kind='ARROWS')   # rotate X: +angle raises the handle
lever_ctrl.rotation_mode = 'XYZ'
rig_ctrl = {}
for side, sx in (("L", 1.0), ("R", -1.0)):
    grip = empty(f"{PX}_Grip.{side}", Vector((GRIP_X*sx, GRIP_Y, 0)) + WRIST_OFF, parent=lever_ctrl)
    relax = empty(f"{PX}_Relax.{side}", V(0.25*sx, 0.02, 0.88), size=0.04)
    htgt = empty(f"{PX}_HandTgt.{side}", None, size=0.06)
    c1 = htgt.constraints.new('COPY_TRANSFORMS'); c1.target = relax; c1.name = "toRelax"
    c2 = htgt.constraints.new('COPY_TRANSFORMS'); c2.target = grip;  c2.name = "toGrip"
    ftgt = empty(f"{PX}_FootTgt.{side}", None, size=0.06)
    ftgt.matrix_world = Matrix.Translation(V(0.14*sx, 0.0 if sx > 0 else 0.08, 0.09))
    kpole = empty(f"{PX}_KneePole.{side}", V(0.14*sx, -1.2, 0.55), size=0.05, kind='PLAIN_AXES')
    epole = empty(f"{PX}_ElbowPole.{side}", V(0.65*sx, 0.55, 0.95), size=0.05, kind='PLAIN_AXES')
    rig_ctrl[side] = dict(grip=grip, relax=relax, htgt=htgt, ftgt=ftgt, kpole=kpole, epole=epole)
# drive hand-target blend from a custom property on the rig
arm["grip"] = 0.0
arm.id_properties_ui("grip").update(min=0.0, max=1.0)
for side in ("L", "R"):
    con = rig_ctrl[side]["htgt"].constraints["toGrip"]
    fc = con.driver_add("influence"); d = fc.driver; d.type = 'AVERAGE'
    var = d.variables.new(); var.name = "g"; var.type = 'SINGLE_PROP'
    var.targets[0].id = arm; var.targets[0].data_path = '["grip"]'

# foot targets: orient to the foot bone's REST orientation so feet stay flat
bpy.context.view_layer.update()
for side, sx in (("L", 1.0), ("R", -1.0)):
    name = ("Left" if sx > 0 else "Right") + "Foot"
    rest3 = arm.pose.bones[name].bone.matrix_local.to_3x3().to_4x4()
    rig_ctrl[side]["ftgt"].matrix_world = Matrix.Translation(V(0.14*sx, 0.0 if sx > 0 else 0.08, 0.09)) @ rest3

# ---------------- IK + constraints ----------------
bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode='POSE')
def ik(bone, target, pole, chain):
    c = arm.pose.bones[bone].constraints.new('IK')
    c.name = "IK"; c.target = target; c.pole_target = pole; c.chain_count = chain
    c.pole_angle = 0.0; c.use_tail = True; c.use_stretch = False; c.iterations = 200
    return c
for side, nm in (("L", "Left"), ("R", "Right")):
    ik(f"{nm}LowerArm", rig_ctrl[side]["htgt"], rig_ctrl[side]["epole"], 2)
    ik(f"{nm}LowerLeg", rig_ctrl[side]["ftgt"], rig_ctrl[side]["kpole"], 2)
    cr = arm.pose.bones[f"{nm}Foot"].constraints.new('COPY_ROTATION')
    cr.name = "FlatFoot"; cr.target = rig_ctrl[side]["ftgt"]
bpy.ops.object.mode_set(mode='OBJECT')
for o in bpy.data.objects: o.select_set(False)

# ---------------- pick pole angles automatically ----------------
dg = bpy.context.evaluated_depsgraph_get()
def joint(bone, end='tail'):
    dg2 = bpy.context.evaluated_depsgraph_get(); ev = arm.evaluated_get(dg2)
    return getattr(ev.pose.bones[bone], end).copy()
def pick(bone_ik, mid_bone, pole_obj, sample_setup=None):
    best = None
    for ang in (0, 90, -90, 180):
        arm.pose.bones[bone_ik].constraints["IK"].pole_angle = math.radians(ang)
        bpy.context.view_layer.update()
        dist = (joint(mid_bone) - pole_obj.matrix_world.translation).length
        if best is None or dist < best[0]: best = (dist, ang)
    arm.pose.bones[bone_ik].constraints["IK"].pole_angle = math.radians(best[1])
    return best[1]
# crouch the hips a little so the knee-bend direction is well defined while testing the legs
hips = arm.pose.bones["Hips"]
hips.location = hips.bone.matrix_local.to_3x3().inverted() @ Vector((0, 0, -0.2))
bpy.context.view_layer.update()
picked = {}
for side, nm in (("L", "Left"), ("R", "Right")):
    picked[f"{nm}LowerArm"] = pick(f"{nm}LowerArm", f"{nm}UpperArm", rig_ctrl[side]["epole"])
    picked[f"{nm}LowerLeg"] = pick(f"{nm}LowerLeg", f"{nm}UpperLeg", rig_ctrl[side]["kpole"])
hips.location = (0, 0, 0)
bpy.context.view_layer.update()
result = {"pole_angles_deg": picked, "bones": len(arm_data.bones), "mesh_verts": len(me.vertices), "stand_S": [round(x,3) for x in S]}
