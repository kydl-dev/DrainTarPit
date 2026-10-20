using System;
using System.Collections.Generic;
using DrainTarPit.Logging;
using UnityEngine;
using UnityEngine.UIElements;

namespace DrainTarPit.Ui
{
    /// <summary>
    /// T-023: the controls of the mod's menus, made from plain VisualElements with inline styles (no theme needed): button, toggle, slider,
    /// drop-down, text field, colour picker. Every change is written to the debug log by the caller's callback (and here for the generic parts).
    /// </summary>
    internal static class UiControls
    {
        internal static Button Button(string text, Action onClick)
        {
            var b = new Button(() => { ModLog.Verbose("Ui: button '" + text + "' pressed"); onClick?.Invoke(); }) { text = text };
            b.style.backgroundColor = UiKit.Btn; b.style.color = UiKit.Text;
            UiKit.Border(b, UiKit.Edge, 1, 4); UiKit.Pad(b, 4, 10);
            b.style.marginLeft = 2; b.style.marginRight = 2; b.style.marginTop = 2; b.style.marginBottom = 2;
            b.RegisterCallback<PointerEnterEvent>(e => b.style.backgroundColor = UiKit.BtnHot);
            b.RegisterCallback<PointerLeaveEvent>(e => b.style.backgroundColor = UiKit.Btn);
            return b;
        }

        internal static Label Heading(string text)
        {
            var l = new Label(text);
            l.style.fontSize = 18; l.style.color = UiKit.Accent; l.style.marginTop = 10; l.style.marginBottom = 4;
            return l;
        }

        internal static Label Note(string text)
        {
            var l = new Label(text);
            l.style.color = UiKit.Dim; l.style.fontSize = 13; l.style.whiteSpace = WhiteSpace.Normal;
            return l;
        }

        /// A row: label on the left (fixed width), control on the right.
        internal static VisualElement Row(string label, VisualElement control)
        {
            var r = new VisualElement();
            r.style.flexDirection = FlexDirection.Row; r.style.alignItems = Align.Center; r.style.marginTop = 3; r.style.marginBottom = 3;
            var l = new Label(label); l.style.width = 130; l.style.flexShrink = 0;
            r.Add(l);
            control.style.flexGrow = 1;
            r.Add(control);
            return r;
        }

        internal static VisualElement Line(params VisualElement[] items)
        {
            var r = new VisualElement();
            r.style.flexDirection = FlexDirection.Row; r.style.flexWrap = Wrap.Wrap; r.style.alignItems = Align.Center;
            foreach (var i in items) r.Add(i);
            return r;
        }

        // --- text field -----------------------------------------------------------------------------------------------------
        internal static TextField TextField(string value, int maxLength, Action<string> changed)
        {
            var f = new TextField { value = value ?? "", maxLength = maxLength };
            f.style.marginLeft = 0; f.style.marginRight = 0;
            var input = f.Q(className: UnityEngine.UIElements.TextField.inputUssClassName) ?? f;
            input.style.backgroundColor = UiKit.Field; input.style.color = UiKit.Text;
            UiKit.Border(input, UiKit.Edge, 1, 3); UiKit.Pad(input, 4, 6);
            input.style.minHeight = 26;
            f.RegisterValueChangedCallback(e => changed?.Invoke(e.newValue));
            return f;
        }

        // --- toggle ---------------------------------------------------------------------------------------------------------
        internal static Button Toggle(string text, bool on, Action<bool> changed)
        {
            bool state = on;
            Button b = null;
            b = Button(Mark(text, state), () => { state = !state; b.text = Mark(text, state); changed?.Invoke(state); });
            return b;
        }

        static string Mark(string text, bool on) { return (on ? "☑ " : "☐ ") + text; }

        // --- slider -----------------------------------------------------------------------------------------------------------
        /// A horizontal slider with its value shown on the right. `format` formats the value.
        internal static VisualElement Slider(float min, float max, float value, string format, Action<float> changed)
        {
            var box = new VisualElement();
            box.style.flexDirection = FlexDirection.Row; box.style.alignItems = Align.Center;
            var track = new VisualElement();
            track.style.flexGrow = 1; track.style.height = 10; track.style.backgroundColor = UiKit.Field;
            UiKit.Border(track, UiKit.Edge, 1, 5);
            var fill = new VisualElement();
            fill.style.position = Position.Absolute; fill.style.left = 0; fill.style.top = 0; fill.style.bottom = 0;
            fill.style.backgroundColor = UiKit.Accent; fill.pickingMode = PickingMode.Ignore;
            track.Add(fill);
            var shown = new Label(); shown.style.width = 56; shown.style.unityTextAlign = TextAnchor.MiddleRight;
            box.Add(track); box.Add(shown);

            float v = Mathf.Clamp(value, min, max);
            void Show() { fill.style.width = Length.Percent(max > min ? (v - min) / (max - min) * 100f : 0f); shown.text = v.ToString(format); }
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
            track.RegisterCallback<PointerUpEvent>(e => { down = false; track.ReleasePointer(e.pointerId); ModLog.Verbose("Ui: slider set to " + v.ToString(format)); });
            Show();
            return box;
        }

