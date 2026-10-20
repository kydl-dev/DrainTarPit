using System.Collections.Generic;
using DrainTarPit.Logging;
using DrainTarPit.TextLocal;
using DrainTarPit.Ui;
using UnityEngine;
using UnityEngine.UIElements;

namespace DrainTarPit.Signs
{
    /// <summary>
    /// T-023 (user, 2026-10-20): the menu E opens on a pool sign (UI Toolkit, UiKit window). Name, font (every font loaded in the game), size,
    /// bold / italic / underline / strike / upper case, alignment, letter spacing, colour, a colour per letter (select letters, paint / rainbow /
    /// gradient), emoji, and the preferred style (save / use). Changes are previewed on the sign on this PC only; Save writes them into the world.
    /// While it is open the character does not move (UiInputLock). Escape, X, Cancel or walking away close it without saving.
    /// </summary>
    internal static class PoolSignMenu
    {
        const string LockName = "PoolSignMenu";
        const float MaxDistance = 6f;
        static readonly string[] Emoji = { "\U0001F41F", "\U0001F4A7", "\U0001F30A", "\U0001F525", "⚓", "⭐", "❤", "☠", "\U0001F37A", "\U0001F33F", "❄", "⚔" };

        static PoolSign _sign;
        static VisualElement _window;
        static string _name;
        static SignStyle _style;
        static readonly HashSet<int> Selected = new();
        static VisualElement _letters, _content;
        static Label _status;
        static System.Action<string> _setLetterColour;
        static string _letterColour = "#FF0000";

        internal static bool IsOpen { get { return _window != null; } }

        /// Opens the menu for `sign`. Returns 1 when it is open, 0 when UI Toolkit is not available (logged).
        internal static int Open(PoolSign sign)
        {
            if (sign == null) return 0;
            if (IsOpen) Close("another sign opened");
            var root = UiKit.Root;
            if (root == null) { ModLog.Warn("PoolSignMenu: UI Toolkit is not available, the sign menu cannot open"); return 0; }
            _sign = sign;
            _name = sign.SignName;
            _style = sign.Style.Clone();
            Selected.Clear();
            ScrollView body;
            _window = UiKit.Window(Loc.T("dtp_ui_sign_title"), () => Close("X"), out body);
            _content = body;
            Build();
            root.Add(_window);
            UiInputLock.Hold(LockName);
            ModLog.Verbose("PoolSignMenu: opened for '" + _name + "' (style " + _style + ", pool " + sign.PoolLabel() + ")");
            return 1;
        }

        /// Called every frame (Plugin.Update): Escape closes, so does walking away or the sign being removed.
        internal static void Tick()
        {
            if (!IsOpen) return;
            if (_sign == null) { Close("the sign was removed"); return; }
            if (ZInput.GetKeyDown(KeyCode.Escape, true)) { Close("Escape"); return; }
            var p = Player.m_localPlayer;
            if (p == null || Vector3.Distance(p.transform.position, _sign.transform.position) > MaxDistance) Close("walked away / no player");
        }

        /// Closes without saving: the sign shows its stored look again.
        internal static void Close(string why)
        {
            if (_sign != null) _sign.EndPreview();
            if (_window != null) _window.RemoveFromHierarchy();
            _window = null; _sign = null; _content = null; _letters = null;
            UiInputLock.Release(LockName);
            ModLog.Verbose("PoolSignMenu: closed (" + why + ")");
        }

        static void Changed(string what)
        {
            _style.Clamp();
            if (_sign != null) _sign.Preview(_name, _style);
            ModLog.Verbose("PoolSignMenu: " + what + " -> preview '" + _name + "' " + _style);
        }

        static void Status(string text) { if (_status != null) _status.text = text; ModLog.Verbose("PoolSignMenu: " + text); }

