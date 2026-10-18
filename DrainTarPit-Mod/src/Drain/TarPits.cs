using System.Collections.Generic;
using System.Reflection;
using DrainTarPit.Building;
using DrainTarPit.Logging;
using HarmonyLib;
using UnityEngine;

namespace DrainTarPit.Drain
{
    /// <summary>
    /// How vanilla tar pits work (from assembly_valheim.dll): a pit is a LiquidVolume with m_liquidType == Tar. It is a
    /// height field - m_depths holds (m_width+1)^2 floats (metres of tar per cell, index = z * (m_width+1) + x),
    /// m_heights the ground height per cell, cell size m_scale, centred on the object's position. A worker thread
    /// evens neighbouring surfaces (m_viscocity) and keeps the total volume constant, under the m_meshDataLock lock.
    /// The ZDO owner saves the depths to the ZDO (s_liquidData) and everybody else reloads them when the revision
    /// changes.
    ///
    /// Draining = subtracting depth from cells near the strainer on the OWNER (we RPC the owner), flagging the volume
    /// dirty + needsSaving. The simulation then pulls the rest of the pit towards the hole, so the level drops
    /// gradually and a concave pit empties from its deepest point.
    /// </summary>
    internal static class TarPits
    {
        public const string RpcDrain = "DTP_Drain";        // client -> pit owner: remove tar
        public const string RpcDrained = "DTP_Drained";    // pit owner -> requesting client: how much was really removed
        public const string RpcDeposit = "DTP_Deposit";    // client -> pit owner: pour tar INTO a tar volume (fills the basin around the point)
        // LEGACY KEYS: VarBaseline, VarRefillTime (here), TarPool.ZPool and DtpPiece.ZLiquid are stored inside existing worlds, so they deliberately keep the old "TPD_" prefix.
        internal const string VarBaseline = "TPD_baseline";   // total volume (m3) before the first drain; used by refill
        const string VarRefillTime = "TPD_refillT";  // world time (ms) of the last drain / refill step; lets refill catch up after unload

        public static readonly List<LiquidVolume> All = new();

        static readonly FieldInfo FDepths = AccessTools.Field(typeof(LiquidVolume), "m_depths");
        static readonly FieldInfo FHeights = AccessTools.Field(typeof(LiquidVolume), "m_heights");
        static readonly FieldInfo FLock = AccessTools.Field(typeof(LiquidVolume), "m_meshDataLock");
        static readonly FieldInfo FDirty = AccessTools.Field(typeof(LiquidVolume), "m_dirty");
        static readonly FieldInfo FNeedsSaving = AccessTools.Field(typeof(LiquidVolume), "m_needsSaving");
        static readonly FieldInfo FNview = AccessTools.Field(typeof(LiquidVolume), "m_nview");

        // The pit's worker thread rewrites m_depths under m_meshDataLock; every read / write here takes it too.
        // IL of LiquidVolume.UpdateThread / Update (assembly_valheim.dll): the game does Monitor.Enter / Monitor.TryEnter on this field, i.e. it is a plain
        // object used as a Monitor (NOT a System.Threading.Mutex - the old code cast it to a Mutex, got null and so took no lock at all, which let the
        // sim thread overwrite drained / deposited depths). Monitor is re-entrant, so callers that already hold it (DrainLocal / RefillTick) are fine.
        // A Mutex is still handled in case a future game version changes the field type.
        sealed class Held : System.IDisposable
        {
            readonly object _o; bool _taken;
            public Held(object o)
            {
                _o = o;
                if (_o == null) return;
                var mx = _o as System.Threading.Mutex;
                if (mx != null) { mx.WaitOne(); _taken = true; }
                else System.Threading.Monitor.Enter(_o, ref _taken);
            }
            public void Dispose()
            {
                if (!_taken) return;
                var mx = _o as System.Threading.Mutex;
                if (mx != null) mx.ReleaseMutex(); else System.Threading.Monitor.Exit(_o);
            }
        }
        static Held Hold(LiquidVolume v) { return new Held(FLock.GetValue(v)); }

        // Every ZDO.Set on the pit's ZDO bumps its DataRevision, and LiquidVolume.Update -> CheckLoad() reloads the depths from the LAST SAVED data
        // whenever m_lastDataRevision differs. Our own bookkeeping writes (baseline / refill time) therefore threw every drain away. Tell the pit that
        // the revision is ours; the changed depths are written out by its normal CheckSave (m_needsSaving).
        static readonly FieldInfo FLastRev = AccessTools.Field(typeof(LiquidVolume), "m_lastDataRevision");
        static void SyncRevision(LiquidVolume v)
        {
            var nv = Nview(v);
            if (FLastRev != null && nv != null && nv.IsValid()) FLastRev.SetValue(v, nv.GetZDO().DataRevision);
        }

        public static bool IsTar(LiquidVolume v) { return v != null && v.m_liquidType == LiquidType.Tar; }
        /// A volume this mod handles: vanilla tar pits and tar pools, and water pools made from pumped water (same height-field simulation, other LiquidType).
        public static bool IsLiquid(LiquidVolume v) { return v != null && (v.m_liquidType == LiquidType.Tar || v.m_liquidType == LiquidType.Water); }
        public static PumpLiquid LiquidOf(LiquidVolume v) { return v != null && v.m_liquidType == LiquidType.Water ? PumpLiquid.Water : PumpLiquid.Tar; }
        static List<float> Depths(LiquidVolume v) { return (List<float>)FDepths.GetValue(v); }
        static List<float> Heights(LiquidVolume v) { return (List<float>)FHeights.GetValue(v); }
        public static ZNetView Nview(LiquidVolume v) { return FNview.GetValue(v) as ZNetView; }

        // --- geometry (mirrors LiquidVolume.WorldToLocal) ---------------------------------------------------------
        static Vector2 ToGrid(LiquidVolume v, Vector3 w)
        {
            var o = v.transform.position;
            float half = v.m_width * v.m_scale * -0.5f;
            return new Vector2((w.x - (o.x + half)) / v.m_scale, (w.z - (o.z + half)) / v.m_scale);
        }

        static bool InGrid(LiquidVolume v, Vector2 g) { return g.x >= 0f && g.y >= 0f && g.x <= v.m_width && g.y <= v.m_width; }

        static float DepthAt(LiquidVolume v, List<float> d, float gx, float gy)
        {
            int stride = v.m_width + 1;
            gx = Mathf.Clamp(gx, 0f, v.m_width); gy = Mathf.Clamp(gy, 0f, v.m_width);
            int x = Mathf.Min((int)gx, v.m_width - 1), y = Mathf.Min((int)gy, v.m_width - 1);
            float fx = gx - x, fy = gy - y;
            float a = Mathf.Lerp(d[y * stride + x], d[y * stride + x + 1], fx);
            float b = Mathf.Lerp(d[(y + 1) * stride + x], d[(y + 1) * stride + x + 1], fx);
            return Mathf.Lerp(a, b, fy);
        }

        /// Finds the pit under world point p. depth = tar depth there, surfaceY = world height of the tar surface.
        public static bool TrySample(Vector3 p, out LiquidVolume vol, out float depth, out float surfaceY, LiquidType type = LiquidType.Tar)
        {
            vol = null; depth = 0f; surfaceY = 0f;
            foreach (var v in All)
            {
                if (v == null || (type != LiquidType.All && v.m_liquidType != type)) continue;      // only volumes of the asked liquid: a tar strainer never sees a water pool
                var g = ToGrid(v, p);
                if (!InGrid(v, g)) continue;
                float dep;
                using (Hold(v))
                {
                    var d = Depths(v);
                    if (d == null || d.Count < (v.m_width + 1) * (v.m_width + 1)) continue;
                    dep = DepthAt(v, d, g.x, g.y);
                }
                if (vol == null || dep > depth) { vol = v; depth = dep; surfaceY = v.GetSurface(p); }
            }
            return vol != null;
        }

        /// Like TrySample, but also looks around p (two rings of 8 points up to `radius` away) and returns the deepest tar
        /// it finds, so the strainer does not have to be dipped in exactly the right spot. `at` = the sampled point.
        public static bool TrySampleNear(Vector3 p, float radius, out LiquidVolume vol, out float depth, out float surfaceY, out Vector3 at, LiquidType type = LiquidType.Tar)
        {
            TrySample(p, out vol, out depth, out surfaceY, type); at = p;
            if (radius < 0.01f) return vol != null;
            for (int ring = 1; ring <= 2; ring++)
                for (int i = 0; i < 8; i++)
                {
                    float ang = i * Mathf.PI * 0.25f, r = radius * ring * 0.5f;
                    var q = p + new Vector3(Mathf.Cos(ang) * r, 0f, Mathf.Sin(ang) * r);
                    LiquidVolume v2; float d2, s2;
                    if (TrySample(q, out v2, out d2, out s2, type) && (vol == null || d2 > depth + 0.001f)) { vol = v2; depth = d2; surfaceY = s2; at = q; }
                }
            return vol != null;
        }

        static readonly FieldInfo FInWater = AccessTools.Field(typeof(LiquidSurface), "m_inWater");

