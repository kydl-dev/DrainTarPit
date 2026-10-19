using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using DrainTarPit.Building;
using DrainTarPit.Logging;
using DrainTarPit.Visuals;
using HarmonyLib;
using UnityEngine;

namespace DrainTarPit.Drain
{
    /// <summary>
    /// Where the pumped liquid ends up. There is NO hole / pool model of our own: the liquid is poured into a real vanilla LiquidVolume
    /// (the same component the world's tar pits use), so it is physical - a mesh surface, the liquid trigger that slows / floats players,
    /// saved in the world, pumpable again with the strainer.
    ///
    /// Tar and water use the same machinery. From the IL of assembly_valheim.dll: LiquidVolume's simulation never reads m_liquidType (only
    /// the constructor writes it); the type is read by LiquidSurface (GetLiquidType) and handed to Character / Floating.SetLiquidLevel, where
    /// Water sets m_waterLevel and Tar sets m_tarLevel. So a water pool is a tar volume whose type is Water, whose material is water and
    /// whose viscosity is lower (it runs more freely).
    ///
    ///   1) Every time the game scene is created (ZNetScene.Awake postfix) runtime clones of a vanilla tar volume prefab are registered as
    ///      `DTP_TarPool`, `DTP_WaterPool2` (and `DTP_WaterPool`, the older coarse-grid one, only so saved pools keep loading) (found in ZNetScene.m_prefabs: the prefab 'TarLiquid', or any prefab with a Tar LiquidVolume;
    ///      the grids are smaller, empty). Nothing is shipped in the asset bundle for them. (0.6.1 did this from Jotunn's
    ///      OnVanillaPrefabsAvailable, which fires in the MAIN MENU where ZNetScene does not exist yet -> nothing was ever found.)
    ///   2) Every stroke the point where the nozzle stream lands is worked out (same exit speed / gravity arc as the visible stream).
    ///   3) If a volume of that liquid (a vanilla pit or one of ours) already covers that point, the liquid goes into it; otherwise a new pool
    ///      is spawned there, its origin at rim height so its ground-height rays see a hole dug with the pickaxe.
    ///   4) The owner of the volume raises the liquid in the basin around the landing point (TarPitDeposit.DepositLocal). The game's own simulation
    ///      does the rest. What does not fit runs over the lowest point of the rim; what reaches the edge of the grid while the ground goes on
    ///      downhill (a long moat) continues into the next pool (Continue).
    /// </summary>
    internal static class TarPool
    {
        public const string PrefabName = "DTP_TarPool";
        public const string WaterPrefabName = "DTP_WaterPool2";
        public const string LegacyWaterPrefabName = "DTP_WaterPool";      // 0.5 m cells, 48x48: pools saved by older versions keep loading with this one
        public const string ZPool = "TPD_pool";                 // ZDO flag on our own volumes (no self-refill)
        const int TarGridWidth = 32;
        const float CellScale = 0.5f;
        const float MaxDepth = 12f;
        public const int MaxRunoffHops = 6;                     // pools in a row that one stroke may continue into

        const string VanillaPitName = "TarLiquid";
        static readonly FieldInfo FNamedPrefabs = AccessTools.Field(typeof(ZNetScene), "m_namedPrefabs");
        static readonly FieldInfo FViscosity = AccessTools.Field(typeof(LiquidVolume), "m_viscocity");

        static GameObject _prefab;          // the tar clone (lives in an inactive container, survives scene changes)
        static GameObject _waterPrefab;     // the water clone
        static GameObject _legacyWaterPrefab;   // the water clone older worlds were saved with (see LegacyWaterPrefabName)
        static GameObject _container;

        static GameObject Get(PumpLiquid liquid) { return liquid == PumpLiquid.Water ? _waterPrefab : _prefab; }

        [HarmonyPatch(typeof(ZNetScene), "Awake")]
        static class Patch_ZNetSceneAwake
        {
            static void Postfix(ZNetScene __instance) { RegisterIn(__instance); }
        }

