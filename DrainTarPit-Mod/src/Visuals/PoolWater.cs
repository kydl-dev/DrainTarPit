using System.Collections.Generic;
using System.Reflection;
using DrainTarPit.Logging;
using HarmonyLib;
using UnityEngine;

namespace DrainTarPit.Visuals
{
    /// <summary>
    /// The look of the water pools the pump makes.
    ///
    /// Why not Unity's HDRP Water System: Valheim renders with the Built-in render pipeline, the HDRP water (volume override, water surface, caustics ...)
    /// does not exist in the game. What does exist is the game's own ocean: WaterVolume has a MeshRenderer (m_waterSurface) whose material gets
    /// `_WaterTime` (every frame, WaterVolume.UpdateMaterials), `_depth` (float[4], the normalized depth at the four corners of the surface; SetupMaterial
    /// fills all four with m_forceDepth when that is set) and `_UseGlobalWind` (from the IL of assembly_valheim.dll).
    ///
    /// WaterStyle was a drop-down (Vanilla / Clear) in 0.9.x; Clear is gone. Until the game's ocean material is found, the pools show a plain
    /// translucent tint (ClearLook, internal only, not selectable) - normally for a moment after loading the world.
    ///
    /// The pools show a COPY of the game's ocean material: as soon as the game has an ocean surface loaded, a copy of its material is put on every water pool. A pool is shallow, so
    /// `_depth` is set to the same low value on all four corners ([Drain] WaterDepthLook): the shader then gives its calm, clear, shallow-water look (waves
    /// damped, the ground shows through) instead of the open sea. The copy has no WaterVolume to feed it, so `_WaterTime` is updated here, once per frame.
    /// </summary>
    internal static class PoolWater
    {
        static readonly FieldInfo FSurface = AccessTools.Field(typeof(WaterVolume), "m_waterSurface");
        static readonly FieldInfo FWaterTime = AccessTools.Field(typeof(WaterVolume), "s_waterTime");
        static readonly PropertyInfo PInstances = AccessTools.Property(typeof(WaterVolume), "Instances");

        static Material _vanilla;                       // our copy of the game's ocean material
        static Material _clear;                         // the fallback (ClearLook)
        static int _gen = 1;                            // bumped whenever the look has to be applied again
        static int _configuredGen;                      // generation the vanilla copy was configured for
        static float _nextSearch;
        static int _timeFrame = -1;
        static readonly Dictionary<LiquidVolume, int> Done = new();

        /// Called from the config events (any thread): the pools pick the new look up on their next Update.
        public static void RequestRefresh() { _gen++; }

        // --- for the live editor (WaterEditor) ----------------------------------------------------------------------------
        /// The material the pools show right now (the ocean copy, or the fallback until the ocean is found). null before any pool has asked for it.
        internal static Material Active
        {
            get
            {
                Material sm;
                var style = ModSettings.WaterStyle != null ? ModSettings.WaterStyle.Value : WaterStyleOption.Game;
                if (style != WaterStyleOption.Game && StyleMats.TryGetValue(style, out sm) && sm != null) return sm;    // the editor tunes the style in use (not saved)
                return _vanilla != null ? _vanilla : _clear;
            }
        }
        internal static bool IsOceanCopy { get { return _vanilla != null; } }

        /// Throws the tuned copy away and copies the game's ocean material again (then the preset file, if any, is applied again unless `withPreset` is false).
        internal static void ResetLook(bool withPreset)
        {
            if (_vanilla != null) Object.Destroy(_vanilla);
            _vanilla = null; _nextSearch = 0f; _skipPreset = !withPreset; _gen++;
        }
        static bool _skipPreset;

        /// The water style in use (Game when the config is not bound yet).
        internal static WaterStyleOption StyleInUse { get { return Signs.PoolStyles.Default; } }   // T-023: the admin's world style, else this PC's config

        /// T-053: the material of the water style in use (null for Game or when the style could not be loaded): the hose water takes its colour from it.
        internal static Material StyleLook() { return StyleLook(StyleInUse); }

        /// T-60: the material of one water style (null for Game or when it could not be loaded): hose water / stream of one pump.
        internal static Material StyleLook(WaterStyleOption s) { return s == WaterStyleOption.Game ? null : StyleMaterial(s); }

