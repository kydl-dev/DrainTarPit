using DrainTarPit.Building;
using DrainTarPit.Drain;
using DrainTarPit.Logging;
using UnityEngine;

namespace DrainTarPit.Visuals
{
    /// T-053 / T-054 (user: hose water and the nozzle stream stayed blue next to a red Ashlands pool): the one colour every water effect outside
    /// the pool uses (water in see-through hoses, the spurt, its splashes and puddle), taken from the pool water style in use. Game style = the
    /// fixed blue the effects always had.
    internal static class WaterTint
    {
        internal static readonly Color GameBody = new(0.30f, 0.55f, 0.72f, 1f);
        static string _lastKey = "";

        /// The body colour (alpha 1) and glow (black = none) of the water in use. Returns the style the colour came from (Game = the fixed blue).
        internal static WaterStyleOption Current(out Color body, out Color glow) { return Current(PoolWater.StyleInUse, out body, out glow); }

        /// The same for one given style (T-60: the style of the pool a pump moves water from / to, StyleFor).
        internal static WaterStyleOption Current(WaterStyleOption wanted, out Color body, out Color glow)
        {
            body = GameBody; glow = Color.black;
            var style = PoolWater.StyleLook(wanted);
            if (style == null)
            {
                Note("Game", body, glow, "fixed blue (" + wanted + " style" + (wanted == WaterStyleOption.Game ? "" : ", its material is not usable") + ")");
                return WaterStyleOption.Game;
            }
            Derive(style, out body, out glow);
            return wanted;
        }

        /// T-60 (user: "water in pipe does not follow the user's style"): the style of the water one pump moves, as THIS player sees the pools:
        /// the pump water pool the strainer is in (where the water comes from), else the pump water pool under the nozzle (where it goes),
        /// else the default (world style / this PC's config). Uses PoolStyles.For, so the player's own choice per pool counts.
        internal static WaterStyleOption StyleFor(Vector3? source, Vector3? dest)
        {
            var from = source.HasValue ? WaterPoolAt(source.Value) : null;
            var to = from == null && dest.HasValue ? WaterPoolAt(dest.Value) : null;
            var pool = from ?? to;
            var style = pool != null ? Signs.PoolStyles.For(pool) : PoolWater.StyleInUse;
            ModLog.Verbose("WaterTint: pump water style " + style + " (" + (from != null ? "from the pool the strainer is in, " + from.name : to != null ? "from the pool under the nozzle, " + to.name : "no pump water pool at either end: default") + ")");
            return style;
        }

        /// StyleFor a pump: its strainer tip (followed through the intake hoses) and the nozzle position.
        internal static WaterStyleOption ForPump(DtpPiece pump, Vector3 nozzle)
        {
            Vector3? src = null;
            try
            {
                var w = pump != null ? HoseNetwork.Walk(pump, "intake", DtpKind.Strainer) : null;
                if (w != null && w.Ok && w.Terminal != null) src = w.Terminal.StrainerTip;
                else ModLog.Verbose("WaterTint: pump intake chain " + (w == null ? "unknown (no pump)" : "not complete (" + w.Reason + ")") + ", only the nozzle end is used");
            }
            catch (MissingReferenceException e) { ModLog.VerboseWarn("WaterTint: a hose was removed while its chain was followed (" + e.Message + "), only the nozzle end is used"); }
            return StyleFor(src, nozzle);
        }

        /// The pump water pool at p (its grid, any depth), null = none.
        static LiquidVolume WaterPoolAt(Vector3 p)
        {
            LiquidVolume v; float depth, surface;
            return TarPits.TrySample(p, out v, out depth, out surface, LiquidType.Water) && TarPits.IsPumpPool(v) ? v : null;
        }

        /// The colour a short look into the style's water gives: light that went through 1 m of it (absorption) plus its own scattered colour,
        /// scaled so the brightest channel is 0.6 (no sky reflection outside the pool). Glow = the style's emission x2.
        static void Derive(Material style, out Color body, out Color glow)
        {
            Color a = style.HasProperty("_Absorption") ? style.GetColor("_Absorption") : new Color(0.3f, 0.1f, 0.05f);
            Color s = style.HasProperty("_ScatterColor") ? style.GetColor("_ScatterColor") : new Color(0.05f, 0.3f, 0.33f);
            var through = new Color(Mathf.Exp(-a.r * 4f), Mathf.Exp(-a.g * 4f), Mathf.Exp(-a.b * 4f));
            Color c = through * 0.6f + s * 0.6f;
            float max = Mathf.Max(c.maxColorComponent, 0.001f);
            body = c * (0.6f / max); body.a = 1f;
            glow = style.HasProperty("_Emission") ? style.GetColor("_Emission") * 2f : Color.black; glow.a = 1f;
            Note(style.name, body, glow, "absorption " + a + ", scatter " + s + ", through 1 m " + through);
        }

        // debug: one line whenever the derived colour changes (style switched, F8 tuning, reset); the same colour again is not repeated
        static void Note(string from, Color body, Color glow, string how)
        {
            string key = from + body + glow;
            if (key == _lastKey) return;
            _lastKey = key;
            ModLog.Verbose("WaterTint: water effects colour from '" + from + "': body " + body + ", glow " + glow + " (" + how + ")");
        }
    }
}