        /// true when this volume's LiquidSurface(s) currently track `who` (a Character) as being inside it
        public static bool Tracks(LiquidVolume v, object who)
        {
            if (v == null || FInWater == null) return false;
            foreach (var ls in v.GetComponentsInChildren<LiquidSurface>(true))
            {
                var list = FInWater.GetValue(ls) as System.Collections.IEnumerable;
                if (list == null) continue;
                foreach (var o in list) if (ReferenceEquals(o, who)) return true;
            }
            return false;
        }

        // --- stale liquid tracking ------------------------------------------------------------------------------------------
        // A LiquidSurface keeps every character / floating item it saw enter in m_inWater and drops it only from OnTriggerExit. A tar volume rebuilds (and
        // empties) its trigger mesh while it is pumped, and Unity sends no exit for a collider whose shape was replaced: the entry stays, the player walks
        // 18-20 m away, UpdateFloaters keeps running for him every physics step and his liquid counter never drops back to 0.
        // Entries outside the grid (+ margin) are removed here with the game's own exit (LiquidSurface.OnTriggerExit, read from its IL):
        // Decrement -> if the count is 0, SetLiquidLevel(-10000) -> remove from m_inWater.
        static readonly FieldInfo FLiquid = AccessTools.Field(typeof(LiquidSurface), "m_liquid");
        const float StaleMargin = 1.5f;      // m outside the grid before a tracked object counts as gone

        static bool NearGrid(LiquidVolume v, Vector3 p, float margin)
        {
            var g = ToGrid(v, p); float m = margin / v.m_scale;
            return g.x >= -m && g.y >= -m && g.x <= v.m_width + m && g.y <= v.m_width + m;
        }

        internal static void PurgeStale(LiquidSurface ls)
        {
            if (ls == null || FInWater == null || FLiquid == null) return;
            var list = FInWater.GetValue(ls) as List<IWaterInteractable>;
            if (list == null || list.Count == 0) return;
            var v = FLiquid.GetValue(ls) as LiquidVolume;
            if (!IsLiquid(v)) return;
            int purged = 0;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var w = list[i];
                var c = w as Component;
                if (c == null) continue;                                     // null / destroyed: the game's own loop drops it
                if (NearGrid(v, c.transform.position, StaleMargin)) continue;
                if (w.Decrement(v.m_liquidType) <= 0) w.SetLiquidLevel(-10000f, v.m_liquidType, ls);
                list.RemoveAt(i);
                purged++;
            }
            if (purged > 0 && ModSettings.DebugLog.Value) ModLog.Info("Pit " + v.name + ": dropped " + purged + " stale tracked object(s) outside its grid");
        }

        /// true when this tar surface already tracks the object behind `col` (what the game's OnTriggerEnter would find by attachedRigidbody -> IWaterInteractable).
        internal static bool AlreadyTracked(LiquidSurface ls, Collider col)
        {
            if (ls == null || col == null || FInWater == null || FLiquid == null) return false;
            if (!IsLiquid(FLiquid.GetValue(ls) as LiquidVolume)) return false;
            var rb = col.attachedRigidbody;
            var w = rb != null ? rb.GetComponent<IWaterInteractable>() : null;
            if (w == null) return false;
            var list = FInWater.GetValue(ls) as List<IWaterInteractable>;
            return list != null && list.Contains(w);
        }

        /// Debug text (dtp pits): what this volume says about world point p - inside its grid?, tar depth there, tar surface height, kind, owner.
        public static string DescribeAt(LiquidVolume v, Vector3 p)
        {
            var g = ToGrid(v, p);
            var nv = Nview(v); var zdo = nv != null && nv.IsValid() ? nv.GetZDO() : null;
            string s = (zdo != null && zdo.GetBool(TarPool.ZPool, false) ? "[pump pool] " : "[vanilla] ") + "grid " + v.m_width + "x" + v.m_width + "@" + v.m_scale + "m, owner=" + (nv != null && nv.IsValid() && nv.IsOwner());
            if (!InGrid(v, g)) return s + ", tracks you: " + Tracks(v, Player.m_localPlayer) + ", you are outside its grid";
            float dep; using (Hold(v)) { var d = Depths(v); dep = d != null && d.Count >= (v.m_width + 1) * (v.m_width + 1) ? DepthAt(v, d, g.x, g.y) : -1f; }
            return s + ", tracks you: " + Tracks(v, Player.m_localPlayer) + ", under you: depth " + dep.ToString("0.00") + " m, surface y " + v.GetSurface(p).ToString("0.00") + " (you y " + p.y.ToString("0.00") + ")";
        }

        public static float TotalVolume(LiquidVolume v)
        {
            float sum = 0f;
            using (Hold(v)) { var d = Depths(v); for (int i = 0; i < d.Count; i++) sum += d[i]; }
            return sum * v.m_scale * v.m_scale;
        }

        /// Tar (m3) one stroke could pull from within radius of world point c (the pull is weighted, see Weight).
        public static float Available(LiquidVolume v, Vector3 c, float radius)
        {
            float sum = 0f;
            ForCells(v, c, radius, (i, depth, w) => sum += depth * w);
            return sum * v.m_scale * v.m_scale;
        }

        // Suction strength at a cell: 1 at the strainer, 0.2 at the edge of the radius (t = distance / radius, squared) - the pit
        // gets a funnel around the strainer instead of a flat-bottomed hole.
        static float Weight(float distSqOverRadiusSq)
        {
            float t = Mathf.Clamp01(1f - distSqOverRadiusSq);
            return 0.2f + 0.8f * t * t;
        }

        static void ForCells(LiquidVolume v, Vector3 c, float radius, System.Action<int, float, float> f)
        {
            using (Hold(v)) ForCellsLocked(v, c, radius, f);
        }

