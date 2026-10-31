using Kydl.Common;

namespace DrainTarPit.Testing
{
    /// <summary>
    /// KydlCore D-084: hooks for DrakesTestLab suites (DrainTarPit-Tests/TestLab/suites/DrainTarPit). A hook is a static method with [TestHook("name")]; DrakesTestLab finds it by the
    /// attribute's NAME (the mod does not reference DrakesTestLab). Hook names carry the mod id so two mods never clash.
    /// </summary>
    internal static class TestHooks
    {
        /// <summary>All Startup_* / World_* checks of InGameTests: "PASS: ..." or "FAIL: ...".</summary>
        [TestHook("DrainTarPit.selftest")]
        static string SelfTest() { return TestLabSupport.RunInGameTests(typeof(InGameTests)); }
    }
}