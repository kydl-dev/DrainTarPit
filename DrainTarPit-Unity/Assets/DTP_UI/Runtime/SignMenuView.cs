using System.Collections.Generic;
using DrainTarPit.Signs;
using UnityEngine;
using UnityEngine.UIElements;

namespace DrainTarPit.Ui
{
    /// <summary>What the sign menu needs from the outside: the game (PoolSignMenu in the mod) or the Unity preview (DTP_UiPreview).</summary>
    public interface ISignMenuHost
    {
        string T(string key, params string[] args);     // localized text
        IList<string> Fonts { get; }                    // every font that can be chosen ("" = default is added by the view)
        int MaxLetters { get; }
        void Preview(string name, SignStyle style);     // show the not-yet-saved look on the sign
        void Save(string name, SignStyle style);
        void Close(string why);
        int SavePreferred(SignStyle style);             // characters written, 0 = failed
        SignStyle LoadPreferred();                      // null = none saved
    }

    /// <summary>
    /// T-023 / T-56R: the pool sign menu (UI Toolkit). Name, emoji, font (every font the host lists), size, bold / italic / underline / strike /
    /// upper case, alignment, letter spacing, colour, a colour per letter (select: paint / rainbow / gradient / clear), preferred style
    /// (save / use), reset to the default style, Save / Cancel. Shared by the game and the Unity preview scene (Assets/DTP_UI/Preview).
    /// </summary>
    public sealed class SignMenuView
    {
        static readonly string[] Emoji = { "\U0001F41F", "\U0001F4A7", "\U0001F30A", "\U0001F525", "⚓", "⭐", "❤", "☠", "\U0001F37A", "\U0001F33F", "❄", "⚔" };

        readonly ISignMenuHost _host;
        readonly HashSet<int> _selected = new HashSet<int>();
        public readonly VisualElement Window;
        readonly VisualElement _content;
        VisualElement _letters;
        Label _status;
        string _name, _letterColour = "#FF0000";
        SignStyle _style;

        public SignMenuView(ISignMenuHost host, string name, SignStyle style)
        {
            _host = host;
            _name = name ?? "";
            _style = style != null ? style.Clone() : new SignStyle();
            UiScroll body;
            Window = UiKit.Window(_host.T("dtp_ui_sign_title"), _host.T("dtp_ui_esc_close"), () => _host.Close("X"), out body);
            _content = body.Content;
            Build();
            UiLog.Verbose("SignMenuView: built for '" + _name + "' (" + _host.Fonts.Count + " font(s), style " + _style + ")");
        }

        public string Name { get { return _name; } }
        public SignStyle Style { get { return _style; } }

        void Changed(string what)
        {
            _style.Clamp();
            _host.Preview(_name, _style);
            UiLog.Verbose("SignMenuView: " + what + " -> '" + _name + "' " + _style);
        }

        void Status(string text) { if (_status != null) _status.text = text; UiLog.Verbose("SignMenuView: " + text); }

