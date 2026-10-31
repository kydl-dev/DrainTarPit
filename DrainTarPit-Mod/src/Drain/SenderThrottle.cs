using System.Collections.Generic;

namespace DrainTarPit.Drain
{
    /// <summary>
    /// Rate limit per sender (player session id) for the drain / deposit RPCs: a sender may ask at most once per MinGap seconds.
    /// Pure logic (no Unity / game types, the clock is passed in) so it is unit tested (DrainTarPit-Tests/Unit, linked file).
    /// Bug #9: the old per-sender dictionaries were never pruned (one entry per player id ever seen until restart). Entries that
    /// have not asked for PruneAfter seconds are dropped once the table holds more than PruneAbove senders.
    /// </summary>
    internal sealed class SenderThrottle
    {
        internal const int PruneAbove = 32;          // a server rarely has more players than this pumping at the same time
        internal const float PruneAfter = 30f;       // s; far longer than any gap between two strokes of one player

        readonly Dictionary<long, float> _last = new Dictionary<long, float>();
        readonly float _minGap;

        internal SenderThrottle(float minGap) { _minGap = minGap; }

        /// Senders currently remembered (diagnostics / tests).
        internal int Count { get { return _last.Count; } }

        /// What the last call decided, for the caller's debug line (null before the first call).
        internal string LastReason { get; private set; }

        /// Expected outcome: the request is allowed exactly when this sender has no request within the last MinGap seconds;
        /// an allowed request is remembered as the sender's latest (so the returned value equals "the table now holds `now` for him").
        internal bool Allow(long sender, float now)
        {
            float last;
            bool seen = _last.TryGetValue(sender, out last);
            bool tooSoon = seen && now - last >= 0f && now - last < _minGap;
            if (tooSoon)
            {
                LastReason = "sender " + sender + " asked again after " + (now - last).ToString("0.000") + " s (< " + _minGap.ToString("0.00") + " s): refused";
                return !tooSoon;
            }
            _last[sender] = now;
            int pruned = _last.Count > PruneAbove ? Prune(now) : 0;
            LastReason = "sender " + sender + (seen ? " after " + (now - last).ToString("0.000") + " s" : " (first request)") + ": allowed" +
                (pruned > 0 ? ", " + pruned + " idle senders dropped" : "");
            float stored;
            // Stryker disable once all : self-check of the invariant above (the entry was just written), cannot be false
            return _last.TryGetValue(sender, out stored) && stored == now;
        }

        /// Drops every sender idle for PruneAfter seconds or more. Expected outcome: the number dropped equals the shrink of the table.
        internal int Prune(float now)
        {
            int before = _last.Count;
            var idle = new List<long>();
            foreach (var kv in _last) if (now - kv.Value >= PruneAfter) idle.Add(kv.Key);
            foreach (var k in idle) _last.Remove(k);
            int dropped = before - _last.Count;
            // Stryker disable once all : self-check of the invariant (every idle key is removed exactly once), the -1 branch cannot happen
            return dropped == idle.Count ? dropped : -1;
        }
    }
}
