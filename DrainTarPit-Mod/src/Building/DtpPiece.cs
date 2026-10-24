using System.Collections.Generic;
using DrainTarPit.Drain;
using Kydl.Common;
using DrainTarPit.Visuals;
using UnityEngine;

namespace DrainTarPit.Building
{
    public enum DtpKind { Other, Pump, Strainer, Nozzle, Hose, Elbow }

    /// <summary>
    /// Marks a placed bundle piece and keeps a registry of all placed pieces, so the hose chain can be derived from
    /// snap point positions (nothing needs to be stored: positions/rotations are already saved with every piece).
    /// Placement ghosts have no valid ZNetView and are never registered.
    /// </summary>
    public class DtpPiece : MonoBehaviour
    {
        public static readonly List<DtpPiece> All = new();

        /// Local Z distance from the strainer origin (hose side) to the end that sits in the tar.
        public const float StrainerLength = 0.9f;
        /// Local Z distance from the nozzle origin to the opening.
        public const float NozzleLength = 0.46f;   // the nozzle model ends at local z = -0.46 (measured in the Unity prefab)

        public DtpKind Kind { get; private set; }
        public readonly List<Transform> Snaps = new();

        void Awake()
        {
            Kind = KindOf(name);
            foreach (Transform t in transform)
                if (t.name.StartsWith("snappoint")) Snaps.Add(t);
        }

        /// ZDO key on a strainer: the liquid it was locked to (PumpLiquid as int, 0 = not locked yet).
        public const string ZLiquid = "DTP_liquid", ZLiquidOld = "TPD_liquid";      // old key: worlds saved before the rename (ZdoKeys)

        /// ZDO keys on a hose / elbow: the look it was built with (HoseStyleOption as int, HoseOpacity). Absent = not stamped yet.
        public const string ZHoseStyle = "DTP_hoseStyle";
        public const string ZHoseOpacity = "DTP_hoseOpacity";

        void Start()
        {
            var nv = GetComponent<ZNetView>();
            if (nv != null && nv.IsValid() && !All.Contains(this))
            {
                All.Add(this);
                if (Kind == DtpKind.Strainer) LockAtBuild(nv);
                else if (Kind == DtpKind.Hose || Kind == DtpKind.Elbow) KeepHoseLook(nv);
            }
        }

        // [Build Time Hose Visuals] Build Hose Style / Opacity decide the look of hoses built from now on. A hose is created from the prefab, which always
        // has the CURRENT build look, so the builder's client (the owner of a new piece) stamps that look into the ZDO once - the stamp is never written
        // again by anything (user, 2026-10-20). Every load of the piece (restart, other clients) and every change of [World Hose Visuals] then shows it
        // through ShowHoseLook. A piece without a stamp that this client does not own keeps the prefab look until its owner stamps it.
        void KeepHoseLook(ZNetView nv)
        {
            try
            {
                var z = nv.GetZDO();
                if (z == null) { ModLog.Verbose("Hose " + name + ": no ZDO, look not stamped"); return; }
                if (z.GetInt(ZHoseStyle, -1) < 0 && nv.IsOwner())
                {
                    z.Set(ZHoseStyle, (int)ModSettings.BuildHoseStyle.Value);
                    z.Set(ZHoseOpacity, ModSettings.BuildHoseOpacity.Value);
                    ModLog.Verbose("Hose " + name + ": built with " + ModSettings.BuildHoseStyle.Value + " / opacity " + ModSettings.BuildHoseOpacity.Value.ToString("0.00") + " (stored for good)");
                }
                ShowHoseLook();
            }
            catch (System.Exception e) { ModLog.VerboseWarn("KeepHoseLook: " + e.Message); }
        }

