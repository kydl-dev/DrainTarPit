using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace DrainTarPit.Tests.Integration
{
    /// <summary>
    /// The player-facing English text table of DrainTarPit (DrainTarPit.DtpText.En; English only since 2.0.0) and the shared JSON writer of Kydl.Common.Loc.
    /// The table is plain data, so everything that can go wrong with it (a blank text, a mangled character, a quote that breaks the generated JSON)
    /// is checkable without the game. Internal types are read by reflection from the built DLL.
    /// </summary>
    [TestFixture]
    public class LocTableTests
    {
        const string TextType = "DrainTarPit.DtpText";
        const string LocType = "Kydl.Common.Loc";
        static readonly Regex Mojibake = new Regex("[\u00C2\u00C3][\u0080-\u00BF]");

        static Dictionary<string, string> English { get { return (Dictionary<string, string>)ModAssembly.GetStatic(TextType, "En"); } }

        public static IEnumerable<string> EnglishKeys() { return English.Keys.ToList(); }

        [Test]
        public void Table_English_HoldsEveryPlayerFacingString()
        {
            Assert.That(English.Count, Is.GreaterThan(40), "the English table should hold every player-facing string of the pump and the hoses");
        }

        [TestCaseSource(nameof(EnglishKeys))]
        public void Key_AnyEntry_StartsWithDtpPrefix(string key)
        {
            Assert.That(key, Does.StartWith("dtp_"), "all tokens of this mod share one prefix so they cannot clash with other mods");
        }

        [TestCaseSource(nameof(EnglishKeys))]
        public void Text_AnyEntry_IsNotBlank(string key)
        {
            Assert.That(English[key], Is.Not.Null.And.Not.Empty.And.Not.Matches(@"^\s+$"));
        }

        [Test]
        public void Text_AllEntries_ContainNoUnicodeReplacementCharacter()
        {
            var bad = English.Where(kv => kv.Value.IndexOf('\uFFFD') >= 0).Select(kv => kv.Key).ToList();

            Assert.That(bad, Is.Empty, "U+FFFD means the source file was saved in a different encoding than it is compiled with");
        }

        [Test]
        public void Text_AllEntries_ContainNoMojibakeSequences()
        {
            var bad = English.Where(kv => Mojibake.IsMatch(kv.Value)).Select(kv => kv.Key).ToList();

            Assert.That(bad, Is.Empty, "UTF-8 text that was decoded as Windows-1252 shows up as sequences like 'A�A�'");
        }

        [Test]
        public void ToJson_FullTable_ProducesParsableJsonWithTheSameContent()
        {
            var json = (string)ModAssembly.Call(LocType, "ToJson", English);

            var parsed = (Dictionary<string, object>)new JavaScriptSerializer().DeserializeObject(json);

            Assert.That(parsed.Count, Is.EqualTo(English.Count));
            foreach (var kv in English) Assert.That(parsed[kv.Key], Is.EqualTo(kv.Value), kv.Key);
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

        // Loc.T (the lookup with the game's Localization as the first choice and the English table as fallback) is NOT tested here: it calls the
        // game's Localization class, which needs .NET Standard 2.1 types (ReadOnlySpan) that a .NET Framework test host does not have (checked: TypeLoadException).
        // That path is covered by the TestLab suite (startup-clean) and the in-game hover texts.

        [Test]
        public void Token_AnyKey_AddsTheDollarPrefix()
        {
            Assert.That(ModAssembly.Call(LocType, "Token", "dtp_pump"), Is.EqualTo("$dtp_pump"));
        }
    }
}
