using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using BepInEx.Logging;

namespace DrainTarPit.Tests.Unit
{
    /// <summary>
    /// The shared ModLog (Kydl.Common, compiled into the mod): line format, session marker, debug switch and the 2.5 s repeat filter
    /// (user standards). Reached by reflection in the built DLL; the BepInEx logger is real, only a capturing listener is added.
    /// </summary>
    [TestFixture]
    public class ModLogTests
    {
        const string LogType = "Kydl.Common.ModLog";
        static readonly Regex TimestampedLine = new Regex(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} - (?<msg>.*)$", RegexOptions.Singleline);

        sealed class CaptureListener : ILogListener
        {
            public readonly List<string> Lines = new List<string>();
            public void LogEvent(object sender, LogEventArgs eventArgs) { Lines.Add(Convert.ToString(eventArgs.Data)); }
            public void Dispose() { }
        }

        CaptureListener _listener;
        ManualLogSource _source;
        bool _debug;

        [SetUp]
        public void SetUp()
        {
            ModAssembly.SetStatic(LogType, "_source", null);
            ModAssembly.SetStatic(LogType, "_sessionStarted", false);
            ModAssembly.Call(LogType, "ResetRepeats");
            _debug = false;
            _listener = new CaptureListener();
            _source = new ManualLogSource("DrainTarPitUnitTest");
            BepInEx.Logging.Logger.Listeners.Add(_listener);
            BepInEx.Logging.Logger.Sources.Add(_source);
            ModAssembly.Call(LogType, "Init", _source, "DrainTarPit", (Func<bool>)(() => _debug));
        }

        [TearDown]
        public void TearDown()
        {
            BepInEx.Logging.Logger.Sources.Remove(_source);
            BepInEx.Logging.Logger.Listeners.Remove(_listener);
            _source.Dispose();
            _listener.Dispose();
            ModAssembly.SetStatic(LogType, "_source", null);
            ModAssembly.SetStatic(LogType, "_sessionStarted", false);
        }

        static string Msg(string line) { return TimestampedLine.Match(line).Groups["msg"].Value; }

        [Test]
        public void Init_FirstCall_WritesTheSessionMarkerAsFirstLine()
        {
            Assert.That(_listener.Lines, Has.Count.EqualTo(1));
            Assert.That(Msg(_listener.Lines[0]), Is.EqualTo("NEW LOG SESSION STARTS HERE"));
        }

        [Test]
        public void Line_MessageWithBraces_IsNotTreatedAsAFormatString()
        {
            var line = (string)ModAssembly.Call(LogType, "Line", "100% {0} {broken");

            Assert.That(Msg(line), Is.EqualTo("100% {0} {broken"));
        }

        [Test]
        public void Info_SameMessageTwiceAtOnce_IsWrittenOnce()
        {
            ModAssembly.Call(LogType, "Info", "same");
            ModAssembly.Call(LogType, "Info", "same");

            Assert.That(_listener.Lines, Has.Count.EqualTo(2), "session marker + one 'same'");
        }

        [Test]
        public void Error_SameMessageTwice_IsAlwaysWritten()
        {
            ModAssembly.Call(LogType, "Error", "boom");
            ModAssembly.Call(LogType, "Error", "boom");

            Assert.That(_listener.Lines, Has.Count.EqualTo(3));
        }

        [Test]
        public void Verbose_DebugOff_WritesNothing()
        {
            ModAssembly.Call(LogType, "Verbose", "chatter");
            ModAssembly.Call(LogType, "VerboseWarn", "chatter");

            Assert.That(_listener.Lines, Has.Count.EqualTo(1), "only the session marker");
        }

        [Test]
        public void Verbose_DebugOn_IsWritten()
        {
            _debug = true;

            ModAssembly.Call(LogType, "Verbose", "chatter");

            Assert.That(_listener.Lines, Has.Count.EqualTo(2));
            Assert.That(Msg(_listener.Lines[1]), Is.EqualTo("chatter"));
        }

        [Test]
        public void Info_NullMessage_WritesPlaceholder()
        {
            Assert.DoesNotThrow(() => ModAssembly.Call(LogType, "Info", new object[] { null }));
            Assert.That(_listener.Lines[1], Does.EndWith(" - (null)"));
        }

        // ---- expected-value checks (user, 2026-10-24): off = WARNING always written, inside = debug line only

        [Test]
        public void Check_ValueInsideTolerance_ReturnsInsideAndWritesNothingWithDebugOff()
        {
            var inside = (bool)ModAssembly.Call(LogType, "Check", "bone turn", 24.0, 24.05, 2.0, "deg");

            Assert.That(inside, Is.True);
            Assert.That(_listener.Lines, Has.Count.EqualTo(1), "only the session line");
        }

        [Test]
        public void Check_ValueOutsideTolerance_WritesWarningWithExpectedValueEvenWithDebugOff()
        {
            var inside = (bool)ModAssembly.Call(LogType, "Check", "coil mismatch", 0.5, 0.0, 0.01, "m");

            Assert.That(inside, Is.False);
            Assert.That(_listener.Lines, Has.Count.EqualTo(2));
            Assert.That(Msg(_listener.Lines[1]), Is.EqualTo("CHECK OFF: coil mismatch = 0.5 m (expected 0 +- 0.01 m), off by 0.5 m"));
        }

        [Test]
        public void CheckRange_ValueAboveMax_WritesWarning()
        {
            var inside = (bool)ModAssembly.Call(LogType, "CheckRange", "scale", 1.5, 0.9, 1.1, "");

            Assert.That(inside, Is.False);
            Assert.That(Msg(_listener.Lines[1]), Is.EqualTo("CHECK OFF: scale = 1.5 (expected 0.9 .. 1.1)"));
        }

        [Test]
        public void Check_NaN_CountsAsOff()
        {
            Assert.That((bool)ModAssembly.Call(LogType, "Check", "x", double.NaN, 1.0, 1.0, "m"), Is.False);
        }
    }
}
