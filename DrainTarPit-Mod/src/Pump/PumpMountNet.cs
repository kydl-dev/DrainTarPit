using Kydl.Common;
using UnityEngine;

namespace DrainTarPit.Pump
{
    /// Who is on the pump. Mounting is arbitrated by the pump's current owner (RPC DTP_Mount -> DTP_MountReply); the player that gets it claims
    /// the ZDO and drives it. This class holds the handshake and the writes of the holder / phase / lever flags into the ZDO.
    internal sealed class PumpMountNet
    {
        readonly PumpController _owner;
        readonly ZNetView _nview;
        bool _pending; float _pendingUntil;

        public PumpMountNet(PumpController owner, ZNetView nview) { _owner = owner; _nview = nview; }

        public ZDO Zdo { get { return _nview != null && _nview.IsValid() ? _nview.GetZDO() : null; } }

        /// A mount request is waiting for the owner's answer.
        public bool Pending { get { return _pending; } }

        // RPCs are registered in Start: ZNetView.Awake (which creates / loads the ZDO) is guaranteed to have run by then.
        public void Register()
        {
            if (_nview != null && _nview.GetZDO() != null)
            {
                _nview.Register<ZPackage>(PumpZdo.RpcMount, OnMountRequest);
                _nview.Register<ZPackage>(PumpZdo.RpcMountReply, OnMountReply);
            }
        }

        /// True once when the request got no answer in time.
        public bool TimedOut()
        {
            if (!_pending || Time.time <= _pendingUntil) return false;
            _pending = false;
            return true;
        }

        /// Name of the player that holds the pump, or null when it is free / the holder is gone / it is `me`.
        public string OccupiedByOther(Player me)
        {
            var z = Zdo; if (z == null) return null;
            long id = z.GetLong(PumpZdo.User, 0L);
            if (id == 0L || (me != null && id == me.GetPlayerID()) || !PumpZdo.IsAlive(id)) return null;
            return z.GetString(PumpZdo.UserName, "someone");
        }

        // Runs on the pump's owner: grants the pump to one player at a time.
        void OnMountRequest(long sender, ZPackage pkg)
        {
            long id = pkg.ReadLong(); string name = pkg.ReadString();
            var z = Zdo; bool ok = false;
            if (z != null)
            {
                long cur = z.GetLong(PumpZdo.User, 0L);
                ok = cur == 0L || cur == id || !PumpZdo.IsAlive(cur);
                if (ok) { z.Set(PumpZdo.User, id); z.Set(PumpZdo.UserName, name); z.Set(PumpZdo.Phase, 0); z.Set(PumpZdo.Active, false); z.Set(PumpZdo.Pumping, false); }
            }
            var reply = new ZPackage(); reply.Write(ok);
            _nview.InvokeRPC(sender, PumpZdo.RpcMountReply, reply);
        }

        // Runs on the player that asked.
        void OnMountReply(long sender, ZPackage pkg)
        {
            bool ok = pkg.ReadBool();
            if (!_pending) { if (ok) Release(); return; }      // answer came too late
            _pending = false;
            var p = Player.m_localPlayer;
            if (!ok) { PumpController.Tell(p, Loc.T("dtp_msg_in_use")); return; }
            if (p == null) { Release(); return; }
            _nview.ClaimOwnership();                           // from now on this client writes the pump's state
            if (!_owner.Mount(p)) Release();
        }

        public void RequestMount(Player p)
        {
            if (Zdo == null) { _owner.Mount(p); return; }      // no ZDO (should not happen): local only
            _pending = true; _pendingUntil = Time.time + 3f;
            var pkg = new ZPackage(); pkg.Write(p.GetPlayerID()); pkg.Write(p.GetPlayerName());
            _nview.InvokeRPC(PumpZdo.RpcMount, pkg);
        }

        /// Clears the holder + lever state in the ZDO (called by whoever holds the pump).
        public void Release()
        {
            var z = Zdo; if (z == null) return;
            if (!_nview.IsOwner()) _nview.ClaimOwnership();
            z.Set(PumpZdo.User, 0L); z.Set(PumpZdo.UserName, ""); z.Set(PumpZdo.Active, false); z.Set(PumpZdo.Pumping, false);
            z.Set(PumpZdo.Phase, 0); z.Set(PumpZdo.PhaseSeq, z.GetInt(PumpZdo.PhaseSeq, 0) + 1);
        }

        public void WritePhase(PumpPhase phase)
        {
            var z = Zdo; if (z == null || !_nview.IsOwner()) return;
            z.Set(PumpZdo.Phase, (int)phase); z.Set(PumpZdo.PhaseSeq, z.GetInt(PumpZdo.PhaseSeq, 0) + 1);
        }

        public void WriteFlag(string key, bool v, bool hasUser) { var z = Zdo; if (hasUser && z != null && _nview.IsOwner()) z.Set(key, v); }
    }
}
