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

        // T-012 step 1 (user, 2026-10-20: "bake the current look first"): the look the user saved ("clearly blue", docs/DrainTarPit.WaterLook.txt) is
        // embedded in the DLL as the BUILT-IN look, so every player gets it, not only someone who has the file in BepInEx/config. A file there still wins
        // (the live editor saves to it). The built-in look never writes the config: its "depth" line is skipped ([Drain] WaterDepthLook keeps its value).
        internal const string BuiltInResource = "DrainTarPit.WaterLook.txt";

        static string[] BuiltInLines()
        {
            var asm = System.Reflection.Assembly.GetExecutingAssembly();
            using (var st = asm.GetManifestResourceStream(BuiltInResource))
            {
                if (st == null) { ModLog.Warn("WaterLook: the built-in look (" + BuiltInResource + ") is missing from the DLL - the pools keep the game's ocean look"); return null; }
                using (var rd = new StreamReader(st)) return rd.ReadToEnd().Split(new[] { '\n' }, System.StringSplitOptions.None);
            }
        }

        /// Applies the look to the material: the preset file in BepInEx/config when there is one, else the built-in look. Properties the shader does not
        /// have are skipped. Returns the number of lines applied.
        public static int Apply(Material m)
        {
            if (m == null) { ModLog.Verbose("WaterLook: no material to apply the look to"); return 0; }
            bool fromFile = File.Exists(Path);
            string[] lines;
            try { lines = fromFile ? File.ReadAllLines(Path) : BuiltInLines(); }
            catch (IOException e) { ModLog.Warn("WaterLook: the preset file could not be read (" + e.Message + "), the built-in look is used"); fromFile = false; lines = BuiltInLines(); }
            if (lines == null) return 0;
            ModLog.Verbose("WaterLook: applying the " + (fromFile ? "saved look from " + Path : "built-in look") + " (" + lines.Length + " lines)");
            return ApplyLines(m, lines, fromFile ? Path : "the built-in look", fromFile);
        }

        static int ApplyLines(Material m, string[] lines, string source, bool allowDepth)
        {
            int applied = 0;
            try
            {
                foreach (var raw in lines)
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
                                if (!allowDepth) { ModLog.Verbose("WaterLook: depth " + p[1] + " of the built-in look skipped ([Drain] WaterDepthLook keeps " + (ModSettings.WaterDepthLook != null ? ModSettings.WaterDepthLook.Value.ToString("0.00") : "?") + ")"); break; }
                                if (ModSettings.WaterDepthLook != null) { ModSettings.WaterDepthLook.Value = Mathf.Clamp01(Parse(p[1])); applied++; }
                                break;
                        }
                    }
                    catch (System.FormatException e) { ModLog.VerboseWarn("WaterLook: line skipped, a number could not be read '" + line + "': " + e.Message); }
                    catch (System.Exception e) { ModLog.VerboseWarn("WaterLook: line skipped '" + line + "': " + e.Message); }
                }
                ModLog.Info("WaterLook: " + applied + " values from " + source + " applied to the pool water.");
            }
            catch (System.Exception e) { ModLog.Warn("WaterLook: could not apply the look from " + source + ": " + e.Message); }
            return applied;
        }

        static float Parse(string s) { return float.Parse(s, NumberStyles.Float, Inv); }
    }
}
