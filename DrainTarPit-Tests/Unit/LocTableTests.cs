using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace DrainTarPit.Tests.Unit
{
    /// <summary>
    /// The player-facing text tables (English + German) in DrainTarPit.TextLocal.Loc. They are plain data, so everything that can go
    /// wrong with them (a key in only one language, a lost {0}, a mangled umlaut, a quote that breaks the generated JSON) is checkable
    /// without the game. The tables are private, so they are read by reflection from the built DLL.
    /// </summary>
    [TestFixture]
    public class LocTableTests
    {
        const string LocType = "DrainTarPit.TextLocal.Loc";
        static readonly Regex Placeholder = new Regex(@"\{\d+\}");
        static readonly Regex Mojibake = new Regex("[\u00C2\u00C3][\u0080-\u00BF]");

        static Dictionary<string, string> English { get { return (Dictionary<string, string>)ModAssembly.GetStatic(LocType, "En"); } }
        static Dictionary<string, string> German { get { return (Dictionary<string, string>)ModAssembly.GetStatic(LocType, "De"); } }

        public static IEnumerable<string> EnglishKeys() { return English.Keys.ToList(); }

        [Test]
        public void Tables_EnglishAndGerman_HaveTheSameKeys()
        {
            var missingInGerman = English.Keys.Except(German.Keys).ToList();
            var onlyInGerman = German.Keys.Except(English.Keys).ToList();

            Assert.That(missingInGerman, Is.Empty, "keys with no German text");
            Assert.That(onlyInGerman, Is.Empty, "keys with no English text");
        }

        [Test]
        public void Tables_English_IsNotEmpty()
        {
            Assert.That(English.Count, Is.GreaterThan(50), "the English table should hold every player-facing string");
        }

        [TestCaseSource(nameof(EnglishKeys))]
        public void Key_AnyEntry_StartsWithDtpPrefix(string key)
        {
            Assert.That(key, Does.StartWith("dtp_"), "all tokens of this mod share one prefix so they cannot clash with other mods");
        }

        [TestCaseSource(nameof(EnglishKeys))]
        public void Text_AnyEntry_IsNotBlankInEitherLanguage(string key)
        {
            Assert.That(English[key], Is.Not.Null.And.Not.Empty.And.Not.Matches(@"^\s+$"), "English");
            if (German.ContainsKey(key)) Assert.That(German[key], Is.Not.Null.And.Not.Empty.And.Not.Matches(@"^\s+$"), "German");
        }

        [TestCaseSource(nameof(EnglishKeys))]
        public void Placeholders_AnyEntry_AreTheSameInBothLanguages(string key)
        {
            if (!German.ContainsKey(key)) Assert.Ignore("covered by Tables_EnglishAndGerman_HaveTheSameKeys");

            var en = Placeholder.Matches(English[key]).Cast<Match>().Select(m => m.Value).OrderBy(x => x).ToList();
            var de = Placeholder.Matches(German[key]).Cast<Match>().Select(m => m.Value).OrderBy(x => x).ToList();

            Assert.That(de, Is.EqualTo(en), "a translation that loses or invents a {n} shows broken text in game");
        }

        [Test]
        public void Text_AllEntries_ContainNoUnicodeReplacementCharacter()
        {
            var bad = English.Concat(German).Where(kv => kv.Value.IndexOf('\uFFFD') >= 0).Select(kv => kv.Key).ToList();

            Assert.That(bad, Is.Empty, "U+FFFD means the source file was saved in a different encoding than it is compiled with");
        }

        [Test]
        public void Text_AllEntries_ContainNoMojibakeSequences()
        {
            var bad = English.Concat(German).Where(kv => Mojibake.IsMatch(kv.Value)).Select(kv => kv.Key).ToList();

            Assert.That(bad, Is.Empty, "UTF-8 text that was decoded as Windows-1252 shows up as sequences like 'Ã¼'");
        }

        [Test]
        public void German_TextWithUmlautsOrSharpS_KeepsThemIntact()
        {
            // sanity check that the umlaut tests above are looking at real data: at least one German string must contain a real umlaut or sharp s
            var withUmlaut = German.Values.Where(v => v.IndexOfAny(new[] { '\u00E4', '\u00F6', '\u00FC', '\u00DF', '\u00C4', '\u00D6', '\u00DC' }) >= 0).ToList();

            Assert.That(withUmlaut, Is.Not.Empty);
        }

        [Test]
        public void ToJson_FullTables_ProduceParsableJsonWithTheSameContent()
        {
            foreach (var table in new[] { English, German })
            {
                var json = (string)ModAssembly.Call(LocType, "ToJson", table);

                var parsed = (Dictionary<string, object>)new JavaScriptSerializer().DeserializeObject(json);

                Assert.That(parsed.Count, Is.EqualTo(table.Count));
                foreach (var kv in table) Assert.That(parsed[kv.Key], Is.EqualTo(kv.Value), kv.Key);
            }
        }

        [Test]
        public void ToJson_QuoteBackslashAndNewline_AreEscapedAndRoundTrip()
        {
            var input = new Dictionary<string, string>
            {
                { "quote", "he said \"hi\"" },
                { "backslash", "C:\\temp" },
                { "newline", "line1\nline2" },
                { "degree", "45\u00b0" },
            };

            var json = (string)ModAssembly.Call(LocType, "ToJson", input);
            var parsed = (Dictionary<string, object>)new JavaScriptSerializer().DeserializeObject(json);

            foreach (var kv in input) Assert.That(parsed[kv.Key], Is.EqualTo(kv.Value), kv.Key);
        }

        [Test]
        public void ToJson_EmptyTable_IsAnEmptyJsonObject()
        {
            var json = (string)ModAssembly.Call(LocType, "ToJson", new Dictionary<string, string>());

            Assert.That(json, Is.EqualTo("{}"));
        }

        [Test]
        public void T_UnknownKey_ReturnsTheKeyItself()
        {
            var text = (string)ModAssembly.Call(LocType, "T", "dtp_this_key_does_not_exist", new string[0]);

            Assert.That(text, Is.EqualTo("dtp_this_key_does_not_exist"));
        }

        [Test]
        public void T_KnownKeyWithArgument_FillsThePlaceholder()
        {
            var text = (string)ModAssembly.Call(LocType, "T", "dtp_strokes", new[] { "7" });

            Assert.That(text, Is.EqualTo("Strokes: 7"));
        }

        [Test]
        public void T_KnownKeyWithNullArguments_LeavesThePlaceholder()
        {
            var text = (string)ModAssembly.Call(LocType, "T", "dtp_strokes", (string[])null);

            Assert.That(text, Does.Contain("{0}"));
        }

        [Test]
        public void Token_AnyKey_AddsTheDollarPrefix()
        {
            Assert.That(ModAssembly.Call(LocType, "Token", "dtp_pump"), Is.EqualTo("$dtp_pump"));
        }
    }
}
