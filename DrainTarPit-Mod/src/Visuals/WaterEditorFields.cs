using UnityEngine;

namespace DrainTarPit.Visuals
{
    /// The live water editor's rows for config entries and fixed values (Tunable, T-068). Split from WaterEditor for the class size limit.
    internal static partial class WaterEditor
    {
        static void Entry(BepInEx.Configuration.ConfigEntry<float> e, string label, float lo, float hi)
        {
            if (e == null) return;
            float nv = Number("cfg." + e.Definition.Key, label, e.Value, lo, hi);
            nv = Mathf.Clamp(nv, lo, hi);
            if (!Mathf.Approximately(nv, e.Value)) { WaterEditorLog.Change(CfgName(e), WaterEditorLog.F(e.Value), WaterEditorLog.F(nv)); e.Value = nv; }
        }

        static void Entry(BepInEx.Configuration.ConfigEntry<int> e, string label, int lo, int hi)
        {
            if (e == null) return;
            int nv = Mathf.Clamp(Mathf.RoundToInt(Number("cfg." + e.Definition.Key, label, e.Value, lo, hi)), lo, hi);
            if (nv != e.Value) { WaterEditorLog.Change(CfgName(e), e.Value.ToString(Inv), nv.ToString(Inv)); e.Value = nv; }
        }

        static void Entry(BepInEx.Configuration.ConfigEntry<bool> e, string label)
        {
            if (e == null) return;
            bool nv = GUILayout.Toggle(e.Value, label);
            if (nv != e.Value) { WaterEditorLog.Change(CfgName(e), e.Value ? "on" : "off", nv ? "on" : "off"); e.Value = nv; }
        }

        // T-068: settings that are fixed in code (Tunable) are tuned the same way; nothing is saved
        static void Entry(Tunable<float> e, string label, float lo, float hi)
        {
            if (e == null) return;
            float nv = Mathf.Clamp(Number("fix." + e.Name, label, e.Value, lo, hi), lo, hi);
            if (!Mathf.Approximately(nv, e.Value)) { WaterEditorLog.Change("fixed [" + e.Section + "] " + e.Name, WaterEditorLog.F(e.Value), WaterEditorLog.F(nv)); e.Value = nv; }
        }

        static void Entry(Tunable<int> e, string label, int lo, int hi)
        {
            if (e == null) return;
            int nv = Mathf.Clamp(Mathf.RoundToInt(Number("fix." + e.Name, label, e.Value, lo, hi)), lo, hi);
            if (nv != e.Value) { WaterEditorLog.Change("fixed [" + e.Section + "] " + e.Name, e.Value.ToString(Inv), nv.ToString(Inv)); e.Value = nv; }
        }

        static void Entry(Tunable<bool> e, string label)
        {
            if (e == null) return;
            bool nv = GUILayout.Toggle(e.Value, label);
            if (nv != e.Value) { WaterEditorLog.Change("fixed [" + e.Section + "] " + e.Name, e.Value ? "on" : "off", nv ? "on" : "off"); e.Value = nv; }
        }

        static string CfgName(BepInEx.Configuration.ConfigEntryBase e)
        {
            return "config [" + e.Definition.Section + "] " + e.Definition.Key;
        }
    }
}
