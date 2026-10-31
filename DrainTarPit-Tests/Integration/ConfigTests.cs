using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx.Configuration;
using UnityEngine;

namespace DrainTarPit.Tests.Integration
{
    /// <summary>
    /// The mod's real config binding (DrainTarPit.ModSettings.Bind) run against real BepInEx ConfigFile objects that read and write real .cfg
    /// files in a temporary folder (never the player's config, never a save file). No game running: ModSettings only needs BepInEx and the
    /// managed Unity assembly (KeyCode, Mathf). Reached by reflection from the built DLL.
    /// </summary>
    [TestFixture]
    public class ConfigTests
    {
        const string Settings = "DrainTarPit.ModSettings";
        string _dir, _path;

        [SetUp]
        public void SetUp()
        {
            // inside the tests folder, not %TEMP%: PROJECT_RULES rule 4 (nothing outside E:\Projects\Valheim). Git-ignored.
            _dir = Path.Combine(TestPaths.TestsRoot, ".tmp", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _path = Path.Combine(_dir, "test.cfg");
        }

        [TearDown]
        public void TearDown()
        {
            try { Directory.Delete(_dir, true); } catch (IOException) { }
        }

        static string Cfg(params string[] lines) { return string.Join(Environment.NewLine, lines) + Environment.NewLine; }

        ConfigFile BindFile(string existingFileText = null, string path = null)
        {
            path = path ?? _path;
            if (existingFileText != null) File.WriteAllText(path, existingFileText);
            var cfg = new ConfigFile(path, true);
            ModAssembly.Call(Settings, "Bind", cfg);
            return cfg;
        }

        static ConfigEntryBase Entry(string field) { return (ConfigEntryBase)ModAssembly.GetStatic(Settings, field); }
        static object Value(string field) { return Entry(field).BoxedValue; }

        // ---------------------------------------------------------------- fresh file / defaults

        [Test]
        public void Bind_NoFileYet_WritesTheFileWithEverySection()
        {
            var cfg = BindFile();
            cfg.Save();

            var text = File.ReadAllText(_path);
            Assert.That(File.Exists(_path), Is.True);
            foreach (var section in new[] { "[General]", "[DebugLog]", "[Effects]", "[Building]", "[Drain]", "[World Hose Visuals]", "[Build Time Hose Visuals]" })
                Assert.That(text, Does.Contain(section));
        }

        [Test]
        public void Bind_NoFileYet_UsesTheDocumentedDefaults()
        {
            BindFile();

            Assert.That(Value("VolumePerStroke"), Is.EqualTo(6f));
            Assert.That(Value("DrainRadius"), Is.EqualTo(4f));
            Assert.That(Value("LiquidFlowSpeed"), Is.EqualTo(3.5f));
            Assert.That(Value("MaxChainPieces"), Is.EqualTo(40));
            Assert.That(Value("RefillMinutes"), Is.EqualTo(60f));
            Assert.That((bool)Value("EnableAll"), Is.EqualTo(true));
            Assert.That((bool)Value("SoundEffects"), Is.EqualTo(true));
            Assert.That((bool)Value("TarSplashSound"), Is.EqualTo(true));
            Assert.That((bool)Value("AutoReach"), Is.EqualTo(true));
            Assert.That(Value("TiltKey"), Is.EqualTo(KeyCode.LeftAlt));
            Assert.That(Value("BuildHoseStyle").ToString(), Is.EqualTo("Solid"));
        }

        [Test]
        public void Bind_NoFileYet_EveryDefaultIsInsideItsAcceptableRange()
        {
            var cfg = BindFile();

            foreach (var def in cfg.Keys)
            {
                var entry = cfg[def];
                var accepted = entry.Description.AcceptableValues;
                if (accepted == null) continue;
                Assert.That(accepted.IsValid(entry.DefaultValue), Is.True, def.Section + "/" + def.Key + " default " + entry.DefaultValue);
            }
        }

        [Test]
        public void Bind_EmptyFile_UsesDefaults()
        {
            BindFile("");

            Assert.That(Value("VolumePerStroke"), Is.EqualTo(6f));
        }

        [Test]
        public void Bind_CalledTwiceOnTheSameFile_DoesNotThrowAndKeepsValues()
        {
            var cfg = BindFile();

            Assert.DoesNotThrow(() => ModAssembly.Call(Settings, "Bind", cfg));
            Assert.That(Value("VolumePerStroke"), Is.EqualTo(6f));
        }

        // ---------------------------------------------------------------- T-068 config review (user, 2026-10-21)

        [Test]
        public void Bind_NoFileYet_HoseVisualDefaults()
        {
            BindFile();

            Assert.That(Value("WorldHoseStyle").ToString(), Is.EqualTo("Default"));
            Assert.That((float)Value("WorldHoseOpacity"), Is.EqualTo(0.45f).Within(1e-6f));
            Assert.That((float)Value("BuildHoseOpacity"), Is.EqualTo(0.45f).Within(1e-6f));
        }

        [Test]
        public void Bind_NoFileYet_FixedValuesAreNotInTheFile()
        {
            var cfg = BindFile();
            cfg.Save();
            var text = File.ReadAllText(_path);

            foreach (var key in new[] { "AlbedoBrightness", "ShaderName", "UseMaskMap", "PoolMeshFix", "PoolMeshReach", "SignBoardBrightness", "SignTextBrightness",
                                        "WaterStyle", "EditorKey", "PieceHealth", "DrySound", "PumpDroplets", "SoundTailSeconds", "SyncPour", "TarSlugLength",
                                        "WaterSplashOffset", "AllowWater", "PitFlowBoost", "PoolFilmDepth", "AttachBool", "AttachClipOverride", "StaminaPerStroke",
                                        "HandIK", "MaxAutoStep", "TestOutDir", "ConfigVersion", "TarFlowSpeed", "WaterCellSize",
                                        "DepositTar", "DepositWater", "PoolWalls", "PoolMenuKey", "WaterDepthLook", "LiquidCellSize", "WaterSplashSound" })
                Assert.That(text, Does.Not.Match(@"(?m)^" + key + @"\s*="), key + " must not be in the player's config");
        }

        [Test]
        public void Bind_FixedValues_HaveTheChosenValues()
        {
            BindFile();

            Assert.That(Fixed("AlbedoBrightness"), Is.EqualTo(1.5f));
            Assert.That(Fixed("PieceHealth"), Is.EqualTo(400f));
            Assert.That(Fixed("AllowWater"), Is.EqualTo(false), "water pumping comes with WaterAndTarPools");
            Assert.That(Fixed("SoundTailSeconds"), Is.EqualTo(0.3f));
            Assert.That(Fixed("TarSlugLength"), Is.EqualTo(2f));
            Assert.That(Fixed("WaterSplashOffset"), Is.EqualTo(-0.3f));
            Assert.That(Fixed("PumpDroplets"), Is.EqualTo(false));
            Assert.That(Fixed("SyncPour"), Is.EqualTo(true));
            Assert.That(Fixed("AttachBool"), Is.EqualTo("attach_chair"));
        }

        static object Fixed(string field) { var t = ModAssembly.GetStatic(Settings, field); return t.GetType().GetProperty("Value").GetValue(t, null); }

        [Test]
        public void Bind_OldEntriesInTheFile_AreDroppedFromTheSavedFile()
        {
            BindFile(Cfg("[General]", "ConfigVersion = 5", "DebugLog = true", "[Visuals]", "AlbedoBrightness = 2", "ClearHoses = false", "[Effects]", "Effects = true", "TarFlow = true"));

            var text = File.ReadAllText(_path);
            foreach (var key in new[] { "ConfigVersion", "AlbedoBrightness", "ClearHoses", "TarFlow", "Effects", "DebugLog" })
                Assert.That(text, Does.Not.Match(@"(?m)^" + key + @"\s*="), key);
        }

        [Test]
        public void Bind_VolumePerStroke_IsLimitedTo5To30()
        {
            BindFile(Cfg("[Drain]", "VolumePerStroke = 100"));

            Assert.That((float)Value("VolumePerStroke"), Is.EqualTo(30f));
        }

        [Test]
        public void Bind_RefillMinutes_IsLimitedTo10To120()
        {
            BindFile(Cfg("[Drain]", "RefillMinutes = 0"));

            Assert.That((float)Value("RefillMinutes"), Is.EqualTo(10f));
        }

        [Test]
        public void Bind_Descriptions_DoNotTalkAboutOlderVersions()
        {
            var cfg = BindFile();

            foreach (var def in cfg.Keys)
            {
                string d = cfg[def].Description.Description;
                foreach (var word in new[] { "old behaviour", "previous", "replaces the old", "before 20", "was the 0.", "(user", "2026" })
                    Assert.That(d, Does.Not.Contain(word), def.Section + "/" + def.Key);
            }
        }

        // ---------------------------------------------------------------- bad input

        [Test]
        public void Bind_MalformedNumber_FallsBackToTheDefault()
        {
            BindFile(Cfg("[Drain]", "VolumePerStroke = abc", "DrainRadius = 3"));

            Assert.That(Value("VolumePerStroke"), Is.EqualTo(6f));
            Assert.That(Value("DrainRadius"), Is.EqualTo(3f), "a bad neighbour must not spoil a good value");
        }

        [Test]
        public void Bind_UnknownKeyCode_FallsBackToTheDefault()
        {
            BindFile(Cfg("[Building]", "TiltKey = NotARealKey"));

            Assert.That(Value("TiltKey"), Is.EqualTo(KeyCode.LeftAlt));
        }

        [Test]
        public void Bind_FileWithGarbageLines_DoesNotThrowAndKeepsGoodValues()
        {
            Assert.DoesNotThrow(() => BindFile(Cfg("this is not a config line", "[Drain]", "= = =", "DrainRadius = 3", "[", "]")));

            Assert.That(Value("DrainRadius"), Is.EqualTo(3f));
        }

        [Test]
        public void Bind_NumberWithCommaDecimal_IsNotMisreadAsAnotherValue()
        {
            BindFile(Cfg("[Drain]", "DrainRadius = 3,5"));

            Assert.That((float)Value("DrainRadius"), Is.EqualTo(4f).Or.EqualTo(3.5f), "either the default or 3.5, never 35 (a locale bug would multiply the value)");
        }

        [Test]
        public void Bind_ValuesBeyondTheAcceptableRange_AreClampedIntoTheRange()
        {
            var probe = BindFile();
            var text = new StringBuilder();
            var expectedKeys = new List<ConfigDefinition>();
            foreach (var def in probe.Keys.OrderBy(d => d.Section))
            {
                var accepted = probe[def].Description.AcceptableValues;
                if (accepted == null) continue;
                var maxProp = accepted.GetType().GetProperty("MaxValue");
                if (maxProp == null) continue;
                var max = maxProp.GetValue(accepted, null);
                string beyond = null;
                if (max is float && (float)max < 1e8f) beyond = ((float)max + 1000f).ToString(CultureInfo.InvariantCulture);
                else if (max is int && (int)max < int.MaxValue - 2000) beyond = ((int)max + 1000).ToString(CultureInfo.InvariantCulture);
                if (beyond == null) continue;
                text.AppendLine("[" + def.Section + "]").AppendLine(def.Key + " = " + beyond);
                expectedKeys.Add(def);
            }
            Assume.That(expectedKeys, Is.Not.Empty, "no ranged numeric entries found");

            var second = BindFile(text.ToString(), Path.Combine(_dir, "range.cfg"));

            foreach (var def in expectedKeys)
            {
                var entry = second[def];
                Assert.That(entry.Description.AcceptableValues.IsValid(entry.BoxedValue), Is.True, def.Section + "/" + def.Key + " = " + entry.BoxedValue);
            }
        }

        // ---------------------------------------------------------------- write / read round trip

        [Test]
        public void SaveAndReload_ChangedValue_SurvivesTheRoundTrip()
        {
            var cfg = BindFile();
            ((ConfigEntry<float>)Entry("VolumePerStroke")).Value = 7f;
            cfg.Save();

            BindFile();

            Assert.That(Value("VolumePerStroke"), Is.EqualTo(7f));
        }

        [Test]
        public void SaveAndReload_NothingChanged_KeepsTheFileStable()
        {
            var cfg = BindFile();
            cfg.Save();
            var first = File.ReadAllText(_path);

            var again = BindFile();
            again.Save();

            Assert.That(File.ReadAllText(_path), Is.EqualTo(first), "binding an already-current file must not rewrite it differently");
        }
    }
}