        // --- layout --------------------------------------------------------------------------------------------------------
        static void Build()
        {
            _content.Clear();
            _content.Add(UiControls.Note(Loc.T("dtp_ui_pool", _sign != null && _sign.Pool() != null ? _sign.Pool().name : Loc.T("dtp_sign_pool_none"))));

            _content.Add(UiControls.Heading(Loc.T("dtp_ui_name")));
            _content.Add(UiControls.TextField(_name, PoolSign.MaxName * 2, v => { _name = v; RebuildLetters(); Changed("name"); }));
            var emo = new VisualElement(); emo.style.flexDirection = FlexDirection.Row; emo.style.flexWrap = Wrap.Wrap;
            foreach (var e in Emoji) { string em = e; emo.Add(UiControls.Button(em, () => AddEmoji(em))); }
            _content.Add(UiControls.Row(Loc.T("dtp_ui_emoji"), emo));
            _content.Add(UiControls.Note(Loc.T("dtp_ui_emoji_note")));

            _content.Add(UiControls.Heading(Loc.T("dtp_ui_text")));
            var fonts = new List<string> { "" }; fonts.AddRange(SignFonts.All());
            _content.Add(UiControls.Row(Loc.T("dtp_ui_font"), UiControls.Dropdown(fonts, _style.Font, f => f.Length == 0 ? Loc.T("dtp_ui_font_default") : f, f => { _style.Font = f; Changed("font"); })));
            _content.Add(UiControls.Row(Loc.T("dtp_ui_size"), UiControls.Line(
                UiControls.Toggle(Loc.T("dtp_ui_auto"), _style.Size <= 0f, on => { _style.Size = on ? 0f : 4f; Changed("auto size " + on); }),
                UiControls.Slider(SignStyle.MinSize, SignStyle.MaxSize, _style.Size <= 0f ? 4f : _style.Size, "0.0", v => { _style.Size = v; Changed("size"); }))));
            _content.Add(UiControls.Row(Loc.T("dtp_ui_style"), UiControls.Line(
                UiControls.Toggle(Loc.T("dtp_ui_bold"), _style.Bold, on => { _style.Bold = on; Changed("bold"); }),
                UiControls.Toggle(Loc.T("dtp_ui_italic"), _style.Italic, on => { _style.Italic = on; Changed("italic"); }),
                UiControls.Toggle(Loc.T("dtp_ui_underline"), _style.Underline, on => { _style.Underline = on; Changed("underline"); }),
                UiControls.Toggle(Loc.T("dtp_ui_strike"), _style.Strike, on => { _style.Strike = on; Changed("strike"); }),
                UiControls.Toggle(Loc.T("dtp_ui_upper"), _style.Upper, on => { _style.Upper = on; Changed("upper case"); }))));
            var aligns = new List<string> { "l", "c", "r" };
            _content.Add(UiControls.Row(Loc.T("dtp_ui_align"), UiControls.Dropdown(aligns, _style.Align.ToString(), AlignName, a => { _style.Align = a[0]; Changed("align"); })));
            _content.Add(UiControls.Row(Loc.T("dtp_ui_spacing"), UiControls.Slider(-20f, 50f, _style.Spacing, "0", v => { _style.Spacing = v; Changed("spacing"); })));

            _content.Add(UiControls.Heading(Loc.T("dtp_ui_colour")));
            System.Action<string> setBase;
            _content.Add(UiControls.ColorPicker(_style.Color, h => { _style.Color = h; Changed("colour"); }, out setBase));

            _content.Add(UiControls.Heading(Loc.T("dtp_ui_letters")));
            _content.Add(UiControls.Note(Loc.T("dtp_ui_letters_note")));
            _letters = new VisualElement(); _letters.style.flexDirection = FlexDirection.Row; _letters.style.flexWrap = Wrap.Wrap;
            _content.Add(_letters);
            RebuildLetters();
            _content.Add(UiControls.Line(
                UiControls.Button(Loc.T("dtp_ui_all"), () => { SelectAll(); }),
                UiControls.Button(Loc.T("dtp_ui_none"), () => { Selected.Clear(); RebuildLetters(); }),
                UiControls.Button(Loc.T("dtp_ui_paint"), Paint),
                UiControls.Button(Loc.T("dtp_ui_rainbow"), Rainbow),
                UiControls.Button(Loc.T("dtp_ui_gradient"), Gradient),
                UiControls.Button(Loc.T("dtp_ui_clear"), ClearLetters)));
            _content.Add(UiControls.ColorPicker(_letterColour, h => { _letterColour = h; }, out _setLetterColour));

            _content.Add(UiControls.Heading(Loc.T("dtp_ui_pref")));
            _content.Add(UiControls.Line(
                UiControls.Button(Loc.T("dtp_ui_pref_save"), () => { int n = SignPrefs.Save(_style); Status(n > 0 ? Loc.T("dtp_ui_pref_saved") : Loc.T("dtp_ui_pref_failed")); }),
                UiControls.Button(Loc.T("dtp_ui_pref_use"), UsePreferred)));

            _status = UiControls.Note("");
            _content.Add(_status);
            var bottom = UiControls.Line(
                UiControls.Button(Loc.T("dtp_ui_save"), Save),
                UiControls.Button(Loc.T("dtp_ui_cancel"), () => Close("Cancel")));
            bottom.style.justifyContent = Justify.FlexEnd; bottom.style.marginTop = 8;
            _content.Add(bottom);
        }