        static void ForCellsLocked(LiquidVolume v, Vector3 c, float radius, System.Action<int, float, float> f)
        {
            var d = Depths(v); int stride = v.m_width + 1;
            var g = ToGrid(v, c);
            float r = radius / v.m_scale;
            int x0 = Mathf.Max(1, Mathf.FloorToInt(g.x - r)), x1 = Mathf.Min(v.m_width - 1, Mathf.CeilToInt(g.x + r));
            int y0 = Mathf.Max(1, Mathf.FloorToInt(g.y - r)), y1 = Mathf.Min(v.m_width - 1, Mathf.CeilToInt(g.y + r));
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float dx = x - g.x, dy = y - g.y;
                    float q = (dx * dx + dy * dy) / (r * r);
                    if (q > 1f) continue;
                    int i = y * stride + x;
                    if (d[i] > 0f) f(i, d[i], Weight(q));
                }
        }

        // --- draining -----------------------------------------------------------------------------------------------
        /// Asks the pit's owner to remove tar (single player routes to itself). The owner answers with RpcDrained so the
        /// requesting client - the one standing at the pump - shows exactly what was removed pouring out at the nozzle.
        public static void RequestDrain(LiquidVolume v, Vector3 center, float radius, float volume, Vector3 nozzleTip, ZDOID pumpId)
        {
            var nv = Nview(v);
            if (nv == null || !nv.IsValid()) return;
            var pkg = new ZPackage();
            pkg.Write(center); pkg.Write(radius); pkg.Write(volume); pkg.Write(nozzleTip); pkg.Write(pumpId);
            nv.InvokeRPC(RpcDrain, pkg);
        }

        // runs on the pit's owner. The request comes from another machine, so it is checked: sane numbers, capped at what one
        // stroke can take, centre inside this pit, the sender near it (the chain is at most MaxChainPieces * ~2.5 m long) and
        // not faster than a pump can be worked.
        static readonly Dictionary<long, float> LastDrainAt = new();
        static void OnDrainRequest(LiquidVolume v, long sender, ZPackage pkg)
        {
            var center = pkg.ReadVector3(); float radius = pkg.ReadSingle(), volume = pkg.ReadSingle();
            var nozzle = pkg.ReadVector3(); var pumpId = pkg.ReadZDOID();
            if (float.IsNaN(radius) || float.IsNaN(volume) || float.IsInfinity(radius) || float.IsInfinity(volume) || volume <= 0f || radius <= 0f) return;
            volume = Mathf.Min(volume, ModSettings.VolumePerStroke.Value * 1.01f + 0.001f);
            radius = Mathf.Min(radius, ModSettings.DrainRadius.Value + 0.01f);
            if (!InGrid(v, ToGrid(v, center))) return;
            bool local = ZDOMan.instance != null && sender == ZDOMan.GetSessionID();
            if (!local)
            {
                var peer = ZNet.instance != null ? ZNet.instance.GetPeer(sender) : null;
                float reach = ModSettings.MaxChainPieces.Value * 2.5f + 15f;
                if (peer == null || (peer.m_refPos - center).sqrMagnitude > reach * reach) return;
            }
            float last; float now = Time.unscaledTime;
            if (LastDrainAt.TryGetValue(sender, out last) && now - last < 0.2f) return;
            LastDrainAt[sender] = now;

            float removed = DrainLocal(v, center, radius, volume);
            if (removed <= 0f) return;
            var reply = new ZPackage();
            reply.Write(removed); reply.Write(nozzle); reply.Write(pumpId);
            Nview(v).InvokeRPC(sender, RpcDrained, reply);
        }

        // runs on the client that pumped; `v` is this machine's copy of the pit that answered, which says what came out (tar or water)
        static void OnDrained(LiquidVolume v, long sender, ZPackage pkg)
        {
            float removed = pkg.ReadSingle(); var nozzle = pkg.ReadVector3(); var pumpId = pkg.ReadZDOID();
            var go = ZNetScene.instance != null ? ZNetScene.instance.FindInstance(pumpId) : null;
            DrainService.Poured(go != null ? go.GetComponent<ZNetView>() : null, nozzle, removed, LiquidOf(v));
        }

        static long NowMs() { return ZNet.instance != null ? (long)(ZNet.instance.GetTimeSeconds() * 1000.0) : 0L; }

        /// Runs on the pit's owner. Returns the volume actually removed.
        public static float DrainLocal(LiquidVolume v, Vector3 center, float radius, float volume)
        {
            var nv = Nview(v);
            if (nv == null || !nv.IsValid() || !nv.IsOwner()) return 0f;
            var d = Depths(v);
            float removed = 0f;
            using (Hold(v))
            {
                float scale2 = v.m_scale * v.m_scale;
                float availableDepthSum = 0f;          // weighted: what a full-strength pull (fraction 1) would take
                ForCells(v, center, radius, (i, depth, w) => availableDepthSum += depth * w);
                if (availableDepthSum <= 0f) return 0f;

                // remember the full volume the first time, so a refill knows where to stop
                var zdo = nv.GetZDO();
                if (zdo != null)
                {
                    if (zdo.GetFloat(VarBaseline, 0f) <= 0f) zdo.Set(VarBaseline, TotalVolume(v));
                    zdo.Set(VarRefillTime, NowMs()); SyncRevision(v);       // refill counts from the last drain
                }

                float wantDepthSum = Mathf.Min(availableDepthSum, volume / scale2);
                float fraction = wantDepthSum / availableDepthSum;     // 0..1; every wet cell in range gives depth * weight * fraction
                ForCells(v, center, radius, (i, depth, w) => { float take = depth * w * fraction; d[i] = depth - take; removed += take; });
                FDirty.SetValue(v, true);
                FNeedsSaving.SetValue(v, true);
                removed *= scale2;
            }
            if (removed > 0f) { BoostFlow(v); WakeItems(center, radius); }
            ModLog.Verbose("Pit " + v.name + ": drained " + removed.ToString("0.00") + " m3");
            return removed;
        }

        // --- depositing (the pumped tar arrives) ----------------------------------------------------------------------------
        // The pumped tar is poured into a real vanilla tar LiquidVolume (see TarPool): either an existing one whose grid contains the
        // landing point, or a new one spawned there. The owner of that volume raises the liquid in the BASIN around the landing point
        // (steepest descent to the lowest reachable cell, then a priority flood: the level rises over the connected low cells until the
        // volume is used up; whatever would go over the rim of the grid runs off). The game's own simulation then spreads it, builds the
        // mesh and the liquid surface (players are slowed like in a vanilla pit), and it is saved with the volume.
        static readonly FieldInfo FHaveHeights = AccessTools.Field(typeof(LiquidVolume), "m_haveHeights");
        static readonly FieldInfo FMaxDepth = AccessTools.Field(typeof(LiquidVolume), "m_maxDepth");
        static readonly Dictionary<LiquidVolume, List<Dep>> PendingDeposits = new();     // waits for the ground heights
        struct Dep { public Vector3 At; public float Volume; public int Hop; }
        static readonly Dictionary<long, float> LastDepositAt = new();

        /// Flags one of our own pools in its ZDO (no self-refill) without making the volume reload its (empty) saved data.
        public static void MarkPool(LiquidVolume v)
        {
            var nv = Nview(v);
            if (nv == null || !nv.IsValid()) return;
            nv.GetZDO().Set(TarPool.ZPool, true);
            SyncRevision(v);
        }

        public static bool Contains(LiquidVolume v, Vector3 p, float margin)
        {
            var g = ToGrid(v, p); float m = margin / v.m_scale;
            return g.x >= m && g.y >= m && g.x <= v.m_width - m && g.y <= v.m_width - m;
        }

        /// `hop` = how many pools in a row the liquid has already run on through (0 = poured by a player, see TarPool.Continue).
        public static void RequestDeposit(LiquidVolume v, Vector3 at, float volume, int hop = 0)
        {
            var nv = Nview(v);
            if (nv == null || !nv.IsValid()) return;
            var pkg = new ZPackage();
            pkg.Write(at); pkg.Write(volume); pkg.Write(hop);
            nv.InvokeRPC(RpcDeposit, pkg);
        }

        // runs on the owner of the volume; the request comes from another machine, so it is checked like a drain request
        static void OnDepositRequest(LiquidVolume v, long sender, ZPackage pkg)
        {
            var at = pkg.ReadVector3(); float volume = pkg.ReadSingle(); int hop = pkg.ReadInt();
            if (float.IsNaN(volume) || float.IsInfinity(volume) || volume <= 0f) return;
            if (hop < 0 || hop > TarPool.MaxRunoffHops + 1) return;
            if (float.IsNaN(at.x) || float.IsNaN(at.y) || float.IsNaN(at.z)) return;
            volume = Mathf.Min(volume, ModSettings.VolumePerStroke.Value * 1.01f + 0.001f);
            if (!InGrid(v, ToGrid(v, at))) return;
            bool local = ZDOMan.instance != null && sender == ZDOMan.GetSessionID();
            if (!local)
            {
                var peer = ZNet.instance != null ? ZNet.instance.GetPeer(sender) : null;
                float reach = ModSettings.MaxChainPieces.Value * 2.5f + 25f;
                if (peer == null || (peer.m_refPos - at).sqrMagnitude > reach * reach) return;
            }
            // a pool that runs over into the next one sends its request in the same moment as the player's (and chains of them in one frame): only the player's own pours are rate limited
            if (hop == 0)
            {
                float last; float now = Time.unscaledTime;
                if (LastDepositAt.TryGetValue(sender, out last) && now - last < 0.2f) return;
                LastDepositAt[sender] = now;
            }
            DepositLocal(v, at, volume, hop);
        }

        static bool HeightsReady(LiquidVolume v) { return FHaveHeights == null || (bool)FHaveHeights.GetValue(v); }

        /// Runs on the owner. Raises the liquid in the basin around `at` by `volume` m3. Queued until the volume knows the ground heights (a freshly spawned one).
        /// What reaches the edge of the grid while the ground goes on downhill continues into the next pool (TarPool.Continue); `hop` counts those pools.
        public static void DepositLocal(LiquidVolume v, Vector3 at, float volume, int hop = 0)
        {
            var nv = Nview(v);
            if (v == null || nv == null || !nv.IsValid() || !nv.IsOwner() || volume <= 0f) return;
            if (!HeightsReady(v))
            {
                List<Dep> q;
                if (!PendingDeposits.TryGetValue(v, out q)) PendingDeposits[v] = q = new List<Dep>();
                q.Add(new Dep { At = at, Volume = volume, Hop = hop });
                return;
            }
            float added, runoff; Vector3 runoffAt;
            _dropThin = 0f;
            using (Hold(v)) added = FillBasin(v, at, volume, out runoff, out runoffAt);
            float dropped = _dropThin * v.m_scale * v.m_scale;
            float lost = Mathf.Max(0f, volume - added - runoff - dropped);       // what is in none of the other numbers: no basin / no reachable cell
            var bg = BudgetOf(v);
            bg.Poured += volume; bg.Stayed += added; bg.Edge += runoff; bg.Thin += dropped; bg.Lost += lost;
            if (added > 0f) { FDirty.SetValue(v, true); FNeedsSaving.SetValue(v, true); BoostFlow(v); }     // the freshly poured liquid spreads out quickly
            ModLog.Verbose("Pit " + v.name + ": " + added.ToString("0.00") + " of " + volume.ToString("0.00") + " m3 stayed in the basin" + (runoff > 0.01f ? ", " + runoff.ToString("0.00") + " m3 reached the edge of the grid" : " (the rest ran off)"));
            LastDeposit = Time.time.ToString("0") + "s: " + added.ToString("0.00") + " of " + volume.ToString("0.00") + " m3 stayed, " + runoff.ToString("0.00") + " m3 reached the grid edge, " + dropped.ToString("0.00") + " m3 dropped as a thin film behind a rim, " + lost.ToString("0.00") + " m3 found no basin (hop " + hop + ")";
            LastDiag = _diag;
            if (ModSettings.DebugLog.Value) ModLog.Info("Pit " + v.name + " deposit: " + LastDeposit + " | " + _diag);
            if (runoff > 0.05f) TarPool.Continue(v, runoffAt, runoff, hop + 1);
        }

        internal static void FlushDeposits(LiquidVolume v)
        {
            List<Dep> q;
            if (PendingDeposits.Count == 0 || !PendingDeposits.TryGetValue(v, out q) || !HeightsReady(v)) return;
            var nv = Nview(v);
            if (nv == null || !nv.IsValid() || !nv.IsOwner()) return;
            PendingDeposits.Remove(v);
            foreach (var p in q) DepositLocal(v, p.At, p.Volume, p.Hop);
        }

        // Called with the pit's mutex held. Returns the volume (m3) that really stayed in the basin. runoff = volume (m3) that left through the border of the
        // grid with the ground still going down there (a moat / ditch that goes on past the edge), runoffAt = that border cell in the world.
        static float FillBasin(LiquidVolume v, Vector3 at, float volume, out float runoff, out Vector3 runoffAt)
        {
            runoff = 0f; runoffAt = at;
            var d = Depths(v); var h = Heights(v);
            int w = v.m_width, stride = w + 1, n = stride * stride;
            if (d == null || h == null || d.Count < n || h.Count < n) return 0f;
            float cell2 = v.m_scale * v.m_scale;
            float maxDepth = FMaxDepth != null ? (float)FMaxDepth.GetValue(v) : 4f;
            // pump pool: a cell at the floor value (-m_maxDepth) is ground DEEPER than the grid's rays reach (FixMissedHeights set it), not a solid column - tar may fill it.
            // vanilla pit: -m_maxDepth means a ray that started inside the ground / hit nothing, treated as solid.
            float buried = IsPumpPool(v) ? float.NegativeInfinity : -maxDepth + 0.03f;
            var g = ToGrid(v, at);
            int cur = Mathf.Clamp(Mathf.RoundToInt(g.y), 1, w - 1) * stride + Mathf.Clamp(Mathf.RoundToInt(g.x), 1, w - 1);
            if (h[cur] <= buried)
            {
                int best = -1, bd = int.MaxValue, cx0 = cur % stride, cy0 = cur / stride;
                for (int dy = -4; dy <= 4; dy++)
                    for (int dx = -4; dx <= 4; dx++)
                    {
                        int x = cx0 + dx, y = cy0 + dy;
                        if (x < 1 || y < 1 || x > w - 1 || y > w - 1) continue;
                        int i = y * stride + x;
                        if (h[i] > buried && dx * dx + dy * dy < bd) { bd = dx * dx + dy * dy; best = i; }
                    }
                if (best < 0) return 0f;
                cur = best;
            }

            // Basin after basin: the first pass fills the basin at the landing point. When the level reaches a point of its rim that is lower than the level
            // (the lowest wall top, a dip in the wall) the rest runs over it and the next pass fills what lies behind it (up to 4 basins in all). Tar that finds
            // no basin runs off.
            float left = volume / cell2, added = 0f, run = 0f; int runCell = -1;
            // where the liquid went, for the editor / the log (LastDiag): the landing cell, then every basin it ran through
            var dOrigin = v.transform.position; float dHalf = w * v.m_scale * -0.5f, dScale = v.m_scale;
            System.Func<int, string> cellPos = c => "(" + (dOrigin.x + dHalf + (c % stride) * dScale).ToString("0.0") + ", " + (dOrigin.y + h[c]).ToString("0.00") + ", " + (dOrigin.z + dHalf + (c / stride) * dScale).ToString("0.0") + ")";
            var diag = new System.Text.StringBuilder();
            diag.Append("landing cell: ground y ").Append((dOrigin.y + h[cur]).ToString("0.00")).Append(", water ").Append(d[cur].ToString("0.00")).Append(" m deep");
            int passes = 0;
            bool overflow = ModSettings.PoolOverflow.Value;
            for (int pass = 0; pass < MaxSpillPasses && left > 1e-5f; pass++)
            {
                int spill, edgeCell, regionCells; float edgeLeft, passLevel;
                added += FloodPass(d, h, w, stride, n, cur, buried, maxDepth, ref left, pass > 0, overflow, out spill, out edgeCell, out edgeLeft, out passLevel, out regionCells);
                passes++;
                if (pass < 6)
                {
                    diag.Append(" | basin ").Append(pass + 1).Append(": ").Append(regionCells).Append(" cells, level y ").Append((dOrigin.y + passLevel).ToString("0.00"));
                    if (spill >= 0) diag.Append(", runs over the rim at ").Append(cellPos(spill));
                    else if (edgeCell >= 0) diag.Append(", leaves the grid at ").Append(cellPos(edgeCell));
                }
                if (edgeCell >= 0) { run = edgeLeft; runCell = edgeCell; }
                if (spill < 0) break;
                cur = spill;
            }
            if (left > 1e-5f) diag.Append(" | ").Append((left * cell2).ToString("0.00")).Append(" m3 left after ").Append(passes).Append(" basins");
            else if (passes > 6) diag.Append(" | ").Append(passes).Append(" basins in all");
            _diag = diag.ToString();
            if (runCell >= 0 && run > 1e-5f)
            {
                var o = v.transform.position; float half = w * v.m_scale * -0.5f;
                runoff = run * cell2;
                runoffAt = new Vector3(o.x + half + (runCell % stride) * v.m_scale, o.y + h[runCell], o.z + half + (runCell / stride) * v.m_scale);
            }
            return added * cell2;
        }

        // --- numbers for the live editor (WaterEditor) --------------------------------------------------------------------
        internal static string LastDiag = "-";          // the path of the last deposit: landing cell and every basin it ran through (owner only)
        static string _diag = "";
        internal static string LastDeposit = "-";      // what the last DepositLocal on this machine did (owner only)

        // Where did the poured liquid go? Counted per pool (owner only, since the world was loaded), shown by the editor:
        // Stayed = raised in the basin, Edge = reached the border of the grid (continues into the next pool or runs off), Thin = a thin film behind a rim that
        // FloodPass does not keep, Lost = in none of the others (no basin found / no reachable cell), ShedEdge = films that ShedFilms let run off the grid border,
        // Evap = films that ShedFilms evaporated. If 'Stayed' is a small part of 'Poured' the water is not staying in the pool, and the other numbers say why.
        internal sealed class Budget { public float Poured, Stayed, Edge, Thin, Lost, ShedEdge, Evap; }
        static readonly Dictionary<LiquidVolume, Budget> Budgets = new();
        static float _dropThin;      // depth sum (cell units) FloodPass dropped as a thin film behind a rim during the current FillBasin

        static Budget BudgetOf(LiquidVolume v)
        {
            Budget b;
            if (!Budgets.TryGetValue(v, out b)) Budgets[v] = b = new Budget();
            return b;
        }

        internal static string BudgetText(LiquidVolume v)
        {
            Budget b;
            if (v == null || !Budgets.TryGetValue(v, out b)) return "nothing poured into this pool on this machine yet";
            return "poured " + b.Poured.ToString("0.0") + " m3 -> stayed in the basin " + b.Stayed.ToString("0.0") + ", reached the grid edge " + b.Edge.ToString("0.0") + ", thin film behind a rim dropped " + b.Thin.ToString("0.0") + ", no basin " + b.Lost.ToString("0.0") + "   |   later: films run off the grid border " + b.ShedEdge.ToString("0.0") + ", evaporated " + b.Evap.ToString("0.0");
        }

        /// Live tuning (editor): sets the base viscosity (the pool's own flow value without the pouring boost). A boost that is running keeps its factor.
        internal static void SetBaseViscosity(LiquidVolume v, float value)
        {
            if (v == null || FVisc == null || value <= 0f) return;
            float[] b;
            if (Boosted.TryGetValue(v, out b)) { b[0] = value; FVisc.SetValue(v, value * Mathf.Max(1f, ModSettings.PitFlowBoost.Value)); }
            else FVisc.SetValue(v, value);
        }

        internal struct PoolStats { public int Width; public float Scale, Total, Max; public int Wet, Thin, WallCells; public bool Owner, Pump; }

        internal static bool Stats(LiquidVolume v, out PoolStats s)
        {
            s = default(PoolStats);
            if (v == null) return false;
            var d = Depths(v);
            int w = v.m_width, n = (w + 1) * (w + 1);
            if (d == null || d.Count < n) return false;
            float film = ModSettings.PoolFilmDepth.Value, sum = 0f;
            using (Hold(v))
            {
                for (int i = 0; i < n; i++)
                {
                    float di = d[i];
                    if (di <= 0.001f) continue;
                    s.Wet++; sum += di;
                    if (di > s.Max) s.Max = di;
                    if (di < film) s.Thin++;
                }
            }
            s.Width = w; s.Scale = v.m_scale; s.Total = sum * v.m_scale * v.m_scale;
            var nv = Nview(v);
            s.Owner = nv != null && nv.IsValid() && nv.IsOwner();
            s.Pump = IsPumpPool(v);
            WallMemo m; s.WallCells = Memos.TryGetValue(v, out m) ? m.Cells.Count : 0;
            return true;
        }

        /// The loaded volume of this type whose grid centre is closest (in x / z) to p.
        internal static LiquidVolume Nearest(Vector3 p, LiquidType type)
        {
            LiquidVolume best = null; float bd = float.MaxValue;
            foreach (var v in All)
            {
                if (v == null || v.m_liquidType != type) continue;
                var o = v.transform.position; float dx = o.x - p.x, dz = o.z - p.z, dd = dx * dx + dz * dz;
                if (dd < bd) { bd = dd; best = v; }
            }
            return best;
        }

        /// Console `dtp cleanpools`: removes the empty water volumes that are NOT pump pools. `dtp spawn DTP_WaterPool2` makes such a volume (no walls, never flagged), and
        /// the stroke picked the nearest grid centre, so a stack of them hijacked the pour and cost a height pass (one ray per cell) per second each. Pools the pump made
        /// (flagged) and anything that holds water are never touched. Returns how many were removed.
        internal static int RemoveJunkPools()
        {
            int removed = 0;
            foreach (var v in new List<LiquidVolume>(All))
            {
                try
                {
                    if (v == null || v.m_liquidType != LiquidType.Water || IsPumpPool(v)) continue;
                    var nv = Nview(v);
                    if (nv == null || !nv.IsValid() || ZNetScene.instance == null) continue;
                    var d = Depths(v);
                    if (d == null || d.Count < (v.m_width + 1) * (v.m_width + 1) || TotalVolume(v) > 0.05f) continue;
                    nv.ClaimOwnership();
                    ZNetScene.instance.Destroy(v.gameObject);
                    removed++;
                }
                catch (System.Exception e) { ModLog.VerboseWarn("RemoveJunkPools: " + e.Message); }
            }
            return removed;
        }

        /// Test button: removes all the liquid from a pool (owner only).
        internal static bool DebugEmpty(LiquidVolume v)
        {
            var nv = Nview(v);
            if (v == null || nv == null || !nv.IsValid() || !nv.IsOwner()) return false;
            var d = Depths(v);
            using (Hold(v))
            {
                for (int i = 0; i < d.Count; i++) d[i] = 0f;
                FDirty.SetValue(v, true); FNeedsSaving.SetValue(v, true);
            }
            return true;
        }

        /// Makes every loaded volume of this liquid rebuild its mesh (PoolMesh settings changed). Same flag the drain / deposit code sets after a depth change.
        internal static void MarkMeshDirty(LiquidType type)
        {
            foreach (var v in All)
                if (v != null && v.m_liquidType == type) FDirty.SetValue(v, true);
        }

        const int MaxSpillPasses = 48;          // basins in a row one stroke may run through (was 4: the liquid that was still left after the 4th basin was lost, see CHANGELOG)
        const float MinSpillDepth = 0.04f;      // m: a film thinner than this, behind a rim, is not kept (it runs off)

        // One basin: runs downhill from `start` to the lowest cell, then raises the level over the connected low cells until `remaining` (depth sum in cell units)
        // is used up. Returns the depth sum added. spill >= 0 = the level reached the rim and a cell behind it is lower than the level: the tar runs over it,
        // `remaining` is what still has to go somewhere.
        // `spill` >= 0: the level reached a lower cell behind the rim (see above). edgeCell >= 0: the level reached the border of the grid (4 cm over its lowest
        // cell) and edgeLeft (depth sum) is what leaves the grid there.
        static float FloodPass(List<float> d, List<float> h, int w, int stride, int n, int start, float buried, float maxDepth, ref float remaining, bool isSpill, bool overflow, out int spill, out int edgeCell, out float edgeLeft, out float levelOut, out int regionOut)
        {
            spill = -1; edgeCell = -1; edgeLeft = 0f; levelOut = 0f; regionOut = 0;
            int cur = start;

            // 1) run downhill to the lowest cell around the start point
            for (int step = 0; step < 60; step++)
            {
                int cx = cur % stride, cy = cur / stride, next = cur; float sBest = h[cur] + d[cur];
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int x = cx + dx, y = cy + dy;
                        if (x < 1 || y < 1 || x > w - 1 || y > w - 1) continue;
                        int i = y * stride + x;
                        if (h[i] <= buried) continue;
                        float s = h[i] + d[i];
                        if (s < sBest - 0.0005f) { sBest = s; next = i; }
                    }
                if (next == cur) break;
                cur = next;
            }

            // 2) priority flood: raise the level over the connected low cells until the volume is used up
            var inRegion = new bool[n]; var inFrontier = new bool[n];
            var region = new List<int> { cur }; inRegion[cur] = true;
            var frontier = new List<int>();
            int[] ox = { 1, -1, 0, 0 }, oy = { 0, 0, 1, -1 };
            System.Action<int> addNeighbours = c =>
            {
                int cx = c % stride, cy = c / stride;
                for (int k = 0; k < 4; k++)
                {
                    int x = cx + ox[k], y = cy + oy[k];
                    if (x < 0 || y < 0 || x > w || y > w) continue;
                    int i = y * stride + x;
                    if (inRegion[i] || inFrontier[i] || h[i] <= buried) continue;
                    inFrontier[i] = true; frontier.Add(i);
                }
            };
            addNeighbours(cur);
            float level = h[cur] + d[cur], rem = remaining;
            bool edge = false;
            int edgeF = -1; float edgeFs = float.MaxValue;           // lowest cell of the grid's border zone seen so far = the way out of this basin
            int partial = -1; float partialAmt = 0f;                 // a lower cell behind a lip that could only be filled partly
            while (rem > 1e-5f && (frontier.Count > 0 || edgeF >= 0))
            {
                float cap = edgeF >= 0 ? edgeFs + MinSpillDepth : float.MaxValue;     // the level at which the liquid runs out over the border of the grid
                int fk = -1; float fs = float.MaxValue;
                for (int k = 0; k < frontier.Count; k++) { int f0 = frontier[k]; float s = h[f0] + d[f0]; if (s < fs) { fs = s; fk = k; } }
                float target = Mathf.Min(fs, cap);
                float need = region.Count * Mathf.Max(0f, target - level);
                if (rem < need) { level += rem / region.Count; rem = 0f; break; }
                rem -= need; level = Mathf.Max(level, target);
                if (cap <= fs) { edge = true; break; }                // the level reached the way out: the rest leaves the grid there
                int f = frontier[fk]; frontier[fk] = frontier[frontier.Count - 1]; frontier.RemoveAt(frontier.Count - 1);
                int fx = f % stride, fy = f / stride;
                if (fx <= 1 || fy <= 1 || fx >= w - 1 || fy >= w - 1)  // the border of the grid is not part of the basin; it is where the liquid leaves once it stands 4 cm over it
                {
                    if (fs < edgeFs) { edgeFs = fs; edgeF = f; }
                    continue;
                }
                // a cell lower than the level: the level is at the lowest point of the rim and the liquid runs over it
                float join = Mathf.Max(0f, level - fs);
                if (overflow && join > 0.02f) { spill = f; break; }
                // (overflow off) a deeper basin behind the lip has to be filled up to the level too, and that costs liquid
                if (join > rem) { partial = f; partialAmt = rem; rem = 0f; break; }
                rem -= join;
                region.Add(f); inRegion[f] = true;
                addNeighbours(f);
            }
            if (edge) { if (edgeF >= 0 && rem > 1e-5f) { edgeCell = edgeF; edgeLeft = rem; } rem = 0f; }
            else if (rem > 1e-5f && spill < 0) { level += rem / region.Count; rem = 0f; }   // closed basin (nothing left on the frontier, no way out): the level just rises
            levelOut = level; regionOut = region.Count;
            if (isSpill && level - h[cur] < MinSpillDepth) { _dropThin += remaining; remaining = 0f; spill = -1; return 0f; }     // a thin film behind the rim: not kept, runs off (counted for the editor)

            float added = 0f;
            foreach (int i in region)
            {
                float nd = Mathf.Min(level - h[i], maxDepth * 2f);
                if (nd > d[i]) { added += nd - d[i]; d[i] = nd; }
            }
            if (partial >= 0 && partialAmt > 0f) { d[partial] += partialAmt; added += partialAmt; }
            remaining = rem;
            return added;
        }

        // --- flow boost ---------------------------------------------------------------------------------------------------
        // The pit simulation moves tar between neighbouring cells with min(sqrt(height difference * m_viscocity), difference / 2) per step,
        // so a bigger m_viscocity makes the rest of the pit run towards the hole faster. While a pit is being pumped (and for
        // PitFlowBoostSeconds after) the value is multiplied by [Drain] PitFlowBoost; afterwards the original is put back.
        static readonly FieldInfo FVisc = AccessTools.Field(typeof(LiquidVolume), "m_viscocity");
        static readonly Dictionary<LiquidVolume, float[]> Boosted = new();     // pit -> { original viscosity, boost ends at (Time.time) }

        static void BoostFlow(LiquidVolume v)
        {
            float k = ModSettings.PitFlowBoost.Value, secs = ModSettings.PitFlowBoostSeconds.Value;
            if (FVisc == null || k <= 1.001f || secs <= 0f) return;
            float[] b;
            if (!Boosted.TryGetValue(v, out b))
            {
                float orig = (float)FVisc.GetValue(v);
                if (orig <= 0f) return;
                Boosted[v] = b = new[] { orig, 0f };
                FVisc.SetValue(v, orig * k);
            }
            b[1] = Time.time + secs;
        }

        static void UnboostIfDone(LiquidVolume v, bool force)
        {
            float[] b;
            if (!Boosted.TryGetValue(v, out b) || (!force && Time.time < b[1])) return;
            if (v != null && FVisc != null) FVisc.SetValue(v, b[0]);
            Boosted.Remove(v);
        }

        /// Items (Tar, meat, ...) that lie in the pit are NOT touched by the pump; they stay where they are. They only get
        /// woken up so they settle on the ground as the liquid under them goes away instead of hanging on a sleeping body.
        static void WakeItems(Vector3 center, float radius)
        {
            try
            {
                foreach (var col in Physics.OverlapSphere(center, radius + 1.5f, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (col == null) continue;
                    var item = col.GetComponentInParent<ItemDrop>();
                    var rb = item != null ? item.GetComponent<Rigidbody>() : null;
                    if (rb != null && !rb.isKinematic) rb.WakeUp();
                }
            }
            catch (System.Exception e) { ModLog.VerboseWarn("WakeItems: " + e.Message); }
        }

        // --- floating items --------------------------------------------------------------------------------------------
        // Tar items (and anything else with a Floating component) that lie on the pit are held up by Floating.CustomFixedUpdate, which pushes the body up
        // towards the liquid level it was last given (m_tarLevel). When the liquid goes away (drained, or the pit flows away from under the item) that
        // level can stay stale - and the body is asleep, so it keeps hanging in the air instead of dropping to the ground. WakeItems only wakes items near the
        // strainer, but the whole pit flows. So every second every floating item that is inside a tar volume's grid and hangs above its surface is given the
        // real (low) level and woken.
        static readonly FieldInfo FFloatTar = AccessTools.Field(typeof(Floating), "m_tarLevel");
        static readonly FieldInfo FFloatWater = AccessTools.Field(typeof(Floating), "m_waterLevel");
        static readonly Dictionary<LiquidVolume, float> SettleTimers = new();

        static void SettleFloaters(LiquidVolume v)
        {
            try
            {
                foreach (var mu in Floating.Instances)
                {
                    var f = mu as Floating;      // Instances is a list of IMonoUpdater in current game versions
                    if (f == null) continue;
                    var pos = f.transform.position;
                    if (!InGrid(v, ToGrid(v, pos))) continue;
                    var rb = f.GetComponent<Rigidbody>();
                    if (rb == null || rb.isKinematic) continue;
                    float surf = v.GetSurface(pos);
                    if (pos.y <= surf + 0.3f) continue;                                    // in / on the liquid: Floating handles it
                    var lvl = v.m_liquidType == LiquidType.Water ? FFloatWater : FFloatTar;      // the level the item was last given by this kind of liquid
                    bool stale = lvl != null && (float)lvl.GetValue(f) > surf + 0.02f;
                    if (!stale && !(rb.IsSleeping() && !Physics.Raycast(pos, Vector3.down, 0.35f, ~0, QueryTriggerInteraction.Ignore))) continue;   // lying on something: leave it alone
                    if (stale) lvl.SetValue(f, surf);
                    rb.WakeUp();
                }
            }
            catch (System.Exception e) { ModLog.VerboseWarn("SettleFloaters: " + e.Message); }
        }

        // --- thin films run off fast --------------------------------------------------------------------------------------
        // The game's simulation spreads liquid thinly and slowly: what runs over the rim of a pool (or is pushed off a wall cell by ApplyWalls) lingered as a
        // film on the outside of the walls and on the ground for minutes. Every ShedInterval the owner goes through the cells of its pump pools that hold less than
        // [Drain] PoolFilmDepth of liquid: a film with lower liquid / ground next to it moves ALL its liquid to the lowest neighbour (about one cell per pass, so
        // it visibly slides down), a film that reaches the border of the grid leaves, and a film that lies flat with nothing deeper next to it evaporates.
        // The shore of a pool (a thin cell next to a deeper one at the same level) has no lower neighbour and stays.
        static readonly Dictionary<LiquidVolume, float> ShedTimers = new();
        static bool[] _filmGot = new bool[0];
        const float ShedInterval = 0.15f;       // s between passes
        const float FilmEvaporation = 0.05f;    // m/s for a film that lies flat
        static readonly int[] Nx = { 1, -1, 0, 0, 1, 1, -1, -1 }, Ny = { 0, 0, 1, -1, 1, -1, 1, -1 };

        static void ShedFilms(LiquidVolume v, float dt)
        {
            if (!ModSettings.PoolShedFilm.Value || !IsLiquid(v) || !IsPumpPool(v) || !HeightsReady(v)) return;
            var nv = Nview(v);
            if (nv == null || !nv.IsValid() || !nv.IsOwner()) return;
            var d = Depths(v); var h = Heights(v);
            int w = v.m_width, stride = w + 1, n = stride * stride;
            if (d == null || h == null || d.Count < n || h.Count < n) return;
            float film = ModSettings.PoolFilmDepth.Value;
            if (_filmGot.Length < n) _filmGot = new bool[n];
            bool changed = false;
            float edgeSum = 0f, evapSum = 0f;       // depth sums (cell units) that left the pool in this pass, for the editor's budget
            using (Hold(v))
            {
                System.Array.Clear(_filmGot, 0, n);
                for (int y = 0; y <= w; y++)
                    for (int x = 0; x <= w; x++)
                    {
                        int i = y * stride + x;
                        float di = d[i];
                        if (di <= 0f || di >= film || _filmGot[i]) continue;
                        if (x <= 1 || y <= 1 || x >= w - 1 || y >= w - 1) { edgeSum += di; d[i] = 0f; changed = true; continue; }   // runs off the grid
                        float bs = h[i] + di - 0.004f; int best = -1; bool deeper = false;
                        for (int k = 0; k < 8; k++)
                        {
                            int j = (y + Ny[k]) * stride + (x + Nx[k]);
                            if (d[j] >= film) deeper = true;
                            float sj = h[j] + d[j];
                            if (sj < bs) { bs = sj; best = j; }
                        }
                        if (best >= 0) { d[best] += di; d[i] = 0f; _filmGot[best] = true; changed = true; }
                        else if (!deeper && ModSettings.PoolFilmEvaporates.Value) { float ev = Mathf.Min(di, FilmEvaporation * dt); evapSum += ev; d[i] = di - ev; changed = true; }     // [Drain] PoolFilmEvaporates (default off) - it was bound but never read, so thin pools always evaporated
                    }
                if (changed) FDirty.SetValue(v, true);
            }
            if (edgeSum > 0f || evapSum > 0f)
            {
                var bg = BudgetOf(v); float c2 = v.m_scale * v.m_scale;
                bg.ShedEdge += edgeSum * c2; bg.Evap += evapSum * c2;
            }
            if (changed) FNeedsSaving.SetValue(v, true);
        }

        // --- refill (owner only, config RefillMinutes > 0) --------------------------------------------------------------
        // A drained pit seeps back to its baseline volume linearly over RefillMinutes (of world time, so it also
        // catches up for the time the pit was unloaded). Nothing is written for pits that were never drained.
        static readonly Dictionary<LiquidVolume, float> RefillTimers = new();

        internal static void RefillTick(LiquidVolume v, float dt)
        {
            float minutes = ModSettings.RefillMinutes.Value;
            if (minutes <= 0f) return;
            var nv = Nview(v);
            if (nv == null || !nv.IsValid() || !nv.IsOwner()) return;
            float t; RefillTimers.TryGetValue(v, out t);
            t += dt;
            if (t < 2f) { RefillTimers[v] = t; return; }
            RefillTimers[v] = 0f;

            var zdo = nv.GetZDO();
            if (zdo.GetBool(TarPool.ZPool, false)) return;      // pools made from pumped tar do not seep back on their own (that would create tar)
            float baseline = zdo.GetFloat(VarBaseline, 0f);
            if (baseline <= 0f) return;                         // never drained

            var d = Depths(v);
            using (Hold(v))
            {
                float scale2 = v.m_scale * v.m_scale;
                float depthSum = 0f;
                for (int i = 0; i < d.Count; i++) if (d[i] > 0f) depthSum += d[i];
                float deficit = baseline - depthSum * scale2;
                if (deficit < 0.01f) return;                    // full again

                long now = NowMs(), last = zdo.GetLong(VarRefillTime, 0L);
                zdo.Set(VarRefillTime, now); SyncRevision(v);
                if (last <= 0L || now <= last) return;
                float add = Mathf.Min(deficit, baseline / (minutes * 60f) * ((now - last) / 1000f));
                if (add <= 0f) return;

                if (depthSum > 0f)
                {
                    float k = 1f + add / scale2 / depthSum;            // raise every wet cell proportionally; the sim spreads it
                    for (int i = 0; i < d.Count; i++) if (d[i] > 0f) d[i] *= k;
                }
                else
                {
                    var h = Heights(v); int stride = v.m_width + 1, best = -1; float bh = float.MaxValue;
                    for (int y = 1; y < v.m_width; y++)
                        for (int x = 1; x < v.m_width; x++) if (h[y * stride + x] < bh) { bh = h[y * stride + x]; best = y * stride + x; }
                    if (best >= 0) d[best] += add / scale2;            // fully drained: seep back in at the lowest point
                }
                FDirty.SetValue(v, true);
                FNeedsSaving.SetValue(v, true);
            }
        }

        // --- Harmony hooks ------------------------------------------------------------------------------------------
        // The three RPCs live on the pit's ZNetView. ZNetView.Register throws on a duplicate, and the ZDO may not exist yet when LiquidVolume.Awake
        // runs (component order), so registration is retried from Update until it has happened exactly once per volume.
        static readonly HashSet<LiquidVolume> Registered = new();

        static void EnsureRegistered(LiquidVolume vol)
        {
            if (vol == null || Registered.Contains(vol)) return;
            var nv = Nview(vol);
            if (nv == null || !nv.IsValid() || nv.GetZDO() == null) return;
            Registered.Add(vol);
            nv.Register<ZPackage>(RpcDrain, (sender, pkg) => OnDrainRequest(vol, sender, pkg));
            nv.Register<ZPackage>(RpcDrained, (sender, pkg) => OnDrained(vol, sender, pkg));
            nv.Register<ZPackage>(RpcDeposit, (sender, pkg) => OnDepositRequest(vol, sender, pkg));
            ModLog.Verbose("TarPits: RPCs registered on '" + vol.name + "' @ " + vol.transform.position.ToString("0"));
        }

        // LiquidVolume.GetSurface clamps the point to the grid (GetDepth / GetHeight clamp), so an object that the volume's trigger still tracks while it is
        // OUTSIDE the grid (stale trigger state, a trigger bigger than the grid) got the surface height of the nearest edge cell: standing 30 m from a pump
        // pool the player was 'swimming in tar'. Outside the grid there is no liquid.
        [HarmonyPatch(typeof(LiquidVolume), "GetSurface", new[] { typeof(Vector3) })]
        static class Patch_GetSurface
        {
            static void Postfix(LiquidVolume __instance, Vector3 p, ref float __result)
            {
                if (!IsLiquid(__instance)) return;
                if (!InGrid(__instance, ToGrid(__instance, p))) __result = -10000f;
            }
        }

        // --- stale ground heights: tar floating over a hole that was dug deeper -------------------------------------------------
        // LiquidVolume.UpdateHeights (about once a second) fires one ray per grid cell from m_maxDepth above the volume's origin, m_maxDepth * 2 long,
        // and writes hit.y - origin.y into m_heights. A ray that hits nothing (ground deeper than origin - m_maxDepth, or a ray that starts inside a
        // hillside) leaves the OLD height in place (IL: the write is skipped when RaycastHit.distance == 0). Dig a pump pool deeper than that and those
        // cells keep their old floor: the tar surface (height + depth) stays up there, floating over the new hole, and its liquid trigger still makes
        // you swim in the air. Fix: after UpdateHeights every cell whose ray missed gets the real terrain height; where even that is below the reach
        // of the grid (-m_maxDepth) the cell keeps the floor value -m_maxDepth (FillBasin lets tar fill it); its tar is NOT removed (0.6.7 removed it,
        // which drained the deep part of a deep hole).
        static readonly FieldInfo FHits = AccessTools.Field(typeof(LiquidVolume), "m_raycastHitsArray");

        internal static bool IsPumpPool(LiquidVolume v)
        {
            var nv = Nview(v);
            return nv != null && nv.IsValid() && nv.GetZDO() != null && nv.GetZDO().GetBool(TarPool.ZPool, false);
        }

        static void FixMissedHeights(LiquidVolume v)
        {
            if (!IsLiquid(v) || ZoneSystem.instance == null || FHits == null || FMaxDepth == null || !IsPumpPool(v)) return;
            var hits = FHits.GetValue(v) as RaycastHit[];
            var h = Heights(v); var d = Depths(v);
            int w = v.m_width, stride = w + 1, n = stride * stride;
            if (hits == null || h == null || d == null || hits.Length < n || h.Count < n || d.Count < n) return;
            float maxDepth = (float)FMaxDepth.GetValue(v);
            var o = v.transform.position;
            float half = w * v.m_scale * -0.5f;
            int moved = 0;
            using (Hold(v))
            {
                for (int y = 0; y <= w; y++)
                    for (int x = 0; x <= w; x++)
                    {
                        int i = y * stride + x;
                        if (hits[i].distance > 0f) continue;                       // the ray hit ground: vanilla already wrote the right height
                        float gh = ZoneSystem.instance.GetGroundHeight(new Vector3(o.x + half + x * v.m_scale, 0f, o.z + half + y * v.m_scale)) - o.y;
                        float nh = Mathf.Clamp(gh, -maxDepth, maxDepth);
                        if (Mathf.Abs(nh - h[i]) > 0.01f) { h[i] = nh; moved++; }     // below the reach of the grid this is -m_maxDepth: the tar column stands on that floor, its surface stays right
                    }
                if (moved > 0) { FDirty.SetValue(v, true); FNeedsSaving.SetValue(v, true); }
            }
            if (moved > 0 && ModSettings.DebugLog.Value)
                ModLog.Info("Pit " + v.name + ": " + moved + " stale ground heights corrected");
        }

        // --- walls: built pieces hold the tar like terrain ----------------------------------------------------------------------------
        // LiquidVolume.UpdateHeights measures the ground with one ray per cell using the volume's m_groundLayer (the vanilla pit's mask: the terrain), so a stone
        // wall ring was not ground for the pool: its cells stayed at the terrain height, the tar level could not rise above it and the tar never stood
        // against the walls. For our own pools every height pass is followed by this one: where a built piece stands on the ground (or on another piece) the
        // cell takes the top of the piece (a sphere cast 0.2 m wide, so thin walls between two grid points are not missed). Cheap when nothing is built there:
        // one overlap query per pass. Pieces of this mod (the hoses over the pool), players, items and anything floating above the pool (a roof) do not count.
        const float WallReach = 6f;                                   // m above the pool origin that a wall may reach
        static readonly Collider[] WallCols = new Collider[4096];      // was 512: a base around the pool easily has more colliders than that, the rest was silently ignored = a hole in the wall
        static readonly RaycastHit[] WallHits = new RaycastHit[64];
        static readonly Dictionary<Collider, bool> WallOk = new();
        static bool[] _wallCand = new bool[0];
        static int _wallMask = -1;
        static float _nextWallWarn;

        sealed class HitHeight : IComparer<RaycastHit>
        {
            public int Compare(RaycastHit a, RaycastHit b) { return a.point.y.CompareTo(b.point.y); }
        }
        static readonly HitHeight ByHeight = new();

        // Where the walls are, remembered: when you come back to a pool the volume loads with the zone, but the walls are separate objects that appear
        // frame after frame - for a while the pool has NO walls, its simulation sees plain terrain at the rim and the liquid that stands above the ground
        // runs out over it and spreads thin across the grid ("the level is back at ground height"). So the raised wall cells (cell -> height above the pool
        // origin) are saved in the pool's ZDO and put back at once on load. For WallGraceSeconds after the load they are kept even if no wall collider has
        // been found there yet; after that only what is really there counts (a wall removed while you were away frees its cells again).
        sealed class WallMemo { public Dictionary<int, float> Cells = new(); public float GraceUntil; public int Logged = -1; }
        static readonly Dictionary<LiquidVolume, WallMemo> Memos = new();
        const string ZWalls = "DTP_walls";
        const float WallGraceSeconds = 30f;

        static WallMemo MemoOf(LiquidVolume v)
        {
            WallMemo m;
            if (Memos.TryGetValue(v, out m)) return m;
            m = new WallMemo { GraceUntil = Time.time + WallGraceSeconds };
            Memos[v] = m;
            try
            {
                var nv = Nview(v); var z = nv != null && nv.IsValid() ? nv.GetZDO() : null;
                string s = z != null ? z.GetString(ZWalls, "") : "";
                if (!string.IsNullOrEmpty(s))
                {
                    var pkg = new ZPackage(s);
                    int width = pkg.ReadInt(), cnt = pkg.ReadInt();
                    if (width == v.m_width)
                        for (int k = 0; k < cnt; k++) { int i = pkg.ReadInt(); float y = pkg.ReadSingle(); m.Cells[i] = y; }
                    ModLog.Verbose("Pit " + v.name + ": " + m.Cells.Count + " remembered wall cells restored");
                }
            }
            catch (System.Exception e) { m.Cells.Clear(); ModLog.VerboseWarn("Wall memory not readable: " + e.Message); }
            return m;
        }

        // the owner writes the wall cells to the ZDO when they changed (SyncRevision: the write must not make the pool reload its depths)
        static void UpdateMemo(LiquidVolume v, WallMemo memo, Dictionary<int, float> now)
        {
            bool same = memo.Cells.Count == now.Count;
            if (same)
                foreach (var kv in now)
                {
                    float old;
                    if (!memo.Cells.TryGetValue(kv.Key, out old) || Mathf.Abs(old - kv.Value) > 0.01f) { same = false; break; }
                }
            memo.Cells = now;
            if (same) return;
            var nv = Nview(v);
            if (nv == null || !nv.IsValid() || !nv.IsOwner() || nv.GetZDO() == null) return;
            var pkg = new ZPackage();
            pkg.Write(v.m_width); pkg.Write(now.Count);
            foreach (var kv in now) { pkg.Write(kv.Key); pkg.Write(kv.Value); }
            nv.GetZDO().Set(ZWalls, pkg.GetBase64());
            SyncRevision(v);
        }

        static bool IsWallCollider(Collider c)
        {
            bool ok;
            if (WallOk.TryGetValue(c, out ok)) return ok;
            ok = !c.isTrigger && c.GetComponentInParent<DtpPiece>() == null && c.GetComponentInParent<Character>() == null &&
                 c.GetComponentInParent<ItemDrop>() == null && c.GetComponentInParent<LiquidVolume>() == null;
            WallOk[c] = ok;
            return ok;
        }

        static void ApplyWalls(LiquidVolume v)
        {
            if (!ModSettings.PoolWalls.Value || !IsLiquid(v) || !IsPumpPool(v) || FMaxDepth == null) return;
            var h = Heights(v); var d = Depths(v);
            int w = v.m_width, stride = w + 1, n = stride * stride;
            if (h == null || d == null || h.Count < n || d.Count < n) return;
            if (_wallMask == -1) _wallMask = LayerMask.GetMask("piece", "static_solid", "Default", "Default_small");
            float maxDepth = (float)FMaxDepth.GetValue(v), sc = v.m_scale;
            var o = v.transform.position;
            float x0 = o.x + w * sc * -0.5f, z0 = o.z + w * sc * -0.5f, span = w * sc;
            var centre = new Vector3(o.x, o.y + (WallReach - maxDepth) * 0.5f, o.z);
            var ext = new Vector3(span * 0.5f, (WallReach + maxDepth) * 0.5f, span * 0.5f);
            int cnt = Physics.OverlapBoxNonAlloc(centre, ext, WallCols, Quaternion.identity, _wallMask, QueryTriggerInteraction.Ignore);
            var live = new Dictionary<int, float>();                  // cell -> wall height (above the pool origin) found by the casts
            if (cnt >= WallCols.Length && Time.time >= _nextWallWarn) { _nextWallWarn = Time.time + 60f; ModLog.Warn("Pit " + v.name + ": more than " + WallCols.Length + " colliders around the pool - some walls may not be seen"); }
            if (cnt > 0) FindWalls(h, w, stride, n, cnt, o, sc, maxDepth, x0, z0, live);
            ApplyWallHeights(v, h, d, w, stride, n, live);
        }

        static void FindWalls(List<float> h, int w, int stride, int n, int cnt, Vector3 o, float sc, float maxDepth, float x0, float z0, Dictionary<int, float> live)
        {
            WallOk.Clear();
            if (_wallCand.Length < n) _wallCand = new bool[n]; else System.Array.Clear(_wallCand, 0, _wallCand.Length);
            bool any = false;
            for (int k = 0; k < cnt; k++)
            {
                var c = WallCols[k];
                if (c == null || !IsWallCollider(c)) continue;
                var b = c.bounds;
                int ax = Mathf.Max(0, Mathf.FloorToInt((b.min.x - x0) / sc) - 1), bx = Mathf.Min(w, Mathf.CeilToInt((b.max.x - x0) / sc) + 1);
                int az = Mathf.Max(0, Mathf.FloorToInt((b.min.z - z0) / sc) - 1), bz = Mathf.Min(w, Mathf.CeilToInt((b.max.z - z0) / sc) + 1);
                for (int z = az; z <= bz; z++)
                    for (int x = ax; x <= bx; x++) { _wallCand[z * stride + x] = true; any = true; }
            }
            if (!any) return;

            float r = Mathf.Min(0.2f, sc * 0.6f), startY = o.y + WallReach;
            {
                for (int i = 0; i < n; i++)
                {
                    if (!_wallCand[i]) continue;
                    var p = new Vector3(x0 + (i % stride) * sc, startY, z0 + (i / stride) * sc);
                    int hc = Physics.SphereCastNonAlloc(p, r, Vector3.down, WallHits, WallReach + maxDepth, _wallMask, QueryTriggerInteraction.Ignore);
                    if (hc <= 0) continue;
                    System.Array.Sort(WallHits, 0, hc, ByHeight);                        // lowest first: a piece counts when it stands on the ground / on a piece below
                    float support = o.y + h[i], top = float.NegativeInfinity;
                    for (int j = 0; j < hc; j++)
                    {
                        var col = WallHits[j].collider;
                        if (col == null || !IsWallCollider(col)) continue;
                        if (col.bounds.min.y > support + 0.6f) continue;                 // floats above (a roof): not a wall
                        float y = WallHits[j].point.y;
                        if (y > support) { support = y; top = y; }
                    }
                    if (float.IsNegativeInfinity(top)) continue;
                    float nh = Mathf.Clamp(top - o.y, -maxDepth, maxDepth);
                    if (nh <= h[i] + 0.02f) continue;
                    live[i] = nh;
                }
            }
        }

        // Raises the cells of the walls (found now + remembered ones while the grace time runs) in the ground heights and saves the set.
        static void ApplyWallHeights(LiquidVolume v, List<float> h, List<float> d, int w, int stride, int n, Dictionary<int, float> live)
        {
            var memo = MemoOf(v);
            var eff = live;
            if (Time.time < memo.GraceUntil)
                foreach (var kv in memo.Cells)
                {
                    float c;
                    if (!eff.TryGetValue(kv.Key, out c) || kv.Value > c) eff[kv.Key] = kv.Value;
                }
            UpdateMemo(v, memo, eff);
            List<int> walls = null;
            using (Hold(v))
            {
                foreach (var kv in eff)
                {
                    int i = kv.Key;
                    if (i < 0 || i >= n || kv.Value <= h[i] + 0.02f) continue;
                    h[i] = kv.Value;
                    (walls ??= new List<int>()).Add(i);
                }
                if (walls == null) return;

                // tar that stood where a wall is now is pushed to the open cells next to it
                bool moved = false;
                var isWall = new bool[n];
                foreach (int i in walls) isWall[i] = true;
                int[] ox = { 1, -1, 0, 0 }, oy = { 0, 0, 1, -1 };
                foreach (int i in walls)
                {
                    if (d[i] <= 0f) continue;
                    int cx = i % stride, cy = i / stride, free = 0;
                    for (int k = 0; k < 4; k++)
                    {
                        int x = cx + ox[k], y = cy + oy[k];
                        if (x >= 0 && y >= 0 && x <= w && y <= w && !isWall[y * stride + x]) free++;
                    }
                    if (free > 0)
                    {
                        float share = d[i] / free;
                        for (int k = 0; k < 4; k++)
                        {
                            int x = cx + ox[k], y = cy + oy[k];
                            if (x >= 0 && y >= 0 && x <= w && y <= w && !isWall[y * stride + x]) d[y * stride + x] += share;
                        }
                    }
                    d[i] = 0f; moved = true;
                }
                FDirty.SetValue(v, true);
                if (moved) FNeedsSaving.SetValue(v, true);
            }
            if (memo.Logged != walls.Count) { memo.Logged = walls.Count; ModLog.Verbose("Pit " + v.name + ": " + walls.Count + " cells raised by built pieces"); }       // only when the number changes (it was one line per pool per second)
        }

        [HarmonyPatch(typeof(LiquidVolume), "UpdateHeights")]
        static class Patch_UpdateHeights
        {
            // The game rewrites every ground height here (walls drop back to terrain) while the pool's worker thread keeps stepping the simulation. Between that
            // write and our wall pass one step could run over the rim and carry a lot of liquid out through every wall cell (a step moves up to half the height
            // difference). The pool's lock is held from before the game's write until the wall pass is done (Monitor, same thread: prefix and finalizer).
            static void Prefix(LiquidVolume __instance, out object __state)
            {
                __state = null;
                try { if (ModSettings.PoolWalls.Value && IsLiquid(__instance) && IsPumpPool(__instance)) __state = Hold(__instance); }
                catch (System.Exception e) { ModLog.VerboseWarn("UpdateHeights lock: " + e.Message); }
            }

            static void Finalizer(object __state)
            {
                var held = __state as System.IDisposable;
                if (held != null) held.Dispose();
            }

            static void Postfix(LiquidVolume __instance)
            {
                try { FixMissedHeights(__instance); }
                catch (System.Exception e) { ModLog.VerboseWarn("FixMissedHeights: " + e.Message); }
                try { ApplyWalls(__instance); }
                catch (System.Exception e) { ModLog.VerboseWarn("ApplyWalls: " + e.Message); }
            }
        }

        [HarmonyPatch(typeof(LiquidVolume), "Awake")]
        static class Patch_Awake
        {
            static void Postfix(LiquidVolume __instance)
            {
                if (!IsLiquid(__instance)) return;
                if (!All.Contains(__instance)) All.Add(__instance);
                EnsureRegistered(__instance);
            }
        }

        [HarmonyPatch(typeof(LiquidVolume), "OnDestroy")]
        static class Patch_OnDestroy
        {
            static void Prefix(LiquidVolume __instance) { All.Remove(__instance); Registered.Remove(__instance); RefillTimers.Remove(__instance); Memos.Remove(__instance); PendingDeposits.Remove(__instance); SettleTimers.Remove(__instance); ShedTimers.Remove(__instance); Budgets.Remove(__instance); UnboostIfDone(__instance, true); }
        }

        [HarmonyPatch(typeof(LiquidVolume), "Update")]
        static class Patch_Update
        {
            static void Postfix(LiquidVolume __instance)
            {
                if (!IsLiquid(__instance)) return;
                EnsureRegistered(__instance);
                if (Boosted.Count > 0) UnboostIfDone(__instance, false);
                if (PendingDeposits.Count > 0) FlushDeposits(__instance);
                RefillTick(__instance, Time.deltaTime);
                float next; SettleTimers.TryGetValue(__instance, out next);
                if (Time.time >= next) { SettleTimers[__instance] = Time.time + 1f; SettleFloaters(__instance); }
                float shedAt; ShedTimers.TryGetValue(__instance, out shedAt);
                if (Time.time >= shedAt)
                {
                    float dt = shedAt > 0f ? Mathf.Min(ShedInterval * 4f, Time.time - (shedAt - ShedInterval)) : ShedInterval;
                    ShedTimers[__instance] = Time.time + ShedInterval;
                    ShedFilms(__instance, dt);
                }
            }
        }
    }
}