        /// Builds the pool prefabs once and (re)registers them in this ZNetScene so the game can spawn them and re-create them from the saved ZDOs.
        /// Both are always registered (even when depositing is switched off in the config) so pools that are already in the world keep loading.
        static void RegisterIn(ZNetScene scene)
        {
            if (scene == null) return;
            try
            {
                var named = FNamedPrefabs != null ? FNamedPrefabs.GetValue(scene) as Dictionary<int, GameObject> : null;
                if (named == null) { ModLog.Warn("TarPool: ZNetScene.m_namedPrefabs not found - the pumped liquid cannot be deposited."); return; }
                RegisterOne(scene, named, PumpLiquid.Tar);
                RegisterOne(scene, named, PumpLiquid.Water);
                RegisterLegacyWater(scene, named);
            }
            catch (System.Exception e) { ModLog.Error("TarPool.RegisterIn failed: " + e); }
        }

        static void RegisterOne(ZNetScene scene, Dictionary<int, GameObject> named, PumpLiquid liquid)
        {
            var go = Get(liquid);
            if (go == null)
            {
                go = BuildPrefab(scene, liquid);
                if (liquid == PumpLiquid.Water) _waterPrefab = go; else _prefab = go;
            }
            if (go == null) return;
            if (!scene.m_prefabs.Contains(go)) scene.m_prefabs.Add(go);
            named[go.name.GetStableHashCode()] = go;
            ModLog.Verbose("TarPool: '" + go.name + "' registered in the ZNetScene.");
        }

        static void RegisterLegacyWater(ZNetScene scene, Dictionary<int, GameObject> named)
        {
            if (_legacyWaterPrefab == null) _legacyWaterPrefab = BuildPrefab(scene, PumpLiquid.Water, true);
            var go = _legacyWaterPrefab;
            if (go == null) return;
            if (!scene.m_prefabs.Contains(go)) scene.m_prefabs.Add(go);
            named[go.name.GetStableHashCode()] = go;
        }

        static GameObject BuildPrefab(ZNetScene scene, PumpLiquid liquid, bool legacy = false)
        {
            bool water = liquid == PumpLiquid.Water;
            var source = FindVanillaTarVolume(scene);
            if (source == null)
            {
                ModLog.Warn("TarPool: no vanilla tar LiquidVolume prefab found - the pumped liquid cannot be deposited (it only pours out visually).");
                LogCandidates(scene);
                return null;
            }
            if (_container == null)
            {
                _container = new GameObject("DTP_PrefabContainer");
                _container.SetActive(false);                      // clones below never run Awake / create a ZDO
                Object.DontDestroyOnLoad(_container);
            }
            var go = Object.Instantiate(source, _container.transform, false);
            go.name = water ? (legacy ? LegacyWaterPrefabName : WaterPrefabName) : PrefabName;
            var lv = go.GetComponentInChildren<LiquidVolume>(true);
            // water pools use finer cells: the surface mesh ends at a grid point, so the gap between the liquid and a wall is up to one cell wide
            float cell = legacy ? CellScale : water ? ModSettings.WaterCellSize.Value : CellScale;
            int width = legacy ? 48 : water ? Mathf.Clamp(Mathf.RoundToInt(ModSettings.WaterPoolSize.Value / cell), 16, 128) : TarGridWidth;
            Set(lv, "m_width", width);
            Set(lv, "m_scale", cell);
            Set(lv, "m_maxDepth", MaxDepth);
            Set(lv, "m_initialVolume", 0f);                  // starts empty: only pumped liquid goes in
            var nv = go.GetComponent<ZNetView>();
            if (nv != null) nv.m_persistent = true;
            if (water) MakeWater(go, lv);
            ModLog.Info("TarPool: '" + go.name + "' registered (clone of vanilla '" + source.name + "', " + width + "x" + width + " cells of " + cell + " m).");
            return go;
        }

