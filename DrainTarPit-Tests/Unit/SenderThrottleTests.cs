using DrainTarPit.Drain;

namespace DrainTarPit.Tests.Unit
{
    /// <summary>
    /// The per-sender rate limit of the drain / deposit RPCs (SenderThrottle, bug #9: the old dictionaries were never pruned).
    /// </summary>
    [TestFixture]
    public class SenderThrottleTests
    {
        [Test]
        public void FirstRequest_IsAllowed()
        {
            var t = new SenderThrottle(0.2f);
            Assert.That(t.Allow(1, 10f), Is.True);
            Assert.That(t.Count, Is.EqualTo(1));
        }

        [Test]
        public void SecondRequestWithinGap_IsRefused_AfterGap_IsAllowed()
        {
            var t = new SenderThrottle(0.2f);
            Assert.That(t.Allow(1, 10f), Is.True);
            Assert.That(t.Allow(1, 10.1f), Is.False);
            Assert.That(t.LastReason, Does.Contain("refused"));
            Assert.That(t.Allow(1, 10.25f), Is.True);
        }

        [Test]
        public void RefusedRequest_DoesNotMoveTheLastTime()
        {
            var t = new SenderThrottle(0.2f);
            t.Allow(1, 10f);
            t.Allow(1, 10.15f);                                      // refused: must not count as the latest request
            Assert.That(t.Allow(1, 10.21f), Is.True);
        }

        [Test]
        public void DifferentSenders_DoNotLimitEachOther()
        {
            var t = new SenderThrottle(0.2f);
            Assert.That(t.Allow(1, 10f), Is.True);
            Assert.That(t.Allow(2, 10f), Is.True);
            Assert.That(t.Count, Is.EqualTo(2));
        }

        [Test]
        public void IdleSenders_ArePruned_WhenTheTableGrows()
        {
            var t = new SenderThrottle(0.2f);
            for (int i = 0; i < SenderThrottle.PruneAbove; i++) t.Allow(i, 0f);
            Assert.That(t.Count, Is.EqualTo(SenderThrottle.PruneAbove));
            t.Allow(999, SenderThrottle.PruneAfter + 1f);           // one more sender, long after the others: they are dropped
            Assert.That(t.Count, Is.EqualTo(1));
            Assert.That(t.LastReason, Does.Contain("idle senders dropped"));
        }

        [Test]
        public void ActiveSenders_AreKept_WhenTheTableGrows()
        {
            var t = new SenderThrottle(0.2f);
            for (int i = 0; i <= SenderThrottle.PruneAbove; i++) t.Allow(i, 5f);
            Assert.That(t.Count, Is.EqualTo(SenderThrottle.PruneAbove + 1));
        }

        [Test]
        public void Prune_ReturnsTheNumberDropped()
        {
            var t = new SenderThrottle(0.2f);
            t.Allow(1, 0f); t.Allow(2, 0f); t.Allow(3, 100f);
            Assert.That(t.Prune(100f), Is.EqualTo(2));
            Assert.That(t.Count, Is.EqualTo(1));
        }
    }
}
