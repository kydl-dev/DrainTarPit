using System.Collections.Generic;
using System.Text;
using DrainTarPit.Building;
using DrainTarPit.Logging;
using Jotunn.Managers;
using UnityEngine;
using UnityEngine.Rendering;

namespace DrainTarPit.Visuals
{
    /// <summary>
    /// Bundle materials were authored for HDRP/Lit (not present in Valheim). At load time this swaps them to a
    /// Valheim shader (default Custom/Piece) and remaps the textures:
    ///   _BaseColorMap -> _MainTex          (sRGB, straight copy)
    ///   _NormalMap    -> _BumpMap          (straight copy: the bundle's normal maps are already in Unity's DXT5nm
    ///                                       layout, R=1 G=y A=x, which is what Valheim's shaders decode. Do NOT re-swizzle.)
    ///   _MaskMap      -> _MetallicTex      (HDRP mask R=metal, A=smoothness; same layout, no repack)
    ///   _EmissionColor is forced to black (HDRP leaves a white one behind -> white glowing models).
    /// </summary>
    internal static class MaterialFixer
    {
        static readonly HashSet<Material> Done = new();
        static Shader _shader;
        static bool _resolved;

        public static Shader ResolveShader()
        {
            if (_resolved) return _shader;
            _resolved = true;

            // Dedicated server / -nographics: there is no renderer, nothing to fix.
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                ModLog.Info("No graphics device (dedicated server?) - skipping material fix.");
                return null;
            }

            var wanted = ModSettings.ShaderName.Value;
            _shader = FindShader(wanted);
            if (_shader == null)
            {
                ModLog.Warn("Shader '" + wanted + "' not found, trying Custom/Piece, then Standard.");
                _shader = FindShader("Custom/Piece") ?? FindShader("Standard");
            }

            if (_shader == null) ModLog.Error("No usable replacement shader found; materials will render pink.");
            else
            {
                ModLog.Info("Material shader: " + _shader.name);
                LogShaderProperties(_shader);
            }
            return _shader;
        }

        static Shader FindShader(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            var s = Shader.Find(name);
            if (s != null) return s;
            try { return PrefabManager.Cache.GetPrefab<Shader>(name); } catch { return null; }
        }

        static void LogShaderProperties(Shader s)
        {
            if (!ModSettings.DebugLog.Value) return;
            var sb = new StringBuilder();
            int n = s.GetPropertyCount();
            for (int i = 0; i < n; i++) sb.Append(s.GetPropertyName(i)).Append(':').Append(s.GetPropertyType(i)).Append("  ");
            ModLog.Info("Shader properties: " + sb);
        }

