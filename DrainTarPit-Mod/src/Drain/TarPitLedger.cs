using System.Collections.Generic;
using System.Globalization;
using DrainTarPit.Logging;
using UnityEngine;
using static DrainTarPit.Drain.TarPits;

namespace DrainTarPit.Drain
{
    /// <summary>
    /// Volume book of a pool (steps 5, 6 and 10 of _agent/DrainTarPit_Water_and_Tar_First_15_Steps.md): checkpoints of the pool's total volume
    /// around every stage that may change it (deposit, wall pass, film shedding), the per-stroke accounting line, and a total-volume time series
    /// while the pool is active (a deposit in the last ActiveSeconds). A change of the total that no checkpoint explains is logged as DRIFT:
    /// that is the game's own simulation / save-reload cycle, or something this mod does not book yet. Owner only (where deposits run).
    /// Lines go to the BepInEx log with [General] DebugLog on, and the pour test copies them into its report (LinesSince).
    /// </summary>
    internal static class TarPitLedger
    {
        internal const float Tolerance = 0.001f;        // m3: per-stroke accounting must balance to this (float rounding of ~10^4 cells)
        internal const float DriftThreshold = 0.01f;    // m3 between two series samples with no booked stage
        const float ActiveSeconds = 120f, SeriesSeconds = 2f;
        const int MaxLines = 600;

        internal sealed class Book
        {
            public float LastDeposit = -999f, NextSeries, LastTotal = float.NaN;
            public readonly List<KeyValuePair<float, string>> Lines = new List<KeyValuePair<float, string>>();
            public int Strokes, Unbalanced; public double Poured, Kept, MaxAbsResidual, Drift;
        }
        internal static readonly Dictionary<LiquidVolume, Book> Books = new Dictionary<LiquidVolume, Book>();

        internal static Book Of(LiquidVolume v)
        {
            Book b;
            if (!Books.TryGetValue(v, out b)) Books[v] = b = new Book();
            return b;
        }

        internal static bool Active(LiquidVolume v)
        {
            Book b;
            return v != null && Books.TryGetValue(v, out b) && Time.time - b.LastDeposit < ActiveSeconds;
        }

        static string F(float x) { return x.ToString("0.000", CultureInfo.InvariantCulture); }
        static string Id(LiquidVolume v) { return "poolId=" + v.GetInstanceID() + " @ " + v.transform.position.ToString("0.0"); }

        internal static void Add(LiquidVolume v, string line)
        {
            var b = Of(v);
            b.Lines.Add(new KeyValuePair<float, string>(Time.time, line));
            if (b.Lines.Count > MaxLines) b.Lines.RemoveRange(0, b.Lines.Count - MaxLines);
            if (ModSettings.DebugLog.Value) ModLog.Info("Ledger " + Id(v) + " t=" + Time.time.ToString("0.00", CultureInfo.InvariantCulture) + ": " + line);
        }

        /// A stage that may change the volume: `before` / `after` are the pool totals (m3) measured around it.
        internal static void Checkpoint(LiquidVolume v, string stage, float before, float after, string note = "")
        {
            var b = Of(v);
            b.LastTotal = after;
            Add(v, "CHECKPOINT " + stage + " total " + F(before) + " -> " + F(after) + " (delta " + F(after - before) + ")" + (string.IsNullOrEmpty(note) ? "" : " " + note));
        }

