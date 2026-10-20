using System.Collections.Generic;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace DrainTarPit.Ui
{
    /// <summary>
    /// T-023 (user: the menus are UI Toolkit, at most 70% of the screen height and 60% of its width, movable). One runtime panel holds every
    /// window of the mod. T-56R (user: "same as normal game interface UI", bigger text, change the menu font): the game's own UI sprites
    /// (woodpanel_settings, button, checkbox, item_background_sunken ...) and fonts (Averia Serif Libre Bold for text, Norse for titles, like the
    /// game's menus), the panel scales with the screen (1080p reference), and every font falls back to the game's NotoEmoji font, so emoji show
    /// whatever font is used. All looks are inline styles: a game build has no USS importer / default theme.
    /// </summary>
    internal static class UiKit
    {
        internal static readonly Color Bg = new(0.13f, 0.10f, 0.07f, 0.97f), Panel = new(0.20f, 0.15f, 0.10f, 1f), Edge = new(0.62f, 0.48f, 0.25f, 1f);
        internal static readonly Color Text = new(0.96f, 0.93f, 0.86f, 1f), Dim = new(0.86f, 0.81f, 0.70f, 1f), Accent = new(1f, 0.64f, 0.10f, 1f);
        internal static readonly Color Btn = new(0.30f, 0.22f, 0.13f, 1f), BtnHot = new(0.42f, 0.31f, 0.17f, 1f), Field = new(0.08f, 0.06f, 0.04f, 1f);
        internal const int FontSize = 18, HeadingSize = 22, TitleSize = 28, NoteSize = 15;

        internal const string SpriteWindow = "woodpanel_settings", SpriteButton = "button", SpriteButtonHot = "button_highlight", SpriteButtonDown = "button_pressed";
        internal const string SpriteCheck = "checkbox", SpriteCheckMark = "checkbox_marker", SpriteSunken = "item_background_sunken", SpriteList = "panel_bkg_128";

        static UIDocument _doc;
        /// The panel's settings (the Unity preview renders the panel into a texture through them).
        internal static PanelSettings Settings { get; private set; }

        /// Throws the panel away (the Unity preview rebuilds it after a script reload / leaving play mode).
        internal static void Reset()
        {
            if (_doc != null) Object.DestroyImmediate(_doc.gameObject);
            _doc = null; Sprites.Clear();
        }
        static readonly Dictionary<string, Sprite> Sprites = new();
        static Font _bodyFont, _titleFont;
        static FontAsset _bodyAsset, _titleAsset;

        /// The full-screen layer every window is added to (picking ignored). null when UI Toolkit failed.
        internal static VisualElement Root
        {
            get
            {
                if (_doc == null) Create();
                return _doc != null ? _doc.rootVisualElement : null;
            }
        }

        static void Create()
        {
            try
            {
                var settings = ScriptableObject.CreateInstance<PanelSettings>();
                settings.name = "DTP_PanelSettings";
                settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;          // bigger on big screens (user: "hard to read" at 3440x1440)
                settings.referenceResolution = new Vector2Int(1920, 1080);
                settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
                settings.match = 1f;                                              // by height: ultrawide screens get the same text size as 16:9
                settings.sortingOrder = 500;
                var go = new GameObject("DTP_UI");
                if (Application.isPlaying) Object.DontDestroyOnLoad(go);
                Settings = settings;
                _doc = go.AddComponent<UIDocument>();
                _doc.panelSettings = settings;
                var root = _doc.rootVisualElement;
                root.style.position = Position.Absolute;
                root.style.left = 0; root.style.top = 0; root.style.right = 0; root.style.bottom = 0;
                root.pickingMode = PickingMode.Ignore;
                LoadSprites();
                string fonts = LoadFonts();
                Font(root, false);
                root.style.color = Text;
                root.style.fontSize = FontSize;
                UiLog.Verbose("UiKit: UI Toolkit panel made (scale with screen " + Screen.width + "x" + Screen.height + " from 1920x1080, " + fonts + ", game sprites " + Sprites.Count + ")");
            }
            catch (System.Exception e)
            {
                UiLog.Error("UiKit: UI Toolkit could not be started - the DrainTarPit menus are not available: " + e);
                _doc = null;
            }
        }

        // --- the game's look ---------------------------------------------------------------------------------------------------
        static void LoadSprites()
        {
            var want = new HashSet<string> { SpriteWindow, SpriteButton, SpriteButtonHot, SpriteButtonDown, SpriteCheck, SpriteCheckMark, SpriteSunken, SpriteList };
            foreach (var s in Resources.FindObjectsOfTypeAll<Sprite>())
                if (s != null && want.Contains(s.name) && !Sprites.ContainsKey(s.name)) Sprites[s.name] = s;
            foreach (var w in want) if (!Sprites.ContainsKey(w)) UiLog.Warn("UiKit: game sprite '" + w + "' not found, a plain colour is used instead");
        }

        /// The game's sprite as a 9-sliced background (its own border), or `fallback` as a flat colour when the sprite is missing.
        internal static void Skin(VisualElement e, string sprite, Color fallback)
        {
            Sprite s;
            if (sprite != null && Sprites.TryGetValue(sprite, out s) && s != null)
            {
                e.style.backgroundImage = new StyleBackground(s);
                e.style.backgroundColor = Color.clear;
                var b = s.border;                                                  // x = left, y = bottom, z = right, w = top
                e.style.unitySliceLeft = (int)b.x; e.style.unitySliceBottom = (int)b.y; e.style.unitySliceRight = (int)b.z; e.style.unitySliceTop = (int)b.w;
            }
            else { e.style.backgroundImage = StyleKeyword.None; e.style.backgroundColor = fallback; }
        }

        // --- fonts --------------------------------------------------------------------------------------------------------------
        static string LoadFonts()
        {
            Font emoji = null;
            foreach (var f in Resources.FindObjectsOfTypeAll<Font>())
            {
                if (f == null) continue;
                string n = f.name;
                if (n == "AveriaSerifLibre-Bold") _bodyFont = f;
                else if (n == "Norsebold" || (n == "Norse" && _titleFont == null)) _titleFont = f;
                else if (n == "NotoEmoji-Regular") emoji = f;
            }
            if (emoji == null) emoji = UnityEngine.Font.CreateDynamicFontFromOSFont(new[] { "Segoe UI Emoji", "Segoe UI Symbol", "Noto Emoji" }, 32);
            var emojiAsset = MakeAsset(emoji, null);
            _bodyAsset = MakeAsset(_bodyFont, emojiAsset);
            _titleAsset = MakeAsset(_titleFont, emojiAsset);
            return "text font " + Describe(_bodyFont, _bodyAsset) + ", title font " + Describe(_titleFont, _titleAsset) + ", emoji font " + Describe(emoji, emojiAsset);
        }

        static string Describe(Font f, FontAsset a) { return f == null ? "MISSING" : "'" + f.name + "'" + (a != null ? " (font asset made)" : " (legacy font, no emoji fallback)"); }

        /// A TextCore font asset made from a game font, with `fallback` for the glyphs it does not have (emoji). null = the legacy font is used.
        static FontAsset MakeAsset(Font f, FontAsset fallback)
        {
            if (f == null) return null;
            try
            {
                var a = FontAsset.CreateFontAsset(f);
                if (a == null) { UiLog.Warn("UiKit: font asset from '" + f.name + "' could not be made"); return null; }
                a.name = "DTP_" + f.name;
                if (fallback != null)
                {
                    if (a.fallbackFontAssetTable == null) a.fallbackFontAssetTable = new List<FontAsset>();
                    a.fallbackFontAssetTable.Add(fallback);
                }
                return a;
            }
            catch (System.Exception e) { UiLog.Warn("UiKit: font asset from '" + f.name + "' failed: " + e.Message); return null; }
        }

        /// Puts the text font (or the title font) on an element.
        internal static void Font(VisualElement e, bool title)
        {
            var asset = title ? _titleAsset : _bodyAsset;
            var font = title ? _titleFont : _bodyFont;
            if (asset != null) e.style.unityFontDefinition = FontDefinition.FromSDFFont(asset);
            else if (font != null) e.style.unityFont = font;
        }

        // --- window ---------------------------------------------------------------------------------------------------------------
        /// A movable window (drag the title bar), 60% of the screen wide at most and 70% high, centred; `body` scrolls.
        internal static VisualElement Window(string title, System.Action onClose, out UiScroll body)
        {
            var w = new VisualElement { name = "dtp-window" };
            w.style.position = Position.Absolute;
            w.style.width = Length.Percent(42); w.style.minWidth = 560; w.style.maxWidth = Length.Percent(60);
            w.style.height = Length.Percent(70); w.style.maxHeight = Length.Percent(70);
            w.style.left = Length.Percent(29); w.style.top = Length.Percent(15);
            Skin(w, SpriteWindow, Bg);
            w.style.unityBackgroundImageTintColor = new Color(0.42f, 0.36f, 0.31f, 1f);      // darker wood: the light panel made grey notes hard to read
            Pad(w, 10, 14);
            w.style.flexDirection = FlexDirection.Column;
            w.pickingMode = PickingMode.Position;

            var bar = new VisualElement();
            bar.style.flexDirection = FlexDirection.Row; bar.style.alignItems = Align.Center; bar.style.flexShrink = 0;
            bar.style.paddingLeft = 6; bar.style.paddingBottom = 6; bar.style.marginBottom = 6;
            bar.style.borderBottomWidth = 1; bar.style.borderBottomColor = Edge;
            var t = new Label(title); t.style.flexGrow = 1; t.style.fontSize = TitleSize; t.style.color = Accent;
            Font(t, true);
            bar.Add(t);
            var x = UiControls.Button("X", onClose); x.style.width = 40;
            bar.Add(x);
            w.Add(bar);
            Drag(bar, w);

            body = new UiScroll(title);
            Pad(body.Content, 4, 6);
            w.Add(body.Root);
            return w;
        }

        /// Dragging `handle` moves `target` (kept on screen).
        static void Drag(VisualElement handle, VisualElement target)
        {
            bool dragging = false;
            Vector2 start = Vector2.zero, origin = Vector2.zero;
            handle.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0 || e.target is Button) return;
                dragging = true; start = e.position; origin = new Vector2(target.resolvedStyle.left, target.resolvedStyle.top);
                handle.CapturePointer(e.pointerId);
            });
            handle.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!dragging) return;
                var p = origin + (Vector2)e.position - start;
                var parent = target.parent;
                float maxX = parent != null ? parent.resolvedStyle.width - 80f : 2000f, maxY = parent != null ? parent.resolvedStyle.height - 40f : 2000f;
                target.style.left = Mathf.Clamp(p.x, -target.resolvedStyle.width + 80f, maxX);
                target.style.top = Mathf.Clamp(p.y, 0f, maxY);
            });
            handle.RegisterCallback<PointerUpEvent>(e =>
            {
                if (!dragging) return;
                dragging = false;
                handle.ReleasePointer(e.pointerId);
                UiLog.Verbose("UiKit: window moved to " + target.resolvedStyle.left.ToString("0") + ", " + target.resolvedStyle.top.ToString("0"));
            });
        }

        internal static void Border(VisualElement e, Color c, float width, float radius)
        {
            e.style.borderTopColor = c; e.style.borderBottomColor = c; e.style.borderLeftColor = c; e.style.borderRightColor = c;
            e.style.borderTopWidth = width; e.style.borderBottomWidth = width; e.style.borderLeftWidth = width; e.style.borderRightWidth = width;
            e.style.borderTopLeftRadius = radius; e.style.borderTopRightRadius = radius; e.style.borderBottomLeftRadius = radius; e.style.borderBottomRightRadius = radius;
        }

        internal static void Pad(VisualElement e, float v, float h)
        {
            e.style.paddingTop = v; e.style.paddingBottom = v; e.style.paddingLeft = h; e.style.paddingRight = h;
        }
    }
}
