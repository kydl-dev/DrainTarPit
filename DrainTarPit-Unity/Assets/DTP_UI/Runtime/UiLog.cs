using System;

namespace DrainTarPit.Ui
{
    /// <summary>
    /// T-56R: the UI code lives in the Unity project (Assets/DTP_UI/Runtime, previewed there) and is compiled into the mod as well. It logs
    /// through these hooks: in Unity they write to the console, in the game the mod points them at its own log (debug-only lines through
    /// ModLog.Verbose, so they follow [General] DebugLog and the 2.5 s duplicate filter).
    /// </summary>
    public static class UiLog
    {
        public static Action<string> VerboseSink = s => UnityEngine.Debug.Log("[DTP UI] " + s);
        public static Action<string> WarnSink = s => UnityEngine.Debug.LogWarning("[DTP UI] " + s);
        public static Action<string> ErrorSink = s => UnityEngine.Debug.LogError("[DTP UI] " + s);

        public static void Verbose(string s) { VerboseSink?.Invoke(s); }
        public static void Warn(string s) { WarnSink?.Invoke(s); }
        public static void Error(string s) { ErrorSink?.Invoke(s); }
    }
}
