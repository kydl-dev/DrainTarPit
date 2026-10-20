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
        internal static WaterStyleOption Current(out Color body, out Color glow)
        {
            body = GameBody; glow = Color.black;
            var style = PoolWater.StyleLook();
            if (style == null)
            {
                Note("Game", body, glow, "fixed blue (Game style, or the style material is not usable)");
                return WaterStyleOption.Game;
            }
            Derive(style, out body, out glow);
            return PoolWater.StyleInUse;
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
