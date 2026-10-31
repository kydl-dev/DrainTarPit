using System.Text;
using DrainTarPit.Building;
using Kydl.Common;
using DrainTarPit.Effects;
using UnityEngine;

namespace DrainTarPit.Drain
{
    internal sealed class StrokeResult
    {
        public bool Valid;           // hose chains + liquid check passed (the stroke counts: animation, stamina)
        public bool Dry;             // valid, but nothing within reach of the strainer (tar only: water never runs dry)
        public bool SlugStarted;     // the slug of this stroke is already shown (TarFlow.Stroke was called by DrainService.Stroke)
        public float Volume;         // m3 requested from the source (estimate of what the owner removes)
        public string Message;       // player-facing reason when !Valid or Dry
        public Vector3 NozzleTip;
        public PumpLiquid Liquid;    // what this stroke moves (the liquid the strainer is locked to)
    }

    /// Validates the pump's hose chains and performs the actual drain for one pump stroke.
    /// The liquid is decided by the strainer: it is locked to tar or water when it is built (DtpPiece.Liquid) and the pipe system only ever moves that.
    internal static class DrainService
    {
        sealed class Check
        {
            public ChainResult Intake, Outlet;
            public LiquidVolume Pit; public float Depth, Surface;
            public Vector3 Tip, Nozzle, Center;
            public DtpPiece Strainer;
            public PumpLiquid Liquid;       // what the strainer is locked to - or, for a strainer that is not locked yet, what it sits in now
            public bool NeedsLock;          // not locked yet: the first real stroke locks it (Stroke, never the hover text)
            public string Problem;          // null = all good
        }

        static Check Evaluate(DtpPiece pump)
        {
            var c = new Check();
            c.Intake = HoseNetwork.Trace(pump, "intake", DtpKind.Strainer);
            c.Outlet = HoseNetwork.Trace(pump, "outlet", DtpKind.Nozzle);
            if (!c.Intake.Ok) { c.Problem = c.Intake.Reason; return c; }
            if (!c.Outlet.Ok) { c.Problem = c.Outlet.Reason; return c; }

            c.Strainer = c.Intake.Terminal;
            c.Tip = c.Strainer.StrainerTip;
            c.Nozzle = c.Outlet.Terminal.NozzleTip;

            c.Liquid = c.Strainer.Liquid;
            if (c.Liquid == PumpLiquid.None)
            {
                c.Liquid = Liquids.Detect(c.Tip);
                c.NeedsLock = c.Liquid != PumpLiquid.None;
            }
            if (c.Liquid == PumpLiquid.None) { c.Problem = Loc.T("dtp_err_not_in_liquid"); return c; }
            return c.Liquid == PumpLiquid.Water ? CheckWater(c) : CheckTar(c);
        }

        static Check CheckTar(Check c)
        {
            Vector3 at;
            if (!TarPits.TrySampleNear(c.Tip, ModSettings.StrainerSearchRadius.Value, out c.Pit, out c.Depth, out c.Surface, out at) || c.Depth < ModSettings.MinTarDepth.Value)
            { c.Problem = Loc.T("dtp_err_not_in_tar"); return c; }
            if (c.Tip.y > c.Surface + ModSettings.StrainerReach.Value) { c.Problem = Loc.T("dtp_err_above_tar"); return c; }
            c.Center = new Vector3(at.x, c.Tip.y, at.z);        // pull from the deepest tar next to the tip

            LiquidVolume np; float nd, ns;
            // only when the tip is really BELOW the tar surface: a nozzle hanging above / beside a pit (the grid is a 2D area, depth is read
            // straight under the tip) is not in the tar
            if (TarPits.TrySample(c.Nozzle, out np, out nd, out ns) && nd >= ModSettings.MinTarDepth.Value && c.Nozzle.y <= ns + 0.05f)
            { c.Problem = Loc.T("dtp_err_nozzle_in_tar"); return c; }
            return c;
        }