        /// One stroke (DepositLocal): the accounting equation (step 6). `poured` and every part in m3; `measured` = the pool's total after - before.
        internal static void Stroke(LiquidVolume v, int hop, float poured, HeightField.FillResult r, float cell2, float nextPool, float edgeDiscarded, float measured)
        {
            var b = Of(v);
            b.LastDeposit = Time.time;
            float retained = r.Retained * cell2, inner = r.OtherBasins * cell2, outside = r.Outside * cell2, rim = r.RimRemoved * cell2,
                  over = r.OverflowRemoved * cell2, unpl = r.Unplaced * cell2, num = r.Numeric * cell2;
            float sum = retained + inner + outside + nextPool + edgeDiscarded + rim + over + unpl + num, residual = poured - sum;
            float kept = retained + inner + outside, diff = measured - kept;
            bool ok = Mathf.Abs(residual) <= Tolerance && Mathf.Abs(diff) <= Mathf.Max(Tolerance, poured * 1e-4f);
            b.Strokes++; b.Poured += poured; b.Kept += kept;
            if (Mathf.Abs(residual) > b.MaxAbsResidual) b.MaxAbsResidual = Mathf.Abs(residual);
            if (!ok) b.Unbalanced++;
            Add(v, "STROKE hop " + hop + ": poured " + F(poured) + " = basin " + F(retained) + " + innerBasins " + F(inner) + " + outsideRim " + F(outside) +
                   " + nextPool " + F(nextPool) + " + edgeDiscarded " + F(edgeDiscarded) + " + rimRemoved " + F(rim) + " + overflowRemoved " + F(over) +
                   " + unplaced " + F(unpl) + " + numeric " + F(num) + " | residual " + F(residual) + " (tolerance " + F(Tolerance) + ") | measured pool delta " + F(measured) +
                   " vs kept " + F(kept) + " (diff " + F(diff) + ") | " + (ok ? "BALANCED" : "UNBALANCED") +
                   " | pool rim " + (r.Pool != null ? (float.IsInfinity(r.Pool.Rim) ? "closed" : F(r.Pool.Rim)) + (r.Pool.Enclosed ? " enclosed" : " not-enclosed") : "?") +
                   ", passes " + r.Passes + ", rim crossings " + r.RimCrossings);
        }

        /// Patch_Update: the total of an active pool every SeriesSeconds (step 10, the planned TotalVolume time series).
        internal static void Tick(LiquidVolume v)
        {
            Book b;
            if (v == null || !Books.TryGetValue(v, out b) || Time.time - b.LastDeposit >= ActiveSeconds || Time.time < b.NextSeries) return;
            b.NextSeries = Time.time + SeriesSeconds;
            float t = TotalVolume(v);
            float delta = float.IsNaN(b.LastTotal) ? 0f : t - b.LastTotal;
            bool drift = Mathf.Abs(delta) > DriftThreshold;
            if (drift) b.Drift += delta;
            Add(v, "SERIES total " + F(t) + " delta since the last booked point " + F(delta) + (drift ? " DRIFT (no deposit / wall pass / film shed booked it)" : ""));
            b.LastTotal = t;
        }

        internal static string Summary(LiquidVolume v)
        {
            Book b;
            if (v == null || !Books.TryGetValue(v, out b)) return "ledger: nothing booked for this pool";
            return "ledger: strokes " + b.Strokes + ", poured " + b.Poured.ToString("0.000", CultureInfo.InvariantCulture) + ", kept " + b.Kept.ToString("0.000", CultureInfo.InvariantCulture) +
                   ", unbalanced strokes " + b.Unbalanced + ", max |residual| " + b.MaxAbsResidual.ToString("0.0000", CultureInfo.InvariantCulture) +
                   ", unexplained drift " + b.Drift.ToString("0.000", CultureInfo.InvariantCulture) + " m3";
        }

        internal static List<string> LinesSince(LiquidVolume v, float since)
        {
            var list = new List<string>();
            Book b;
            if (v == null || !Books.TryGetValue(v, out b)) return list;
            foreach (var kv in b.Lines) if (kv.Key >= since) list.Add("t=" + kv.Key.ToString("0.00", CultureInfo.InvariantCulture) + " " + kv.Value);
            return list;
        }

        /// Patch_OnDestroy: a pool that disappears while it is being measured is a stage of its own (pool replacement / unload, step 10).
        internal static void Destroyed(LiquidVolume v)
        {
            Book b;
            if (v == null || !Books.TryGetValue(v, out b)) return;
            if (Time.time - b.LastDeposit < ActiveSeconds) ModLog.Verbose("Ledger " + Id(v) + ": pool destroyed / unloaded while active; " + Summary(v));
            Books.Remove(v);
        }
    }
}
