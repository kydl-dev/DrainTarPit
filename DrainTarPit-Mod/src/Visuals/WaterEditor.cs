using System.Collections.Generic;
using System.Globalization;
using DrainTarPit.Drain;
using DrainTarPit.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace DrainTarPit.Visuals
{
    /// <summary>
    /// The live water editor (IMGUI window, [Visuals] EditorKey, default F8).
    ///
    /// What it edits: the ONE material that every water pool shows (PoolWater.Active: the copy of the game's ocean material). Every shader property
    /// (floats / ranges, colours, vectors, texture tiling + offset, keywords, render queue) is a slider / field and applies to all pools at once, live.
    /// It also has the pool settings from the config (depth look, mesh fix, film shedding, walls, ...), the numbers of the nearest water pool and test
    /// buttons (pour a stroke of water at the crosshair, empty the pool), so a look AND the pool behaviour can be judged without leaving the game.
    ///
    /// [Save look] writes BepInEx/config/DrainTarPit.WaterLook.txt (see WaterLook): it is applied again at every start and it is the draft to build a Unity
    /// material from. While the window is open the player cannot move (Player.TakeInput = false) and the camera does not follow the mouse.
    /// NOT tested in game.
    /// </summary>
    internal static class WaterEditor
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        const int WinId = 0x44545057;

        static bool _open;
        static Rect _win = new(20f, 20f, 480f, 700f);
        static Vector2 _scroll;
        static string _filter = "";
        static string _newKeyword = "";
        static string _status = "";
        static Texture2D _bg;            // 1x1 opaque texture: the window background (the default IMGUI window skin is see-through)
        static GUIStyle _winStyle;
        static readonly Color BgColor = new Color(0.10f, 0.11f, 0.13f, 1f);
        static readonly Dictionary<string, string> Buf = new();
        static readonly Dictionary<string, Vector2> Ranges = new();
        static readonly Dictionary<string, bool> Fold = new();

        // nearest water pool (refreshed twice a second)
        static float _nextInfo;
        static LiquidVolume _pool;
        static TarPitStats.PoolStats _stats;
        static bool _haveStats;
        static float _poolDist;

        public static bool IsOpen { get { return _open; } }

        // --- install / input ------------------------------------------------------------------------------------------------
        /// Called once from Plugin.Awake after the other patches. The input blocking (TakeInput / camera / mouse wheel patches) lives in WaterEditorInput.
        public static void Install() { WaterEditorInput.Install(); }

        /// Plugin.Update
        public static void Tick()
        {
            try
            {
                var key = ModSettings.EditorKey != null ? ModSettings.EditorKey.Value : KeyCode.None;
                if (key != KeyCode.None && Player.m_localPlayer != null && BepInEx.UnityInput.Current.GetKeyDown(key)) SetOpen(!_open);
                if (!_open) return;
                if (Player.m_localPlayer == null) { SetOpen(false); return; }
                WaterEditorLog.Tick();
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                if (Time.unscaledTime >= _nextInfo) { _nextInfo = Time.unscaledTime + 0.5f; RefreshPool(); }
            }
            catch (System.Exception e) { ModLog.VerboseWarn("WaterEditor.Tick: " + e.Message); }
        }

        static void SetOpen(bool open)
        {
            bool was = _open;
            _open = open;
            if (was != open)
            {
                if (!open) WaterEditorLog.Flush();
                WaterEditorLog.Event(open ? "window opened" : "window closed");
            }
            var cfg = Plugin.Instance != null ? Plugin.Instance.Config : null;
            if (cfg != null)
            {
                // a slider writes the config entry on every frame: no file write (and no config-file reload) while dragging, one save when the window closes
                cfg.SaveOnConfigSet = !open;
                if (!open) { try { cfg.Save(); } catch { } }
            }
            if (open) { _nextInfo = 0f; _status = ""; }
        }

        static void RefreshPool()
        {
            var p = Player.m_localPlayer;
            _pool = p != null ? TarPitStats.Nearest(p.transform.position, LiquidType.Water) : null;
            _haveStats = _pool != null && TarPitStats.Stats(_pool, out _stats);
            if (_pool != null)
            {
                var o = _pool.transform.position; var q = p.transform.position;
                _poolDist = Mathf.Sqrt((o.x - q.x) * (o.x - q.x) + (o.z - q.z) * (o.z - q.z));
            }
        }

        // --- window ---------------------------------------------------------------------------------------------------------
        /// The default IMGUI window skin is partly transparent. A 1x1 texture with alpha 1 is used as the background of every state of the window style, and the
        /// same colour is also drawn as a solid rectangle under the window, so nothing of the game shows through.
        static GUIStyle WindowStyle()
        {
            if (_bg == null)
            {
                _bg = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                _bg.SetPixel(0, 0, BgColor);
                _bg.Apply();
                _bg.hideFlags = HideFlags.HideAndDontSave;       // survives scene changes / Resources.UnloadUnusedAssets
                _winStyle = null;
            }
            if (_winStyle == null)
            {
                _winStyle = new GUIStyle(GUI.skin.window) { border = new RectOffset(0, 0, 0, 0) };
                _winStyle.normal.background = _bg;   _winStyle.onNormal.background = _bg;
                _winStyle.hover.background = _bg;    _winStyle.onHover.background = _bg;
                _winStyle.active.background = _bg;   _winStyle.onActive.background = _bg;
                _winStyle.focused.background = _bg;  _winStyle.onFocused.background = _bg;
                _winStyle.normal.textColor = Color.white; _winStyle.onNormal.textColor = Color.white;
                _winStyle.hover.textColor = Color.white;  _winStyle.onHover.textColor = Color.white;
                _winStyle.active.textColor = Color.white; _winStyle.onActive.textColor = Color.white;
                _winStyle.focused.textColor = Color.white; _winStyle.onFocused.textColor = Color.white;
            }
            return _winStyle;
        }

        /// Plugin.OnGUI
        public static void Draw()
        {
            if (!_open) return;
            try
            {
                if (Event.current.rawType == EventType.MouseUp) WaterEditorLog.Flush();       // a drag ended: log the final values
                _win.height = Mathf.Min(720f, Screen.height - 40f);
                var style = WindowStyle();
                if (Event.current.type == EventType.Repaint)
                {
                    var oldColor = GUI.color; GUI.color = Color.white;
                    GUI.DrawTexture(_win, _bg, ScaleMode.StretchToFill, false);       // opaque, same rectangle as the window
                    GUI.color = oldColor;
                }
                _win = GUI.Window(WinId, _win, DrawWindow, "DrainTarPit water editor   (" + ModSettings.EditorKey.Value + " closes)", style);
                _win.x = Mathf.Clamp(_win.x, -_win.width + 80f, Screen.width - 80f);
                _win.y = Mathf.Clamp(_win.y, 0f, Screen.height - 30f);
            }
            catch (System.Exception e) { ModLog.VerboseWarn("WaterEditor.Draw: " + e.Message); }
        }

        static void DrawWindow(int id)
        {
            _scroll = GUILayout.BeginScrollView(_scroll);
            var m = PoolWater.Active;

            DrawLookButtons(m);
            DrawPoolSettings();
            DrawPoolInfo();
            if (m == null) GUILayout.Label("No pool material yet. Load a world (a water pool or a pump must exist for the look to be built).");
            else DrawMaterial(m);

            if (_status.Length > 0) GUILayout.Label(_status);
            GUILayout.EndScrollView();
            GUI.DragWindow(new Rect(0f, 0f, 10000f, 22f));
        }

        // --- buttons ----------------------------------------------------------------------------------------------------------
        static void DrawLookButtons(Material m)
        {
            Header("Look");
            GUILayout.Label(m == null ? "material: none" : "material '" + m.name + "'  shader '" + (m.shader != null ? m.shader.name : "?") + "'  " + (PoolWater.StyleInUse != WaterStyleOption.Game ? "(water style " + PoolWater.StyleInUse + ": tuning is NOT saved)" : PoolWater.IsOceanCopy ? "(copy of the ocean)" : "(FALLBACK tint: the ocean material was not found yet)"));
            var style = PoolWater.StyleInUse;
            if (style != WaterStyleOption.Game)
            {
                // T-050: a Unity style material: its tuning lives only for this session, so the only action is going back to its shipped values
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Reset " + style + " to its shipped values"))
                {
                    int dropped = PoolWater.ResetStyle(style);
                    Result("Reset " + style, dropped == 1 ? "tuned copy thrown away, shipped values loaded again" : "style was not loaded yet, nothing to reset");
                }
                if (GUILayout.Button("Dump to log")) { ModLog.Info(WaterLook.Dump(m)); Result("Dump to log", "written to the BepInEx log"); }
                GUILayout.EndHorizontal();
                return;
            }
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Save look")) Result("Save look", WaterLook.Save(m) ? "saved to " + WaterLook.Path : "saving failed (see the log)");
            if (GUILayout.Button("Reload saved look")) { PoolWater.ResetLook(true); Result("Reload saved look", "ocean material copied again + saved look applied"); }
            if (GUILayout.Button("Reset to ocean")) { PoolWater.ResetLook(false); Result("Reset to ocean", "ocean material copied again, no saved look"); }
            if (GUILayout.Button("Dump to log")) { ModLog.Info(WaterLook.Dump(m)); Result("Dump to log", "written to the BepInEx log"); }
            GUILayout.EndHorizontal();
        }

        static void DrawPoolSettings()
        {
            Header("Pool behaviour / mesh (config, applies live)");
            if (!Open("settings", true)) return;
            Entry(ModSettings.WaterDepthLook, "depth look (0 shallow .. 1 sea)", 0f, 1f);
            Entry(ModSettings.PoolMeshFix, "mesh fix (dry points follow the water)");
            Entry(ModSettings.PoolMeshReach, "mesh fix reach (cells)", 1, 16);
            Entry(ModSettings.PoolShedFilm, "shed thin films");
            Entry(ModSettings.PoolFilmDepth, "film depth (m)", 0.01f, 0.2f);
            Entry(ModSettings.PoolFilmEvaporates, "flat films evaporate");
            Entry(ModSettings.PoolWalls, "built pieces are walls");
            Entry(ModSettings.PoolOverflow, "overflow over the rim");
            Entry(ModSettings.PoolRunoff, "run on into the next pool");
            Header("Water flow (applies live, to existing and new pools)");
            Entry(ModSettings.WaterFlow, "water flow (viscosity x tar)", 1f, 64f);
            Entry(ModSettings.PitFlowBoost, "flow boost while pouring (x)", 1f, 100f);
            Entry(ModSettings.PitFlowBoostSeconds, "boost lasts after the last stroke (s)", 0f, 300f);
            GUILayout.Label("flow between two cells grows with the square root of the viscosity; the boost is multiplied on top of the water flow for the next strokes. read at world load: WaterCellSize " + ModSettings.WaterCellSize.Value.ToString("0.##", Inv) + " m, WaterPoolSize " + ModSettings.WaterPoolSize.Value.ToString("0.#", Inv) + " m (change in the config, reload the world)");
        }

        static void DrawPoolInfo()
        {
            Header("Nearest water pool");
            if (_pool == null) GUILayout.Label("no water pool loaded");
            else if (_haveStats)
            {
                GUILayout.Label("'" + _pool.name + "' " + _poolDist.ToString("0") + " m away   grid " + _stats.Width + "x" + _stats.Width + " @ " + _stats.Scale.ToString("0.##", Inv) + " m   owner " + _stats.Owner + "   pump pool " + _stats.Pump);
                GUILayout.Label("volume " + _stats.Total.ToString("0.00", Inv) + " m3   wet cells " + _stats.Wet + " (thinner than film depth: " + _stats.Thin + ")   deepest " + _stats.Max.ToString("0.000", Inv) + " m   wall cells " + _stats.WallCells);
            }
            else GUILayout.Label("pool found but its data is not ready");
            GUILayout.Label("last deposit: " + TarPitStats.LastDeposit);
            GUILayout.Label("where the water went (this pool, since the world was loaded): " + TarPitStats.BudgetText(_pool));
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Pour one stroke at crosshair")) Result("Pour one stroke at crosshair", PourAtCrosshair());
            if (GUILayout.Button("Empty this pool")) Result("Empty this pool", _pool != null && TarPitDebug.DebugEmpty(_pool) ? "pool emptied" : "not emptied (no pool, or you are not its owner)");
            GUILayout.EndHorizontal();
            if (GUILayout.Button("Empty pool at crosshair (tar or water pump pool)")) Result("Empty pool at crosshair", EmptyAtCrosshair());
        }

        static string EmptyAtCrosshair()
        {
            var cam = Camera.main;
            if (cam == null) return "no camera";
            RaycastHit hit;
            if (!Physics.Raycast(cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f)), out hit, 80f, ~0, QueryTriggerInteraction.Ignore)) return "the crosshair points at nothing within 80 m";
            return TarPitDebug.EmptyPumpPoolAt(hit.point);
        }

        static string PourAtCrosshair()
        {
            var cam = Camera.main;
            if (cam == null) return "no camera";
            RaycastHit hit;
            if (!Physics.Raycast(cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f)), out hit, 80f, ~0, QueryTriggerInteraction.Ignore)) return "the crosshair points at nothing within 80 m";
            return TarPool.DebugPour(hit.point, ModSettings.VolumePerStroke.Value, PumpLiquid.Water);
        }

        // --- the material ---------------------------------------------------------------------------------------------------
        static void DrawMaterial(Material m)
        {
            Header("Material (applies to every pool)");
            GUILayout.BeginHorizontal();
            GUILayout.Label("filter", GUILayout.Width(40f));
            _filter = GUILayout.TextField(_filter);
            if (GUILayout.Button("x", GUILayout.Width(24f))) _filter = "";
            GUILayout.EndHorizontal();

            int q = Mathf.RoundToInt(Number("queue", "render queue", m.renderQueue, 1000f, 4000f));
            if (q != m.renderQueue) { WaterEditorLog.Change("material render queue", m.renderQueue.ToString(Inv), q.ToString(Inv)); m.renderQueue = q; }

            var sh = m.shader;
            if (sh == null) return;
            int count = sh.GetPropertyCount();
            string f = _filter.Trim().ToLowerInvariant();

            if (Open("floats", true))
                for (int i = 0; i < count; i++)
                {
                    var t = sh.GetPropertyType(i);
                    if (t == ShaderPropertyType.Color || t == ShaderPropertyType.Vector || t == ShaderPropertyType.Texture) continue;
                    string name = sh.GetPropertyName(i);
                    if (!Match(name, f)) continue;
                    float v = m.GetFloat(name), lo, hi;
                    if (t == ShaderPropertyType.Range) { var r = sh.GetPropertyRangeLimits(i); lo = r.x; hi = r.y; }
                    else { var r = RangeFor(name, v); lo = r.x; hi = r.y; }
                    float nv = Number(name, name, v, lo, hi);
                    if (!Mathf.Approximately(nv, v)) { WaterEditorLog.Change("material " + name, WaterEditorLog.F(v), WaterEditorLog.F(nv)); m.SetFloat(name, nv); }
                }

            if (Open("colors", true))
                for (int i = 0; i < count; i++)
                {
                    if (sh.GetPropertyType(i) != ShaderPropertyType.Color) continue;
                    string name = sh.GetPropertyName(i);
                    if (!Match(name, f)) continue;
                    var c = m.GetColor(name);
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(name, GUILayout.Width(170f));
                    var sw = GUILayoutUtility.GetRect(40f, 16f, GUILayout.Width(40f));
                    var old = GUI.color; GUI.color = new Color(c.r, c.g, c.b, 1f); GUI.DrawTexture(sw, Texture2D.whiteTexture); GUI.color = old;
                    GUILayout.EndHorizontal();
                    var n = new Color(
                        Number(name + ".r", "   r", c.r, 0f, RangeFor(name + ".r", c.r).y),
                        Number(name + ".g", "   g", c.g, 0f, RangeFor(name + ".g", c.g).y),
                        Number(name + ".b", "   b", c.b, 0f, RangeFor(name + ".b", c.b).y),
                        Number(name + ".a", "   a", c.a, 0f, RangeFor(name + ".a", c.a).y));
                    if (n != c) { WaterEditorLog.Change("material " + name, WaterEditorLog.F(c), WaterEditorLog.F(n)); m.SetColor(name, n); }
                }

            if (Open("vectors", true))
                for (int i = 0; i < count; i++)
                {
                    if (sh.GetPropertyType(i) != ShaderPropertyType.Vector) continue;
                    string name = sh.GetPropertyName(i);
                    if (!Match(name, f)) continue;
                    var v = m.GetVector(name);
                    GUILayout.Label(name);
                    var n = new Vector4(
                        Number(name + ".x", "   x", v.x, RangeFor(name + ".x", v.x).x, RangeFor(name + ".x", v.x).y),
                        Number(name + ".y", "   y", v.y, RangeFor(name + ".y", v.y).x, RangeFor(name + ".y", v.y).y),
                        Number(name + ".z", "   z", v.z, RangeFor(name + ".z", v.z).x, RangeFor(name + ".z", v.z).y),
                        Number(name + ".w", "   w", v.w, RangeFor(name + ".w", v.w).x, RangeFor(name + ".w", v.w).y));
                    if (n != v) { WaterEditorLog.Change("material " + name, WaterEditorLog.F(v), WaterEditorLog.F(n)); m.SetVector(name, n); }
                }

            if (Open("textures", false))
                for (int i = 0; i < count; i++)
                {
                    if (sh.GetPropertyType(i) != ShaderPropertyType.Texture) continue;
                    string name = sh.GetPropertyName(i);
                    if (!Match(name, f)) continue;
                    var tex = m.GetTexture(name);
                    GUILayout.Label(name + "  =  " + (tex != null ? tex.name : "none"));
                    var s = m.GetTextureScale(name); var o = m.GetTextureOffset(name);
                    var ns = new Vector2(Number(name + ".sx", "   tiling x", s.x, 0f, RangeFor(name + ".sx", s.x).y), Number(name + ".sy", "   tiling y", s.y, 0f, RangeFor(name + ".sy", s.y).y));
                    var no = new Vector2(Number(name + ".ox", "   offset x", o.x, -1f, 1f), Number(name + ".oy", "   offset y", o.y, -1f, 1f));
                    if (ns != s) { WaterEditorLog.Change("material " + name + " tiling", WaterEditorLog.F(s), WaterEditorLog.F(ns)); m.SetTextureScale(name, ns); }
                    if (no != o) { WaterEditorLog.Change("material " + name + " offset", WaterEditorLog.F(o), WaterEditorLog.F(no)); m.SetTextureOffset(name, no); }
                }

            if (Open("keywords", false))
            {
                foreach (var k in m.shaderKeywords)
                {
                    if (!Match(k, f)) continue;
                    if (!GUILayout.Toggle(true, k)) { WaterEditorLog.Event("material keyword '" + k + "' enabled -> disabled"); m.DisableKeyword(k); break; }       // collection changed: draw again next frame
                }
                GUILayout.BeginHorizontal();
                _newKeyword = GUILayout.TextField(_newKeyword);
                if (GUILayout.Button("enable keyword", GUILayout.Width(120f)) && _newKeyword.Trim().Length > 0) { WaterEditorLog.Event("material keyword '" + _newKeyword.Trim() + "' disabled -> enabled"); m.EnableKeyword(_newKeyword.Trim()); _newKeyword = ""; }
                GUILayout.EndHorizontal();
            }
        }

        // --- helpers --------------------------------------------------------------------------------------------------------
        static void Header(string text)
        {
            GUILayout.Space(6f);
            GUILayout.Label("<b>" + text + "</b>", RichLabel());
        }

        static GUIStyle _rich;
        static GUIStyle RichLabel()
        {
            if (_rich == null) { _rich = new GUIStyle(GUI.skin.label) { richText = true }; }
            return _rich;
        }

        /// A button was pressed: show the result in the window and write it to the log.
        static void Result(string button, string text)
        {
            _status = text;
            WaterEditorLog.Event("button '" + button + "' -> " + text);
        }

        static bool Open(string key, bool defaultOpen)
        {
            bool o;
            if (!Fold.TryGetValue(key, out o)) o = defaultOpen;
            o = GUILayout.Toggle(o, (o ? "v  " : ">  ") + key, GUI.skin.button);
            Fold[key] = o;
            return o;
        }

        static bool Match(string name, string filterLower)
        {
            return filterLower.Length == 0 || name.ToLowerInvariant().Contains(filterLower);
        }

        /// The slider range of a property that has none: made from the first value seen (0 .. 2x, at least 0 .. 1) and widened when a typed value falls outside.
        static Vector2 RangeFor(string key, float v)
        {
            Vector2 r;
            if (!Ranges.TryGetValue(key, out r))
            {
                float a = Mathf.Abs(v);
                r = new Vector2(v < 0f ? v * 2f : 0f, Mathf.Max(1f, a * 2f));
            }
            if (v < r.x) r.x = v;
            if (v > r.y) r.y = v;
            Ranges[key] = r;
            return r;
        }

        /// label + slider + text field; returns the new value.
        static float Number(string key, string label, float v, float lo, float hi)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(170f));
            float nv = GUILayout.HorizontalSlider(v, lo, hi);
            string ctl = "dtpf:" + key;
            GUI.SetNextControlName(ctl);
            bool focused = GUI.GetNameOfFocusedControl() == ctl;
            string cur;
            if (!focused || !Buf.TryGetValue(ctl, out cur)) cur = nv.ToString("0.####", Inv);
            string typed = GUILayout.TextField(cur, GUILayout.Width(64f));
            if (typed != cur)
            {
                Buf[ctl] = typed;
                float p;
                if (float.TryParse(typed, NumberStyles.Float, Inv, out p)) nv = p;
            }
            else if (focused) Buf[ctl] = cur;
            GUILayout.EndHorizontal();
            return nv;
        }

        static void Entry(BepInEx.Configuration.ConfigEntry<float> e, string label, float lo, float hi)
        {
            if (e == null) return;
            float nv = Number("cfg." + e.Definition.Key, label, e.Value, lo, hi);
            nv = Mathf.Clamp(nv, lo, hi);
            if (!Mathf.Approximately(nv, e.Value)) { WaterEditorLog.Change(CfgName(e), WaterEditorLog.F(e.Value), WaterEditorLog.F(nv)); e.Value = nv; }
        }

        static void Entry(BepInEx.Configuration.ConfigEntry<int> e, string label, int lo, int hi)
        {
            if (e == null) return;
            int nv = Mathf.Clamp(Mathf.RoundToInt(Number("cfg." + e.Definition.Key, label, e.Value, lo, hi)), lo, hi);
            if (nv != e.Value) { WaterEditorLog.Change(CfgName(e), e.Value.ToString(Inv), nv.ToString(Inv)); e.Value = nv; }
        }

        static void Entry(BepInEx.Configuration.ConfigEntry<bool> e, string label)
        {
            if (e == null) return;
            bool nv = GUILayout.Toggle(e.Value, label);
            if (nv != e.Value) { WaterEditorLog.Change(CfgName(e), e.Value ? "on" : "off", nv ? "on" : "off"); e.Value = nv; }
        }

        static string CfgName(BepInEx.Configuration.ConfigEntryBase e)
        {
            return "config [" + e.Definition.Section + "] " + e.Definition.Key;
        }
    }
}
