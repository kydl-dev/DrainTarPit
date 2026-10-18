using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using DrainTarPit.Logging;
using UnityEngine;
using UnityEngine.Rendering;

namespace DrainTarPit.Visuals
{
    /// <summary>
    /// The water look as a text file (BepInEx/config/DrainTarPit.WaterLook.txt): every property of the pool material as the live editor (WaterEditor) left it.
    /// The file is read again every time the pools get their material (game start, world load), so a look tuned in game survives a restart,
    /// and it is the "draft" that a Unity material / bundle can be made from (property names are the real shader property names).
    ///
    /// Lines:   float NAME v   |   color NAME r g b a   |   vector NAME x y z w   |   tiling NAME sx sy ox oy   |   keyword NAME   |   queue N   |   depth v   |   # comment
    /// </summary>
    internal static class WaterLook
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static string Path { get { return System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "DrainTarPit.WaterLook.txt"); } }

        static string F(float v) { return v.ToString("R", Inv); }

        /// Every editable property of the material as preset text.
        public static string Dump(Material m)
        {
            var sb = new StringBuilder();
            if (m == null) return "";
            var sh = m.shader;
            sb.AppendLine("# DrainTarPit water look - material '" + m.name + "', shader '" + (sh != null ? sh.name : "?") + "'");
            sb.AppendLine("depth " + F(ModSettings.WaterDepthLook.Value));
            sb.AppendLine("queue " + m.renderQueue);
            if (sh != null)
            {
                int count = sh.GetPropertyCount();
                for (int i = 0; i < count; i++)
                {
                    string name = sh.GetPropertyName(i);
                    switch (sh.GetPropertyType(i))
                    {
                        case ShaderPropertyType.Color:
                            var c = m.GetColor(name);
                            sb.AppendLine("color " + name + " " + F(c.r) + " " + F(c.g) + " " + F(c.b) + " " + F(c.a));
                            break;
                        case ShaderPropertyType.Vector:
                            var v = m.GetVector(name);
                            sb.AppendLine("vector " + name + " " + F(v.x) + " " + F(v.y) + " " + F(v.z) + " " + F(v.w));
                            break;
                        case ShaderPropertyType.Texture:
                            var ts = m.GetTextureScale(name); var to = m.GetTextureOffset(name);       // the texture itself stays; its tiling / offset are part of the look
                            sb.AppendLine("tiling " + name + " " + F(ts.x) + " " + F(ts.y) + " " + F(to.x) + " " + F(to.y));
                            break;
                        default:
                            sb.AppendLine("float " + name + " " + F(m.GetFloat(name)));
                            break;
                    }
                }
            }
            foreach (var k in m.shaderKeywords) sb.AppendLine("keyword " + k);
            return sb.ToString();
        }

        public static bool Save(Material m)
        {
            try { File.WriteAllText(Path, Dump(m)); ModLog.Info("WaterLook: preset saved to " + Path); return true; }
            catch (System.Exception e) { ModLog.Warn("WaterLook: could not save the preset: " + e.Message); return false; }
        }

        /// Applies the preset file (if there is one) to the material. Properties the shader does not have are skipped. Returns the number of lines applied.
        public static int Apply(Material m)
        {
            if (m == null) return 0;
            int applied = 0;
            try
            {
                if (!File.Exists(Path)) return 0;
                foreach (var raw in File.ReadAllLines(Path))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line[0] == '#') continue;
                    var p = line.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
                    if (p.Length < 2) continue;
                    try
                    {
                        switch (p[0])
                        {
                            case "float":
                                if (p.Length >= 3 && m.HasProperty(p[1])) { m.SetFloat(p[1], Parse(p[2])); applied++; }
                                break;
                            case "color":
                                if (p.Length >= 6 && m.HasProperty(p[1])) { m.SetColor(p[1], new Color(Parse(p[2]), Parse(p[3]), Parse(p[4]), Parse(p[5]))); applied++; }
                                break;
                            case "vector":
                                if (p.Length >= 6 && m.HasProperty(p[1])) { m.SetVector(p[1], new Vector4(Parse(p[2]), Parse(p[3]), Parse(p[4]), Parse(p[5]))); applied++; }
                                break;
                            case "tiling":
                                if (p.Length >= 6 && m.HasProperty(p[1])) { m.SetTextureScale(p[1], new Vector2(Parse(p[2]), Parse(p[3]))); m.SetTextureOffset(p[1], new Vector2(Parse(p[4]), Parse(p[5]))); applied++; }
                                break;
                            case "keyword":
                                m.EnableKeyword(p[1]); applied++;
                                break;
                            case "queue":
                                m.renderQueue = (int)Parse(p[1]); applied++;
                                break;
                            case "depth":
                                if (ModSettings.WaterDepthLook != null) { ModSettings.WaterDepthLook.Value = Mathf.Clamp01(Parse(p[1])); applied++; }
                                break;
                        }
                    }
                    catch (System.Exception e) { ModLog.VerboseWarn("WaterLook: line skipped '" + line + "': " + e.Message); }
                }
                if (applied > 0) ModLog.Info("WaterLook: " + applied + " values from " + Path + " applied to the pool water.");
            }
            catch (System.Exception e) { ModLog.Warn("WaterLook: could not read the preset: " + e.Message); }
            return applied;
        }

        static float Parse(string s) { return float.Parse(s, NumberStyles.Float, Inv); }
    }
}