        // Water comes from a water pool (a finite LiquidVolume this mod filled: c.Pit is set and the stroke is a normal drain) or, when the tip is not in a pool, from the
        // world's water plane, which cannot be lowered or emptied and only has to be under the tip.
        static Check CheckWater(Check c)
        {
            if (!ModSettings.AllowWater.Value) { c.Problem = Loc.T("dtp_err_water_off"); return c; }
            Vector3 at;
            if (TarPits.TrySampleNear(c.Tip, ModSettings.StrainerSearchRadius.Value, out c.Pit, out c.Depth, out c.Surface, out at, LiquidType.Water)
                && c.Depth >= ModSettings.MinTarDepth.Value && c.Tip.y <= c.Surface + ModSettings.StrainerReach.Value)
            {
                c.Center = new Vector3(at.x, c.Tip.y, at.z);        // pull from the deepest water next to the tip
                LiquidVolume wp; float wd, ws;
                if (TarPits.TrySample(c.Nozzle, out wp, out wd, out ws, LiquidType.Water) && wd >= ModSettings.MinTarDepth.Value && c.Nozzle.y <= ws + 0.05f)
                { c.Problem = Loc.T("dtp_err_nozzle_in_water"); return c; }
                float gd, gs;
                if (Liquids.WaterDepthAt(c.Nozzle, out gd, out gs) && c.Nozzle.y <= gs + 0.05f) { c.Problem = Loc.T("dtp_err_nozzle_in_water"); return c; }
                return c;
            }
            c.Pit = null;

            float depth, surface;
            if (!Liquids.WaterDepthAt(c.Tip, out depth, out surface)) { c.Problem = Loc.T("dtp_err_not_in_water"); return c; }
            if (c.Tip.y > surface + ModSettings.StrainerReach.Value) { c.Problem = Loc.T("dtp_err_above_water"); return c; }
            c.Depth = depth; c.Surface = surface;
            c.Center = c.Tip;

            float nd, ns;
            if (Liquids.WaterDepthAt(c.Nozzle, out nd, out ns) && c.Nozzle.y <= ns + 0.05f)
            { c.Problem = Loc.T("dtp_err_nozzle_in_water"); return c; }
            return c;
        }

        /// Multi-line status shown in the pump hover text.
        public static string Describe(DtpPiece pump)
        {
            var c = Evaluate(pump);
            var sb = new StringBuilder();
            sb.Append(c.Intake.Ok ? "<color=#9be28b>" + Loc.T("dtp_intake_ok") + "</color>" : "<color=#e8a05a>" + Loc.T("dtp_intake_bad", c.Intake.Reason) + "</color>");
            sb.Append('\n');
            sb.Append(c.Outlet.Ok ? "<color=#9be28b>" + Loc.T("dtp_outlet_ok") + "</color>" : "<color=#e8a05a>" + Loc.T("dtp_outlet_bad", c.Outlet.Reason) + "</color>");
            if (c.Intake.Ok && c.Outlet.Ok)
            {
                sb.Append('\n');
                sb.Append(c.Problem == null
                    ? "<color=#9be28b>" + Loc.T("dtp_ready_liquid", Liquids.Name(c.Liquid)) + "</color>"
                    : "<color=#e8a05a>" + c.Problem + "</color>");
            }
            return sb.ToString();
        }

        public static StrokeResult Stroke(DtpPiece pump, ZNetView pumpView)
        {
            var r = new StrokeResult();
            if (!ModSettings.DrainOn) { r.Message = Loc.T(ModSettings.On ? "dtp_drain_off" : "dtp_mod_off"); ModLog.Verbose("Stroke refused: " + (ModSettings.On ? "[Drain] Enable is off" : "[General] EnableAll is off")); return r; }   // T-068: inert
            var c = Evaluate(pump);
            if (c.Problem != null) { r.Message = c.Problem; return r; }
            r.Valid = true;
            r.NozzleTip = c.Nozzle;
            r.Liquid = c.Liquid;

            if (c.NeedsLock && c.Strainer != null) c.Strainer.LockLiquid(c.Liquid);      // the first real stroke fixes what this pipe system moves
            MarkLiquid(pumpView, c.Liquid);                                              // other clients tint the slug / pour with it
            if (c.Liquid == PumpLiquid.Water && c.Pit == null) return StrokeWater(pump, pumpView, c, r);     // the sea: no pit to ask

            float radius = ModSettings.DrainRadius.Value;
            float avail = TarPitDrain.Available(c.Pit, c.Center, radius);
            if (avail < 0.01f) { r.Dry = true; r.Message = Loc.T("dtp_err_air"); return r; }

            r.Volume = Mathf.Min(ModSettings.VolumePerStroke.Value, avail);
            var pumpId = pumpView != null && pumpView.IsValid() ? pumpView.GetZDO().m_uid : ZDOID.None;
            // The slug of this stroke has to exist BEFORE the request goes out: when this machine also owns the pit (single player, host) the
            // owner's answer - Poured - comes back inside RequestDrain, and Poured lines the nozzle effects up with this slug (TarFlow.ClaimArrival).
            TarFlow.Stroke(pump, true, c.Liquid);
            r.SlugStarted = true;
            TarPitDrain.RequestDrain(c.Pit, c.Center, radius, r.Volume, c.Nozzle, pumpId);   // the pit owner answers -> TarPitDrain.OnDrained -> Poured
            return r;
        }

