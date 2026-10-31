using DrainTarPit.Drain;

namespace DrainTarPit.Tests.Unit
{
    /// <summary>
    /// Edges of SenderThrottle that decide real behaviour: exactly at the gap, the same instant, a clock that starts below zero, the exact
    /// prune limits and the debug line the caller writes (it is the only thing an admin sees when a pumping player is refused).
    /// </summary>
    [TestFixture]
    public class SenderThrottleBoundaryTests
    {
        [Test]
        public void Allow_ExactlyAtTheGap_IsAllowed()
        {
            var t = new SenderThrottle(0.5f);
            t.Allow(1, 10f);

            Assert.That(t.Allow(1, 10.5f), Is.True, "a request exactly MinGap after the last one is allowed (the gap is the minimum)");
        }

        [Test]
        public void Allow_SameInstantAgain_IsRefused()
        {
            var t = new SenderThrottle(0.5f);
            t.Allow(1, 10f);

            Assert.That(t.Allow(1, 10f), Is.False, "two requests with the same timestamp are one request too many");
        }

        [Test]
        public void Allow_ClockMovesBackwards_IsAllowedAndRemembered()
        {
            var t = new SenderThrottle(0.5f);
            t.Allow(1, 10f);

            Assert.That(t.Allow(1, 9f), Is.True, "a smaller timestamp (new session clock) must not lock the sender out");
            Assert.That(t.Allow(1, 9.2f), Is.False, "and the new time is the one remembered");
        }

        [Test]
        public void Allow_NegativeClock_BehavesLikeAnyOtherClock()
        {
            var t = new SenderThrottle(0.2f);
            t.Allow(1, -5f);

            Assert.That(t.Allow(1, -4.9f), Is.False);
            Assert.That(t.Allow(1, -4.7f), Is.True);
        }

        [Test]
        public void LastReason_BeforeTheFirstCall_IsNull()
        {
            Assert.That(new SenderThrottle(0.2f).LastReason, Is.Null);
        }

        [Test]
        public void LastReason_FirstRequest_NamesTheSender()
        {
            var t = new SenderThrottle(0.2f);
            t.Allow(7, 10f);

            Assert.That(t.LastReason, Is.EqualTo("sender 7 (first request): allowed"));
        }

        [Test]
        public void LastReason_RepeatRequest_ShowsTheElapsedTime()
        {
            var t = new SenderThrottle(0.2f);
            t.Allow(7, 10f);
            t.Allow(7, 10.5f);

            Assert.That(t.LastReason, Is.EqualTo("sender 7 after 0.500 s: allowed"));
        }

        [Test]
        public void LastReason_Refusal_ShowsElapsedTimeAndTheLimit()
        {
            var t = new SenderThrottle(0.2f);
            t.Allow(7, 10f);
            t.Allow(7, 10.1f);

            Assert.That(t.LastReason, Does.StartWith("sender 7 asked again after 0.100 s (< 0.20 s): refused"));
        }

        [Test]
        public void Allow_TableExactlyAtTheLimit_IsNotPruned()
        {
            var t = new SenderThrottle(0.2f);
            for (int i = 0; i < SenderThrottle.PruneAbove - 1; i++) t.Allow(i, 0f);
            t.Allow(500, SenderThrottle.PruneAfter + 5f);              // the table now holds exactly PruneAbove senders

            Assert.That(t.Count, Is.EqualTo(SenderThrottle.PruneAbove), "pruning starts only when the table holds MORE than PruneAbove");
            Assert.That(t.LastReason, Does.Not.Contain("dropped"));
        }

        [Test]
        public void Allow_PruningRun_ReportsHowManyWereDropped()
        {
            var t = new SenderThrottle(0.2f);
            for (int i = 0; i < SenderThrottle.PruneAbove; i++) t.Allow(i, 0f);
            t.Allow(900, SenderThrottle.PruneAfter + 1f);

            Assert.That(t.LastReason, Does.EndWith("allowed, " + SenderThrottle.PruneAbove + " idle senders dropped"));
        }

        [Test]
        public void Prune_SenderIdleExactlyPruneAfter_IsDropped_JustUnderIsKept()
        {
            var t = new SenderThrottle(0.2f);
            t.Allow(1, 0f);
            t.Allow(2, 0.5f);

            Assert.That(t.Prune(SenderThrottle.PruneAfter), Is.EqualTo(1), "idle for exactly PruneAfter seconds counts as idle");
            Assert.That(t.Count, Is.EqualTo(1));
        }

        [Test]
        public void Prune_NothingIdle_ReturnsZero()
        {
            var t = new SenderThrottle(0.2f);
            t.Allow(1, 10f);

            Assert.That(t.Prune(11f), Is.EqualTo(0));
            Assert.That(t.Count, Is.EqualTo(1));
        }
    }
}
