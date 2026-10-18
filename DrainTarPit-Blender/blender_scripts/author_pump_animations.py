"""DrainTarPit - author + bake player pump animations (Blender 5.x). Re-run safe.
Needs build_assets.py + build_player_proxy.py first. Stages: stage1_solve() -> stage2_key() -> stage3_bake_export().
Clips (30 fps): PumpEnter 0-15, PumpIdle 0-30 (loop), PumpCycle 0-36 (loop), PumpPush 0-15, PumpPull 0-19, PumpExit 0-15.
Lever angle +SW = handle up, -SW = handle down. Rod follows via driver. Humanoid FBX -> Unity Assets/Animation/DTP_PlayerPump.fbx"""
import bpy, math, json, os
from mathutils import Vector, Matrix

FPS, SW = 30, 22.0
UNITY_ANIM = r"E:\Projects\Valheim\DrainTarPit\DrainTarPit\Assets\Animation"
rig = bpy.data.objects["DTP_Proxy_Rig"]; lc = bpy.data.objects["DTP_Proxy_LeverCtrl"]
lever = bpy.data.objects["DTP_Pump_Manual_Lever"]; rod = bpy.data.objects["DTP_Pump_Manual_PistonRod"]
sc = bpy.context.scene; PB = rig.pose.bones
W = {"Hips": .25, "Spine": .20, "Chest": .25, "UpperChest": .30, "Neck": -.30, "Head": -.35}
# Legacy IK cannot choose a bend plane from a perfectly straight T-pose chain -> constant pre-bend (elbows down, knees forward)
PRE = {"LeftLowerArm": (0, 0, math.radians(-25)), "RightLowerArm": (0, 0, math.radians(-25)),
       "LeftLowerLeg": (0, 0, math.radians(20)), "RightLowerLeg": (0, 0, math.radians(20))}
# name: (theta, lean, head, crouch_dz)
DEF = {"grab": (0, 8, 3, -.02), "top": (SW, 11, 6, 0.0), "top_b": (SW, 12.5, 6.5, -.012),
       "push_mid": (0, 15, 5, -.07), "bottom": (-SW, 27, 6, -.14), "pull_mid": (0, 10, 4, -.08)}
CYCLE = [(0,"top"),(7,"push_mid"),(15,"bottom"),(17,"bottom"),(26,"pull_mid"),(36,"top")]
CLIPS = {"PumpEnter": [(0,"rest"),(8,"grab"),(15,"top")],
         "PumpIdle":  [(0,"top"),(15,"top_b"),(30,"top")],
         "PumpCycle": CYCLE,
         "PumpPush":  [(f,p) for f,p in CYCLE if f <= 15],
         "PumpPull":  [(f-17,p) for f,p in CYCLE if f >= 17],
         "PumpExit":  [(0,"top"),(7,"grab"),(15,"rest")]}

def link_lever():
    sc.render.fps = FPS
    for c in list(lever.constraints):
        if c.name == "DTP_FollowCtrl": lever.constraints.remove(c)
    c = lever.constraints.new('COPY_ROTATION'); c.name = "DTP_FollowCtrl"; c.target = lc
    c.use_x, c.use_y, c.use_z = True, False, False; c.target_space = 'WORLD'; c.owner_space = 'WORLD'
    try: rod.driver_remove("location", 2)
    except Exception: pass
    d = rod.driver_add("location", 2).driver; d.type = 'SCRIPTED'
    v = d.variables.new(); v.name = 'a'; v.type = 'SINGLE_PROP'; v.targets[0].id = lc; v.targets[0].data_path = 'rotation_euler[0]'
    d.expression = "1.3 - 0.3*sin(a)"

def fix_grip_drivers():
    """build_player_proxy.py's AVERAGE drivers never resolve (is_valid False) -> rebuild as scripted drivers."""
    for s in "LR":
        ht = bpy.data.objects["DTP_Proxy_HandTgt."+s]; ht.animation_data_clear()
        d = ht.constraints["toGrip"].driver_add("influence").driver; d.type = 'SCRIPTED'; d.expression = "g"
        v = d.variables.new(); v.name = "g"; v.type = 'SINGLE_PROP'
        v.targets[0].id_type = 'OBJECT'; v.targets[0].id = rig; v.targets[0].data_path = '["grip"]'

