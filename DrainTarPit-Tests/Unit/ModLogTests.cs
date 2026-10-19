using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using BepInEx.Logging;

namespace DrainTarPit.Tests.Unit
{
    /// <summary>
    /// ModLog is the only place the mod writes log output. Its line format and its "NEW LOG SESSION STARTS HERE" marker are what a person
    /// searches for in a BepInEx log, so they must not change by accident. Reached by reflection from the built DLL; the BepInEx logger
    /// is real, only a capturing listener is added.
    /// </summary>
    [TestFixture]
    public class ModLogTests
    {
        const string LogType = "DrainTarPit.Logging.ModLog";
        static readonly Regex TimestampedLine = new Regex(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} - (?<msg>.*)$", RegexOptions.Singleline);

        sealed class CaptureListener : ILogListener
        {
            public readonly List<string> Lines = new List<string>();
            public void LogEvent(object sender, LogEventArgs eventArgs) { Lines.Add(Convert.ToString(eventArgs.Data)); }
            public void Dispose() { }
        }

        CaptureListener _listener;
        ManualLogSource _source;

        [SetUp]
        public void SetUp()
        {
            // ModLog keeps static state (source + "session started"): reset it so every test starts like a fresh game launch
            ModAssembly.SetStatic(LogType, "_source", null);
            ModAssembly.SetStatic(LogType, "_sessionStarted", false);

            _listener = new CaptureListener();
            _source = new ManualLogSource("DtpUnitTest");
            BepInEx.Logging.Logger.Listeners.Add(_listener);
            BepInEx.Logging.Logger.Sources.Add(_source);
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

        [Test]
        public void Line_NormalMessage_HasTimestampSeparatorAndMessage()
        {
            var line = (string)ModAssembly.Call(LogType, "Line", "hello");

            var m = TimestampedLine.Match(line);
            Assert.That(m.Success, Is.True, line);
            Assert.That(m.Groups["msg"].Value, Is.EqualTo("hello"));
        }

        [Test]
        public void Line_NullMessage_UsesNullPlaceholder()
        {
            var line = (string)ModAssembly.Call(LogType, "Line", new object[] { null });

            Assert.That(TimestampedLine.Match(line).Groups["msg"].Value, Is.EqualTo("(null)"));
        }

        [Test]
        public void Line_MessageWithBracesAndPercent_IsNotTreatedAsAFormatString()
        {
            var line = (string)ModAssembly.Call(LogType, "Line", "100% {0} {broken");

            Assert.That(TimestampedLine.Match(line).Groups["msg"].Value, Is.EqualTo("100% {0} {broken"));
        }

        [Test]
        public void Line_Timestamp_UsesInvariantCultureDigits()
        {
            var old = System.Threading.Thread.CurrentThread.CurrentCulture;
            try
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("ar-SA");

                var line = (string)ModAssembly.Call(LogType, "Line", "x");

                Assert.That(TimestampedLine.IsMatch(line), Is.True, "a non-Gregorian / non-Latin-digit culture must not change the log format: " + line);
            }
            finally { System.Threading.Thread.CurrentThread.CurrentCulture = old; }
        }

        [Test]
        public void Init_FirstCall_WritesTheSessionMarkerAsFirstLine()
        {
            ModAssembly.Call(LogType, "Init", _source);

            Assert.That(_listener.Lines, Has.Count.EqualTo(1));
            Assert.That(TimestampedLine.Match(_listener.Lines[0]).Groups["msg"].Value, Is.EqualTo("NEW LOG SESSION STARTS HERE"));
        }

        [Test]
        public void Init_CalledTwice_WritesTheSessionMarkerOnlyOnce()
        {
            ModAssembly.Call(LogType, "Init", _source);
            ModAssembly.Call(LogType, "Init", _source);

            Assert.That(_listener.Lines, Has.Count.EqualTo(1));
        }

        [Test]
        public void Info_AfterInit_WritesOneTimestampedLine()
        {
            ModAssembly.Call(LogType, "Init", _source);

            ModAssembly.Call(LogType, "Info", "a message");

            Assert.That(_listener.Lines, Has.Count.EqualTo(2));
            Assert.That(TimestampedLine.Match(_listener.Lines[1]).Groups["msg"].Value, Is.EqualTo("a message"));
        }

        [Test]
        public void WarnAndError_AfterInit_AreWritten()
        {
            ModAssembly.Call(LogType, "Init", _source);

            ModAssembly.Call(LogType, "Warn", "w");
            ModAssembly.Call(LogType, "Error", "e");

            Assert.That(_listener.Lines, Has.Count.EqualTo(3));
            Assert.That(_listener.Lines[1], Does.EndWith(" - w"));
            Assert.That(_listener.Lines[2], Does.EndWith(" - e"));
        }

        [Test]
        public void Verbose_WhenDebugLogIsNotBound_WritesNothing()
        {
            ModAssembly.Call(LogType, "Init", _source);

            ModAssembly.Call(LogType, "Verbose", "chatter");
            ModAssembly.Call(LogType, "VerboseWarn", "chatter");

            Assert.That(_listener.Lines, Has.Count.EqualTo(1), "only the session marker; Verbose/VerboseWarn are silent until [General] DebugLog is on");
        }

        [Test]
        public void Info_NullMessage_DoesNotThrowAndWritesPlaceholder()
        {
            ModAssembly.Call(LogType, "Init", _source);

            Assert.DoesNotThrow(() => ModAssembly.Call(LogType, "Info", new object[] { null }));
            Assert.That(_listener.Lines[1], Does.EndWith(" - (null)"));
        }
    }
}
