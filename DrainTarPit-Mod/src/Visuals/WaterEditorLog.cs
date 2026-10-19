using System.Collections.Generic;
using System.Globalization;
using DrainTarPit.Logging;
using UnityEngine;

namespace DrainTarPit.Visuals
{
    /// <summary>
    /// Writes every change made in the water editor (WaterEditor, F8) to the BepInEx log at once (PLAN T-018, user spec W-8).
    /// One line per change: "WaterEditor: &lt;what&gt; &lt;old&gt; -> &lt;new&gt;". Buttons and the window opening / closing are one line each (Event).
    ///
    /// A slider drag (or typing in a number field) changes the value many times a second. For each control the first change is logged at
    /// once; further changes within Interval seconds are only remembered. The remembered value is logged when Interval has passed
    /// (Change), when the control has not changed for Interval seconds (Tick) or when the mouse button is released (Flush): that line is the
    /// final value. The "old" of that line is the value of the control's last logged line, so a drag reads as a chain of old -> new steps.
    /// ModLog.Info is used, so the lines are written whether or not [General] DebugLog is on.
    /// NOT tested in game.
    /// </summary>
    internal static class WaterEditorLog
    {
        const float Interval = 0.25f;
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        sealed class Pending
        {
            public string Old;        // value of the last logged line (or the value before the first change)
            public string Held;       // newest value that is not logged yet, null when nothing is waiting
            public float LastLog;     // unscaled time of the last logged line
            public float HeldAt;      // unscaled time of the newest change
        }

        static readonly Dictionary<string, Pending> Controls = new();
        static readonly List<string> Keys = new();

        public static string F(float v) { return v.ToString("0.####", Inv); }
        public static string F(Vector2 v) { return "(" + F(v.x) + ", " + F(v.y) + ")"; }
        public static string F(Vector4 v) { return "(" + F(v.x) + ", " + F(v.y) + ", " + F(v.z) + ", " + F(v.w) + ")"; }
        public static string F(Color c) { return "(" + F(c.r) + ", " + F(c.g) + ", " + F(c.b) + ", " + F(c.a) + ")"; }

        /// A control changed its value. <paramref name="what"/> names the control (the same text every time for the same control).
        public static void Change(string what, string oldValue, string newValue)
        {
            float now = Time.unscaledTime;
            Pending p;
            if (!Controls.TryGetValue(what, out p))
            {
                p = new Pending { Old = oldValue, LastLog = float.NegativeInfinity };
                Controls[what] = p;
            }
            p.HeldAt = now;
            if (now - p.LastLog >= Interval)
            {
                Write(what, p.Old, newValue);
                p.Old = newValue; p.Held = null; p.LastLog = now;
            }
            else p.Held = newValue;
        }

        /// Something that is not a value change: a button, a keyword switch, the window opening / closing. Logged at once.
        public static void Event(string text)
        {
            ModLog.Info("WaterEditor: " + text);
        }

        /// Every frame while the editor is open (WaterEditor.Tick): logs the final value of controls that stopped changing and forgets idle controls.
        public static void Tick()
        {
            if (Controls.Count == 0) return;
            float now = Time.unscaledTime;
            Keys.Clear();
            foreach (var kv in Controls)
            {
                var p = kv.Value;
                if (p.Held != null && now - p.HeldAt >= Interval)
                {
                    Write(kv.Key, p.Old, p.Held);
                    p.Old = p.Held; p.Held = null; p.LastLog = now;
                }
                if (p.Held == null && now - p.LastLog >= Interval) Keys.Add(kv.Key);   // idle: the next change starts from the real value again
            }
            for (int i = 0; i < Keys.Count; i++) Controls.Remove(Keys[i]);
        }

        /// The mouse button was released or the window closed: the remembered values are the final values, log them now.
        public static void Flush()
        {
            if (Controls.Count == 0) return;
            foreach (var kv in Controls)
                if (kv.Value.Held != null) Write(kv.Key, kv.Value.Old, kv.Value.Held);
            Controls.Clear();
        }

        static void Write(string what, string oldValue, string newValue)
        {
            ModLog.Info("WaterEditor: " + what + " " + oldValue + " -> " + newValue);
        }
    }
}
