using System.Collections.Generic;
using DrainTarPit.Signs;
using UnityEngine;
using UnityEngine.UIElements;

namespace DrainTarPit.Ui
{
    /// <summary>One named pool in the pool menu.</summary>
    public sealed class PoolMenuEntry
    {
        public string Id = "", Name = "", Style = "", Portal = "";     // Style = the sign's text style (SignStyle text form)
        public Vector3 Pos;
        public float PortalDistance = -1f;                              // -1 = no portal in the world
        public string MyStyle = "";                                     // this player's choice, "" = the default
    }

    /// <summary>What the pool menu needs from the outside: the game (PoolMenu in the mod) or the Unity preview.</summary>
    public interface IPoolMenuHost
    {
        string T(string key, params string[] args);
        string MenuKey { get; }                  // the key that opens / closes the menu (shown in the footer)
        bool IsAdmin { get; }
        string WorldStyle { get; }               // "" = not set in this world
        string DefaultStyle { get; }             // what a pool shows without a personal choice
        IList<string> Styles { get; }            // Game, Tropical, Lake, Ashlands
        IList<PoolMenuEntry> Pools { get; }
        bool Waiting { get; }                    // the list was asked for and has not arrived yet
        void SetWorldStyle(string style);        // admin only; "" = clear
        void SetPoolStyle(string id, string style); // "" = back to the default
        void Refresh();
        void Close(string why);
    }

    /// <summary>
    /// T-023 (d) (e) (f) (user, 2026-10-20): the pool menu (F9). Admins set the world water style (every pool, every player); every player
    /// sees the pools that have a named sign (name in its sign style, position, closest portal) and picks a style per pool that only they see.
    /// </summary>
    public sealed class PoolMenuView
    {
        readonly IPoolMenuHost _host;
        public readonly VisualElement Window;
        readonly VisualElement _content;

        public PoolMenuView(IPoolMenuHost host)
        {
            _host = host;
            UiScroll body;
            Window = UiKit.Window(_host.T("dtp_ui_pools_title"), _host.T("dtp_ui_pools_close", _host.MenuKey), () => _host.Close("X"), out body);
            _content = body.Content;
            Rebuild();
        }

        /// Draws everything again (new list from the server, a style changed).
        public void Rebuild()
        {
            _content.Clear();
            BuildWorld();
            BuildList();
            UiLog.Verbose("PoolMenuView: built (" + _host.Pools.Count + " pool(s), admin " + _host.IsAdmin + ", world style '" + _host.WorldStyle + "', waiting " + _host.Waiting + ")");
        }

        string StyleName(string s) { return s.Length == 0 ? _host.T("dtp_ui_style_notset") : _host.T("dtp_ui_style_" + s.ToLowerInvariant()); }

        void BuildWorld()
        {
            _content.Add(UiControls.Heading(_host.T("dtp_ui_world_style")));
            if (_host.IsAdmin)
            {
                var opts = new List<string> { "" }; opts.AddRange(_host.Styles);
                _content.Add(UiControls.Row(_host.T("dtp_ui_world_style_all"), UiControls.Dropdown(opts, _host.WorldStyle, StyleName, s => _host.SetWorldStyle(s))));
                _content.Add(UiControls.Note(_host.T("dtp_ui_world_style_note")));
            }
            else
            {
                _content.Add(UiControls.Row(_host.T("dtp_ui_world_style_all"), UiKit.Label(StyleName(_host.WorldStyle), UiKit.FontSize, UiKit.Gold)));
                _content.Add(UiControls.Note(_host.T("dtp_ui_world_style_admin_only")));
            }
        }

        void BuildList()
        {
            _content.Add(UiControls.Heading(_host.T("dtp_ui_pools")));
            var top = UiControls.Line(UiKit.Label(_host.T("dtp_ui_pools_count", _host.Pools.Count.ToString()), UiKit.FontSize, UiKit.Text), UiControls.Button(_host.T("dtp_ui_refresh"), _host.Refresh));
            top.style.justifyContent = Justify.SpaceBetween;
            _content.Add(top);
            _content.Add(UiControls.Note(_host.T("dtp_ui_pools_note")));
            if (_host.Waiting && _host.Pools.Count == 0) { _content.Add(UiControls.Note(_host.T("dtp_ui_pools_loading"))); return; }
            if (_host.Pools.Count == 0) { _content.Add(UiControls.Note(_host.T("dtp_ui_pools_none"))); return; }
            foreach (var p in _host.Pools) _content.Add(Card(p));
        }

        VisualElement Card(PoolMenuEntry p)
        {
            var card = new VisualElement(); card.style.flexShrink = 0;
            card.style.backgroundColor = UiKit.PanelLight; UiKit.Borders(card, 1, UiKit.Border); UiKit.Radius(card, 6); UiKit.Pad(card, 10, 14);
            card.style.marginTop = 8;

            var name = new Label(SignStyle.Parse(p.Style).RenderForUi(p.Name));
            name.enableRichText = true; name.style.fontSize = UiKit.SectionSize + 4; name.style.flexShrink = 0; name.style.whiteSpace = WhiteSpace.Normal;
            card.Add(name);
            card.Add(Info(_host.T("dtp_ui_pool_pos", p.Pos.x.ToString("0"), p.Pos.z.ToString("0"), p.Pos.y.ToString("0"))));
            card.Add(Info(p.PortalDistance < 0f ? _host.T("dtp_ui_pool_noportal")
                : _host.T("dtp_ui_pool_portal", p.Portal.Length > 0 ? p.Portal : _host.T("dtp_ui_pool_portal_unnamed"), p.PortalDistance.ToString("0"))));

            var opts = new List<string> { "" }; opts.AddRange(_host.Styles);
            string def = _host.T("dtp_ui_style_default", StyleName(_host.DefaultStyle));
            string id = p.Id;
            card.Add(UiControls.Row(_host.T("dtp_ui_my_style"), UiControls.Dropdown(opts, p.MyStyle, s => s.Length == 0 ? def : StyleName(s), s => _host.SetPoolStyle(id, s))));
            return card;
        }

        static Label Info(string text)
        {
            var l = UiKit.Label(text, UiKit.SmallSize, UiKit.TextDim);
            l.style.flexShrink = 0; l.style.marginTop = 2;
            return l;
        }
    }
}
