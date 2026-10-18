using DrainTarPit.Building;
using DrainTarPit.TextLocal;
using UnityEngine;

namespace DrainTarPit.Drain
{
    internal sealed class ChainResult
    {
        public bool Ok;
        public string Reason;          // why it failed (player-facing)
        public DtpPiece Terminal;      // the strainer / nozzle at the end
        public int Pieces;             // hoses/elbows between pump and terminal
    }

    /// One hose / elbow on the way from the pump: the snap point we came in through and the one we leave by.
    internal sealed class HoseLink { public DtpPiece Piece; public Transform Entry, Exit; }

    /// Result of walking the hose chain once; used by the drain check (Trace) AND the tar flow visuals (TarFlow), so both always agree.
    internal sealed class HoseWalk
    {
        public bool Ok;
        public string Reason;
        public Transform Start;                              // the pump's snap point the chain begins at
        public readonly System.Collections.Generic.List<HoseLink> Links = new();   // hoses / elbows between pump and terminal
        public DtpPiece Terminal; public Transform TerminalEntry;
    }

    /// <summary>
    /// Follows hose pieces from a pump snap point to the strainer (intake) or nozzle (outlet). Two pieces count as
    /// connected when a snap point of one is within ModSettings.SnapTolerance of the other's. Derived from positions on
    /// demand, so nothing has to be saved or synced for the links themselves.
    /// </summary>
    internal static class HoseNetwork
    {
        public static ChainResult Trace(DtpPiece pump, string snapKey, DtpKind wantedTerminal)
        {
            var w = Walk(pump, snapKey, wantedTerminal);
            return new ChainResult { Ok = w.Ok, Reason = w.Reason, Terminal = w.Terminal, Pieces = w.Links.Count };
        }

        public static HoseWalk Walk(DtpPiece pump, string snapKey, DtpKind wantedTerminal)
        {
            var res = new HoseWalk();
            Transform exit = null;
            foreach (var s in pump.Snaps)
                if (s != null && s.name.ToLowerInvariant().Contains(snapKey)) { exit = s; break; }
            if (exit == null) { res.Reason = Loc.T("dtp_h_no_snap", Loc.T("dtp_side_" + snapKey)); return res; }
            res.Start = exit;

            string side = Loc.T(wantedTerminal == DtpKind.Strainer ? "dtp_side_intake" : "dtp_side_outlet");
            var visited = new System.Collections.Generic.HashSet<DtpPiece> { pump };
            float tol = ModSettings.SnapTolerance.Value;

            for (int step = 0; step < ModSettings.MaxChainPieces.Value; step++)
            {
                DtpPiece next = null; Transform entry = null; float best = tol;
                foreach (var p in DtpPiece.All)
                {
                    if (p == null || visited.Contains(p)) continue;
                    foreach (var s in p.Snaps)
                    {
                        if (s == null) continue;
                        float d = Vector3.Distance(s.position, exit.position);
                        if (d < best) { best = d; next = p; entry = s; }
                    }
                }
                if (next == null)
                {
                    res.Reason = step == 0 ? Loc.T("dtp_h_nothing", side) : Loc.T("dtp_h_thin_air", side);
                    return res;
                }
                visited.Add(next);

                if (next.Kind == wantedTerminal) { res.Ok = true; res.Terminal = next; res.TerminalEntry = entry; return res; }
                if (next.Kind == DtpKind.Strainer || next.Kind == DtpKind.Nozzle || next.Kind == DtpKind.Pump)
                {
                    res.Reason = Loc.T(wantedTerminal == DtpKind.Strainer ? "dtp_h_must_end_strainer" : "dtp_h_must_end_nozzle");
                    return res;
                }

                Transform other = null;                       // continue through the far end of this hose/elbow
                foreach (var s in next.Snaps) if (s != null && s != entry) { other = s; break; }
                if (other == null) { res.Reason = Loc.T("dtp_h_broken"); return res; }
                res.Links.Add(new HoseLink { Piece = next, Entry = entry, Exit = other });
                exit = other;
            }
            res.Reason = Loc.T("dtp_h_too_long", side);
            return res;
        }
    }
}
