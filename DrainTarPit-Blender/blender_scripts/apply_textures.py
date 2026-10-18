"""Apply the procedural PBR maps (Blender/out/DTP_<Name>_{BaseColor,Normal,Mask}.png) to the 4 DTP materials.
Re-run after build_assets.py (which recreates placeholder materials). Run inside Blender: Scripting tab > Open > Run Script,
or:  blender DrainTarPit.blend --python blender_scripts/apply_textures.py
Mask map: R=metallic, G=AO, A=smoothness (roughness = 1 - A). Normal = OpenGL (Y+), same as Unity."""
import bpy, os

outdir = os.path.join(os.path.dirname(bpy.data.filepath), "out")
MAT_MAP = {"DTP_LeatherTarred": "Leather", "DTP_IronDark": "Iron", "DTP_WoodDark": "Wood", "DTP_Rope": "Rope"}
AO_STRENGTH = {"Leather": 0.7, "Iron": 0.6, "Wood": 0.7, "Rope": 0.8}

def load_img(path, cs):
    img = bpy.data.images.load(path, check_existing=True)
    img.reload()
    img.colorspace_settings.name = cs
    img.alpha_mode = 'CHANNEL_PACKED'  # smoothness lives in alpha: never premultiply
    return img

for mname, short in MAT_MAP.items():
    mat = bpy.data.materials.get(mname) or bpy.data.materials.new(mname)
    nt = mat.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial"); out.location = (900, 0)
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled"); bsdf.location = (600, 0)
    nt.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
    uv = nt.nodes.new("ShaderNodeTexCoord"); uv.location = (-900, 0)
    mp = nt.nodes.new("ShaderNodeMapping"); mp.location = (-700, 0)
    nt.links.new(uv.outputs["UV"], mp.inputs["Vector"])

    def tex(kind, cs, y):
        n = nt.nodes.new("ShaderNodeTexImage"); n.location = (-450, y)
        n.image = load_img(os.path.join(outdir, "DTP_%s_%s.png" % (short, kind)), cs)
        n.interpolation = 'Smart'; n.extension = 'REPEAT'; n.label = kind
        nt.links.new(mp.outputs["Vector"], n.inputs["Vector"])
        return n

    t_base, t_norm, t_mask = tex("BaseColor", "sRGB", 300), tex("Normal", "Non-Color", -50), tex("Mask", "Non-Color", -400)
    sep = nt.nodes.new("ShaderNodeSeparateColor"); sep.location = (-150, -400)
    nt.links.new(t_mask.outputs["Color"], sep.inputs["Color"])

    mix = nt.nodes.new("ShaderNodeMix"); mix.data_type = 'RGBA'; mix.blend_type = 'MULTIPLY'; mix.location = (150, 250)
    mix.inputs["Factor"].default_value = AO_STRENGTH[short]
    nt.links.new(t_base.outputs["Color"], mix.inputs["A"]); nt.links.new(sep.outputs["Green"], mix.inputs["B"])
    nt.links.new(mix.outputs["Result"], bsdf.inputs["Base Color"])
    nt.links.new(sep.outputs["Red"], bsdf.inputs["Metallic"])

    inv = nt.nodes.new("ShaderNodeMath"); inv.operation = 'SUBTRACT'; inv.location = (150, -450); inv.inputs[0].default_value = 1.0
    nt.links.new(t_mask.outputs["Alpha"], inv.inputs[1]); nt.links.new(inv.outputs["Value"], bsdf.inputs["Roughness"])

    nm = nt.nodes.new("ShaderNodeNormalMap"); nm.space = 'TANGENT'; nm.uv_map = "UVMap"; nm.location = (150, -100)
    nt.links.new(t_norm.outputs["Color"], nm.inputs["Color"]); nt.links.new(nm.outputs["Normal"], bsdf.inputs["Normal"])

bpy.ops.file.make_paths_relative()
print("[DTP] textures applied from", outdir)