def fix_poles():
    """Elbow poles from build_player_proxy.py sat behind the shoulders (collinear with the reach axis). Move them out/down at the
    sides, then re-pick every limb's pole angle (closest mid-joint to its pole) on a mid-reach pose with the pre-bend applied."""
    S = bpy.data.objects["DTP_Pump_Manual_standPoint"].matrix_world.translation
    for s, sx in (("L", 1), ("R", -1)):
        bpy.data.objects["DTP_Proxy_ElbowPole."+s].location = S + Vector((0.7*sx, 0.0, 0.9))
    set_pose(dict(theta=0, lean=15, head=5, dy=-0.1, dz=-0.07, grip=1.0)); picked = {}
    for s, nm in (("L", "Left"), ("R", "Right")):
        for low, up, pole in ((nm+"LowerArm", nm+"UpperArm", "DTP_Proxy_ElbowPole."+s), (nm+"LowerLeg", nm+"UpperLeg", "DTP_Proxy_KneePole."+s)):
            ik = PB[low].constraints["IK"]; best = None
            for a in range(-180, 181, 30):
                ik.pole_angle = math.radians(a); bpy.context.view_layer.update(); dg = bpy.context.evaluated_depsgraph_get(); ev = rig.evaluated_get(dg)
                d = (ev.pose.bones[up].tail - bpy.data.objects[pole].evaluated_get(dg).matrix_world.translation).length
                if best is None or d < best[0]: best = (d, a)
            ik.pole_angle = math.radians(best[1]); picked[low] = best[1]
    return picked

def hips_local(dy, dz): return PB["Hips"].bone.matrix_local.to_3x3().inverted() @ Vector((0, dy, dz))
def set_pose(p):
    lc.rotation_euler = (math.radians(p["theta"]), 0, 0); rig["grip"] = p["grip"]; rig.update_tag()
    for n, k in W.items(): PB[n].rotation_euler = (math.radians(p["lean"]*k + (p["head"] if n == "Head" else 0)), 0, 0)
    for b, e in PRE.items(): PB[b].rotation_euler = e
    PB["Hips"].location = hips_local(p["dy"], p["dz"])
def evaluate():
    bpy.context.view_layer.update(); dg = bpy.context.evaluated_depsgraph_get(); ev = rig.evaluated_get(dg); e = 0.0
    for s, n in (("L","Left"),("R","Right")):
        e += (ev.pose.bones[n+"LowerArm"].tail - bpy.data.objects["DTP_Proxy_HandTgt."+s].evaluated_get(dg).matrix_world.translation).length
        e += (ev.pose.bones[n+"LowerLeg"].tail - bpy.data.objects["DTP_Proxy_FootTgt."+s].evaluated_get(dg).matrix_world.translation).length
    return e
def clear_anim():
    for o in (rig, lc):
        if o.animation_data: o.animation_data.action = None

def stage1_solve():
    link_lever(); fix_grip_drivers(); clear_anim(); fix_poles(); P = {"rest": dict(theta=0, lean=0, head=0, dy=0, dz=0, grip=0.0, err=0)}
    for name, (th, ln, hd, dz) in DEF.items():
        p = dict(theta=th, lean=ln, head=hd, dy=0.0, dz=dz, grip=1.0)
        def cost(dy):
            p["dy"] = dy; set_pose(p); return evaluate()*10 + 0.1*abs(dy)
        best = min(((cost(i*.02), i*.02) for i in range(-20, 7)))
        best = min(((cost(best[1]+i*.005), best[1]+i*.005) for i in range(-4, 5)))
        p["dy"] = round(best[1], 4); set_pose(p); p["err"] = round(evaluate(), 4); P[name] = p
    sc["dtp_P"] = json.dumps(P); return P

def make_clip(name, keys, P):
    for n in ("DTP_Src_"+name, "DTP_Src_"+name+"_Lever"):
        if n in bpy.data.actions: bpy.data.actions.remove(bpy.data.actions[n])
    ra = bpy.data.actions.new("DTP_Src_"+name); la = bpy.data.actions.new("DTP_Src_"+name+"_Lever")
    for a in (ra, la): a.use_fake_user = True
    for o, a in ((rig, ra), (lc, la)):
        o.animation_data_create(); o.animation_data.action = a
    for f, pn in keys:
        set_pose(P[pn])
        for n in W: PB[n].keyframe_insert("rotation_euler", frame=f, group=n)
        PB["Hips"].keyframe_insert("location", frame=f, group="Hips")
        for pb_name in PRE: PB[pb_name].keyframe_insert("rotation_euler", frame=f, group=pb_name)
        rig.keyframe_insert('["grip"]', frame=f, group="Props"); lc.keyframe_insert("rotation_euler", index=0, frame=f)
    return (keys[0][0], keys[-1][0])

