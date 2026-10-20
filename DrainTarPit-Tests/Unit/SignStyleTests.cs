using System.Collections.Generic;
using DrainTarPit.Signs;

namespace DrainTarPit.Tests.Unit
{
    /// <summary>T-023: the pool sign's text style (SignStyle): text form round trip, bad input, rich text, letters, gradient.</summary>
    [TestFixture]
    public class SignStyleTests
    {
        static SignStyle Full()
        {
            var s = new SignStyle { Font = "Norse|Bold=x", Size = 4.5f, Bold = true, Italic = true, Underline = true, Strike = true, Upper = true, Align = 'r', Color = "#112233", Spacing = 7.5f };
            s.Letters[0] = "#FF0000"; s.Letters[3] = "#00FF00";
            return s;
        }

        [Test]
        public void Serialize_Parse_RoundTrip_KeepsEveryValue()
        {
            var problems = new List<string>();
            var back = SignStyle.Parse(Full().Serialize(), problems);

            Assert.That(problems, Is.Empty);
            Assert.That(back.Serialize(), Is.EqualTo(Full().Serialize()));
            Assert.That(back.Font, Is.EqualTo("Norse|Bold=x"), "separators inside a value are escaped");
            Assert.That(back.Letters.Count, Is.EqualTo(2));
            Assert.That(back.Letters[3], Is.EqualTo("#00FF00"));
        }

        [Test]
        public void Parse_EmptyOrNull_GivesTheDefaultStyle()
        {
            Assert.That(SignStyle.Parse(null).Serialize(), Is.EqualTo(new SignStyle().Serialize()));
            Assert.That(SignStyle.Parse("").Serialize(), Is.EqualTo(new SignStyle().Serialize()));
        }

        [Test]
        public void Parse_BadValues_KeepDefaultsAndAreReported()
        {
            var problems = new List<string>();
            var s = SignStyle.Parse("v1|size=abc|col=#GGGGGG|lc=x:#FF0000,2:nope,4:#0F0|what=1|broken", problems);

            Assert.That(s.Size, Is.EqualTo(0f));
            Assert.That(s.Color, Is.EqualTo("#FFFFFF"));
            Assert.That(s.Letters.Count, Is.EqualTo(1));
            Assert.That(s.Letters[4], Is.EqualTo("#00FF00"), "#rgb short form is read");
            Assert.That(problems.Count, Is.EqualTo(6));
        }

        [Test]
        public void Parse_NoVersionTag_IsReportedAndIgnored()
        {
            var problems = new List<string>();
            var s = SignStyle.Parse("size=3", problems);
            Assert.That(s.Size, Is.EqualTo(0f));
            Assert.That(problems, Has.Count.EqualTo(1));
        }

        [Test]
        public void Parse_DecimalComma_IsRead()
        {
            Assert.That(SignStyle.Parse("v1|size=3,5").Size, Is.EqualTo(3.5f));
        }

        [Test]
        public void Clamp_KeepsSizeAndSpacingInRange_ZeroSizeStaysAuto()
        {
            Assert.That(SignStyle.Parse("v1|size=99|sp=-80").Size, Is.EqualTo(SignStyle.MaxSize));
            Assert.That(SignStyle.Parse("v1|size=99|sp=-80").Spacing, Is.EqualTo(-20f));
            Assert.That(SignStyle.Parse("v1|size=0").Size, Is.EqualTo(0f));
            Assert.That(SignStyle.Parse("v1|size=0.1").Size, Is.EqualTo(SignStyle.MinSize));
        }

        [Test]
        public void Hex_AcceptsShortAndLongForms_RejectsTheRest()
        {
            Assert.That(SignStyle.Hex("abc"), Is.EqualTo("#AABBCC"));
            Assert.That(SignStyle.Hex("#a1b2c3"), Is.EqualTo("#A1B2C3"));
            Assert.That(SignStyle.Hex("#12345"), Is.Null);
            Assert.That(SignStyle.Hex("zzzzzz"), Is.Null);
            Assert.That(SignStyle.Hex(null), Is.Null);
        }

        [Test]
        public void Render_PlainName_HasAlignAndBaseColourOnly()
        {
            Assert.That(new SignStyle().Render("Pool"), Is.EqualTo("<align=center><color=#FFFFFF>Pool</color>"));
        }

        [Test]
        public void Render_FullStyle_HasEveryTagAndLetterColours()
        {
            string r = Full().Render("Abcd");
            Assert.That(r, Does.StartWith("<align=right><cspace=0.075em><b><i><u><s><uppercase><color=#112233>"));
            Assert.That(r, Does.Contain("<color=#FF0000>A</color>bc<color=#00FF00>d</color>"));
        }

        [Test]
        public void Render_AngleBracketsTypedByAPlayer_AreNotTags()
        {
            string r = new SignStyle().Render("<b>");
            Assert.That(r, Does.Contain("<noparse><</noparse>b<noparse>></noparse>"));
        }

        [Test]
        public void SplitLetters_EmojiIsOneLetter()
        {
            var letters = SignStyle.SplitLetters("a\U0001F41Fb");          // a, fish emoji (two UTF-16 chars), b
            Assert.That(letters, Has.Count.EqualTo(3));
            Assert.That(letters[1], Is.EqualTo("\U0001F41F"));
        }

        [Test]
        public void Render_LetterColourOnAnEmoji_WrapsTheWholeEmoji()
        {
            var s = new SignStyle();
            s.Letters[1] = "#00FF00";
            Assert.That(s.Render("a\U0001F41Fb"), Does.Contain("<color=#00FF00>\U0001F41F</color>"));
        }

        [Test]
        public void Gradient_ColoursEveryLetterFromOneEndToTheOther()
        {
            var s = new SignStyle();
            int n = s.Gradient(0, 2, "#000000", "#FF0000");
            Assert.That(n, Is.EqualTo(3));
            Assert.That(s.Letters[0], Is.EqualTo("#000000"));
            Assert.That(s.Letters[1], Is.EqualTo("#800000"));
            Assert.That(s.Letters[2], Is.EqualTo("#FF0000"));
            Assert.That(s.Gradient(2, 1, "#000000", "#FF0000"), Is.EqualTo(0), "an empty range colours nothing");
        }

        [Test]
        public void Clone_IsIndependent()
        {
            var a = Full();
            var b = a.Clone();
            b.Letters[0] = "#FFFFFF"; b.Bold = false;
            Assert.That(a.Letters[0], Is.EqualTo("#FF0000"));
            Assert.That(a.Bold, Is.EqualTo(true));
            Assert.That(b.Serialize(), Is.Not.EqualTo(a.Serialize()));
        }
    }
}
