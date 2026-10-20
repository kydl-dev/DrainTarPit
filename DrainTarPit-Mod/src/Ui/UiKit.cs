using DrainTarPit.Logging;
using UnityEngine;
using UnityEngine.UIElements;

namespace DrainTarPit.Ui
{
    /// <summary>
    /// T-023 (user: the menus are UI Toolkit, at most 70% of the screen height and 60% of its width, movable, font Norse). One runtime panel
    /// (UIDocument on a hidden object, made on first use) holds every window of the mod. All looks are inline styles set from code: a game build
    /// has no USS importer and may not carry Unity's default runtime theme, so nothing here depends on one.
    /// </summary>
    internal static class UiKit
    {
        internal static readonly Color Bg = new(0.13f, 0.10f, 0.07f, 0.96f), Panel = new(0.20f, 0.15f, 0.10f, 1f), Edge = new(0.62f, 0.48f, 0.25f, 1f);
        internal static readonly Color Text = new(0.93f, 0.87f, 0.74f, 1f), Dim = new(0.65f, 0.60f, 0.50f, 1f), Accent = new(1f, 0.78f, 0.35f, 1f);
        internal static readonly Color Btn = new(0.30f, 0.22f, 0.13f, 1f), BtnHot = new(0.42f, 0.31f, 0.17f, 1f), Field = new(0.08f, 0.06f, 0.04f, 1f);

        static UIDocument _doc;
        static PanelSettings _settings;
        static Font _norse;
        static string _fontReport = "not looked up yet";

        /// The full-screen layer every window is added to (picking ignored, so clicks outside a window reach nothing of ours). null when UI Toolkit failed.
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
                _settings = ScriptableObject.CreateInstance<PanelSettings>();
                _settings.name = "DTP_PanelSettings";
                _settings.scaleMode = PanelScaleMode.ConstantPixelSize;
                _settings.sortingOrder = 500;                              // over the game's HUD
                var go = new GameObject("DTP_UI");
                Object.DontDestroyOnLoad(go);
                _doc = go.AddComponent<UIDocument>();
                _doc.panelSettings = _settings;
                var root = _doc.rootVisualElement;
                root.style.position = Position.Absolute;
                root.style.left = 0; root.style.top = 0; root.style.right = 0; root.style.bottom = 0;
                root.pickingMode = PickingMode.Ignore;
                FindNorse();
                if (_norse != null) root.style.unityFont = _norse;
                root.style.color = Text;
                root.style.fontSize = 16;
                ModLog.Verbose("UiKit: UI Toolkit panel made (theme " + (_settings.themeStyleSheet != null ? _settings.themeStyleSheet.name : "none") +
                    ", text settings " + (_settings.textSettings != null ? _settings.textSettings.name : "none") + ", font " + _fontReport + ", screen " + Screen.width + "x" + Screen.height + ")");
            }
            catch (System.Exception e)
            {
                ModLog.Error("UiKit: UI Toolkit could not be started - the DrainTarPit menus are not available: " + e);
                _doc = null;
            }
        }

        /// The game's Norse font (always loaded: the game UI uses it). Logged which one was found.
        static void FindNorse()
        {
            Font best = null;
            var names = new System.Text.StringBuilder();
            foreach (var f in Resources.FindObjectsOfTypeAll<Font>())
            {
                if (f == null) continue;
                if (names.Length < 400) names.Append(f.name).Append(", ");
                string n = f.name.ToLowerInvariant();
                if (n == "norse" || (best == null && n.StartsWith("norse") && !n.Contains("bold"))) best = f;
            }
            _norse = best;
            _fontReport = best != null ? "'" + best.name + "'" : "Norse NOT found (fonts loaded: " + names + ")";
        }

        // --- window -------------------------------------------------------------------------------------------------------
        /// A movable window (drag the title bar) at most 60% of the screen wide and 70% high, centred. `body` scrolls.
        internal static VisualElement Window(string title, System.Action onClose, out ScrollView body)
        {
            var w = new VisualElement { name = "dtp-window" };
            w.style.position = Position.Absolute;
            w.style.maxWidth = Length.Percent(60); w.style.maxHeight = Length.Percent(70);
            w.style.width = Length.Percent(36); w.style.minWidth = 420;
            w.style.left = Length.Percent(32); w.style.top = Length.Percent(15);
            w.style.backgroundColor = Bg;
            Border(w, Edge, 2, 6);
            w.style.flexDirection = FlexDirection.Column;
            w.pickingMode = PickingMode.Position;

            var bar = new VisualElement();
            bar.style.flexDirection = FlexDirection.Row; bar.style.alignItems = Align.Center;
            bar.style.backgroundColor = Panel;
            bar.style.paddingLeft = 10; bar.style.paddingRight = 4; bar.style.paddingTop = 4; bar.style.paddingBottom = 4;
            var t = new Label(title); t.style.flexGrow = 1; t.style.fontSize = 20; t.style.color = Accent;
            bar.Add(t);
            var x = UiControls.Button("X", onClose); x.style.width = 32;
            bar.Add(x);
            w.Add(bar);
            Drag(bar, w);

            body = new ScrollView(ScrollViewMode.Vertical);
            body.style.flexGrow = 1; body.style.flexShrink = 1;
            body.contentContainer.style.paddingLeft = 12; body.contentContainer.style.paddingRight = 12;
            body.contentContainer.style.paddingTop = 8; body.contentContainer.style.paddingBottom = 10;
            w.Add(body);
            return w;
        }

        /// Dragging `handle` moves `target` (kept on screen).
        static void Drag(VisualElement handle, VisualElement target)
        {
            bool dragging = false;
            Vector2 start = Vector2.zero, origin = Vector2.zero;
            handle.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0) return;
                dragging = true; start = e.position; origin = new Vector2(target.resolvedStyle.left, target.resolvedStyle.top);
                handle.CapturePointer(e.pointerId);
                ModLog.Verbose("UiKit: window drag started at " + origin);
            });
            handle.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!dragging) return;
                var p = origin + (Vector2)e.position - start;
                var parent = target.parent;
                float maxX = parent != null ? parent.resolvedStyle.width - 60f : 2000f, maxY = parent != null ? parent.resolvedStyle.height - 30f : 2000f;
                target.style.left = Mathf.Clamp(p.x, -target.resolvedStyle.width + 60f, maxX);
                target.style.top = Mathf.Clamp(p.y, 0f, maxY);
            });
            handle.RegisterCallback<PointerUpEvent>(e =>
            {
                if (!dragging) return;
                dragging = false;
                handle.ReleasePointer(e.pointerId);
                ModLog.Verbose("UiKit: window moved to " + target.resolvedStyle.left.ToString("0") + ", " + target.resolvedStyle.top.ToString("0"));
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