def stage2_key():
    P = json.loads(sc["dtp_P"]); rng = {}
    for name, keys in CLIPS.items(): rng[name] = make_clip(name, keys, P)
    sc["dtp_rng"] = json.dumps(rng); return rng

def export_rig():
    for n in ("DTP_Anim_Body", "DTP_Anim_Rig"):
        if n in bpy.data.objects: bpy.data.objects.remove(bpy.data.objects[n], do_unlink=True)
    col = bpy.data.collections.get("DTP_Export") or bpy.data.collections.new("DTP_Export")
    if col.name not in sc.collection.children: sc.collection.children.link(col)
    ex = bpy.data.objects.new("DTP_Anim_Rig", rig.data.copy()); col.objects.link(ex)
    S = bpy.data.objects["DTP_Pump_Manual_standPoint"].matrix_world.translation
    ex.location = -S                                  # feet origin at world origin, facing -Y (Unity +Z)
    b2 = bpy.data.objects["DTP_Proxy_Body"].copy(); b2.data = b2.data.copy(); b2.name = "DTP_Anim_Body"; col.objects.link(b2)
    b2.parent = ex; b2.matrix_parent_inverse = Matrix.Identity(4)
    for m in b2.modifiers:
        if m.type == 'ARMATURE': m.object = ex
    bpy.context.view_layer.update()                   # a new armature object has no .pose until the depsgraph builds it
    return ex

def stage3_bake_export():
    P = json.loads(sc["dtp_P"]); rng = json.loads(sc["dtp_rng"]); ex = export_rig(); curves = {"fps": FPS, "swing": SW, "clips": {}}
    ex.animation_data_create()
    for name, (f0, f1) in rng.items():
        rig.animation_data.action = bpy.data.actions["DTP_Src_"+name]; lc.animation_data.action = bpy.data.actions["DTP_Src_"+name+"_Lever"]
        old = bpy.data.actions.get("DTP_Player_"+name)
        if old: bpy.data.actions.remove(old)
        act = bpy.data.actions.new("DTP_Player_"+name); act.use_fake_user = True; ex.animation_data.action = act
        prev = {}; th = []
        for b in ex.data.bones: ex.pose.bones[b.name].rotation_mode = 'QUATERNION'
        for f in range(f0, f1+1):
            sc.frame_set(f); ev = rig.evaluated_get(bpy.context.evaluated_depsgraph_get())
            th.append(round(math.degrees(lc.rotation_euler.x), 4))
            G = {b.name: ev.pose.bones[b.name].matrix.copy() for b in ex.data.bones}
            for b in ex.data.bones:                                           # parents come first
                basis = (b.parent.matrix_local.inverted() @ b.matrix_local).inverted() @ G[b.parent.name].inverted() @ G[b.name] if b.parent else b.matrix_local.inverted() @ G[b.name]
                loc, rot, _ = basis.decompose()
                if b.name in prev and prev[b.name].dot(rot) < 0: rot.negate()
                prev[b.name] = rot.copy(); pb = ex.pose.bones[b.name]
                pb.location = loc; pb.rotation_quaternion = rot
                pb.keyframe_insert("location", frame=f, group=b.name); pb.keyframe_insert("rotation_quaternion", frame=f, group=b.name)
        ex.animation_data.action = None
        tr = ex.animation_data.nla_tracks.new(); tr.name = "DTP_Player_"+name
        st = tr.strips.new("DTP_Player_"+name, f0, act); st.name = "DTP_Player_"+name
        curves["clips"][name] = th
    clear_anim(); sc.frame_set(0)
    os.makedirs(UNITY_ANIM, exist_ok=True)
    with open(os.path.join(UNITY_ANIM, "DTP_PumpCurves.json"), "w") as fh: json.dump(curves, fh)
    for o in bpy.data.objects: o.select_set(False)
    ex.select_set(True); bpy.data.objects["DTP_Anim_Body"].select_set(True); bpy.context.view_layer.objects.active = ex
    bpy.ops.export_scene.fbx(filepath=os.path.join(UNITY_ANIM, "DTP_PlayerPump.fbx"), use_selection=True, object_types={'ARMATURE','MESH'},
        add_leaf_bones=False, bake_anim=True, bake_anim_use_all_actions=False, bake_anim_use_nla_strips=True,
        bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0, bake_anim_step=1.0,
        primary_bone_axis='Y', secondary_bone_axis='X', axis_forward='-Z', axis_up='Y',
        apply_scale_options='FBX_SCALE_ALL', path_mode='COPY', embed_textures=False)
    return {k: len(v) for k, v in curves["clips"].items()}
