using System.Collections.Generic;
using DrainTarPit.Signs;
using DrainTarPit.Ui;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// T-56R (user: "generate the UI in Unity ... then u can preview it"): opens the mod's menus in Unity, built by the very code the mod
/// compiles (Assets/DTP_UI/Runtime). Press Play in Assets/DTP_UI/Preview/DTP_UiPreview.unity: the sign menu opens and is fully usable;
/// changes are written to the Console and shown on the preview sign line at the top. The game's UI sprites and fonts are copied from the
/// AssetRipper project into GameAssets (preview only: nothing here goes into the asset bundle or the mod).
/// </summary>
public class DTP_UiPreview : MonoBehaviour, ISignMenuHost
{
    public Texture2D atlas1, atlas2;            // UIAtlas1.png (woodpanel_settings), UIAtlas2.png (buttons, check boxes, fields)
    public TextAsset spriteTable;               // sprites.txt: name;atlas;x;y;w;h;borderL;borderB;borderR;borderT (from the AssetRipper .asset files)
    public Font[] fonts;                        // AveriaSerifLibre-Bold, Norsebold, Norse, NotoEmoji-Regular: referenced so they are loaded for UiKit
    public string signName = "Fishpond \U0001F41F";

    SignMenuView _view;
    UnityEngine.UIElements.Label _sign;
    readonly List<Sprite> _made = new List<Sprite>();

    void OnEnable()
    {
        if (!Application.isPlaying) return;
        MakeSprites();
        UiKit.Reset();
        var root = UiKit.Root;
        if (root == null) { Debug.LogError("[DTP UI] UiKit could not start"); return; }
        _sign = new UnityEngine.UIElements.Label();
        _sign.style.position = UnityEngine.UIElements.Position.Absolute; _sign.style.left = 20; _sign.style.top = 10; _sign.style.fontSize = 34;
        _sign.enableRichText = true;
        root.Add(_sign);
        Open();
    }

    void OnDisable()
    {
        UiKit.Reset();
        foreach (var s in _made) if (s != null) Destroy(s);
        _made.Clear();
    }

    void Update()
    {
        if (_view == null && Input.GetKeyDown(KeyCode.E)) Open();       // E opens it again after Save / Cancel
    }

    void Open()
    {
        _view = new SignMenuView(this, signName, new SignStyle());
        UiKit.Root.Add(_view.Window);
        Preview(signName, new SignStyle());
    }

    void MakeSprites()
    {
        if (spriteTable == null) return;
        foreach (var line in spriteTable.text.Split('\n'))
        {
            var p = line.Trim().Split(';');
            if (p.Length < 10) continue;
            var tex = p[1] == "1" ? atlas1 : atlas2;
            if (tex == null) continue;
            float F(int i) { return float.Parse(p[i], System.Globalization.CultureInfo.InvariantCulture); }
            var s = Sprite.Create(tex, new Rect(F(2), F(3), F(4), F(5)), new Vector2(0.5f, 0.5f), 50f, 0, SpriteMeshType.FullRect, new Vector4(F(6), F(7), F(8), F(9)));
            s.name = p[0];
            _made.Add(s);
        }
        Debug.Log("[DTP UI] preview: " + _made.Count + " game sprite(s) made, " + (fonts != null ? fonts.Length : 0) + " font(s) referenced");
    }

    // --- screenshots (called from the editor, e.g. by the agent through the Unity MCP) ------------------------------------------
    /// Renders the panel into a 1920x1080 texture from now on (needs one frame before Shot). Returns the target size.
    public static string RenderToTexture()
    {
        var ps = UiKit.Settings;
        if (ps == null) return "panel not started (enter Play mode)";
        if (ps.targetTexture == null) { ps.targetTexture = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32); ps.clearColor = true; ps.colorClearValue = new Color(0.22f, 0.27f, 0.20f, 1f); }
        return ps.targetTexture.width + "x" + ps.targetTexture.height;
    }

    /// Writes the last rendered frame to `path` (PNG).
    public static string Shot(string path)
    {
        var ps = UiKit.Settings;
        if (ps == null || ps.targetTexture == null) return "call RenderToTexture first, then wait a frame";
        var rt = ps.targetTexture;
        RenderTexture.active = rt;
        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply();
        RenderTexture.active = null;
        System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
        DestroyImmediate(tex);
        return "saved " + path;
    }

    /// Scrolls the n-th scroll area (0 = the window body) to `y`.
    public static string ScrollTo(int n, float y)
    {
        var all = UiKit.Root.Query<UnityEngine.UIElements.VisualElement>("dtp-scroll").ToList();
        if (n >= all.Count) return "only " + all.Count + " scroll area(s)";
        ((UiScroll)all[n].userData).ScrollTo(y);
        return "scrolled";
    }

    /// Opens the n-th drop-down (0 = font, 1 = alignment) as if clicked.
    public static string OpenDropdown(int n)
    {
        var list = UiKit.Root.Query<UnityEngine.UIElements.Button>().Where(b => b.text != null && b.text.EndsWith("▾")).ToList();
        if (n >= list.Count) return "only " + list.Count + " drop-down(s)";
        using (var e = UnityEngine.UIElements.NavigationSubmitEvent.GetPooled()) { e.target = list[n]; list[n].SendEvent(e); }
        return "opened '" + list[n].text + "'";
    }

    // --- ISignMenuHost ------------------------------------------------------------------------------------------------------
    public string T(string key, params string[] args) { return UiStrings.English(key, args); }
    public string PoolLabel { get { return "DTP_WaterPool2 (preview)"; } }
    public IList<string> Fonts
    {
        get
        {
            var l = new List<string>();
            foreach (var f in Resources.FindObjectsOfTypeAll<Font>()) if (f != null && !l.Contains(f.name)) l.Add(f.name);
            for (int i = 0; i < 30; i++) l.Add("Preview font " + i);            // the game lists ~40 fonts: test the long list
            l.Sort();
            return l;
        }
    }
    public int MaxLetters { get { return 40; } }

    public void Preview(string name, SignStyle style)
    {
        if (_sign != null) _sign.text = "Sign: " + StripTmpOnly(style.Render(name));
    }

    // UI Toolkit rich text has no <align>, <cspace>, <uppercase>: shown without them on the preview line
    static string StripTmpOnly(string s)
    {
        return System.Text.RegularExpressions.Regex.Replace(s, "</?(align|cspace|uppercase|noparse|s)[^>]*>", "");
    }

    public void Save(string name, SignStyle style) { Debug.Log("[DTP UI] preview: Save '" + name + "' " + style); signName = name; Close("saved"); }

    public void Close(string why)
    {
        if (_view != null) _view.Window.RemoveFromHierarchy();
        _view = null;
        Debug.Log("[DTP UI] preview: closed (" + why + ") - press E to open it again");
    }

    string _pref;
    public int SavePreferred(SignStyle style) { _pref = style.Serialize(); return _pref.Length; }
    public SignStyle LoadPreferred() { return _pref == null ? null : SignStyle.Parse(_pref); }
}
