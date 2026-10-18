using System;
using System.Globalization;
using BepInEx.Logging;

namespace DrainTarPit.Logging
{
    /// <summary>
    /// The ONLY place in the mod that writes log output. Every other file calls ModLog.Info / Warn / Error / Verbose / VerboseWarn
    /// and never touches a BepInEx logger directly, so there is exactly one place to change how logs look or where they go.
    ///
    /// Every line carries the date and time (see LineFormat). The first line of a session is
    ///     {date&amp;time} - NEW LOG SESSION STARTS HERE
    /// and is written by Init (called first thing in Plugin.Awake), so the start of each game launch is easy to find in a log.
    ///
    /// To change the look of the logs edit ONLY the three constants in the "look" block below.
    /// </summary>
    internal static class ModLog
    {
        // ---- look of a log line: edit here and nowhere else ------------------------------------------------------------
        const string TimeFormat = "yyyy-MM-dd HH:mm:ss.fff";      // date & time
        const string LineFormat = "{0} - {1}";                    // {0} = date & time, {1} = the message
        const string SessionText = "NEW LOG SESSION STARTS HERE"; // the first line of every session (uses LineFormat too)
        // ----------------------------------------------------------------------------------------------------------------

        static ManualLogSource _source;
        static bool _sessionStarted;

        /// Call once, first thing in Plugin.Awake. Writes the "NEW LOG SESSION STARTS HERE" line.
        public static void Init(ManualLogSource source)
        {
            _source = source;
            StartSession();
        }

        static void StartSession()
        {
            if (_sessionStarted) return;
            _sessionStarted = true;
            Info(SessionText);
        }

        // Falls back to a source of our own when something logs before Init (so no line is ever lost or throws).
        static ManualLogSource Source
        {
            get
            {
                if (_source == null) _source = Logger.CreateLogSource(Plugin.ModName);
                return _source;
            }
        }

        static string Line(string message)
        {
            return string.Format(CultureInfo.InvariantCulture, LineFormat, DateTime.Now.ToString(TimeFormat, CultureInfo.InvariantCulture), message ?? "(null)");
        }

        public static void Info(string message) { Write(LogLevel.Info, message); }
        public static void Warn(string message) { Write(LogLevel.Warning, message); }
        public static void Error(string message) { Write(LogLevel.Error, message); }

        // The single line that actually writes. A failure while logging is swallowed: logging must never take the game down.
        static void Write(LogLevel level, string message)
        {
            try { Source.Log(level, Line(message)); }
            catch { }
        }

        /// Info that is only written when [General] DebugLog is on.
        public static void Verbose(string message) { if (DebugOn) Info(message); }
        /// Warning that is only written when [General] DebugLog is on.
        public static void VerboseWarn(string message) { if (DebugOn) Warn(message); }

        static bool DebugOn { get { return ModSettings.DebugLog != null && ModSettings.DebugLog.Value; } }
    }
}