        // The sea has no pit to ask: the source is the world's water plane and never runs dry, so the stroke pours its full volume at once (the pour itself
        // still waits for the slug to reach the nozzle, see Poured).
        static StrokeResult StrokeWater(DtpPiece pump, ZNetView pumpView, Check c, StrokeResult r)
        {
            r.Volume = ModSettings.VolumePerStroke.Value;
            TarFlow.Stroke(pump, true, PumpLiquid.Water);
            r.SlugStarted = true;
            Poured(pumpView, c.Nozzle, r.Volume, PumpLiquid.Water);
            return r;
        }

        /// The pump's ZDO carries what the pump is moving, so every client that sees the pump colours the slug and the pour the same way.
        static void MarkLiquid(ZNetView pumpView, PumpLiquid liquid)
        {
            if (pumpView == null || !pumpView.IsValid() || !pumpView.IsOwner()) return;
            var zdo = pumpView.GetZDO();
            if (zdo != null && zdo.GetInt(Pump.PumpZdo.Liquid, 0) != (int)liquid) zdo.Set(Pump.PumpZdo.Liquid, (int)liquid);
        }

        /// Called on the pumping client with the volume that really came out of the source. The pump only moves the LIQUID - Tar items, meat or anything
        /// else lying in the pit is never touched. The liquid pours out of the outlet pipe / nozzle (stream, splashes, puddle, sound) and, for tar, is
        /// deposited where the stream lands NOT now but when the slug of this stroke has travelled through the hoses and reaches the nozzle
        /// (PumpFx.PourWhenArrives); the pit owner's answer only says how much came out. The pour is also written to the pump's ZDO (counter +
        /// position + volume) right away, so every other client that has the pump loaded shows it too, lined up with its own copy of the slug
        /// (PumpObserver.Update). With WaterAndTarPools the liquid is deposited into a real pool where the stream lands (PoolHooks.Deposit).
        public static void Poured(ZNetView pumpView, Vector3 nozzleTip, float volume, PumpLiquid liquid = PumpLiquid.Tar)
        {
            if (volume <= 0f) return;
            Vector3 dir = Vector3.down;                              // fallback when the nozzle cannot be found
            var piece = pumpView != null && pumpView.IsValid() ? pumpView.GetComponent<DtpPiece>() : null;
            if (piece != null)
            {
                var outlet = HoseNetwork.Trace(piece, "outlet", DtpKind.Nozzle);
                if (outlet.Ok && outlet.Terminal != null) dir = outlet.Terminal.NozzleDir;
            }
            var at = nozzleTip + dir * 0.2f;                         // start the spurt in FRONT of the nozzle's open end (the stretched droplets trail back along the stream)
            // the liquid lands where the stream lands and fills the hole / basin / moat there
            // T-069: only with WaterAndTarPools (PoolHooks.Deposit); without it the liquid just pours out visually
            System.Action deposit = null;
            if (PoolHooks.Deposit != null && (liquid == PumpLiquid.Tar || liquid == PumpLiquid.Water)) deposit = () => PoolHooks.Deposit(at, dir, volume, liquid);
            else ModLog.Verbose("Pour: " + volume.ToString("0.00") + " m3 of " + liquid + " pours out visually only (" + (PoolHooks.Deposit == null ? "WaterAndTarPools not installed" : "nothing to deposit") + ")");
            PumpFx.PourWhenArrives(piece, at, dir, volume, deposit, liquid);
            if (pumpView == null || !pumpView.IsValid()) return;
            if (!pumpView.IsOwner()) return;                        // someone else took over the pump meanwhile; they cannot hear this stroke
            var zdo = pumpView.GetZDO();
            zdo.Set(Pump.PumpZdo.Liquid, (int)liquid);
            zdo.Set(Pump.PumpZdo.Nozzle, at);
            zdo.Set(Pump.PumpZdo.NozzleDir, dir);
            zdo.Set(Pump.PumpZdo.SplashVol, volume);
            zdo.Set(Pump.PumpZdo.SplashSeq, zdo.GetInt(Pump.PumpZdo.SplashSeq, 0) + 1);
        }
    }
}
