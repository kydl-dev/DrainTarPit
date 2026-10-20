"""Exports each DTP_ root (with children) to the Unity project as FBX (Y-up, -Z fwd, baked transform)."""
import bpy, os
out_dir = r"E:\Projects\Valheim\DrainTarPit\DrainTarPit\Assets\Models"
os.makedirs(out_dir, exist_ok=True)
roots = ["DTP_Hose_Straight_2m", "DTP_Hose_Straight_1m", "DTP_Hose_Elbow90",
         "DTP_Hose_IntakeStrainer", "DTP_Hose_OutletNozzle", "DTP_Pump_Manual"]

def descendants(o):
    out = [o]
    for c in o.children:
        out += descendants(c)
    return out

for name in roots:
    root = bpy.data.objects[name]
    old = root.location.copy()
    root.location = (0, 0, 0)
    bpy.context.view_layer.update()
    for o in bpy.data.objects: o.select_set(False)
    for o in descendants(root): o.select_set(True)
    bpy.context.view_layer.objects.active = root
    bpy.ops.export_scene.fbx(filepath=os.path.join(out_dir, name + ".fbx"), use_selection=True,
                             object_types={'MESH', 'EMPTY'}, apply_scale_options='FBX_SCALE_ALL',
                             axis_forward='-Z', axis_up='Y', bake_space_transform=True,
                             mesh_smooth_type='FACE', add_leaf_bones=False,
                             path_mode='COPY', embed_textures=False)
    root.location = old
