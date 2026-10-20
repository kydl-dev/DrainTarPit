using System.Collections.Generic;
using DrainTarPit.Drain;
using DrainTarPit.Logging;
using DrainTarPit.TextLocal;
using TMPro;
using UnityEngine;

namespace DrainTarPit.Signs
{
    /// <summary>
    /// T-023 (user, 2026-10-20): the pool sign. Anyone in the world can name it (E opens PoolSignMenu); the name and its look (SignStyle) are
    /// stored in the sign's ZDO, so every player sees the same sign. The pump pool it stands on / next to is the pool it names
    /// (looked up within PoolReach of the post). Replaces the game's Sign component on the cloned prefab (PoolSignPiece).
    /// </summary>
    public class PoolSign : MonoBehaviour, Hoverable, Interactable
    {
        internal const string KeyName = "DTP_signName", KeyStyle = "DTP_signStyle";
        internal const int MaxName = 40;
        const float PoolReach = 2.5f;

        internal static readonly List<PoolSign> Loaded = new();

        ZNetView _nview;
        TMP_Text _text;
        TMP_FontAsset _defFont;
        float _defMin = 1f, _defMax = 8f;
        uint _lastRev = uint.MaxValue;
        string _previewName;                          // set while the menu shows a not-yet-saved look on this sign
        SignStyle _previewStyle;

        internal string SignName { get; private set; } = "";
        internal SignStyle Style { get; private set; } = new();

        void Awake()
        {
            _nview = GetComponent<ZNetView>();
            _text = GetComponentInChildren<TMP_Text>(true);
            if (_text != null) { _defFont = _text.font; _defMin = _text.fontSizeMin; _defMax = _text.fontSizeMax; _text.richText = true; }
            if (_nview == null || _nview.GetZDO() == null) return;           // the build ghost: no ZDO
            Loaded.Add(this);
            Refresh();
            InvokeRepeating(nameof(Refresh), 1f, 1f);
            ModLog.Verbose("PoolSign: loaded at " + transform.position.ToString("0.0") + ", name '" + SignName + "', text object " + (_text != null ? "found" : "MISSING"));
        }

        void OnDestroy() { Loaded.Remove(this); }

        /// Reads name + style from the ZDO when it changed (another player saved it) and redraws.
        void Refresh()
        {
            if (_nview == null || !_nview.IsValid()) return;
            var zdo = _nview.GetZDO();
            if (zdo.DataRevision == _lastRev) return;
            _lastRev = zdo.DataRevision;
            SignName = zdo.GetString(KeyName, "");
            var problems = new List<string>();
            Style = SignStyle.Parse(zdo.GetString(KeyStyle, ""), problems);
            if (problems.Count > 0) ModLog.VerboseWarn("PoolSign '" + SignName + "': style read with problems: " + string.Join("; ", problems));
            if (_previewStyle == null) Draw(SignName, Style);
            ModLog.Verbose("PoolSign: '" + SignName + "' updated from the world (revision " + _lastRev + ", style " + Style + ")");
        }

        void Draw(string name, SignStyle style)
        {
            if (_text == null) return;
            string how = SignFonts.Apply(_text, style, _defFont, _defMin, _defMax);
            _text.text = style.Render(name);
            ModLog.Verbose("PoolSign: drawn '" + name + "' (" + how + ")");
        }

        // --- preview while the menu is open (only on this PC, nothing is sent) --------------------------------------------
        internal void Preview(string name, SignStyle style) { _previewName = name; _previewStyle = style.Clone(); Draw(_previewName, _previewStyle); }

        internal void EndPreview()
        {
            if (_previewStyle == null) return;
            _previewName = null; _previewStyle = null;
            Draw(SignName, Style);
            ModLog.Verbose("PoolSign: preview ended, '" + SignName + "' shown as stored");
        }

        /// Saves name + style into the world (every player sees it). Returns the stored name.
        internal string Save(string name, SignStyle style)
        {
            name = Clean(name);
            if (_nview == null || !_nview.IsValid()) { ModLog.Warn("PoolSign: save refused, the sign has no network object"); return SignName; }
            _nview.ClaimOwnership();
            var zdo = _nview.GetZDO();
            zdo.Set(KeyName, name);
            zdo.Set(KeyStyle, style.Serialize());
            _previewName = null; _previewStyle = null;
            _lastRev = uint.MaxValue;
            Refresh();
            ModLog.Info("PoolSign: saved '" + name + "' at " + transform.position.ToString("0.0") + " (pool " + PoolLabel() + ")");
            return SignName;
        }

        /// Line breaks and control characters out, at most MaxName letters.
        internal static string Clean(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            var sb = new System.Text.StringBuilder();
            foreach (char ch in name) if (!char.IsControl(ch)) sb.Append(ch);
            var letters = SignStyle.SplitLetters(sb.ToString().Trim());
            if (letters.Count > MaxName) letters.RemoveRange(MaxName, letters.Count - MaxName);
            return string.Concat(letters);
        }

        // --- the pool it names -------------------------------------------------------------------------------------------
        /// The pump pool under / next to the sign (null = none within PoolReach).
        internal LiquidVolume Pool()
        {
            LiquidVolume v; float depth, surface; Vector3 at;
            if (TarPits.TrySampleNear(transform.position, PoolReach, out v, out depth, out surface, out at, LiquidType.All) && TarPits.IsPumpPool(v)) return v;
            return null;
        }

        internal string PoolLabel()
        {
            var v = Pool();
            return v == null ? "none within " + PoolReach + " m" : v.name + " (" + (TarPits.IsTar(v) ? "tar" : "water") + ") at " + v.transform.position.ToString("0.0");
        }

        // --- Hoverable / Interactable --------------------------------------------------------------------------------------
        public string GetHoverName() { return Loc.T("dtp_piece_sign"); }
        public float GetHoverOffset() { return 0f; }

        public string GetHoverText()
        {
            string title = string.IsNullOrEmpty(SignName) ? Loc.T("dtp_sign_unnamed") : "\"" + SignName + "\"";
            string pool = Pool() != null ? Loc.T("dtp_sign_pool_ok") : Loc.T("dtp_sign_pool_none");
            string text = title + "\n" + pool + "\n[<color=yellow><b>$KEY_Use</b></color>] " + Loc.T("dtp_sign_edit");
            return Localization.instance != null ? Localization.instance.Localize(text) : text;    // the game only fills $KEY_Use through Localize
        }

        /// E: opens the sign menu (holding E does nothing). Returns whether the menu is open now.
        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            int open = 0;
            if (!hold)
            {
                ModLog.Verbose("PoolSign: E on '" + SignName + "' by " + (user != null ? user.GetHoverName() : "?") + ", pool " + PoolLabel());
                open = PoolSignMenu.Open(this);
            }
            return open == 1;
        }

        const int ItemsAccepted = 0;            // nothing can be used on the sign
        public bool UseItem(Humanoid user, ItemDrop.ItemData item) { return item != null && ItemsAccepted > 0; }
    }
}