        // --- layout --------------------------------------------------------------------------------------------------------
        void Build()
        {
            _content.Clear();

            _content.Add(UiControls.Heading(_host.T("dtp_ui_name")));
            _content.Add(UiControls.TextField(_name, _host.MaxLetters * 2, v => { _name = v; RebuildLetters(); Changed("name"); }));
            var emo = new VisualElement(); emo.style.flexDirection = FlexDirection.Row; emo.style.flexWrap = Wrap.Wrap; emo.style.flexShrink = 0;
            foreach (var e in Emoji) { string em = e; emo.Add(UiControls.Button(em, () => AddEmoji(em))); }
            _content.Add(UiControls.Row(_host.T("dtp_ui_emoji"), emo));
            _content.Add(UiControls.Note(_host.T("dtp_ui_emoji_note")));

            _content.Add(UiControls.Heading(_host.T("dtp_ui_text")));
            var fonts = new List<string> { "" }; fonts.AddRange(_host.Fonts);
            _content.Add(UiControls.Row(_host.T("dtp_ui_font"), UiControls.Dropdown(fonts, _style.Font, f => f.Length == 0 ? _host.T("dtp_ui_font_default") : f, f => { _style.Font = f; Changed("font"); })));
            _content.Add(UiControls.Row(_host.T("dtp_ui_size"), UiControls.Line(
                UiControls.Toggle(_host.T("dtp_ui_auto"), _style.Size <= 0f, on => { _style.Size = on ? 0f : 4f; Changed("auto size " + on); }),
                Grow(UiControls.Slider(SignStyle.MinSize, SignStyle.MaxSize, _style.Size <= 0f ? 4f : _style.Size, "0.0", v => { _style.Size = v; Changed("size"); })))));
            _content.Add(UiControls.Row(_host.T("dtp_ui_style"), UiControls.Line(
                UiControls.Toggle(_host.T("dtp_ui_bold"), _style.Bold, on => { _style.Bold = on; Changed("bold"); }),
                UiControls.Toggle(_host.T("dtp_ui_italic"), _style.Italic, on => { _style.Italic = on; Changed("italic"); }),
                UiControls.Toggle(_host.T("dtp_ui_underline"), _style.Underline, on => { _style.Underline = on; Changed("underline"); }),
                UiControls.Toggle(_host.T("dtp_ui_strike"), _style.Strike, on => { _style.Strike = on; Changed("strike"); }),
                UiControls.Toggle(_host.T("dtp_ui_upper"), _style.Upper, on => { _style.Upper = on; Changed("upper case"); }))));
            var aligns = new List<string> { "l", "c", "r" };
            _content.Add(UiControls.Row(_host.T("dtp_ui_align"), UiControls.Dropdown(aligns, _style.Align.ToString(), AlignName, a => { _style.Align = a[0]; Changed("align"); })));
            _content.Add(UiControls.Row(_host.T("dtp_ui_spacing"), UiControls.Slider(-20f, 50f, _style.Spacing, "0", v => { _style.Spacing = v; Changed("spacing"); })));

            _content.Add(UiControls.Heading(_host.T("dtp_ui_colour")));
            System.Action<string> setBase;
            _content.Add(UiControls.ColorPicker(_style.Color, BaseColour, out setBase));

            _content.Add(UiControls.Heading(_host.T("dtp_ui_letters")));
            _content.Add(UiControls.Note(_host.T("dtp_ui_letters_note")));
            _letters = new VisualElement(); _letters.style.flexDirection = FlexDirection.Row; _letters.style.flexWrap = Wrap.Wrap; _letters.style.flexShrink = 0;
            _content.Add(_letters);
            RebuildLetters();
            _content.Add(UiControls.Line(
                UiControls.Button(_host.T("dtp_ui_all"), SelectAll),
                UiControls.Button(_host.T("dtp_ui_none"), () => { _selected.Clear(); RebuildLetters(); }),
                UiControls.Button(_host.T("dtp_ui_paint"), Paint),
                UiControls.Button(_host.T("dtp_ui_rainbow"), Rainbow),
                UiControls.Button(_host.T("dtp_ui_gradient"), Gradient),
                UiControls.Button(_host.T("dtp_ui_clear"), ClearLetters)));
            System.Action<string> setLetter;
            _content.Add(UiControls.ColorPicker(_letterColour, LetterColour, out setLetter));

            _content.Add(UiControls.Heading(_host.T("dtp_ui_pref")));
            _content.Add(UiControls.Line(
                UiControls.Button(_host.T("dtp_ui_pref_save"), () => { int n = _host.SavePreferred(_style); Status(_host.T(n > 0 ? "dtp_ui_pref_saved" : "dtp_ui_pref_failed")); }),
                UiControls.Button(_host.T("dtp_ui_pref_use"), UsePreferred),
                UiControls.Button(_host.T("dtp_ui_reset"), ResetStyle)));

            _status = UiControls.Note("");
            _content.Add(_status);
            var bottom = UiControls.Line(
                UiControls.Button(_host.T("dtp_ui_save"), () => _host.Save(_name, _style), true),
                UiControls.Button(_host.T("dtp_ui_cancel"), () => _host.Close("Cancel")));
            bottom.style.justifyContent = Justify.FlexEnd; bottom.style.marginTop = 12;
            _content.Add(bottom);
        }

        static VisualElement Grow(VisualElement e) { e.style.flexGrow = 1; e.style.minWidth = 200; return e; }

        string AlignName(string a) { return _host.T(a == "l" ? "dtp_ui_left" : a == "r" ? "dtp_ui_right" : "dtp_ui_center"); }

        // --- letters ---------------------------------------------------------------------------------------------------------
        void RebuildLetters()
        {
            if (_letters == null) return;
            _letters.Clear();
            var letters = SignStyle.SplitLetters(_name);
            _selected.RemoveWhere(i => i >= letters.Count);
            for (int i = 0; i < letters.Count; i++)
            {
                int idx = i;
                string hex;
                if (!_style.Letters.TryGetValue(i, out hex)) hex = _style.Color;
                Color c; ColorUtility.TryParseHtmlString(hex, out c);
                var chip = new Label(letters[i]);
                chip.style.minWidth = 36; chip.style.height = 40; chip.style.unityTextAlign = TextAnchor.MiddleCenter; chip.style.fontSize = 24;
                chip.style.color = c; chip.style.marginRight = 4; chip.style.marginTop = 4; chip.style.flexShrink = 0;
                bool sel = _selected.Contains(i);
                chip.style.backgroundColor = UiKit.Track; UiKit.Radius(chip, 4); UiKit.Borders(chip, sel ? 2 : 1, sel ? UiKit.Gold : UiKit.Border);
                chip.RegisterCallback<PointerDownEvent>(e =>
                {
                    if (!_selected.Remove(idx)) _selected.Add(idx);
                    UiLog.Verbose("SignMenuView: letter " + idx + " " + (_selected.Contains(idx) ? "selected" : "unselected"));
                    RebuildLetters();
                });
                _letters.Add(chip);
            }
        }