        // --- drop-down --------------------------------------------------------------------------------------------------------
        /// A button that opens a scrolling list of `options` over the window. `label` turns an option into its shown text.
        internal static Button Dropdown(IList<string> options, string current, Func<string, string> label, Action<string> chosen)
        {
            Button b = null;
            b = Button(label(current) + "  ▾", () =>
            {
                var root = b.panel != null ? b.panel.visualTree : null;
                if (root == null) return;
                var shade = new VisualElement();
                shade.style.position = Position.Absolute; shade.style.left = 0; shade.style.top = 0; shade.style.right = 0; shade.style.bottom = 0;
                shade.style.backgroundColor = new Color(0, 0, 0, 0.35f);
                var list = new ScrollView(ScrollViewMode.Vertical);
                var wb = b.worldBound;
                list.style.position = Position.Absolute; list.style.left = wb.xMin; list.style.top = wb.yMax;
                list.style.width = Mathf.Max(wb.width, 220f); list.style.maxHeight = 320;
                list.style.backgroundColor = UiKit.Bg; UiKit.Border(list, UiKit.Edge, 1, 4);
                foreach (var o in options)
                {
                    string opt = o;
                    var item = Button(label(opt), () => { b.text = label(opt) + "  ▾"; shade.RemoveFromHierarchy(); chosen?.Invoke(opt); });
                    item.style.unityTextAlign = TextAnchor.MiddleLeft;
                    if (opt == current) item.style.color = UiKit.Accent;
                    list.Add(item);
                }
                shade.RegisterCallback<PointerDownEvent>(e => { if (e.target == shade) { shade.RemoveFromHierarchy(); ModLog.Verbose("Ui: drop-down closed without a choice"); } });
                shade.Add(list);
                root.Add(shade);
                ModLog.Verbose("Ui: drop-down opened with " + options.Count + " option(s)");
            });
            return b;
        }

        // --- colour -----------------------------------------------------------------------------------------------------------
        static readonly string[] Palette = { "#FFFFFF", "#000000", "#E8D5A9", "#C0392B", "#E67E22", "#F1C40F", "#2ECC71", "#1ABC9C", "#3498DB", "#9B59B6", "#FF66CC", "#7F8C8D" };

        /// Hex field + red / green / blue sliders + a palette. `changed` gets "#RRGGBB". Returns the picker; `set` moves it to a new colour from outside.
        internal static VisualElement ColorPicker(string hex, Action<string> changed, out Action<string> set)
        {
            var box = new VisualElement();
            Color c;
            if (!ColorUtility.TryParseHtmlString(hex ?? "#FFFFFF", out c)) c = Color.white;
            var swatch = new VisualElement(); swatch.style.width = 40; swatch.style.height = 24; UiKit.Border(swatch, UiKit.Edge, 1, 3);
            TextField field = null;
            VisualElement sliders = new VisualElement();
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
                if (quiet || !ColorUtility.TryParseHtmlString(v.StartsWith("#") ? v : "#" + v, out p) || v.TrimStart('#').Length != 6) return;
                c = p; RebuildSliders(); Apply(true);
            });
            field.style.width = 90; field.style.flexGrow = 0;
            void RebuildSliders()
            {
                sliders.Clear();
                sliders.Add(Row("R", Slider(0, 255, c.r * 255f, "0", v => { c.r = v / 255f; Apply(false); })));
                sliders.Add(Row("G", Slider(0, 255, c.g * 255f, "0", v => { c.g = v / 255f; Apply(false); })));
                sliders.Add(Row("B", Slider(0, 255, c.b * 255f, "0", v => { c.b = v / 255f; Apply(false); })));
            }
            var pal = new VisualElement(); pal.style.flexDirection = FlexDirection.Row; pal.style.flexWrap = Wrap.Wrap;
            foreach (var p in Palette)
            {
                Color pc; ColorUtility.TryParseHtmlString(p, out pc);
                var s = new VisualElement(); s.style.width = 22; s.style.height = 22; s.style.marginRight = 3; s.style.marginTop = 3;
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