        /// T-050 (user: "reset tropical to its initial values"): throws the tuned copy of a style material away; the next pool update copies the
        /// style's material from the bundle again (its shipped values). Returns the number of copies thrown away (0 = the style was not loaded yet).
        internal static int ResetStyle(WaterStyleOption style)
        {
            int dropped = 0;
            Material m;
            if (StyleMats.TryGetValue(style, out m))
            {
                StyleMats.Remove(style);
                if (m != null) { Object.Destroy(m); dropped++; }
            }
            StyleMissing.Remove(style);                                      // a style that failed to load gets one more try
            _gen++;                                                          // every pool takes the fresh copy on its next update
            ModLog.Verbose("PoolWater: water style " + style + " reset to its shipped values (" + dropped + " tuned copy thrown away, look generation " + _gen + ")");
            return dropped;
        }

        // --- fallback look ------------------------------------------------------------------------------------------------
        /// A translucent, clear blue surface (much clearer than the old 0.62 alpha tint). The tar material is only used as the base when the Standard
        /// shader is not in the game build (it is stripped from player builds); its ripple is flattened.
        public static Material ClearLook(Material tar)
        {
            if (_clear != null) return _clear;
            var tint = new Color(0.20f, 0.50f, 0.62f, 0.38f);
            Material m = null;
            var sh = Shader.Find("Standard");
            if (sh != null)
            {
                m = new Material(sh);
                m.SetFloat("_Mode", 3f);                                          // Transparent
                m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                m.SetInt("_ZWrite", 0);
                m.DisableKeyword("_ALPHATEST_ON");
                m.EnableKeyword("_ALPHABLEND_ON");
                m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.98f);
                if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
            }
            else if (tar != null)
            {
                m = new Material(tar);
                if (m.HasProperty("_BumpScale")) m.SetFloat("_BumpScale", 0.15f);   // calm: the tar's ripple is toned down
                if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.98f);
                if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.98f);
                if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
            }
            if (m == null) return null;
            m.renderQueue = 3000;
            m.name = "DTP_WaterClear";
            m.color = tint;
            Object.DontDestroyOnLoad(m);
            return _clear = m;
        }

        // --- the game's ocean ---------------------------------------------------------------------------------------------
        static Material BuildVanilla()
        {
            try
            {
                var list = PInstances != null ? PInstances.GetValue(null, null) as System.Collections.IEnumerable : null;
                if (list == null) return FromLoadedMaterials();
                foreach (var o in list)
                {
                    var wv = o as WaterVolume;
                    var r = wv != null && FSurface != null ? FSurface.GetValue(wv) as MeshRenderer : null;
                    var src = r != null ? r.sharedMaterial : null;
                    if (src == null || src.shader == null) continue;
                    var m = new Material(src) { name = "DTP_WaterVanilla" };
                    Object.DontDestroyOnLoad(m);
                    ModLog.Info("PoolWater: using a copy of the game's ocean material '" + src.name + "' (shader '" + src.shader.name + "') for water pools.");
                    return m;
                }
            }
            catch (System.Exception e) { ModLog.Warn("PoolWater: could not copy the ocean material: " + e.Message); }
            return FromLoadedMaterials();
        }

        // T-012 step 1: no ocean surface loaded yet (inland, right after loading the world) - the game's water material is usually in memory anyway (the
        // ocean prefab); take it from there so the pools get their real look at once instead of the plain tint. Searched at most every 2 s (Current).
        const string WaterShader = "Custom/Water";
        static Material FromLoadedMaterials()
        {
            try
            {
                Material best = null;
                foreach (var m in Resources.FindObjectsOfTypeAll<Material>())
                {
                    if (m == null || m.shader == null || m.shader.name != WaterShader || m.name.StartsWith("DTP_")) continue;
                    if (best == null || (m.name.ToLowerInvariant().StartsWith("water") && !best.name.ToLowerInvariant().StartsWith("water"))) best = m;
                }
                if (best == null) { ModLog.Verbose("PoolWater: no ocean surface and no loaded '" + WaterShader + "' material yet, the plain tint is used for now"); return null; }
                var c = new Material(best) { name = "DTP_WaterVanilla" };
                Object.DontDestroyOnLoad(c);
                ModLog.Info("PoolWater: no ocean surface loaded, using a copy of the loaded game material '" + best.name + "' (shader '" + WaterShader + "') for water pools.");
                return c;
            }
            catch (System.Exception e) { ModLog.Warn("PoolWater: could not search the loaded materials: " + e.Message); return null; }
        }

        static void Configure(Material m)
        {
            float d = Mathf.Clamp01(ModSettings.WaterDepthLook.Value);
            m.SetFloatArray("_depth", new[] { d, d, d, d });
            m.SetFloat("_UseGlobalWind", 0f);                                      // a pool has no open-sea swell
        }

        // T-022 (user, 2026-10-20): [Visuals] WaterStyle. Game = the ocean copy below; the others = a preset material of the mod's own shader
        // (DTP/PoolWater, in the bundle, procedural: no texture or shader from the game). One copy per style, made the first time it is used.
        static readonly Dictionary<WaterStyleOption, Material> StyleMats = new();
        static readonly HashSet<WaterStyleOption> StyleMissing = new();

        static Material StyleMaterial(WaterStyleOption style)
        {
            Material m;
            if (StyleMats.TryGetValue(style, out m) && m != null) return m;
            if (StyleMissing.Contains(style)) return null;
            string asset = "assets/materials/water/dtp_water_" + style.ToString().ToLowerInvariant() + ".mat";
            try
            {
                var src = Plugin.Bundle != null ? Plugin.Bundle.LoadAsset<Material>(asset) : null;
                if (src == null || src.shader == null || !src.shader.isSupported)
                {
                    StyleMissing.Add(style);
                    ModLog.Warn("PoolWater: water style " + style + " is not usable (" + (src == null ? "'" + asset + "' not in the bundle" : "shader " + (src.shader == null ? "missing" : src.shader.name + " not supported on this graphics card")) + ") - the Game water is used");
                    return null;
                }
                m = new Material(src) { name = "DTP_Water_" + style };
                Object.DontDestroyOnLoad(m);
                StyleMats[style] = m;
                ModLog.Info("PoolWater: water style " + style + " loaded (material '" + src.name + "', shader '" + src.shader.name + "')");
                return m;
            }
            catch (System.Exception e) { StyleMissing.Add(style); ModLog.Warn("PoolWater: water style " + style + " could not be loaded: " + e.Message + " - the Game water is used"); return null; }
        }

        /// T-023: each pool its own style (Signs.PoolStyles: this player's choice for the pool > the admin's world style > [Visuals] WaterStyle).
        static Material Current(LiquidVolume v)
        {
            var style = v != null ? Signs.PoolStyles.For(v) : StyleInUse;
            if (style != WaterStyleOption.Game)
            {
                var sm = StyleMaterial(style);
                if (sm != null) return sm;
            }
            return GameWater();
        }

        static Material GameWater()
        {
            if (_vanilla == null && Time.unscaledTime >= _nextSearch)
            {
                _nextSearch = Time.unscaledTime + 2f;
                _vanilla = BuildVanilla();
                if (_vanilla != null)
                {
                    _gen++;
                    if (!_skipPreset) WaterLook.Apply(_vanilla);                   // once per fresh copy: the look saved from the live editor (BepInEx/config/DrainTarPit.WaterLook.txt)
                    _skipPreset = false;
                }
            }
            if (_vanilla != null)
            {
                if (_configuredGen != _gen) { Configure(_vanilla); _configuredGen = _gen; }
                return _vanilla;
            }
            return _clear;
        }

        // `_WaterTime` is what makes the ripples move; the game only feeds the materials of its own WaterVolumes
        static void FeedTime()
        {
            if (_vanilla == null || Time.frameCount == _timeFrame) return;
            _timeFrame = Time.frameCount;
            float t = Time.time;
            try { if (FWaterTime != null) t = (float)FWaterTime.GetValue(null); } catch { }
            _vanilla.SetFloat("_WaterTime", t);
        }

        static void Tick(LiquidVolume v)
        {
            if (v == null || v.m_liquidType != LiquidType.Water || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
            if (_vanilla == null) GameWater();                                 // keeps looking for the ocean material (every 2 s); finding it redraws all pools
            int g;
            bool fresh = Done.TryGetValue(v, out g) && g == _gen;
            if (fresh) { FeedTime(); return; }                                 // ripple time of the ocean copy (once per frame, only when it exists)
            var mat = Current(v);
            if (mat == null) return;
            if (mat == _vanilla) FeedTime();
            Done[v] = _gen;
            ModLog.Verbose("PoolWater: pool " + v.name + " at " + v.transform.position.ToString("0") + " drawn with '" + mat.name + "' (look generation " + _gen + ")");
            foreach (var r in v.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer) continue;
                var mats = new Material[Mathf.Max(1, r.sharedMaterials.Length)];
                for (int i = 0; i < mats.Length; i++) mats[i] = mat;
                r.sharedMaterials = mats;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        [HarmonyPatch(typeof(LiquidVolume), "Update")]
        static class Patch_Update
        {
            static void Postfix(LiquidVolume __instance)
            {
                try { Tick(__instance); }
                catch (System.Exception e) { ModLog.VerboseWarn("PoolWater.Tick: " + e.Message); }
            }
        }

        [HarmonyPatch(typeof(LiquidVolume), "OnDestroy")]
        static class Patch_OnDestroy
        {
            static void Prefix(LiquidVolume __instance) { Done.Remove(__instance); }
        }
    }
}