        void SelectAll() { _selected.Clear(); int n = SignStyle.SplitLetters(_name).Count; for (int i = 0; i < n; i++) _selected.Add(i); RebuildLetters(); }

        List<int> Sel() { var l = new List<int>(_selected); l.Sort(); return l; }

        // T-67 (user: "clicking on the colors under the general Colors or the Letter-Colors does not actually change the color"; log 10:31:
        // col changed every time, but every letter had its own colour from an old gradient, which covers the text colour; and the letter
        // colour was only used by Paint). Text colour: when EVERY letter has its own colour they are cleared, so the new colour shows.
        // Letter colour: selected letters take it at once (preset, sliders, hex), Paint still works.
        void BaseColour(string h)
        {
            _style.Color = h;
            int n = SignStyle.SplitLetters(_name).Count, own = 0;
            for (int i = 0; i < n; i++) if (_style.Letters.ContainsKey(i)) own++;
            if (n > 0 && own == n) { _style.Letters.Clear(); Status(_host.T("dtp_ui_letters_reset")); UiLog.Verbose("SignMenuView: all " + n + " letters had their own colour, cleared for the text colour " + h); }
            RebuildLetters(); Changed("colour " + h);
        }

        void LetterColour(string h)
        {
            _letterColour = h;
            var sel = Sel();
            if (sel.Count == 0) { UiLog.Verbose("SignMenuView: letter colour " + h + " picked, no letter selected (Paint / Gradient use it)"); return; }
            foreach (int i in sel) _style.Letters[i] = h;
            RebuildLetters(); Changed("letter colour " + h + " on " + sel.Count + " selected letter(s)");
        }

        void Paint()
        {
            var sel = Sel();
            if (sel.Count == 0) { Status(_host.T("dtp_ui_select_first")); return; }
            foreach (int i in sel) _style.Letters[i] = _letterColour;
            RebuildLetters(); Changed("painted " + sel.Count + " letter(s) " + _letterColour);
        }

        void Rainbow()
        {
            var sel = Sel();
            if (sel.Count == 0) { Status(_host.T("dtp_ui_select_first")); return; }
            for (int k = 0; k < sel.Count; k++)
                _style.Letters[sel[k]] = "#" + ColorUtility.ToHtmlStringRGB(Color.HSVToRGB(sel.Count == 1 ? 0f : k / (float)sel.Count, 0.85f, 1f));
            RebuildLetters(); Changed("rainbow over " + sel.Count + " letter(s)");
        }

        /// From the text colour to the letter colour over the selected letters (first to last selected).
        void Gradient()
        {
            var sel = Sel();
            if (sel.Count == 0) { Status(_host.T("dtp_ui_select_first")); return; }
            int n = _style.Gradient(sel[0], sel[sel.Count - 1], _style.Color, _letterColour);
            RebuildLetters(); Changed("gradient over " + n + " letter(s) " + _style.Color + " -> " + _letterColour);
        }

        void ClearLetters()
        {
            var sel = Sel();
            if (sel.Count == 0) { int all = _style.Letters.Count; _style.Letters.Clear(); RebuildLetters(); Changed("cleared all " + all + " letter colour(s)"); return; }
            foreach (int i in sel) _style.Letters.Remove(i);
            RebuildLetters(); Changed("cleared " + sel.Count + " letter colour(s)");
        }

        void AddEmoji(string em)
        {
            if (SignStyle.SplitLetters(_name).Count >= _host.MaxLetters) { Status(_host.T("dtp_ui_name_full")); return; }
            _name += em;
            Build();                                                       // the name field shows the new text
            Changed("emoji added");
        }

        // --- preferred / default style -------------------------------------------------------------------------------------------
        void UsePreferred()
        {
            var p = _host.LoadPreferred();
            if (p == null) { Status(_host.T("dtp_ui_pref_none")); return; }
            _style = p;
            Build();
            Changed("preferred style used");
            Status(_host.T("dtp_ui_pref_used"));
        }

        /// T-56R (user: "a 3rd button: Reset to default style"): every style value back to the default; the name stays.
        void ResetStyle()
        {
            _style = new SignStyle();
            Build();
            Changed("reset to the default style");
            Status(_host.T("dtp_ui_reset_done"));
        }
    }
}
