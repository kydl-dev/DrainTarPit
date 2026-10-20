using System.Collections.Generic;
using DrainTarPit.Logging;
using DrainTarPit.TextLocal;
using DrainTarPit.Ui;
using UnityEngine;

namespace DrainTarPit.Signs
{
    /// <summary>
    /// T-023 (d) (e) (f): the pool menu in the game ([Visuals] PoolMenuKey, F9). The window is PoolMenuView (Unity project, previewed there);
    /// this is its host: the named pools from the server (PoolRegistry), the world style (admin), this player's style per pool (PoolStyles).
    /// While it is open the character does not move (UiInputLock); the key again, Escape or X close it.
    /// </summary>
    internal sealed class PoolMenu : IPoolMenuHost
    {
        const string LockName = "PoolMenu";
        static PoolMenu _open;
        PoolMenuView _view;
        int _shownVersion = -1;
        readonly List<PoolMenuEntry> _pools = new();

        internal static bool IsOpen { get { return _open != null; } }

        /// Every frame (Plugin.Update): the menu key opens / closes; a new list from the server redraws it; Escape closes.
        internal static void Tick()
        {
            var key = ModSettings.PoolMenuKey != null ? ModSettings.PoolMenuKey.Value : KeyCode.None;
            if (_open == null)
            {
                if (key == KeyCode.None || !ZInput.GetKeyDown(key, true) || Player.m_localPlayer == null) return;
                if (Chat.instance != null && Chat.instance.HasFocus() || global::Console.IsVisible() || Menu.IsVisible() || InventoryGui.IsVisible()) return;
                Open();
                return;
            }
            if (ZInput.GetKeyDown(key, true) || ZInput.GetKeyDown(KeyCode.Escape, true) || Player.m_localPlayer == null) { _open.Close(Player.m_localPlayer == null ? "no player" : "key"); return; }
            if (PoolRegistry.Version != _open._shownVersion) _open.Redraw();
        }

        static void Open()
        {
            var root = UiKit.Root;
            if (root == null) { ModLog.Warn("PoolMenu: UI Toolkit is not available, the pool menu cannot open"); return; }
            var m = new PoolMenu();
            int asked = PoolRegistry.Request();
            m.Fill();
            m._view = new PoolMenuView(m);
            root.Add(m._view.Window);
            _open = m;
            UiInputLock.Hold(LockName);
            ModLog.Verbose("PoolMenu: opened (list request sent: " + asked + ", world style '" + m.WorldStyle + "', admin " + m.IsAdmin + ")");
        }

        void Fill()
        {
            _pools.Clear();
            foreach (var e in PoolRegistry.Received)
            {
                var mine = PoolStyles.MineFor(e.Id);
                _pools.Add(new PoolMenuEntry { Id = e.Id, Name = e.Name, Style = e.Style, Pos = e.Pos, Portal = e.Portal, PortalDistance = e.PortalDistance, MyStyle = mine.HasValue ? mine.Value.ToString() : "" });
            }
            _shownVersion = PoolRegistry.Version;
        }

        void Redraw()
        {
            Fill();
            if (_view != null) _view.Rebuild();
            ModLog.Verbose("PoolMenu: redrawn with " + _pools.Count + " pool(s)");
        }

        // --- IPoolMenuHost -------------------------------------------------------------------------------------------------
        public string T(string key, params string[] args) { return Loc.T(key, args); }
        public string MenuKey { get { return ModSettings.PoolMenuKey != null ? ModSettings.PoolMenuKey.Value.ToString() : "F9"; } }
        public bool IsAdmin { get { return ZNet.instance != null && ZNet.instance.LocalPlayerIsAdminOrHost(); } }
        public string WorldStyle { get { var w = PoolStyles.World(); return w.HasValue ? w.Value.ToString() : ""; } }
        public string DefaultStyle { get { return PoolStyles.Default.ToString(); } }
        public IList<string> Styles { get { return System.Enum.GetNames(typeof(WaterStyleOption)); } }
        public IList<PoolMenuEntry> Pools { get { return _pools; } }
        public bool Waiting { get { return PoolRegistry.Version == 0 && Time.unscaledTime - PoolRegistry.RequestedAt < 10f; } }

        public void SetWorldStyle(string style)
        {
            int sent = PoolRegistry.SetWorldStyle(style);
            ModLog.Verbose("PoolMenu: world style '" + style + "' asked for (sent " + sent + ")");
            Plugin.Instance.RunCoroutine(RedrawSoon());                       // the global key comes back from the server a moment later
        }

        System.Collections.IEnumerator RedrawSoon()
        {
            yield return new WaitForSecondsRealtime(0.6f);
            if (_open == this && _view != null) { _view.Rebuild(); ModLog.Verbose("PoolMenu: redrawn after the world style change (now '" + WorldStyle + "')"); }
        }

        public void SetPoolStyle(string id, string style)
        {
            WaterStyleOption s;
            WaterStyleOption? choice = style.Length > 0 && System.Enum.TryParse(style, true, out s) ? s : (WaterStyleOption?)null;
            int n = PoolStyles.SetMine(id, choice);
            foreach (var p in _pools) if (p.Id == id) p.MyStyle = choice.HasValue ? choice.Value.ToString() : "";
            ModLog.Verbose("PoolMenu: my style for pool " + id + " -> '" + style + "' (" + n + " personal choice(s) in this world)");
        }

        public void Refresh() { int asked = PoolRegistry.Request(); ModLog.Verbose("PoolMenu: refresh asked (" + asked + ")"); }

        public void Close(string why)
        {
            if (_view != null) _view.Window.RemoveFromHierarchy();
            _view = null;
            if (_open == this) _open = null;
            UiInputLock.Release(LockName);
            ModLog.Verbose("PoolMenu: closed (" + why + ")");
        }
    }
}