        /// Shows this hose as [World Hose Visuals] says: Default = the look stored at build time, Transparent / Solid = that for every hose (World Hose
        /// Opacity). Reads the stamp only, never writes it. Expected outcome: the look that was asked for was resolved (false = nothing to show yet).
        internal bool ShowHoseLook()
        {
            if (Kind != DtpKind.Hose && Kind != DtpKind.Elbow) return Kind == DtpKind.Hose || Kind == DtpKind.Elbow;
            var nv = GetComponent<ZNetView>();
            var z = nv != null && nv.IsValid() ? nv.GetZDO() : null;
            int stamped = z != null ? z.GetInt(ZHoseStyle, -1) : -1;
            var world = ModSettings.WorldHoseStyle.Value;
            HoseStyleOption style; float opacity; string why;
            if (world == WorldHoseStyleOption.Transparent) { style = HoseStyleOption.Transparent; opacity = ModSettings.WorldHoseOpacity.Value; why = "World Hose Style Transparent"; }
            else if (world == WorldHoseStyleOption.Solid) { style = HoseStyleOption.Solid; opacity = 1f; why = "World Hose Style Solid"; }
            else if (stamped >= 0) { style = (HoseStyleOption)stamped; opacity = z.GetFloat(ZHoseOpacity, ModSettings.BuildHoseOpacity.Value); why = "its own build look"; }
            else
            {   // not stamped yet (its owner stamps it): show what the prefab gives, i.e. the current build look (also undoes an earlier World override)
                style = ModSettings.BuildHoseStyle.Value; opacity = ModSettings.BuildHoseOpacity.Value; why = "no build look stored yet, current build look";
            }
            int changed = MaterialFixer.ApplyLook(gameObject, style, opacity);
            ModLog.Verbose("Hose " + name + ": shown " + style + (style == HoseStyleOption.Transparent ? " " + opacity.ToString("0.00") : "") + " (" + why + ", " + changed + " renderer(s) changed)");
            return changed >= 0;
        }

        /// [World Hose Visuals] changed: every loaded hose is shown again. Returns how many hoses were shown.
        internal static int ShowAllHoseLooks()
        {
            int n = 0;
            foreach (var p in All.ToArray())
            {
                if (p == null || (p.Kind != DtpKind.Hose && p.Kind != DtpKind.Elbow)) continue;
                try { if (p.ShowHoseLook()) n++; }
                catch (System.Exception e) { ModLog.VerboseWarn("ShowHoseLook " + p.name + ": " + e.Message); }
            }
            return n;
        }

        /// The liquid this strainer is locked to (strainers only). None = not locked yet: it was built on dry ground / before the water update.
        public PumpLiquid Liquid
        {
            get
            {
                var nv = GetComponent<ZNetView>();
                var z = nv != null && nv.IsValid() ? nv.GetZDO() : null;
                return z != null ? (PumpLiquid)DrainTarPit.Drain.ZdoKeys.Int(z, ZLiquid, ZLiquidOld) : PumpLiquid.None;
            }
        }

        /// Locks the strainer to `liquid` for good (the first lock wins; later calls do nothing). Writes the owner's ZDO, so ownership is taken when needed.
        public bool LockLiquid(PumpLiquid liquid)
        {
            if (liquid == PumpLiquid.None) return false;
            var nv = GetComponent<ZNetView>();
            if (nv == null || !nv.IsValid()) return false;
            if (!nv.IsOwner()) nv.ClaimOwnership();
            var z = nv.GetZDO();
            if (z == null || DrainTarPit.Drain.ZdoKeys.Int(z, ZLiquid, ZLiquidOld) != 0) return false;
            z.Set(ZLiquid, (int)liquid);
            ModLog.Verbose("Strainer " + name + " locked to " + liquid);
            return true;
        }

        // A freshly placed strainer is owned by the player who built it and its ZDO has no lock yet: what its tip sits in right now is what its whole
        // pipe system will move. When the tip is in neither (dry ground), or the pit is not loaded yet (a strainer from an older world), it stays unlocked
        // and locks on its first real stroke (DrainService.Stroke).
        void LockAtBuild(ZNetView nv)
        {
            try
            {
                if (!nv.IsOwner()) return;
                var z = nv.GetZDO();
                if (z == null || DrainTarPit.Drain.ZdoKeys.Int(z, ZLiquid, ZLiquidOld) != 0) return;
                var found = Liquids.Detect(StrainerTip);
                if (found != PumpLiquid.None) LockLiquid(found);
            }
            catch (System.Exception e) { ModLog.VerboseWarn("LockAtBuild: " + e.Message); }
        }

        void OnDestroy() { All.Remove(this); }

        public static DtpKind KindOf(string n)
        {
            if (n.Contains("Pump")) return DtpKind.Pump;
            if (n.Contains("IntakeStrainer")) return DtpKind.Strainer;
            if (n.Contains("OutletNozzle")) return DtpKind.Nozzle;
            if (n.Contains("Elbow")) return DtpKind.Elbow;
            if (n.Contains("Hose")) return DtpKind.Hose;
            return DtpKind.Other;
        }

        /// World position of the end of the strainer that has to be under the tar.
        public Vector3 StrainerTip { get { return transform.TransformPoint(0f, 0f, -StrainerLength); } }
        /// World position of the nozzle opening.
        public Vector3 NozzleTip { get { return transform.TransformPoint(0f, 0f, -NozzleLength); } }
        /// World direction the liquid leaves the nozzle (the tip is at local -Z).
        public Vector3 NozzleDir { get { return -transform.forward; } }
    }
}