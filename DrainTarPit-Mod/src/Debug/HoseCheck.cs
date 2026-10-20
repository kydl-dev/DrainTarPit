using System.Text;
using DrainTarPit.Building;
using DrainTarPit.Logging;
using DrainTarPit.Visuals;
using UnityEngine;

namespace DrainTarPit.Debug
{
    /// <summary>
    /// Console `dtp hosecheck` (T-44, user: World Hose Style = Solid but the hoses still look transparent; the log said every hose was set Solid).
    /// Dumps what is really on the hose you look at: its stored build look, the world setting, and for every renderer every material (name, shader,
    /// blend mode, render queue, colour alpha, whether it is one of our see-through copies) plus the colour in the renderer's property block (the
    /// game's MaterialMan writes colours there, e.g. the hammer highlight). That shows whether the transparency comes from our material or elsewhere.
    /// </summary>
    internal static class HoseCheck
    {
        const float Reach = 12f;

        /// Expected outcome: a report about one hose (the returned text says which), or why none was found.
        internal static string Run()
        {
            var p = Player.m_localPlayer;
            if (p == null || GameCamera.instance == null) return "hosecheck: no local player / camera";
            DtpPiece piece = null;
            var cam = GameCamera.instance.transform;
            RaycastHit hit;
            if (Physics.Raycast(cam.position, cam.forward, out hit, 50f, ~0, QueryTriggerInteraction.Ignore) && hit.collider != null)
                piece = hit.collider.GetComponentInParent<DtpPiece>();
            string how = "at the crosshair";
            if (piece == null || (piece.Kind != DtpKind.Hose && piece.Kind != DtpKind.Elbow))
            {
                piece = null; float best = Reach; how = "nearest to you";
                foreach (var d in DtpPiece.All)
                {
                    if (d == null || (d.Kind != DtpKind.Hose && d.Kind != DtpKind.Elbow)) continue;
                    float dist = Vector3.Distance(d.transform.position, p.transform.position);
                    if (dist < best) { best = dist; piece = d; }
                }
            }
            if (piece == null) return "hosecheck: no hose at the crosshair or within " + Reach + " m";

            var sb = new StringBuilder();
            var nv = piece.GetComponent<ZNetView>();
            var z = nv != null && nv.IsValid() ? nv.GetZDO() : null;
            sb.Append("hosecheck " + piece.name + " (" + how + ") @ " + piece.transform.position.ToString("0.0") + ": stored build look " +
                (z == null ? "no ZDO" : z.GetInt(DtpPiece.ZHoseStyle, -1) < 0 ? "none" : ((HoseStyleOption)z.GetInt(DtpPiece.ZHoseStyle, -1)) + " " + z.GetFloat(DtpPiece.ZHoseOpacity, -1f).ToString("0.00")) +
                ", World Hose Style " + ModSettings.WorldHoseStyle.Value + " " + ModSettings.WorldHoseOpacity.Value.ToString("0.00") +
                ", Build Hose Style " + ModSettings.BuildHoseStyle.Value + ", LODGroup " + (piece.GetComponentInChildren<LODGroup>(true) != null));
            var block = new MaterialPropertyBlock();
            int n = 0;
            foreach (var r in piece.GetComponentsInChildren<Renderer>(true))
            {
                n++;
                sb.Append("\n  renderer " + n + " '" + r.name + "' enabled " + r.enabled + ", visible " + r.isVisible);
                bool hasBlock = r.HasPropertyBlock();
                if (hasBlock) { r.GetPropertyBlock(block); sb.Append(", property block _Color " + (block.HasColor("_Color") ? block.GetColor("_Color").ToString() : "not set")); }
                else sb.Append(", no property block");
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null) { sb.Append("\n    [" + i + "] null"); continue; }
                    sb.Append("\n    [" + i + "] " + m.name + " shader " + (m.shader != null ? m.shader.name : "none") + ", mode " + (m.HasProperty("_Mode") ? m.GetFloat("_Mode").ToString("0") : "-") +
                        ", queue " + m.renderQueue + ", colour alpha " + (m.HasProperty("_Color") ? m.color.a.ToString("0.00") : "-") + ", ALPHABLEND " + m.IsKeywordEnabled("_ALPHABLEND_ON") +
                        ", our see-through copy " + MaterialFixer.IsClearCopy(m));
                }
            }
            if (n == 0) sb.Append("\n  no renderer");
            var text = sb.ToString();
            ModLog.Info(text);
            return text;
        }
    }
}
