using System.Linq;
using DrainTarPit.Drain;
using DrainTarPit.Logging;
using DrainTarPit.Pump;
using DrainTarPit.Visuals;
using Jotunn.Entities;
using UnityEngine;

namespace DrainTarPit.Debug
{
    /// Console helpers for the in-game load test (F5 console).
    ///   dtp_info                      - log bundle/shader/animator status
    ///   dtp_spawn [prefab]            - spawn a piece 3 m in front of you (default DTP_Pump_Manual)
    ///   dtp_anim <active|pump|off>    - poke the nearest pump's animator
    internal class DtpCommands : ConsoleCommand
    {
        public override string Name { get { return "dtp"; } }
        public override bool IsCheat { get { return true; } }       // only with devcommands enabled
        public override string Help { get { return "TarPitDrain test: dtp info | dtp spawn [prefab] | dtp anim <active|pump|off> | dtp pits | dtp clips | dtp state | dtp ptest <watch|pour|tank|matrix|rim|fields|out|stop> (water pool tests, see _agent/TESTLOG.md)"; } }

        public override void Run(string[] args)
        {
            var sub = args.Length > 0 ? args[0].ToLowerInvariant() : "info";
            switch (sub)
            {
                case "info": Info(); break;
                case "spawn": Spawn(args.Length > 1 ? args[1] : "DTP_Pump_Manual"); break;
                case "anim": Anim(args.Length > 1 ? args[1].ToLowerInvariant() : ""); break;
                case "pits": Pits(); break;
                case "state": State(); break;
                case "ptest": PoolTests.Run(args.Skip(1).ToArray()); break;
                case "clips": Print(PlayerPumpAnimations.Describe(Player.m_localPlayer)); break;
                default: Print(Help); break;
            }
        }

        static void Print(string s) { Console.instance.Print(s); ModLog.Info(s); }

        static void Info()
        {
            var shader = MaterialFixer.ResolveShader();
            Print("Bundle: " + (Plugin.Bundle != null ? "loaded" : "NOT loaded") + ", shader: " + (shader != null ? shader.name : "none"));
            foreach (var n in new[] { "DTP_Pump_Manual", "DTP_Hose_Straight_1m", "DTP_Hose_Straight_2m", "DTP_Hose_Elbow90", "DTP_Hose_IntakeStrainer", "DTP_Hose_OutletNozzle" })
                Print("  " + n + ": " + (ZNetScene.instance != null && ZNetScene.instance.GetPrefab(n) != null ? "in ZNetScene" : "MISSING"));
            var pump = Object.FindObjectsByType<PumpController>(FindObjectsSortMode.None).FirstOrDefault();
            Print("  pump animator: " + (pump != null ? pump.DescribeAnimator() : "(no pump in the world)"));
            foreach (var c in new[] { "pumpenter", "pumpidle", "pumppush", "pumppull", "pumpcycle", "pumpexit" })
                Print("  player clip " + c + ": " + (PlayerPumpAnimations.Get(c) != null ? "ok" : "MISSING"));
        }

        static void Spawn(string name)
        {
            var p = Player.m_localPlayer;
            if (p == null) { Print("No local player."); return; }
            var prefab = ZNetScene.instance.GetPrefab(name);
            if (prefab == null) { Print("Prefab not found: " + name); return; }
            var pos = p.transform.position + p.transform.forward * 3f + Vector3.up * 0.5f;
            Object.Instantiate(prefab, pos, Quaternion.Euler(0f, p.transform.eulerAngles.y + 180f, 0f));
            Print("Spawned " + name);
        }

        static void Pits()
        {
            var p = Player.m_localPlayer;
            if (p != null)
                Print("  player liquid: tarLevel=" + CharField(p, "m_tarLevel") + " waterLevel=" + CharField(p, "m_waterLevel") + " liquidLevel=" + CharField(p, "m_liquidLevel") +
                      " cachedDepth=" + CharField(p, "m_cashedInLiquidDepth") + " y=" + p.transform.position.y.ToString("0.00"));
            if (TarPits.All.Count == 0) { Print("No loaded tar pits."); return; }
            foreach (var v in TarPits.All.Where(x => x != null).OrderBy(x => p != null ? Vector3.Distance(x.transform.position, p.transform.position) : 0f))
            {
                var nv = TarPits.Nview(v); var zdo = nv != null && nv.IsValid() ? nv.GetZDO() : null;
                float baseline = zdo != null ? zdo.GetFloat(TarPits.VarBaseline, 0f) : 0f;
                Print("  " + v.name + " @ " + v.transform.position.ToString("0") + (p != null ? " (" + Vector3.Distance(v.transform.position, p.transform.position).ToString("0") + " m)" : "") +
                    ": " + TarPits.TotalVolume(v).ToString("0.0") + " m3" + (baseline > 0f ? " of " + baseline.ToString("0.0") + " (drained before)" : ""));
                if (p != null) Print("      " + TarPitDebug.DescribeAt(v, p.transform.position) + ", InTar=" + PlayerInTar(p));
            }
        }

        static string CharField(Character c, string name)
        {
            var f = HarmonyLib.AccessTools.Field(typeof(Character), name);
            var v = f != null ? f.GetValue(c) : null;
            return v is float ? ((float)v).ToString("0.00") : (v != null ? v.ToString() : "?");
        }

        // Character.InTar is not public
        static string PlayerInTar(Player p)
        {
            var m = HarmonyLib.AccessTools.Method(typeof(Character), "InTar");
            return m != null ? m.Invoke(p, null).ToString() : "?";
        }

        static void State()
        {
            var p = Player.m_localPlayer;
            var pump = Object.FindObjectsByType<PumpController>(FindObjectsSortMode.None).OrderBy(x => p != null ? Vector3.Distance(x.transform.position, p.transform.position) : 0f).FirstOrDefault();
            Print(pump == null ? "No pump loaded." : "Pump state: " + pump.DescribeState());
        }

        static void Anim(string what)
        {
            var p = Player.m_localPlayer;
            if (p == null) { Print("No local player."); return; }
            var pump = Object.FindObjectsByType<PumpController>(FindObjectsSortMode.None).OrderBy(x => Vector3.Distance(x.transform.position, p.transform.position)).FirstOrDefault();
            if (pump == null) { Print("No pump nearby."); return; }
            switch (what)
            {
                case "active": pump.SetActive(true); break;
                case "off": pump.SetActive(false); break;
                case "pump": pump.Stroke(); break;
                default: Print("dtp anim <active|pump|off>"); return;
            }
            Print("Pump: " + what);
        }
    }
}
