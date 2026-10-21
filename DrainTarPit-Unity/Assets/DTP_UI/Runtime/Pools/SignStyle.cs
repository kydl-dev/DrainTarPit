using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace WaterAndTarPools.Signs
{
    /// <summary>
    /// T-023 (user, 2026-10-20): how the name on a pool sign looks. The same for everyone (stored in the world with the sign, user answer).
    /// Pure: no Unity / game types, so it is unit tested and compiled into the tests as well (DrainTarPit.UnitTests).
    /// Letters are counted as text elements (an emoji made of two UTF-16 chars is ONE letter), so a per-letter colour never splits one.
    /// </summary>
    public sealed class SignStyle
    {
        public const int Version = 1;
        public const float MinSize = 0.5f, MaxSize = 12f;

        public string Font = "";                 // "" = the sign's default font; else the name of a font loaded in the game
        public float Size;                       // 0 = auto size (fits the board, like the game's sign); else a fixed size
        public bool Bold, Italic, Underline, Strike, Upper;
        public char Align = 'c';                 // l / c / r
        public string Color = "#FFFFFF";         // base colour, #RRGGBB
        public float Spacing;                    // letter spacing (em / 100), -20 .. 50
        public readonly SortedDictionary<int, string> Letters = new();   // letter index -> #RRGGBB (letters not listed use Color)

        public SignStyle Clone()
        {
            var copy = new SignStyle { Font = Font, Size = Size, Bold = Bold, Italic = Italic, Underline = Underline, Strike = Strike, Upper = Upper, Align = Align, Color = Color, Spacing = Spacing };
            foreach (var kv in Letters) copy.Letters[kv.Key] = kv.Value;
            return copy;
        }

        // --- text form (stored in the sign's ZDO and in the preferred-style file) ---------------------------------------
        /// "v1|font=..|size=..|b=0|i=0|u=0|s=0|up=0|al=c|col=#FFFFFF|sp=0|lc=0:#FF0000,3:#00FF00" (values URL-escaped).
        public string Serialize()
        {
            var sb = new StringBuilder("v" + Version);
            Add(sb, "font", Font);
            Add(sb, "size", Size.ToString("0.###", CultureInfo.InvariantCulture));
            Add(sb, "b", Bold ? "1" : "0"); Add(sb, "i", Italic ? "1" : "0"); Add(sb, "u", Underline ? "1" : "0");
            Add(sb, "s", Strike ? "1" : "0"); Add(sb, "up", Upper ? "1" : "0");
            Add(sb, "al", Align.ToString());
            Add(sb, "col", Color);
            Add(sb, "sp", Spacing.ToString("0.###", CultureInfo.InvariantCulture));
            var lc = new StringBuilder();
            foreach (var kv in Letters) { if (lc.Length > 0) lc.Append(','); lc.Append(kv.Key).Append(':').Append(kv.Value); }
            Add(sb, "lc", lc.ToString());
            return sb.ToString();
        }

        static void Add(StringBuilder sb, string key, string value) { sb.Append('|').Append(key).Append('=').Append(Uri.EscapeDataString(value ?? "")); }

        /// Reads the text form. Unknown keys are skipped, bad values keep the default; `problems` lists what was skipped (for the debug log).
        public static SignStyle Parse(string text, List<string> problems = null)
        {
            var s = new SignStyle();
            if (string.IsNullOrEmpty(text)) return s;
            var parts = text.Split('|');
            if (!parts[0].StartsWith("v")) { problems?.Add("no version tag"); return s; }
            for (int i = 1; i < parts.Length; i++)
            {
                int eq = parts[i].IndexOf('=');
                if (eq <= 0) { problems?.Add("bad part '" + parts[i] + "'"); continue; }
                string key = parts[i].Substring(0, eq), val;
                try { val = Uri.UnescapeDataString(parts[i].Substring(eq + 1)); }
                catch (UriFormatException) { problems?.Add("bad escape in '" + key + "'"); continue; }
                switch (key)
                {
                    case "font": s.Font = val; break;
                    case "size": s.Size = Num(val, 0f, problems, key); break;
                    case "b": s.Bold = val == "1"; break;
                    case "i": s.Italic = val == "1"; break;
                    case "u": s.Underline = val == "1"; break;
                    case "s": s.Strike = val == "1"; break;
                    case "up": s.Upper = val == "1"; break;
                    case "al": s.Align = val == "l" || val == "r" ? val[0] : 'c'; break;
                    case "col":
                        string hex = Hex(val);
                        if (hex != null) s.Color = hex; else problems?.Add("bad colour '" + val + "'");
                        break;
                    case "sp": s.Spacing = Num(val, 0f, problems, key); break;
                    case "lc": ReadLetters(s, val, problems); break;
                    default: problems?.Add("unknown key '" + key + "'"); break;
                }
            }
            s.Clamp();
            return s;
        }

        static void ReadLetters(SignStyle s, string val, List<string> problems)
        {
            if (val.Length == 0) return;
            foreach (var item in val.Split(','))
            {
                int c = item.IndexOf(':');
                int idx;
                string hex = c > 0 ? Hex(item.Substring(c + 1)) : null;
                if (c <= 0 || !int.TryParse(item.Substring(0, c), NumberStyles.Integer, CultureInfo.InvariantCulture, out idx) || idx < 0 || hex == null)
                { problems?.Add("bad letter colour '" + item + "'"); continue; }
                s.Letters[idx] = hex;
            }
        }

        static float Num(string v, float fallback, List<string> problems, string key)
        {
            float f;
            if (float.TryParse(v.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out f)) return f;
            problems?.Add("bad number for '" + key + "': '" + v + "'");
            return fallback;
        }

        /// "#RRGGBB" (upper case) from "#rgb", "rgb", "#rrggbb" or "rrggbb"; null when it is not a colour.
        public static string Hex(string v)
        {
            if (v == null) return null;
            v = v.Trim().TrimStart('#');
            if (v.Length == 3) v = new string(new[] { v[0], v[0], v[1], v[1], v[2], v[2] });
            if (v.Length != 6) return null;
            foreach (char ch in v) if (!Uri.IsHexDigit(ch)) return null;
            return "#" + v.ToUpperInvariant();
        }

        /// Keeps the numbers in range (a hand-edited or old value must not make the sign unreadable).
        public void Clamp()
        {
            if (Size != 0f) Size = Math.Max(MinSize, Math.Min(MaxSize, Size));
            Spacing = Math.Max(-20f, Math.Min(50f, Spacing));
            if (Align != 'l' && Align != 'r') Align = 'c';
            Color = Hex(Color) ?? "#FFFFFF";
            if (Font == null) Font = "";
        }

        // --- letters ------------------------------------------------------------------------------------------------------
        /// The letters of a name as text elements (an emoji or a letter with an accent mark stays one letter).
        public static List<string> SplitLetters(string name)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(name)) return list;
            var e = StringInfo.GetTextElementEnumerator(name);
            while (e.MoveNext()) list.Add((string)e.Current);
            return list;
        }

        /// Colours from `from` to `to` over the letters first..last (inclusive). Returns the number of letters coloured.
        public int Gradient(int first, int last, string from, string to)
        {
            string a = Hex(from), b = Hex(to);
            if (a == null || b == null || first < 0 || last < first) return 0;
            for (int i = first; i <= last; i++)
            {
                float t = last == first ? 0f : (i - first) / (float)(last - first);
                Letters[i] = Mix(a, b, t);
            }
            return last - first + 1;
        }

        static int Channel(string hex, int k) { return Convert.ToInt32(hex.Substring(1 + k * 2, 2), 16); }

        static string Mix(string a, string b, float t)
        {
            var sb = new StringBuilder("#");
            for (int k = 0; k < 3; k++) sb.Append(((int)Math.Round(Channel(a, k) + (Channel(b, k) - Channel(a, k)) * t)).ToString("X2"));
            return sb.ToString();
        }

        // --- rich text for the sign's TextMeshPro text (font and size are set on the text object itself) --------------------
        /// Emoji (user: "force emoji display font to be their normal font"): pictographs above the basic plane, a letter with the emoji
        /// selector U+FE0F, or a symbol from the arrows / dingbats blocks on.
        public static bool IsEmoji(string letter)
        {
            int cp = string.IsNullOrEmpty(letter) ? 0 : char.IsSurrogatePair(letter, 0) ? char.ConvertToUtf32(letter, 0) : letter[0];
            return cp > 0 && (cp >= 0x1F000 || letter.IndexOf('\uFE0F') >= 0 ||
                              (cp >= 0x2190 && CharUnicodeInfo.GetUnicodeCategory(letter, 0) == UnicodeCategory.OtherSymbol));
        }

        /// The name with this style as TextMeshPro rich text. '<' and '>' typed by a player are shown as letters, never read as tags.
        /// `emojiFont` (a TextMeshPro font name the text can reach) is put around every emoji, so emoji keep the emoji font whatever Font is.
        public string Render(string name) { return Render(name, null); }

        public string Render(string name, string emojiFont)
        {
            var sb = new StringBuilder();
            sb.Append("<align=").Append(Align == 'l' ? "left" : Align == 'r' ? "right" : "center").Append('>');
            if (Spacing != 0f) sb.Append("<cspace=").Append((Spacing / 100f).ToString("0.###", CultureInfo.InvariantCulture)).Append("em>");
            if (Bold) sb.Append("<b>");
            if (Italic) sb.Append("<i>");
            if (Underline) sb.Append("<u>");
            if (Strike) sb.Append("<s>");
            if (Upper) sb.Append("<uppercase>");
            sb.Append("<color=").Append(Color).Append('>');
            var letters = SplitLetters(name);
            for (int i = 0; i < letters.Count; i++)
            {
                string letter = Safe(letters[i]), hex;
                if (!string.IsNullOrEmpty(emojiFont) && IsEmoji(letters[i])) letter = "<font=\"" + emojiFont + "\">" + letter + "</font>";
                if (Letters.TryGetValue(i, out hex) && hex != Color) sb.Append("<color=").Append(hex).Append('>').Append(letter).Append("</color>");
                else sb.Append(letter);
            }
            sb.Append("</color>");
            return sb.ToString();
        }

        /// The name for a UI Toolkit label (the pool menu): the same colours, bold, italic, underline, strike and upper case; alignment, letter
        /// spacing and fonts are left out (UI Toolkit's rich text does not have them). '<' / '>' typed by a player become look-alike brackets.
        public string RenderForUi(string name)
        {
            var sb = new StringBuilder();
            if (Bold) sb.Append("<b>");
            if (Italic) sb.Append("<i>");
            if (Underline) sb.Append("<u>");
            if (Strike) sb.Append("<s>");
            sb.Append("<color=").Append(Color).Append('>');
            var letters = SplitLetters(Upper ? (name ?? "").ToUpperInvariant() : name);
            for (int i = 0; i < letters.Count; i++)
            {
                string letter = letters[i] == "<" ? "‹" : letters[i] == ">" ? "›" : letters[i], hex;
                if (Letters.TryGetValue(i, out hex) && hex != Color) sb.Append("<color=").Append(hex).Append('>').Append(letter).Append("</color>");
                else sb.Append(letter);
            }
            sb.Append("</color>");
            if (Strike) sb.Append("</s>");
            if (Underline) sb.Append("</u>");
            if (Italic) sb.Append("</i>");
            if (Bold) sb.Append("</b>");
            return sb.ToString();
        }

        static string Safe(string letter)
        {
            if (letter == "<") return "<noparse><</noparse>";
            if (letter == ">") return "<noparse>></noparse>";
            return letter;
        }

        /// Short form for the debug log.
        public override string ToString() { return Serialize(); }
    }
}
