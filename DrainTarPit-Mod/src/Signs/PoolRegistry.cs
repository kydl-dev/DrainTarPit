using System.Collections.Generic;
using System.Reflection;
using DrainTarPit.Logging;
using HarmonyLib;
using UnityEngine;

namespace DrainTarPit.Signs
{
    /// <summary>
    /// T-023 (d) (e) (f) (user, 2026-10-20): the world's named pools for the pool menu, and the admin's world water style.
    /// The SERVER answers: it holds every object of the world in memory (ZDOMan), so it lists every pool sign with a name and the closest
    /// portal (tag) to it without loading any zone - nothing is kept active, nothing has to be visited (the user's teleport-tracking idea is not
    /// needed for that). Clients ask with DTP_PoolListReq and get DTP_PoolList back. The world style is a world-saved global key
    /// ("dtp_waterstyle <style>") that only an admin can set (DTP_SetWorldStyle, checked on the server). RPCs are registered when a game
    /// session's ZRoutedRpc appears (Plugin.Update -> Tick), no Harmony patch needed.
    /// </summary>
    internal static class PoolRegistry
    {
        internal const string RpcListReq = "DTP_PoolListReq", RpcList = "DTP_PoolList", RpcSetWorldStyle = "DTP_SetWorldStyle";
        internal const string WorldKey = "dtp_waterstyle";
        const float CacheSeconds = 5f;

        internal sealed class Entry
        {
            public string Id = "", Name = "", Style = "", Portal = "";
            public Vector3 Pos;
            public float PortalDistance = -1f;              // -1 = no portal in the world
        }

        static ZRoutedRpc _registeredOn;
        static readonly List<Entry> Cache = new();
        static float _cacheAt = -100f;
        static List<string> _portalPrefabs;

        /// The last list the server sent (the menu shows it). Bumped `Version` when a new one arrived.
        internal static readonly List<Entry> Received = new();
        internal static int Version;
        internal static float RequestedAt = -100f;

        internal static void Tick()
        {
            var rpc = ZRoutedRpc.instance;
            if (rpc == null || rpc == _registeredOn) return;
            _registeredOn = rpc;
            rpc.Register<ZPackage>(RpcListReq, OnListRequest);
            rpc.Register<ZPackage>(RpcList, OnList);
            rpc.Register<string>(RpcSetWorldStyle, OnSetWorldStyle);
            _portalPrefabs = null; Cache.Clear(); _cacheAt = -100f;
            ModLog.Verbose("PoolRegistry: RPCs registered for this session (" + (ZNet.instance != null && ZNet.instance.IsServer() ? "server" : "client") + ")");
        }

        // --- client side ----------------------------------------------------------------------------------------------------
        /// Asks the server for the list (the answer arrives in Received). Returns 1 when sent, 0 when not in a game.
        internal static int Request()
        {
            if (ZRoutedRpc.instance == null || _registeredOn != ZRoutedRpc.instance) return 0;
            RequestedAt = Time.unscaledTime;
            ZRoutedRpc.instance.InvokeRoutedRPC(ServerId(), RpcListReq, new ZPackage());
            ModLog.Verbose("PoolRegistry: pool list requested from the server");
            return 1;
        }

        /// Asks the server to set the world water style (the server checks that the sender is an admin).
        internal static int SetWorldStyle(string style)
        {
            if (ZRoutedRpc.instance == null || _registeredOn != ZRoutedRpc.instance) return 0;
            ZRoutedRpc.instance.InvokeRoutedRPC(ServerId(), RpcSetWorldStyle, style ?? "");
            ModLog.Verbose("PoolRegistry: world water style " + style + " sent to the server");
            return 1;
        }

        /// The server's peer id (on the host / in single player: our own).
        static long ServerId()
        {
            if (ZNet.instance == null || ZNet.instance.IsServer()) return ZNet.GetUID();
            var p = ZNet.instance.GetServerPeer();
            return p != null ? p.m_uid : ZNet.GetUID();
        }

        static void OnList(long sender, ZPackage pkg)
        {
            Received.Clear();
            int n = pkg.ReadInt();
            for (int i = 0; i < n; i++)
                Received.Add(new Entry { Id = pkg.ReadString(), Name = pkg.ReadString(), Style = pkg.ReadString(), Pos = pkg.ReadVector3(), Portal = pkg.ReadString(), PortalDistance = pkg.ReadSingle() });
            Version++;
            ModLog.Verbose("PoolRegistry: " + n + " named pool(s) received from the server");
        }

