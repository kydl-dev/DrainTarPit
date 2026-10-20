using DrainTarPit.Building;
using DrainTarPit.Drain;
using DrainTarPit.Effects;
using UnityEngine;

namespace DrainTarPit.Pump
{
    /// Another player is on the pump: mirrors the pump's ZDO on this client. The lever follows DTP_active / DTP_pumping / DTP_pumpSeq, the
    /// mounted player's clips follow DTP_phase / seq (the same clips, started when the change arrives), the hands get the same IK.
    internal sealed class PumpObserver
    {
        readonly PumpController _owner;
        readonly ZNetView _nview;
        readonly DtpPiece _piece;
        readonly PumpLever _lever;
        readonly Transform _fxPoint;                // fx_outlet, for the droplet / sound effects
        int _seenPhaseSeq = -1, _seenPumpSeq = -1, _seenStrokes = -1, _seenSplash = -1;

        /// The player mounted on this pump when that is somebody else (we only mirror).
        public Player Remote { get; private set; }
        public PumpPhase RemotePhase { get; private set; }

        public PumpObserver(PumpController owner, ZNetView nview, DtpPiece piece, PumpLever lever, Transform fxPoint)
        {
            _owner = owner; _nview = nview; _piece = piece; _lever = lever; _fxPoint = fxPoint;
        }

        /// The local player took the pump over: drop the mirrored player without releasing its clips.
        public void ForgetRemote() { Remote = null; }

        public void Update()
        {
            if (PieceSetup.Headless) return;                    // dedicated server: nothing to show (mount arbitration is the RPC)
            var z = _nview != null && _nview.IsValid() ? _nview.GetZDO() : null;
            if (z == null) return;
            long uid = z.GetLong(PumpZdo.User, 0L);
            var me = Player.m_localPlayer;
            bool occupied = uid != 0L && !(me != null && uid == me.GetPlayerID()) && PumpZdo.IsAlive(uid);
            if (!occupied) { if (Remote != null || _seenPhaseSeq != -1 || _owner.IsActive) End(); return; }

            var pl = Player.GetPlayer(uid);
            if (pl != Remote) { if (Remote != null) PlayerPumpAnimations.Release(Remote); Remote = pl; _seenPhaseSeq = -1; }

            _owner.MirrorActive(z.GetBool(PumpZdo.Active, false));
            _lever.Set("pumping", z.GetBool(PumpZdo.Pumping, false));
            var liquid = (PumpLiquid)z.GetInt(PumpZdo.Liquid, (int)PumpLiquid.Tar);
            int st = z.GetInt(PumpZdo.Strokes, 0);
            if (_seenStrokes == -1) _seenStrokes = st;
            else if (st != _seenStrokes)
            {
                _seenStrokes = st;
                // the stroke counter of a continuous loop is raised shortly before that loop starts (PumpController CycleLead 0.1 s); a tap starts at once
                var timing = RemotePhase == PumpPhase.Cycle ? PumpClips.CycleTiming(0.1f) : PumpClips.TapTiming();
                PumpFx.Stroke(_fxPoint, timing); TarFlow.Stroke(_piece, true, liquid);
            }
            int sp = z.GetInt(PumpZdo.SplashSeq, 0);
            if (_seenSplash == -1) _seenSplash = sp;
            else if (sp != _seenSplash)
            {
                _seenSplash = sp;
                var np = z.GetVec3(PumpZdo.Nozzle, Vector3.zero);
                // the liquid coming out at the nozzle, when this client's copy of the slug gets there
                if (np != Vector3.zero) PumpFx.PourWhenArrives(_piece, np, z.GetVec3(PumpZdo.NozzleDir, Vector3.down), z.GetFloat(PumpZdo.SplashVol, 1f), null, liquid);
            }
            int ps = z.GetInt(PumpZdo.PumpSeq, 0);
            if (_seenPumpSeq == -1) _seenPumpSeq = ps;
            else if (ps != _seenPumpSeq) { _seenPumpSeq = ps; _lever.Set("pump", true); }

            int seq = z.GetInt(PumpZdo.PhaseSeq, 0);
            if (Remote != null && seq != _seenPhaseSeq)
            {
                if (!PlayerPumpAnimations.Ready) PlayerPumpAnimations.TryDetect(Remote);
                if (PlayerPumpAnimations.Ready)
                {
                    RemotePhase = (PumpPhase)z.GetInt(PumpZdo.Phase, 0);
                    string clip = PumpClips.ClipOf(RemotePhase);
                    if (clip != null) PlayerPumpAnimations.Play(Remote, clip);
                    _seenPhaseSeq = seq;
                }
            }
        }

        void End()
        {
            if (Remote != null) PlayerPumpAnimations.Release(Remote);
            Remote = null; _seenPhaseSeq = -1; _seenPumpSeq = -1; _seenStrokes = -1; _seenSplash = -1; RemotePhase = PumpPhase.None;
            _owner.ResetIkWeight();
            _lever.Set("pumping", false); _owner.MirrorActive(false);
        }
    }
}