        public static void Apply(GameObject prefab)
        {
            var shader = ResolveShader();
            if (shader == null) return;

            foreach (var r in prefab.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) if (mats[i] != null) Fix(mats[i], shader);
                r.sharedMaterials = mats;
            }
        }

        static void Fix(Material m, Shader shader)
        {
            if (!Done.Add(m)) return;                     // shared between prefabs: convert once
            if (m.shader == shader) return;

            // read everything from the HDRP material *before* swapping the shader
            var albedo = GetTex(m, "_BaseColorMap") ?? GetTex(m, "_MainTex");
            var normal = GetTex(m, "_NormalMap");
            var mask = GetTex(m, "_MaskMap");
            var tint = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : Color.white;
            var metallic = m.HasProperty("_Metallic") ? m.GetFloat("_Metallic") : 0f;
            var smooth = m.HasProperty("_Smoothness") ? m.GetFloat("_Smoothness") : 0.3f;
            var normalScale = m.HasProperty("_NormalScale") ? m.GetFloat("_NormalScale") : 1f;

            m.shader = shader;
            m.shaderKeywords = new string[0];             // drop HDRP keywords (_MASKMAP, _NORMALMAP_TANGENT_SPACE, ...)

            Set(m, "_MainTex", Brighten(albedo, ModSettings.AlbedoBrightness.Value));
            if (m.HasProperty("_Color")) m.SetColor("_Color", tint);

            if (normal != null && m.HasProperty("_BumpMap"))
            {
                m.SetTexture("_BumpMap", normal);   // already DXT5nm-swizzled by Unity's Normal-map import (R=1,G=y,A=x)
                if (m.HasProperty("_BumpScale")) m.SetFloat("_BumpScale", normalScale);
                m.EnableKeyword("_NORMALMAP");
            }

            // HDRP materials carry a legacy white _EmissionColor; Custom/Piece reads it -> everything glows white.
            if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", Color.black);
            if (m.HasProperty("_EmissionMap")) m.SetTexture("_EmissionMap", null);
            m.DisableKeyword("_EMISSION");
            if (m.HasProperty("_MetalColor")) m.SetColor("_MetalColor", Color.white);

            bool useMask = mask != null && ModSettings.UseMaskMap.Value;
            if (useMask && m.HasProperty("_MetallicTex"))
            {
                // Valheim Custom/Piece: _MetallicTex (R metal, A smoothness = HDRP mask layout)
                m.SetTexture("_MetallicTex", mask);
                if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 1f);
                if (m.HasProperty("_MetallicAlphaGloss")) m.SetFloat("_MetallicAlphaGloss", 1f);
                if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 1f);
            }
            else if (useMask && m.HasProperty("_MetallicGlossMap"))
            {
                // Standard fallback
                m.SetTexture("_MetallicGlossMap", mask);
                m.EnableKeyword("_METALLICGLOSSMAP");
                if (m.HasProperty("_GlossMapScale")) m.SetFloat("_GlossMapScale", 1f);
                if (m.HasProperty("_OcclusionMap")) m.SetTexture("_OcclusionMap", mask);
            }
            else
            {
                if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
                if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smooth);
            }

            if (ModSettings.DebugLog.Value)
                ModLog.Info("Fixed material " + m.name + " (albedo=" + Name(albedo) + ", normal=" + Name(normal) + ", mask=" + Name(mask) + ")");
        }

        // --- brighter colour textures -------------------------------------------------------------------------------------
        static readonly Dictionary<Texture, Texture> BrightCache = new();

        /// Gamma-lifted copy of a colour texture (c' = c^(1/b)); alpha is kept. Falls back to the original on any problem.
        static Texture Brighten(Texture src, float b)
        {
            if (src == null || b <= 1.001f) return src;
            Texture done;
            if (BrightCache.TryGetValue(src, out done)) return done;
            RenderTexture rt = null, prev = RenderTexture.active;
            try
            {
                rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                Graphics.Blit(src, rt);
                RenderTexture.active = rt;
                var t = new Texture2D(src.width, src.height, TextureFormat.RGBA32, true, false);
                t.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                var lut = new byte[256];
                for (int i = 0; i < 256; i++) lut[i] = (byte)Mathf.Clamp(Mathf.RoundToInt(Mathf.Pow(i / 255f, 1f / b) * 255f), 0, 255);
                var px = t.GetPixels32();
                for (int i = 0; i < px.Length; i++) { px[i].r = lut[px[i].r]; px[i].g = lut[px[i].g]; px[i].b = lut[px[i].b]; }
                t.SetPixels32(px);
                t.Apply(true);
                t.name = src.name + "_bright"; t.wrapMode = src.wrapMode; t.filterMode = src.filterMode;
                BrightCache[src] = t;
                return t;
            }
            catch (System.Exception e) { ModLog.Warn("Could not brighten texture " + src.name + ": " + e.Message); BrightCache[src] = src; return src; }
            finally { RenderTexture.active = prev; if (rt != null) RenderTexture.ReleaseTemporary(rt); }
        }

        // --- see-through hoses --------------------------------------------------------------------------------------------
        // Two config categories (ModSettingsHose, user 2026-10-20):
        //  [Build Time Hose Visuals] Build Hose Style / Opacity: when either changes (RequestRefresh) the next Tick - on the main thread - re-applies
        //   the look to the registered hose PREFABS, so the build ghost and every hose placed from then on have it. A placed hose stores the look it was
        //   built with in its ZDO (DtpPiece.KeepHoseLook), once, and nothing ever changes that stamp.
        //  [World Hose Visuals] World Hose Style / Opacity: when either changes (RequestWorldRefresh) every loaded hose is shown again through
        //   DtpPiece.ShowHoseLook: Default = its own stamp, Transparent / Solid = that for all (World Hose Opacity). Every load does the same.
        // Each translucent material is a separate copy per opacity (never edited afterwards), so showing one look never changes another hose.
        static volatile bool _worldRefreshPending;

        /// Called from the config events (any thread): every loaded hose is shown again in the next Tick (main thread).
        public static void RequestWorldRefresh() { _worldRefreshPending = true; ModLog.Verbose("Hoses: world look refresh queued for the next frame"); }
        static bool _noStandardLogged;
        static readonly Dictionary<string, Material> ClearCache = new();       // original wall material + opacity -> its see-through copy
        static readonly Dictionary<Material, Material> ClearOrigin = new();    // see-through copy -> the original wall material
        static readonly List<GameObject> HosePrefabs = new();
        static volatile bool _refreshPending;

        static bool IsHose(string n) { return n.Contains("Hose_Straight") || n.Contains("Elbow"); }

        static bool WantsClear { get { return ModSettings.BuildHoseStyle.Value == HoseStyleOption.Transparent && SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null; } }

        /// Registers a straight hose / elbow prefab and gives it the current look. Call after Apply.
        public static void MakeHosesClear(GameObject prefab)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null || !IsHose(prefab.name)) return;
            if (!HosePrefabs.Contains(prefab)) HosePrefabs.Add(prefab);
            ApplyHoseStyle(prefab);
        }

        /// Called from the config events (any thread): the refresh itself runs in Tick, on the main thread.
        public static void RequestRefresh() { _refreshPending = true; ModLog.Verbose("Hoses: build look refresh of the hose prefabs queued for the next frame"); }

        /// Called every frame from Plugin.Update (one bool check).
        public static void Tick()
        {
            if (_worldRefreshPending)
            {
                _worldRefreshPending = false;
                int shown = Building.DtpPiece.ShowAllHoseLooks();
                ModLog.Info("World Hose Style " + ModSettings.WorldHoseStyle.Value + (ModSettings.WorldHoseStyle.Value == WorldHoseStyleOption.Transparent ? " (opacity " +
                    ModSettings.WorldHoseOpacity.Value.ToString("0.00") + ")" : "") + ": " + shown + " loaded hose(s) shown again");
            }
            if (!_refreshPending) return;
            _refreshPending = false;
            RefreshHoses();
        }

        static void RefreshHoses()
        {
            try
            {
                if (HosePrefabs.Count == 0) return;                                  // nothing registered yet (dedicated server, or before the pieces exist)
                int changed = 0;
                foreach (var prefab in HosePrefabs) if (prefab != null) changed += ApplyHoseStyle(prefab);
                ModLog.Info("Build Hose Style " + ModSettings.BuildHoseStyle.Value + (WantsClear ? " (opacity " + ModSettings.BuildHoseOpacity.Value.ToString("0.00") + ")" : "") + " will be used for hoses built from now on (" + changed + " prefab renderer(s) changed); hoses already built keep the look they were built with.");
            }
            catch (System.Exception e) { ModLog.Warn("Could not refresh the hose look: " + e.Message); }
        }

        // The wall is the first material called '...leather...', else the first material of the model.
        static int WallSlot(Material[] mats)
        {
            for (int i = 0; i < mats.Length; i++)
                if (mats[i] != null && mats[i].name.ToLowerInvariant().Contains("leather")) return i;
            return mats.Length > 0 && mats[0] != null ? 0 : -1;
        }

        /// Puts the wall material of every renderer under `root` (a hose PREFAB) in line with [Build Time Hose Visuals]: the see-through copy for Transparent, the
        /// original for Solid. Returns how many renderers changed.
        static int ApplyHoseStyle(GameObject root) { return ApplyLook(root, ModSettings.BuildHoseStyle.Value, ModSettings.BuildHoseOpacity.Value); }

        /// Same, for an explicit style / opacity (a placed hose re-applies the look it was built with).
        public static int ApplyLook(GameObject root, HoseStyleOption style, float opacity)
        {
            bool clear = style == HoseStyleOption.Transparent && SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;
            Shader std = clear ? ResolveClearShader() : null;
            if (clear && std == null)
            {
                if (!_noStandardLogged) { _noStandardLogged = true; ModLog.Warn("ClearHoses: no transparent-capable shader found (tried Standard, Sprites/Default, Hidden/Internal-Colored) - hoses stay solid."); }
                clear = false;
            }
            int changed = 0;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                int slot = WallSlot(mats);
                if (slot < 0) continue;
                var current = mats[slot];
                Material original;
                if (!ClearOrigin.TryGetValue(current, out original)) original = current;
                var wanted = clear ? Clear(original, std, opacity) : original;
                if (wanted == current) continue;
                mats[slot] = wanted;
                r.sharedMaterials = mats;
                changed++;
            }
            return changed;
        }

        // Valheim's player build strips 'Standard'. Fall back to shaders Unity always ships: Sprites/Default (texture + tint + alpha blend, double-sided,
        // unlit) and Hidden/Internal-Colored (flat tint + alpha blend).
        static Shader _clearShader; static bool _clearResolved;
        static Shader ResolveClearShader()
        {
            if (_clearResolved) return _clearShader;
            _clearResolved = true;
            foreach (var n in new[] { "Standard", "Sprites/Default", "Hidden/Internal-Colored" })
            {
                var s = FindShader(n);
                if (s != null) { _clearShader = s; ModLog.Info("ClearHoses shader: " + s.name); break; }
            }
            return _clearShader;
        }

        // opacity of a translucent material (the colour is shaped by the shader that was picked for it)
        static void SetOpacity(Material c, float a)
        {
            string sn = c.shader != null ? c.shader.name : "";
            if (sn == "Standard") c.color = new Color(1f, 1f, 1f, a);
            else if (c.HasProperty("_Color")) c.SetColor("_Color", sn == "Sprites/Default" ? new Color(1f, 1f, 1f, a) : new Color(0.55f, 0.5f, 0.4f, a));
        }

        static Material Clear(Material src, Shader std, float opacity)
        {
            Material c;
            opacity = Mathf.Clamp(Mathf.Round(opacity * 100f) / 100f, 0.01f, 1f);   // one material per whole percent
            var key = src.GetInstanceID() + "|" + Mathf.RoundToInt(opacity * 100f);
            if (ClearCache.TryGetValue(key, out c) && c != null) return c;
            c = new Material(std) { name = src.name + "_clear" };
            var tex = src.HasProperty("_MainTex") ? src.GetTexture("_MainTex") : null;
            if (tex != null && c.HasProperty("_MainTex")) c.mainTexture = tex;
            if (std.name != "Standard")
            {
                // Sprites/Default blends premultiplied (it multiplies rgb by alpha itself); Internal-Colored needs its blend set by hand
                if (c.HasProperty("_SrcBlend")) c.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                if (c.HasProperty("_DstBlend")) c.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                if (c.HasProperty("_ZWrite")) c.SetInt("_ZWrite", 0);
                if (c.HasProperty("_Cull")) c.SetInt("_Cull", 0);
                c.renderQueue = 3000;
            }
            else
            {
                if (c.HasProperty("_Metallic")) c.SetFloat("_Metallic", 0f);
                if (c.HasProperty("_Glossiness")) c.SetFloat("_Glossiness", 0.4f);
                c.SetFloat("_Mode", 2f);                                              // Fade
                c.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                c.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                c.SetInt("_ZWrite", 0);
                c.DisableKeyword("_ALPHATEST_ON"); c.DisableKeyword("_ALPHAPREMULTIPLY_ON"); c.EnableKeyword("_ALPHABLEND_ON");
                c.renderQueue = 3000;
            }
            SetOpacity(c, opacity);
            ClearCache[key] = c;
            ClearOrigin[c] = src;
            return c;
        }

        static Texture GetTex(Material m, string prop) => m.HasProperty(prop) ? m.GetTexture(prop) : null;
        static string Name(Object o) => o == null ? "none" : o.name;
        static void Set(Material m, string prop, Texture t) { if (t != null && m.HasProperty(prop)) m.SetTexture(prop, t); }
    }
}