        static string AlignName(string a) { return Loc.T(a == "l" ? "dtp_ui_left" : a == "r" ? "dtp_ui_right" : "dtp_ui_center"); }

        // --- letters ---------------------------------------------------------------------------------------------------------
        static void RebuildLetters()
        {
            if (_letters == null) return;
            _letters.Clear();
            var letters = SignStyle.SplitLetters(_name);
            Selected.RemoveWhere(i => i >= letters.Count);
            for (int i = 0; i < letters.Count; i++)
            {
                int idx = i;
                string hex;
                if (!_style.Letters.TryGetValue(i, out hex)) hex = _style.Color;
                Color c; ColorUtility.TryParseHtmlString(hex, out c);
                var chip = new Label(letters[i]);
                chip.style.minWidth = 26; chip.style.height = 30; chip.style.unityTextAlign = TextAnchor.MiddleCenter;
                chip.style.color = c; chip.style.backgroundColor = UiKit.Field; chip.style.marginRight = 3; chip.style.marginTop = 3;
                UiKit.Border(chip, Selected.Contains(i) ? UiKit.Accent : UiKit.Edge, Selected.Contains(i) ? 3 : 1, 3);
                chip.RegisterCallback<PointerDownEvent>(e => { if (!Selected.Remove(idx)) Selected.Add(idx); ModLog.Verbose("PoolSignMenu: letter " + idx + " " + (Selected.Contains(idx) ? "selected" : "unselected")); RebuildLetters(); });
                _letters.Add(chip);
            }
        }

        static void SelectAll() { Selected.Clear(); int n = SignStyle.SplitLetters(_name).Count; for (int i = 0; i < n; i++) Selected.Add(i); RebuildLetters(); }

        static List<int> Sel() { var l = new List<int>(Selected); l.Sort(); return l; }

        static void Paint()
        {
            var sel = Sel();
            if (sel.Count == 0) { Status(Loc.T("dtp_ui_select_first")); return; }
            foreach (int i in sel) _style.Letters[i] = _letterColour;
            RebuildLetters(); Changed("painted " + sel.Count + " letter(s) " + _letterColour);
        }

        static void Rainbow()
        {
            var sel = Sel();
            if (sel.Count == 0) { Status(Loc.T("dtp_ui_select_first")); return; }
            for (int k = 0; k < sel.Count; k++)
                _style.Letters[sel[k]] = "#" + ColorUtility.ToHtmlStringRGB(Color.HSVToRGB(sel.Count == 1 ? 0f : k / (float)sel.Count, 0.85f, 1f));
            RebuildLetters(); Changed("rainbow over " + sel.Count + " letter(s)");
        }

        /// From the text colour to the letter colour over the selected letters (first to last selected).
        static void Gradient()
        {
            var sel = Sel();
            if (sel.Count == 0) { Status(Loc.T("dtp_ui_select_first")); return; }
            int n = _style.Gradient(sel[0], sel[sel.Count - 1], _style.Color, _letterColour);
            RebuildLetters(); Changed("gradient over " + n + " letter(s) " + _style.Color + " -> " + _letterColour);
        }

        static void ClearLetters()
        {
            var sel = Sel();
            if (sel.Count == 0) { int all = _style.Letters.Count; _style.Letters.Clear(); RebuildLetters(); Changed("cleared all " + all + " letter colour(s)"); return; }
            foreach (int i in sel) _style.Letters.Remove(i);
            RebuildLetters(); Changed("cleared " + sel.Count + " letter colour(s)");
        }

        static void AddEmoji(string em)
        {
            if (SignStyle.SplitLetters(_name).Count >= PoolSign.MaxName) { Status(Loc.T("dtp_ui_name_full")); return; }
            _name += em;
            Build();                                                     // the name field shows the new text
            Changed("emoji added");
        }

        // --- preferred style / save --------------------------------------------------------------------------------------------
        static void UsePreferred()
        {
            var p = SignPrefs.Load();
            if (p == null) { Status(Loc.T("dtp_ui_pref_none")); return; }
            _style = p;
            Build();
            Changed("preferred style used");
            Status(Loc.T("dtp_ui_pref_used"));
        }

        static void Save()
        {
            if (_sign == null) { Close("sign gone before save"); return; }
            string stored = _sign.Save(_name, _style);
            ModLog.Verbose("PoolSignMenu: saved as '" + stored + "'");
            Close("saved");
        }
    }
}
