using System;
using System.Globalization;
using System.IO;
using BepInEx.Configuration;

namespace DrainTarPit.Tests.Unit
{
    /// <summary>
    /// A decimal COMMA typed in the .cfg (the habit in most of Europe) is read by BepInEx as a thousands separator (3,5 -> 35). ModSettingsCommaFix reads the file
    /// before the first Bind and puts the meant value (3.5) back after binding. Real BepInEx ConfigFile on a temp file, the mod's code through the built DLL.
    /// </summary>
    [TestFixture]
    public class CommaFixTests
    {
        const string Type = "DrainTarPit.ModSettingsCommaFix";
        string _dir;

        [SetUp] public void MakeDir() { _dir = Path.Combine(Path.GetTempPath(), "dtp-commafix-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(_dir); }
        [TearDown] public void DropDir() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

        ConfigFile Config(string text)
        {
            var path = Path.Combine(_dir, "t.cfg");
            File.WriteAllText(path, text);
            return new ConfigFile(path, false);
        }

        static string[] Capture(ConfigFile c) { return (string[])ModAssembly.Call(Type, "Capture", c); }
        static void Apply(ConfigFile c, string[] lines) { ModAssembly.Call(Type, "Apply", c, lines); }

        [Test]
        public void FloatWithComma_GetsTheMeantValue_AfterBinding()
        {
            var c = Config("[Drain]\n\nRadius = 3,5\n");
            var lines = Capture(c);
            var radius = c.Bind("Drain", "Radius", 1f);

            Apply(c, lines);

            Assert.That(radius.Value, Is.EqualTo(3.5f));
        }

        [Test]
        public void DoubleWithComma_GetsTheMeantValue_AndNegativeNumbersWork()
        {
            var c = Config("[A]\nRate = -0,25\n");
            var lines = Capture(c);
            var rate = c.Bind("A", "Rate", 1.0);

            Apply(c, lines);

            Assert.That(rate.Value, Is.EqualTo(-0.25));
        }

        [Test]
        public void IntegerEntryWithComma_IsLeftAlone()
        {
            var c = Config("[A]\nCount = 3,5\n");
            var lines = Capture(c);
            var count = c.Bind("A", "Count", 7);
            var before = count.Value;

            Apply(c, lines);

            Assert.That(count.Value, Is.EqualTo(before));
        }

        [Test]
        public void TextEntryWithComma_IsLeftAlone()
        {
            var c = Config("[A]\nName = 3,5\n");
            var lines = Capture(c);
            var name = c.Bind("A", "Name", "x");

            Apply(c, lines);

            Assert.That(name.Value, Is.EqualTo("3,5"));
        }

        [Test]
        public void NormalDotNumber_IsLeftAlone()
        {
            var c = Config("[A]\nRadius = 4.25\n");
            var lines = Capture(c);
            var radius = c.Bind("A", "Radius", 1f);

            Apply(c, lines);

            Assert.That(radius.Value, Is.EqualTo(4.25f));
        }

        [Test]
        public void SameKeyInAnotherSection_IsNotTouched()
        {
            var c = Config("[A]\nRadius = 3,5\n[B]\nRadius = 2\n");
            var lines = Capture(c);
            var a = c.Bind("A", "Radius", 1f);
            var b = c.Bind("B", "Radius", 1f);

            Apply(c, lines);

            Assert.That(new[] { a.Value, b.Value }, Is.EqualTo(new[] { 3.5f, 2f }));
        }

        [Test]
        public void KeyThatIsNotBound_IsIgnored_NoException()
        {
            var c = Config("[A]\nGhost = 3,5\n");
            var lines = Capture(c);

            Assert.DoesNotThrow(() => Apply(c, lines));
        }

        [Test]
        public void NoFile_CaptureGivesNull_ApplyDoesNothing()
        {
            var c = new ConfigFile(Path.Combine(_dir, "missing.cfg"), false);
            File.Delete(Path.Combine(_dir, "missing.cfg"));

            Assert.That(Capture(c), Is.Null);
            Assert.DoesNotThrow(() => Apply(c, null));
            Assert.DoesNotThrow(() => Apply(null, new[] { "x" }));
            Assert.That(Capture(null), Is.Null);
        }

        [Test]
        public void ClampedByTheEntryRange_AsUsual()
        {
            var c = Config("[A]\nRadius = 99,5\n");
            var lines = Capture(c);
            var radius = c.Bind("A", "Radius", 1f, new ConfigDescription("", new AcceptableValueRange<float>(0f, 8f)));

            Apply(c, lines);

            Assert.That(radius.Value, Is.EqualTo(8f));
        }

        [Test]
        public void CommentsAndBlankLines_AreSkipped()
        {
            var c = Config("# Radius = 3,5\n[A]\n\n## text\nRadius = 3,5\n");
            var lines = Capture(c);
            var radius = c.Bind("A", "Radius", 1f);

            Apply(c, lines);

            Assert.That(radius.Value.ToString(CultureInfo.InvariantCulture), Is.EqualTo("3.5"));
        }

        [Test]
        public void Capture_FileCannotBeRead_ReturnsNull_NoException()
        {
            var c = Config("[A]\nRadius = 3,5\n");

            using (new FileStream(c.ConfigFilePath, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                Assert.That(Capture(c), Is.Null, "a config file another program holds open must not break the start of the mod");
            }
        }

        [Test]
        public void Capture_ExistingFile_ReturnsItsLines()
        {
            var c = Config("[A]\nRadius = 3,5\n");

            Assert.That(Capture(c), Does.Contain("Radius = 3,5"));
        }
    }
}
