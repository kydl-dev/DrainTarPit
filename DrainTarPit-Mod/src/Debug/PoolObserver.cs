using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using DrainTarPit.Drain;
using DrainTarPit.Logging;
using UnityEngine;
using static DrainTarPit.Debug.PoolTestOut;
using static DrainTarPit.Debug.PoolSnapshot;
using static DrainTarPit.Debug.PoolTankTests;

namespace DrainTarPit.Debug
{
    /// One observed pool: takes a sample, tracks revision / save events and drops, and writes the sample line to the report (PoolTests).
    internal static class PoolObserver
    {
        const float DropEvent = 0.3f;               // m3 lost between two samples without a pour = an event worth a line

        // --- one run (a pool being observed) -----------------------------------------------------------------------------------
        internal sealed class Obs
        {
            public string Label; public LiquidVolume V; public Tank Tank; public float T0, Poured, Expected = float.NaN;
            public Snap Last; public float PouredAtLast;
            public long PrevOwn = long.MinValue, PrevZdo = long.MinValue; public int PrevBytes = int.MinValue, PrevHash;
            public float MaxLevel = float.MinValue; public int Drops, Reloads, Saves, InvalidSnapshots;
        }

        internal static Snap Sample(Obs r)
        {
            var s = Take(r.V, r.Tank);
            float t = Time.time - r.T0;
            var sb = new StringBuilder();
            sb.Append(r.Label).Append(" t=").Append(F2(t)).Append(" poured=").Append(F2(r.Poured))
              .Append(" observer{").Append(TarPool.DescribeVolume(r.V)).Append("}");
            if (!s.Valid)
            {
                r.InvalidSnapshots++;
                sb.Append(" SNAPSHOT_INVALID reason=\"").Append(s.InvalidReason).Append("\"");
                Out(sb.ToString());
                r.Last = s; r.PouredAtLast = r.Poured;
                return s;
            }
            long own, zdo; int bytes, hash;
            TarPitDebug.Revisions(r.V, out own, out zdo, out bytes, out hash);
            var ev = new StringBuilder();
            if (r.PrevOwn != long.MinValue)
            {
                if (own != r.PrevOwn) { ev.Append(" EVT game-rev ").Append(r.PrevOwn).Append("->").Append(own); r.Reloads++; }
                if (zdo != r.PrevZdo) ev.Append(" EVT zdo-rev ").Append(r.PrevZdo).Append("->").Append(zdo);
                if (bytes != r.PrevBytes || hash != r.PrevHash) { ev.Append(" EVT saved-data ").Append(r.PrevBytes).Append("B->").Append(bytes).Append("B"); r.Saves++; }
            }
            r.PrevOwn = own; r.PrevZdo = zdo; r.PrevBytes = bytes; r.PrevHash = hash;
            if (r.Last != null && r.Last.Valid && r.Poured <= r.PouredAtLast && s.Total - r.Last.Total < -DropEvent)
            {
                r.Drops++; ev.Append(" EVT DROP ").Append(F2(s.Total - r.Last.Total)).Append(" m3 without a pour");
            }
            if (s.Level > r.MaxLevel) r.MaxLevel = s.Level;
            sb.Append(" snapshot=valid total=").Append(F2(s.Total)).Append(" level=").Append(F(s.Level));
            if (!float.IsNaN(r.Expected)) sb.Append(" exp=").Append(F(r.Expected));
            sb.Append(" basin=").Append(F2(s.Inside)).Append(" elsewhere=").Append(F2(s.Outside)).Append(" gridedge=").Append(F2(s.Border));
            if (r.Tank != null) sb.Append(" tankIn=").Append(F2(s.TankIn)).Append(" onWalls=").Append(F2(s.Ring));
            sb.Append(" cells=").Append(s.Region).Append(" spread=").Append(F(s.Spread)).Append(" spill4=").Append(F(s.Spill4)).Append(" spill8=").Append(F(s.Spill8))
              .Append(" wet=").Append(s.Wet).Append(" walls=").Append(s.Walls).Append(" grid=").Append(s.Width).Append("x").Append(s.Width)
              .Append(" cell=").Append(F(s.CellScale)).Append(" depthCells=").Append(s.DepthCount).Append(" heightCells=").Append(s.HeightCount).Append(ev);
            Out(sb.ToString());
            r.Last = s; r.PouredAtLast = r.Poured;
            return s;
        }
    }
}
