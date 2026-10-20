using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace DrainTarPit.Ui
{
    /// <summary>
    /// T-023: the controls of the mod's menus, made from plain VisualElements with inline styles and the game's own UI sprites (UiKit.Skin):
    /// button, check box, slider, drop-down, text field, colour picker. Nothing shrinks (flexShrink 0): the window scrolls instead (T-56R).
    /// </summary>
    internal static class UiControls
    {
        internal static Button Button(string text, Action onClick)
        {
            var b = new Button(() => { UiLog.Verbose("Ui: button '" + text + "' pressed"); onClick?.Invoke(); }) { text = text };
            UiKit.Skin(b, UiKit.SpriteButton, UiKit.Btn);
            b.style.color = UiKit.Text; b.style.fontSize = UiKit.FontSize;
            b.style.borderTopWidth = 0; b.style.borderBottomWidth = 0; b.style.borderLeftWidth = 0; b.style.borderRightWidth = 0;
            UiKit.Pad(b, 6, 14);
            b.style.minHeight = 36; b.style.flexShrink = 0;
            b.style.marginLeft = 3; b.style.marginRight = 3; b.style.marginTop = 3; b.style.marginBottom = 3;
            b.RegisterCallback<PointerEnterEvent>(e => { UiKit.Skin(b, UiKit.SpriteButtonHot, UiKit.BtnHot); b.style.color = UiKit.Accent; });
            b.RegisterCallback<PointerLeaveEvent>(e => { UiKit.Skin(b, UiKit.SpriteButton, UiKit.Btn); b.style.color = UiKit.Text; });
            b.RegisterCallback<PointerDownEvent>(e => UiKit.Skin(b, UiKit.SpriteButtonDown, UiKit.Btn), TrickleDown.TrickleDown);
            return b;
        }

        /// A button that looks like an input field (sunken, text on the left): the closed drop-down.
        internal static Button Field(string text, Action onClick)
        {
            var b = Button(text, onClick);
            UiKit.Skin(b, UiKit.SpriteSunken, UiKit.Field);
            UiKit.Border(b, UiKit.Edge, 1, 3);
            b.style.unityTextAlign = TextAnchor.MiddleLeft;
            b.RegisterCallback<PointerEnterEvent>(e => { UiKit.Skin(b, UiKit.SpriteSunken, UiKit.Field); b.style.color = UiKit.Accent; });
            b.RegisterCallback<PointerLeaveEvent>(e => { UiKit.Skin(b, UiKit.SpriteSunken, UiKit.Field); b.style.color = UiKit.Text; });
            b.RegisterCallback<PointerDownEvent>(e => UiKit.Skin(b, UiKit.SpriteSunken, UiKit.Field), TrickleDown.TrickleDown);
            return b;
        }

        internal static Label Heading(string text)
        {
            var l = new Label(text);
            UiKit.Font(l, true);
            l.style.fontSize = UiKit.HeadingSize; l.style.color = UiKit.Accent; l.style.marginTop = 14; l.style.marginBottom = 4; l.style.flexShrink = 0;
            l.style.borderBottomWidth = 1; l.style.borderBottomColor = new Color(UiKit.Edge.r, UiKit.Edge.g, UiKit.Edge.b, 0.5f);
            return l;
        }

        internal static Label Note(string text)
        {
            var l = new Label(text);
            l.style.color = UiKit.Dim; l.style.fontSize = UiKit.NoteSize; l.style.whiteSpace = WhiteSpace.Normal; l.style.flexShrink = 0;
            return l;
        }

        /// A row: label on the left (fixed width), control on the right.
        internal static VisualElement Row(string label, VisualElement control)
        {
            var r = new VisualElement();
            r.style.flexDirection = FlexDirection.Row; r.style.alignItems = Align.Center; r.style.marginTop = 3; r.style.marginBottom = 3; r.style.flexShrink = 0;
            var l = new Label(label); l.style.width = 160; l.style.flexShrink = 0;
            r.Add(l);
            control.style.flexGrow = 1; control.style.flexShrink = 1;
            r.Add(control);
            return r;
        }

        internal static VisualElement Line(params VisualElement[] items)
        {
            var r = new VisualElement();
            r.style.flexDirection = FlexDirection.Row; r.style.flexWrap = Wrap.Wrap; r.style.alignItems = Align.Center; r.style.flexShrink = 0;
            foreach (var i in items) r.Add(i);
            return r;
        }

        // --- text field -----------------------------------------------------------------------------------------------------
        internal static TextField TextField(string value, int maxLength, Action<string> changed)
        {
            var f = new TextField { value = value ?? "", maxLength = maxLength };
            f.style.marginLeft = 0; f.style.marginRight = 0; f.style.flexShrink = 0;
            var input = f.Q(className: UnityEngine.UIElements.TextField.inputUssClassName) ?? f;
            UiKit.Skin(input, UiKit.SpriteSunken, UiKit.Field);
            input.style.color = UiKit.Text; input.style.fontSize = UiKit.FontSize;
            UiKit.Pad(input, 5, 8);
            input.style.minHeight = 34;
            f.RegisterValueChangedCallback(e => changed?.Invoke(e.newValue));
            return f;
        }

        // --- check box ------------------------------------------------------------------------------------------------------
        /// The game's check box with a label; click anywhere on it.
        internal static VisualElement Toggle(string text, bool on, Action<bool> changed)
        {
            bool state = on;
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row; row.style.alignItems = Align.Center; row.style.flexShrink = 0;
            row.style.marginRight = 14; row.style.marginTop = 3; row.style.marginBottom = 3;
            // the box: the game's sunken field with an orange square in it when on (the atlas 'checkbox' sprite showed a star in the preview)
            var box = new VisualElement(); box.style.width = 24; box.style.height = 24; box.style.marginRight = 6;
            UiKit.Skin(box, UiKit.SpriteSunken, UiKit.Field);
            UiKit.Border(box, UiKit.Edge, 1, 3);
            var mark = new VisualElement(); mark.style.position = Position.Absolute; mark.style.left = 5; mark.style.top = 5; mark.style.right = 5; mark.style.bottom = 5;
            mark.style.backgroundColor = UiKit.Accent; UiKit.Border(mark, UiKit.Accent, 0, 2);
            mark.pickingMode = PickingMode.Ignore;
            box.Add(mark);
            row.Add(box);
            var l = new Label(text); l.pickingMode = PickingMode.Ignore; row.Add(l);
            void Show() { mark.style.display = state ? DisplayStyle.Flex : DisplayStyle.None; }
            row.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0) return;
                state = !state; Show();
                UiLog.Verbose("Ui: check box '" + text + "' " + (state ? "on" : "off"));
                changed?.Invoke(state);
            });
            Show();
            return row;
        }

        // --- slider -----------------------------------------------------------------------------------------------------------
        /// A horizontal slider with its value shown on the right. `format` formats the value.
        internal static VisualElement Slider(float min, float max, float value, string format, Action<float> changed)
        {
            var box = new VisualElement();
            box.style.flexDirection = FlexDirection.Row; box.style.alignItems = Align.Center; box.style.flexShrink = 0; box.style.minHeight = 30;
            var track = new VisualElement();
            track.style.flexGrow = 1; track.style.height = 14;
            UiKit.Skin(track, UiKit.SpriteSunken, UiKit.Field);
            var fill = new VisualElement();
            fill.style.position = Position.Absolute; fill.style.left = 2; fill.style.top = 2; fill.style.bottom = 2;
            fill.style.backgroundColor = UiKit.Accent; fill.pickingMode = PickingMode.Ignore;
            track.Add(fill);
            var shown = new Label(); shown.style.width = 64; shown.style.unityTextAlign = TextAnchor.MiddleRight;
            box.Add(track); box.Add(shown);

            float v = Mathf.Clamp(value, min, max);
            void Show() { fill.style.width = Length.Percent(max > min ? (v - min) / (max - min) * 98f : 0f); shown.text = v.ToString(format); }
            void SetFrom(float localX)
            {
                float w = track.resolvedStyle.width;
                if (w <= 1f) return;
                v = Mathf.Lerp(min, max, Mathf.Clamp01(localX / w));
                Show();
                changed?.Invoke(v);
            }
            bool down = false;
            track.RegisterCallback<PointerDownEvent>(e => { down = true; track.CapturePointer(e.pointerId); SetFrom(e.localPosition.x); });
            track.RegisterCallback<PointerMoveEvent>(e => { if (down) SetFrom(e.localPosition.x); });
            track.RegisterCallback<PointerUpEvent>(e => { if (!down) return; down = false; track.ReleasePointer(e.pointerId); UiLog.Verbose("Ui: slider set to " + v.ToString(format)); });
            Show();
            return box;
        }

        // --- drop-down --------------------------------------------------------------------------------------------------------
        const float ItemHeight = 38f, ListMax = 380f;

        /// A button that opens a scrolling list of `options` under it. `label` turns an option into its shown text.
        internal static Button Dropdown(IList<string> options, string current, Func<string, string> label, Action<string> chosen)
        {
            Button b = null;
            string cur = current;
            b = Field(label(current) + "  ▾", () =>
            {
                // T-56R: the list goes into UiKit.Root (it carries the font). The panel's own visualTree has no font: the list items had no text
                // (the empty lines of the font / alignment lists in game).
                var root = UiKit.Root;
                if (root == null) return;
                var shade = new VisualElement();
                shade.style.position = Position.Absolute; shade.style.left = 0; shade.style.top = 0; shade.style.right = 0; shade.style.bottom = 0;
                shade.style.backgroundColor = new Color(0, 0, 0, 0.35f);
                shade.name = "dtp-dropdown";
                var box = new VisualElement();
                var wb = b.worldBound;
                float h = Mathf.Min(ListMax, options.Count * ItemHeight + 12f);
                float top = wb.yMax + h > root.layout.height ? Mathf.Max(0f, wb.yMin - h) : wb.yMax;     // opens upwards near the bottom of the screen
                box.style.position = Position.Absolute; box.style.left = wb.xMin; box.style.top = top;
                box.style.width = Mathf.Max(wb.width, 260f); box.style.height = h;
                UiKit.Skin(box, UiKit.SpriteList, UiKit.Bg); UiKit.Pad(box, 4, 4);
                var scroll = new UiScroll("drop-down");
                box.Add(scroll.Root);
                foreach (var o in options)
                {
                    string opt = o;
                    var item = Button(label(opt), () => { cur = opt; b.text = label(opt) + "  ▾"; shade.RemoveFromHierarchy(); chosen?.Invoke(opt); });
                    item.style.unityTextAlign = TextAnchor.MiddleLeft; item.style.height = ItemHeight - 6f;
                    if (opt == cur) item.style.color = UiKit.Accent;
                    scroll.Content.Add(item);
                }
                shade.RegisterCallback<PointerDownEvent>(e => { if (e.target == shade) { shade.RemoveFromHierarchy(); UiLog.Verbose("Ui: drop-down closed without a choice"); } });
                shade.Add(box);
                root.Add(shade);
                int at = options.IndexOf(cur);
                if (at > 3) scroll.ScrollTo((at - 3) * ItemHeight);
                UiLog.Verbose("Ui: drop-down opened with " + options.Count + " option(s), current '" + cur + "'");
            });
            return b;
        }

        // --- colour -----------------------------------------------------------------------------------------------------------
        static readonly string[] Palette = { "#FFFFFF", "#000000", "#E8D5A9", "#C0392B", "#E67E22", "#F1C40F", "#2ECC71", "#1ABC9C", "#3498DB", "#9B59B6", "#FF66CC", "#7F8C8D" };

        /// Hex field + red / green / blue sliders + a palette. `changed` gets "#RRGGBB". `set` moves it to a new colour from outside.
        internal static VisualElement ColorPicker(string hex, Action<string> changed, out Action<string> set)
        {
            var box = new VisualElement(); box.style.flexShrink = 0;
            Color c;
            if (!ColorUtility.TryParseHtmlString(hex ?? "#FFFFFF", out c)) c = Color.white;
            var swatch = new VisualElement(); swatch.style.width = 44; swatch.style.height = 30; swatch.style.marginRight = 6; UiKit.Border(swatch, UiKit.Edge, 1, 3);
            TextField field = null;
            var sliders = new VisualElement(); sliders.style.flexShrink = 0;
            bool quiet = false;
            void Apply(bool fromField)
            {
                string h = "#" + ColorUtility.ToHtmlStringRGB(c);
                swatch.style.backgroundColor = c;
                if (!fromField) { quiet = true; field.value = h; quiet = false; }
                changed?.Invoke(h);
            }
            field = TextField("#" + ColorUtility.ToHtmlStringRGB(c), 7, v =>
            {
                Color p;
                if (quiet || v.TrimStart('#').Length != 6 || !ColorUtility.TryParseHtmlString(v.StartsWith("#") ? v : "#" + v, out p)) return;
                c = p; RebuildSliders(); Apply(true);
            });
            field.style.width = 110;
            void RebuildSliders()
            {
                sliders.Clear();
                sliders.Add(Row("R", Slider(0, 255, c.r * 255f, "0", v => { c.r = v / 255f; Apply(false); })));
                sliders.Add(Row("G", Slider(0, 255, c.g * 255f, "0", v => { c.g = v / 255f; Apply(false); })));
                sliders.Add(Row("B", Slider(0, 255, c.b * 255f, "0", v => { c.b = v / 255f; Apply(false); })));
            }
            var pal = new VisualElement(); pal.style.flexDirection = FlexDirection.Row; pal.style.flexWrap = Wrap.Wrap; pal.style.flexShrink = 0;
            foreach (var p in Palette)
            {
                Color pc; ColorUtility.TryParseHtmlString(p, out pc);
                var s = new VisualElement(); s.style.width = 26; s.style.height = 26; s.style.marginRight = 4; s.style.marginTop = 4;
                s.style.backgroundColor = pc; UiKit.Border(s, UiKit.Edge, 1, 3);
                s.RegisterCallback<PointerDownEvent>(e => { c = pc; RebuildSliders(); Apply(false); });
                pal.Add(s);
            }
            box.Add(Line(swatch, field));
            box.Add(sliders);
            box.Add(pal);
            RebuildSliders();
            swatch.style.backgroundColor = c;
            set = h => { Color p; if (ColorUtility.TryParseHtmlString(h, out p)) { c = p; RebuildSliders(); quiet = true; field.value = h; quiet = false; swatch.style.backgroundColor = c; } };
            return box;
        }
    }
}