        // --- server side ----------------------------------------------------------------------------------------------------
        static void OnListRequest(long sender, ZPackage req)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
            if (Time.unscaledTime - _cacheAt > CacheSeconds) Build();
            var pkg = new ZPackage();
            pkg.Write(Cache.Count);
            foreach (var e in Cache) { pkg.Write(e.Id); pkg.Write(e.Name); pkg.Write(e.Style); pkg.Write(e.Pos); pkg.Write(e.Portal); pkg.Write(e.PortalDistance); }
            ZRoutedRpc.instance.InvokeRoutedRPC(sender, RpcList, pkg);
            ModLog.Verbose("PoolRegistry: sent " + Cache.Count + " named pool(s) to peer " + sender);
        }

        /// Every pool sign with a name, and the closest portal to it, straight from the server's object list.
        static void Build()
        {
            Cache.Clear();
            _cacheAt = Time.unscaledTime;
            var signs = All(PoolSignPiece.PrefabName);
            var portals = new List<ZDO>();
            foreach (var p in PortalPrefabs()) portals.AddRange(All(p));
            foreach (var z in signs)
            {
                string name = z.GetString(PoolSign.KeyName, "");
                if (string.IsNullOrEmpty(name)) continue;                     // user: "if a pool has no sign with name set then it is not displayed"
                var e = new Entry { Id = z.m_uid.ToString(), Name = name, Style = z.GetString(PoolSign.KeyStyle, ""), Pos = z.GetPosition() };
                float best = float.MaxValue;
                foreach (var p in portals)
                {
                    float d = Vector3.Distance(e.Pos, p.GetPosition());
                    if (d < best) { best = d; e.Portal = p.GetString(ZDOVars.s_tag, ""); }
                }
                e.PortalDistance = best < float.MaxValue ? best : -1f;
                Cache.Add(e);
            }
            Cache.Sort((a, b) => string.Compare(a.Name, b.Name, System.StringComparison.OrdinalIgnoreCase));
            ModLog.Verbose("PoolRegistry: " + signs.Count + " pool sign(s), " + Cache.Count + " named, " + portals.Count + " portal(s) in the world (" + string.Join(", ", PortalPrefabs()) + ")");
        }

        static List<ZDO> All(string prefab)
        {
            var list = new List<ZDO>();
            if (ZDOMan.instance == null) return list;
            int index = 0, guard = 0;
            while (!ZDOMan.instance.GetAllZDOsWithPrefabIterative(prefab, list, ref index) && guard++ < 100000) { }
            return list;
        }

        /// The game's portal prefabs (everything with a TeleportWorld component), found once per session.
        static List<string> PortalPrefabs()
        {
            if (_portalPrefabs != null) return _portalPrefabs;
            _portalPrefabs = new List<string>();
            if (ZNetScene.instance != null)
                foreach (var p in ZNetScene.instance.m_prefabs)
                    if (p != null && p.GetComponent<TeleportWorld>() != null) _portalPrefabs.Add(p.name);
            return _portalPrefabs;
        }

        static void OnSetWorldStyle(long sender, string style)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer() || ZoneSystem.instance == null) return;
            if (!SenderIsAdmin(sender)) { ModLog.Warn("PoolRegistry: peer " + sender + " is not an admin - world water style '" + style + "' refused"); return; }
            if (string.IsNullOrEmpty(style))
            {
                ZoneSystem.instance.RemoveGlobalKey(WorldKey);
                ModLog.Info("PoolRegistry: world water style cleared by peer " + sender + " (each player's config decides again)");
                return;
            }
            WaterStyleOption s;
            if (!System.Enum.TryParse(style, true, out s)) { ModLog.Warn("PoolRegistry: unknown water style '" + style + "' refused"); return; }
            ZoneSystem.instance.SetGlobalKey(WorldKey + " " + s.ToString().ToLowerInvariant());
            ModLog.Info("PoolRegistry: world water style set to " + s + " by peer " + sender);
        }

        static readonly MethodInfo MListContains = AccessTools.Method(typeof(ZNet), "ListContainsId");
        static readonly FieldInfo FAdminList = AccessTools.Field(typeof(ZNet), "m_adminList");

        /// The host itself, or a peer whose platform id is in the server's admin list.
        static bool SenderIsAdmin(long sender)
        {
            if (sender == ZNet.GetUID()) return ZNet.instance.IsServer();
            var peer = ZNet.instance.GetPeer(sender);
            int admin = 0;
            string host = "?";
            if (peer != null && peer.m_rpc != null && MListContains != null && FAdminList != null)
            {
                host = peer.m_rpc.GetSocket().GetHostName();
                object result = MListContains.Invoke(ZNet.instance, new[] { FAdminList.GetValue(ZNet.instance), (object)host });
                admin = result is bool b && b ? 1 : 0;
            }
            else ModLog.VerboseWarn("PoolRegistry: admin check not possible for peer " + sender + " (peer or game method missing) - treated as not admin");
            ModLog.Verbose("PoolRegistry: peer " + sender + " (" + host + ") admin: " + admin);
            return admin == 1;
        }
    }
}