        // --- water ------------------------------------------------------------------------------------------------------
        /// Turns the tar clone into a water volume: liquid type Water (LiquidSurface hands that type to Character.SetLiquidLevel -> m_waterLevel, so
        /// players swim and items float instead of getting tarred), a freer-flowing simulation, a water look, and none of the tar bubbles / sounds.
        static void MakeWater(GameObject go, LiquidVolume lv)
        {
            lv.m_liquidType = LiquidType.Water;
            try
            {
                if (FViscosity != null)
                {
                    float v = (float)FViscosity.GetValue(lv);
                    if (v > 0f) { _tarBaseVisc = v; FViscosity.SetValue(lv, v * ModSettings.WaterFlow.Value); }     // flow between cells grows with sqrt(viscosity)
                }
                Set(lv, "m_randomEffectInterval", 1000000f);      // the tar's random bubble effects
                Set(lv, "m_castShadow", false);
            }
            catch (System.Exception e) { ModLog.Warn("TarPool: water tuning failed: " + e.Message); }

            Material tarMat = null;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                if (!(r is ParticleSystemRenderer) && r.sharedMaterial != null) { tarMat = r.sharedMaterial; break; }
            var mat = PoolWater.ClearLook(tarMat);        // the fallback look; PoolWater swaps in the game's ocean material once it is loaded
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer) continue;
                if (mat == null) continue;
                var mats = new Material[Mathf.Max(1, r.sharedMaterials.Length)];
                for (int i = 0; i < mats.Length; i++) mats[i] = mat;
                r.sharedMaterials = mats;
            }
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true)) if (!HostsLiquid(ps.gameObject)) ps.gameObject.SetActive(false);
            foreach (var z in go.GetComponentsInChildren<ZSFX>(true)) Quiet(z.gameObject, z);
            foreach (var a in go.GetComponentsInChildren<AudioSource>(true)) Quiet(a.gameObject, a);
        }

        // --- live water flow ----------------------------------------------------------------------------------------------------
        static float _tarBaseVisc;       // the vanilla tar viscosity the water pools are derived from (water = this x [Drain] WaterFlow)

        /// [Drain] WaterFlow changed (the editor's slider, the config manager, the .cfg file): the water pools that exist and the prefabs for new ones get the new
        /// viscosity right away. A pouring boost that is running keeps its factor (TarPitFlow.SetBaseViscosity). Nothing happens before the first pool prefab was built.
        internal static void ApplyWaterFlow()
        {
            if (_tarBaseVisc <= 0f || FViscosity == null) return;
            float value = _tarBaseVisc * ModSettings.WaterFlow.Value;
            try
            {
                foreach (var p in new[] { _waterPrefab, _legacyWaterPrefab })
                {
                    var plv = p != null ? p.GetComponentInChildren<LiquidVolume>(true) : null;
                    if (plv != null) FViscosity.SetValue(plv, value);
                }
                foreach (var v in TarPits.All)
                    if (v != null && v.m_liquidType == LiquidType.Water) TarPitFlow.SetBaseViscosity(v, value);
            }
            catch (System.Exception e) { ModLog.VerboseWarn("TarPool.ApplyWaterFlow: " + e.Message); }
        }

        // switches a tar-only effect off without touching the objects that carry the liquid itself
        static void Quiet(GameObject host, Behaviour b)
        {
            if (HostsLiquid(host)) { b.enabled = false; return; }
            host.SetActive(false);
        }

        static bool HostsLiquid(GameObject host)
        {
            return host.GetComponent<LiquidVolume>() != null || host.GetComponent<LiquidSurface>() != null || host.GetComponent<MeshRenderer>() != null;
        }

        // diagnostics for the next log when nothing was found
        static void LogCandidates(ZNetScene scene)
        {
            int n = 0;
            foreach (var p in scene.m_prefabs)
            {
                if (p == null) continue;
                var lv = p.GetComponentInChildren<LiquidVolume>(true);
                bool tarName = p.name.IndexOf("tar", System.StringComparison.OrdinalIgnoreCase) >= 0;
                if (lv == null && !tarName) continue;
                if (++n > 25) { ModLog.Info("TarPool: ... more candidates not listed"); break; }
                ModLog.Info("TarPool: candidate '" + p.name + "' LiquidVolume=" + (lv != null ? lv.m_liquidType.ToString() : "none") + " ZNetView=" + (p.GetComponent<ZNetView>() != null));
            }
        }

        static void Set(object o, string field, object value)
        {
            var f = AccessTools.Field(o.GetType(), field);
            if (f == null) { ModLog.Warn("TarPool: LiquidVolume has no field " + field); return; }
            f.SetValue(o, System.Convert.ChangeType(value, f.FieldType));
        }

        // a game prefab with a ZNetView on its root and a tar LiquidVolume (on the root or below it), so cloning it gives just the liquid, not a whole
        // location. The vanilla pit prefab 'TarLiquid' wins.
        static GameObject FindVanillaTarVolume(ZNetScene scene)
        {
            GameObject best = null;
            foreach (var p in scene.m_prefabs)
            {
                if (p == null) continue;
                var lv = p.GetComponentInChildren<LiquidVolume>(true);
                if (lv == null || lv.m_liquidType != LiquidType.Tar || p.GetComponent<ZNetView>() == null) continue;
                if (p.name == PrefabName || p.name == WaterPrefabName || p.name == LegacyWaterPrefabName) continue;       // our own clones (a second ZNetScene.Awake)
                ModLog.Verbose("TarPool: tar volume prefab candidate '" + p.name + "'");
                if (p.name == VanillaPitName) return p;
                if (best == null) best = p;
            }
            return best;
        }

        // --- landing point of the stream ---------------------------------------------------------------------------------
        static readonly RaycastHit[] LandHits = new RaycastHit[16];

        // ReSharper disable Unity.PerformanceAnalysis
        /// Follows the stream from the nozzle (exit speed NozzlePressure along dir, gravity) until it hits ground. `time` = flight time.
        static Vector3 Landing(Vector3 pos, Vector3 dir, out float time)
        {
            Vector3 v = dir.normalized * Mathf.Max(0.5f, ModSettings.NozzlePressure.Value), p = pos, g = new(0f, -9.81f, 0f);
            const float dt = 0.02f;
            time = 0f;
            for (int i = 0; i < 250; i++)
            {
                Vector3 np = p + v * dt + g * (0.5f * dt * dt);
                Vector3 seg = np - p; float len = seg.magnitude;
                if (len > 1e-4f)
                {
                    int cnt = Physics.RaycastNonAlloc(p, seg / len, LandHits, len, ~0, QueryTriggerInteraction.Ignore);
                    float best = float.MaxValue; Vector3 hp = Vector3.zero;
                    for (int k = 0; k < cnt; k++)
                    {
                        var h = LandHits[k];
                        if (h.collider == null || h.distance >= best) continue;
                        if (h.collider.GetComponentInParent<DtpPiece>() != null || h.collider.GetComponentInParent<Character>() != null) continue;
                        if (h.normal.y < 0.3f) continue;
                        best = h.distance; hp = h.point;
                    }
                    if (best < float.MaxValue) { time += dt * (best / len); return hp; }
                }
                v += g * dt; p = np; time += dt;
            }
            if (ZoneSystem.instance != null) p.y = ZoneSystem.instance.GetGroundHeight(p);
            return p;
        }

        // highest ground around the landing point = the rim; the pool's origin sits there so its rays (MaxDepth up, 2 x MaxDepth down) see the hole
        static float RimHeight(Vector3 p)
        {
            if (ZoneSystem.instance == null) return p.y;
            float top = ZoneSystem.instance.GetGroundHeight(p);
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI * 0.25f;
                top = Mathf.Max(top, ZoneSystem.instance.GetGroundHeight(p + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 2.5f));
            }
            return top + 0.2f;
        }

        // --- deposit ------------------------------------------------------------------------------------------------------
        /// Called on the pumping client with the volume that really left the source. The liquid arrives when the stream lands.
        public static void DepositAtNozzle(Vector3 at, Vector3 dir, float volume, PumpLiquid liquid = PumpLiquid.Tar)
        {
            if (volume <= 0f || Plugin.Instance == null) return;
            try
            {
                float flight;
                var land = Landing(at, dir, out flight);
                Plugin.Instance.RunCoroutine(DepositAfter(flight, land, volume, liquid));
            }
            catch (System.Exception e) { ModLog.Warn("TarPool deposit failed: " + e.Message); }
        }

        static IEnumerator DepositAfter(float delay, Vector3 land, float volume, PumpLiquid liquid)
        {
            yield return new WaitForSeconds(Mathf.Clamp(delay, 0f, 3f));
            try
            {
                if (liquid == PumpLiquid.Water)
                {
                    float depth, surface;
                    if (Liquids.WaterDepthAt(land, out depth, out surface)) yield break;      // lands in the sea / a lake: it just joins it
                }
                var target = FindVolume(land, liquid);
                bool made = target == null;
                if (made) target = Spawn(land, liquid);
                if (target == null) yield break;
                ModLog.Verbose("TarPool: deposit " + volume.ToString("0.000", CultureInfo.InvariantCulture) + " m3 at " + land.ToString("0.00") +
                    " into " + DescribeVolume(target) + (made ? " (new pool)" : " (existing pool)"));
                TarPitDeposit.RequestDeposit(target, land, volume, 0);
            }
            catch (System.Exception e) { ModLog.Warn("TarPool deposit failed: " + e.Message); }
        }

        // An existing volume of this liquid with the point well inside its grid; the closest centre wins.
        // Water is special: only pools marked with this mod's ZDO flag may receive pumped water. Older test runs
        // left many empty, unflagged DTP_WaterPool2 instances overlapping the real pool; picking one silently made
        // a pour disappear from the pool the user expected to measure. Tar may still be deposited into vanilla tar pits.
        internal static LiquidVolume FindVolume(Vector3 p, PumpLiquid liquid)
        {
            LiquidVolume best = null; float bd = float.MaxValue;
            foreach (var v in TarPits.All)
            {
                if (v == null || TarPits.LiquidOf(v) != liquid) continue;
                if (!HasUsableGridGeometry(v)) continue;
                var g = TarPits.ToGrid(v, p);
                if (!TarPits.InGrid(v, g) || !TarPits.Contains(v, p, 1.5f)) continue;
                if (liquid == PumpLiquid.Water && !TarPits.IsPumpPool(v)) continue;
                float d = (v.transform.position - p).sqrMagnitude;
                if (d < bd) { bd = d; best = v; }
            }
            return best;
        }

        static bool HasUsableGridGeometry(LiquidVolume v)
        {
            return v != null && v.m_width > 0 && v.m_width <= 512 &&
                   v.m_scale > 0f && !float.IsNaN(v.m_scale) && !float.IsInfinity(v.m_scale);
        }

        internal static bool GridCoversPoint(LiquidVolume v, Vector3 p)
        {
            if (!HasUsableGridGeometry(v)) return false;
            return TarPits.InGrid(v, TarPits.ToGrid(v, p));
        }

        /// Test button of the live editor: pours `volume` m3 of `liquid` at a world point exactly like a stroke would (into the pool around it, or a new pool).
        internal static string DebugPour(Vector3 land, float volume, PumpLiquid liquid)
        {
            LiquidVolume ignored;
            return DebugPour(land, volume, liquid, out ignored);
        }

        /// Same as DebugPour, and returns the exact pool selected before RequestDeposit so the observer can sample that instance rather than re-selecting a nearby one.
        internal static string DebugPour(Vector3 land, float volume, PumpLiquid liquid, out LiquidVolume target)
        {
            target = null;
            if (ZNetScene.instance == null) return "no world loaded";
            if (liquid == PumpLiquid.Water)
            {
                float depth, surface;
                if (Liquids.WaterDepthAt(land, out depth, out surface)) return "that point is in the sea / a lake: water just joins it";
            }
            target = FindVolume(land, liquid);
            bool made = target == null;
            if (made) target = Spawn(land, liquid);
            if (target == null) return "no pool prefab (see the log: TarPool ...)";
            TarPitDeposit.RequestDeposit(target, land, volume, 0);
            return (made ? "new pool spawned, " : "existing pool, ") + volume.ToString("0.0", CultureInfo.InvariantCulture) + " m3 requested; receiver " + DescribeVolume(target);
        }

        /// Diagnostic identity for test and verbose deposit logs. Instance ID is stable for the lifetime of this Unity object (not across game sessions).
        internal static string DescribeVolume(LiquidVolume v)
        {
            if (v == null) return "pool=<none/destroyed>";
            int w = v.m_width;
            long expected = w >= 0 ? ((long)w + 1L) * ((long)w + 1L) : -1L;
            int depths = -1, heights = -1;
            bool ready = false;
            string gridError = null;
            try
            {
                ready = TarPitDebug.Ready(v);
                using (TarPitDebug.Lock(v))
                {
                    var d = TarPitDebug.DepthList(v);
                    var h = TarPitDebug.HeightList(v);
                    depths = d != null ? d.Count : -1;
                    heights = h != null ? h.Count : -1;
                }
            }
            catch (System.Exception e) { gridError = e.GetType().Name; }
            bool scaleValid = v.m_scale > 0f && !float.IsNaN(v.m_scale) && !float.IsInfinity(v.m_scale);
            bool valid = ready && w > 0 && scaleValid && expected > 0 && depths >= expected && heights >= expected && gridError == null;
            return "poolId=" + v.GetInstanceID() + " name=\"" + v.name + "\" pos=" + v.transform.position.ToString("0.00") +
                " liquid=" + TarPits.LiquidOf(v) + " pumpPool=" + TarPits.IsPumpPool(v) + " grid=" + w + "x" + w + " cell=" + v.m_scale.ToString("0.000", CultureInfo.InvariantCulture) +
                "m heightsReady=" + ready + " depthCells=" + (depths < 0 ? "null" : depths.ToString(CultureInfo.InvariantCulture)) + " heightCells=" + (heights < 0 ? "null" : heights.ToString(CultureInfo.InvariantCulture)) +
                " expectedCells=" + expected.ToString(CultureInfo.InvariantCulture) + " gridValid=" + valid + (gridError != null ? " gridError=" + gridError : "");
        }

        internal static bool GridIsValid(LiquidVolume v)
        {
            if (v == null || v.m_width <= 0 || v.m_width > 512 || v.m_scale <= 0f || float.IsNaN(v.m_scale) || float.IsInfinity(v.m_scale)) return false;
            long expected = ((long)v.m_width + 1L) * ((long)v.m_width + 1L);
            if (expected <= 0 || expected > int.MaxValue) return false;
            try
            {
                if (!TarPitDebug.Ready(v)) return false;
                using (TarPitDebug.Lock(v))
                {
                    var d = TarPitDebug.DepthList(v); var h = TarPitDebug.HeightList(v);
                    return d != null && h != null && d.Count >= expected && h.Count >= expected;
                }
            }
            catch { return false; }
        }

        static LiquidVolume Spawn(Vector3 land, PumpLiquid liquid)
        {
            var prefab = Get(liquid);
            if (prefab == null || ZNetScene.instance == null) return null;
            var pos = new Vector3(land.x, RimHeight(land), land.z);
            var go = Object.Instantiate(prefab, pos, Quaternion.identity);
            var lv = go.GetComponentInChildren<LiquidVolume>(true);
            if (lv == null) return null;
            TarPits.MarkPool(lv);
            ModLog.Verbose("TarPool: new " + liquid + " pool at " + pos.ToString("0.0"));
            return lv;
        }

        /// Test harness (Debug/PoolTests.cs): a fresh, flagged, empty water pool with its origin at the rim height of `land`.
        internal static LiquidVolume SpawnForTest(Vector3 land) { return Spawn(land, PumpLiquid.Water); }

        /// Test harness: removes a pool made by SpawnForTest.
        internal static void DestroyForTest(LiquidVolume v)
        {
            var nv = v != null ? TarPits.Nview(v) : null;
            if (nv == null || !nv.IsValid() || ZNetScene.instance == null) return;
            nv.ClaimOwnership();
            ZNetScene.instance.Destroy(v.gameObject);
        }

        // --- runoff into the next pool --------------------------------------------------------------------------------------
        /// Runs on the owner of `from`. The liquid reached the edge of from's grid and the ground goes on downhill there (a long moat, a ditch, a slope):
        /// the rest `volume` (m3) is poured into the pool beyond the edge, which is made if it does not exist yet. `hop` counts the pools in a row,
        /// so a stroke can never spread over the whole map.
        public static void Continue(LiquidVolume from, Vector3 edge, float volume, int hop)
        {
            if (from == null || volume < 0.05f || hop > MaxRunoffHops || ZNetScene.instance == null || !ModSettings.PoolRunoff.Value) return;
            try
            {
                var liquid = TarPits.LiquidOf(from);
                var o = from.transform.position;
                var dir = new Vector3(edge.x - o.x, 0f, edge.z - o.z);
                if (dir.sqrMagnitude < 0.01f) return;
                dir.Normalize();
                var into = edge + dir * 1.5f;                              // lands just beyond the edge
                var target = FindVolume(into, liquid);
                if (target == null)
                {
                    var prefab = Get(liquid);
                    var plv = prefab != null ? prefab.GetComponentInChildren<LiquidVolume>(true) : null;
                    if (plv == null) return;
                    float half = plv.m_width * plv.m_scale * 0.5f;
                    target = Spawn(edge + dir * Mathf.Max(3f, half - 4f), liquid);    // the new grid covers the edge point with 4 m to spare
                }
                if (target == null || target == from) return;
                ModLog.Verbose("TarPool: " + volume.ToString("0.00", CultureInfo.InvariantCulture) + " m3 continues from " + DescribeVolume(from) +
                    " into next pool " + DescribeVolume(target) + " (hop " + hop + ")");
                TarPitDeposit.RequestDeposit(target, into, volume, hop);
            }
            catch (System.Exception e) { ModLog.Warn("TarPool runoff failed: " + e.Message); }
        }
    }
}
