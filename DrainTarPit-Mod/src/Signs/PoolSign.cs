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
        /// T-60: the sign's own key (written once, never changes) and the liquid of its pool ("tar" / "water").
        internal const string KeyId = "DTP_signKey", KeyLiquid = "DTP_signLiquid";
        internal const int MaxName = 40;
        const float PoolReach = PoolFinder.Reach;

        internal static readonly List<PoolSign> Loaded = new();
        /// Bumped when a sign loads, unloads or gets another name: PoolStyles finds the pools' signs again, the pools redraw.
        internal static int Generation;

        static void Changed(string why) { Generation++; Visuals.PoolWater.RequestRefresh(); ModLog.Verbose("PoolSign: " + why + " -> pools look for their sign again"); }

        /// The sign's key for the players' personal pool styles. T-60 (user: "water style for users is not preserved after game restart"): the
        /// world id (ZDOID) is NOT kept across restarts (the log showed 1:127823 -> 1:127788 -> 1:127991 for the same sign), so the key is its
        /// own value in the ZDO (KeyId), written once by the owner; until then (and on the server) the same value is made from the position.
        internal string Id { get { return _nview != null && _nview.GetZDO() != null ? KeyOf(_nview.GetZDO()) : ""; } }

        internal static string KeyOf(ZDO z)
        {
            if (z == null) return "";
            string k = z.GetString(KeyId, "");
            return k.Length > 0 ? k : PosKey(z.GetPosition());
        }

        static string PosKey(Vector3 p)
        {
            var c = System.Globalization.CultureInfo.InvariantCulture;
            return "p" + p.x.ToString("0.0", c) + "_" + p.y.ToString("0.0", c) + "_" + p.z.ToString("0.0", c);
        }

        /// The owner writes the key (once) and the liquid of the pool (when it changed) into the ZDO. Returns the number of values written.
        int Stamp()
        {
            if (_nview == null || !_nview.IsValid() || !_nview.IsOwner()) return 0;
            var zdo = _nview.GetZDO();
            int n = 0;
            if (zdo.GetString(KeyId, "").Length == 0) { zdo.Set(KeyId, PosKey(zdo.GetPosition())); n++; ModLog.Verbose("PoolSign: key " + zdo.GetString(KeyId, "") + " written for '" + SignName + "'"); }
            string liquid = PoolFinder.LiquidName(_pool);
            if (_pool != null && zdo.GetString(KeyLiquid, "") != liquid) { zdo.Set(KeyLiquid, liquid); n++; ModLog.Verbose("PoolSign: '" + SignName + "' names a " + liquid + " pool (written to the sign)"); }
            return n;
        }

        // --- the pool it names (cached: found again when it unloads or was not found yet) ----------------------------------------
        LiquidVolume _pool;
        float _poolDist = -1f, _nextPoolLook;

        /// Looks for the pool when none is known (not loaded yet when the sign loaded, or unloaded since). Returns 1 when the pool changed.
        int LookForPool()
        {
            if (_pool != null || Time.time < _nextPoolLook) return 0;
            _nextPoolLook = Time.time + 2f;
            var v = PoolFinder.Find(transform.position, PoolReach, out _poolDist);
            if (v == null) return 0;
            _pool = v;
            ModLog.Verbose("PoolSign: '" + SignName + "' names " + PoolLabel());
            Stamp();
            Changed("sign '" + SignName + "' found its pool");
            return 1;
        }

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
            GrabBoard();
            Refresh();
            Changed("sign loaded");
            InvokeRepeating(nameof(Refresh), 1f, 1f);
            ModLog.Verbose("PoolSign: loaded at " + transform.position.ToString("0.0") + ", name '" + SignName + "', text object " + (_text != null ? "found" : "MISSING"));
        }

        void OnDestroy()
        {
            foreach (var b in _board) if (b.Mat != null) Destroy(b.Mat);
            if (Loaded.Remove(this)) Changed("sign unloaded");
        }

        // --- brightness (T-59b, user: "adjust the color of the sign to be a bit more brighter") -----------------------------
        struct BoardMat { public Material Mat; public Color Orig; }
        readonly List<BoardMat> _board = new();

        /// The board's (not the post's) materials as this sign's own copies, remembering their colours.
        void GrabBoard()
        {
            foreach (var r in GetComponentsInChildren<MeshRenderer>(true))
            {
                if (r.transform.parent != null && r.transform.parent.name.StartsWith("post")) continue;
                if (r.name.StartsWith("post")) continue;
                foreach (var m in r.materials)                                   // .materials = instances for this renderer only
                    if (m != null && m.HasProperty("_Color")) _board.Add(new BoardMat { Mat = m, Orig = m.color });
            }
            Brighten();
        }

        void Brighten()
        {
            float b = ModSettings.SignBoardBrightness != null ? ModSettings.SignBoardBrightness.Value : 1f;
            foreach (var x in _board) x.Mat.color = new Color(x.Orig.r * b, x.Orig.g * b, x.Orig.b * b, x.Orig.a);
            ModLog.Verbose("PoolSign: board x" + b.ToString("0.00") + " (" + _board.Count + " material(s)), text x" + (ModSettings.SignTextBrightness != null ? ModSettings.SignTextBrightness.Value.ToString("0.00") : "1"));
        }

        /// A brightness setting changed: every loaded sign redraws its board and text.
        internal static int BrightnessChanged()
        {
            foreach (var s in Loaded) { if (s == null) continue; s.Brighten(); s.Draw(s._previewName ?? s.SignName, s._previewStyle ?? s.Style); }
            return Loaded.Count;
        }

        /// Reads name + style from the ZDO when it changed (another player saved it) and redraws.
        void Refresh()
        {
            if (_nview == null || !_nview.IsValid()) return;
            LookForPool();
            var zdo = _nview.GetZDO();
            if (zdo.DataRevision == _lastRev) return;
            _lastRev = zdo.DataRevision;
            string before = SignName;
            SignName = zdo.GetString(KeyName, "");
            if (before != SignName) Changed("sign renamed '" + before + "' -> '" + SignName + "'");
            var problems = new List<string>();
            Style = SignStyle.Parse(zdo.GetString(KeyStyle, ""), problems);
            if (problems.Count > 0) ModLog.VerboseWarn("PoolSign '" + SignName + "': style read with problems: " + string.Join("; ", problems));
            if (_previewStyle == null) Draw(SignName, Style);
            Stamp();
            ModLog.Verbose("PoolSign: '" + SignName + "' updated from the world (revision " + _lastRev + ", style " + Style + ")");
        }

        void Draw(string name, SignStyle style)
        {
            if (_text == null) return;
            string how = SignFonts.Apply(_text, style, _defFont, _defMin, _defMax);
            _text.text = style.Render(name, SignFonts.EmojiFont);
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
            Stamp();
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

        /// The pool under / next to the sign: a pump pool (water or tar) or a tar pit (PoolFinder; null = none within PoolReach).
        internal LiquidVolume Pool() { LookForPool(); return _pool; }

        internal string PoolLabel()
        {
            var v = _pool;
            return v == null ? "none within " + PoolReach + " m" : v.name + " (" + PoolFinder.LiquidName(v) + ") at " + v.transform.position.ToString("0.0") + (_poolDist > 0f ? ", " + _poolDist.ToString("0.0") + " m away" : ", under the sign");
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
