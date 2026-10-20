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
        const int LatestConfigVersion = 5; // ModSettings.Migrate: raise this together with a new migration step and add a test for the step
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
            foreach (var section in new[] { "[General]", "[Visuals]", "[Effects]", "[Building]", "[Drain]", "[Pump]" })
                Assert.That(text, Does.Contain(section));
        }

        [Test]
        public void Bind_NoFileYet_UsesTheDocumentedDefaults()
        {
            BindFile();

            Assert.That(Value("VolumePerStroke"), Is.EqualTo(6f));
            Assert.That(Value("DrainRadius"), Is.EqualTo(4f));
            Assert.That(Value("TarFlowSpeed"), Is.EqualTo(3.5f));
            Assert.That(Value("TarSlugLength"), Is.EqualTo(2f));
            Assert.That((float)Value("MaxBackBend"), Is.EqualTo(45f));
            Assert.That((float)Value("MaxKneeBend"), Is.EqualTo(0.08f).Within(1e-6f));
            Assert.That(Value("PieceHealth"), Is.EqualTo(200f));
            Assert.That(Value("MaxChainPieces"), Is.EqualTo(24));
            Assert.That(Value("TiltKey"), Is.EqualTo(KeyCode.LeftAlt));
            Assert.That(Value("BuildHoseStyle").ToString(), Is.EqualTo("Solid"));
        }

        [Test]
        public void Bind_NoFileYet_SetsTheConfigVersionToTheLatestMigration()
        {
            BindFile();

            Assert.That(Value("ConfigVersion"), Is.EqualTo(LatestConfigVersion));
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
            Assert.That(Value("ConfigVersion"), Is.EqualTo(LatestConfigVersion));
        }

        [Test]
        public void Bind_CalledTwiceOnTheSameFile_DoesNotThrowAndKeepsValues()
        {
            var cfg = BindFile();

            Assert.DoesNotThrow(() => ModAssembly.Call(Settings, "Bind", cfg));
            Assert.That(Value("VolumePerStroke"), Is.EqualTo(6f));
        }

        // ---------------------------------------------------------------- migration (ModSettings.Migrate)

        [Test]
        public void Migrate_FileFromBeforeVersion1WithOldDefaults_UpgradesThemToTheNewDefaults()
        {
            BindFile(Cfg("[Drain]", "VolumePerStroke = 1.5", "DrainRadius = 2.5", "[Effects]", "TarFlowSpeed = 2.5", "TarSlugLength = 1.2"));

            Assert.That(Value("VolumePerStroke"), Is.EqualTo(6f));
            Assert.That(Value("DrainRadius"), Is.EqualTo(4f));
            Assert.That(Value("TarFlowSpeed"), Is.EqualTo(3.5f));
            Assert.That(Value("TarSlugLength"), Is.EqualTo(2f));
        }

        [Test]
        public void Migrate_FileFromBeforeVersion1WithCustomValues_KeepsTheUsersChoice()
        {
            BindFile(Cfg("[Drain]", "VolumePerStroke = 2", "DrainRadius = 2.5"));

            Assert.That(Value("VolumePerStroke"), Is.EqualTo(2f), "not an old default, so it is the user's own value");
            Assert.That(Value("DrainRadius"), Is.EqualTo(4f), "this one was still the old default");
        }

        [Test]
        public void Migrate_FileFromBeforeVersion2WithOldPumpPosture_UpgradesIt()
        {
            BindFile(Cfg("[General]", "ConfigVersion = 1", "[Pump]", "MaxKneeBend = 0.25", "MaxBackBend = 35"));

            Assert.That((float)Value("MaxKneeBend"), Is.EqualTo(0.08f).Within(1e-6f));
            Assert.That((float)Value("MaxBackBend"), Is.EqualTo(45f));
        }

        [Test]
        public void Migrate_FileAlreadyAtVersion2_DoesNotTouchAnOldLookingValue()
        {
            BindFile(Cfg("[General]", "ConfigVersion = 2", "[Pump]", "MaxBackBend = 35"));

            Assert.That((float)Value("MaxBackBend"), Is.EqualTo(35f), "migrations run once per version");
        }

        [Test]
        public void Migrate_OrphanedClearHosesFalse_BecomesBuildHoseStyleSolid()
        {
            BindFile(Cfg("[General]", "ConfigVersion = 2", "[Visuals]", "ClearHoses = false", "HoseStyle = Transparent"));

            Assert.That(Value("BuildHoseStyle").ToString(), Is.EqualTo("Solid"));
        }

        [Test]
        public void Migrate_AlreadyAtVersion3_IgnoresAClearHosesLeftover()
        {
            BindFile(Cfg("[General]", "ConfigVersion = 3", "[Visuals]", "ClearHoses = false", "HoseStyle = Transparent"));

            Assert.That(Value("BuildHoseStyle").ToString(), Is.EqualTo("Transparent"));
        }

        [Test]
        public void Migrate_Version3HoseLook_MovesToBuildTimeHoseVisuals()
        {
            BindFile(Cfg("[General]", "ConfigVersion = 3", "[Visuals]", "HoseStyle = Transparent", "HoseOpacity = 0,3"));

            Assert.That(Value("BuildHoseStyle").ToString(), Is.EqualTo("Transparent"));
            Assert.That((float)Value("BuildHoseOpacity"), Is.EqualTo(0.3f).Within(1e-6f), "decimal comma is accepted too");
            Assert.That(Value("WorldHoseStyle").ToString(), Is.EqualTo("Default"), "the world style is new: it starts at Default (each hose its own look)");
            var text = File.ReadAllText(_path);
            Assert.That(text, Does.Contain("[Build Time Hose Visuals]"));
            Assert.That(text, Does.Contain("[World Hose Visuals]"));
            Assert.That(text, Does.Not.Match(@"(?m)^HoseStyle\s*="), "the old key is dropped from the saved file");
        }

        [Test]
        public void Migrate_AlreadyAtVersion4_IgnoresAnOldHoseStyleLeftover()
        {
            BindFile(Cfg("[General]", "ConfigVersion = 4", "[Visuals]", "HoseStyle = Transparent"));

            Assert.That(Value("BuildHoseStyle").ToString(), Is.EqualTo("Solid"));
        }

        [Test]
        public void Migrate_FromVersion4_TurnsPoolRimDisappearOnOnce()
        {
            BindFile(Cfg("[General]", "ConfigVersion = 4", "[Drain]", "PoolRimDisappear = false"));

            Assert.That((bool)Value("PoolRimDisappear"), Is.EqualTo(true));
        }

        [Test]
        public void Migrate_AlreadyAtVersion5_KeepsPoolRimDisappearOff()
        {
            BindFile(Cfg("[General]", "ConfigVersion = 5", "[Drain]", "PoolRimDisappear = false"));

            Assert.That((bool)Value("PoolRimDisappear"), Is.EqualTo(false));
        }

        [Test]
        public void Bind_NoFileYet_PoolRimDisappearIsOn()
        {
            BindFile();

            Assert.That((bool)Value("PoolRimDisappear"), Is.EqualTo(true));
        }

        [Test]
        public void Bind_NoFileYet_HoseVisualDefaults()
        {
            BindFile();

            Assert.That(Value("WorldHoseStyle").ToString(), Is.EqualTo("Default"));
            Assert.That((float)Value("WorldHoseOpacity"), Is.EqualTo(0.45f).Within(1e-6f));
            Assert.That((float)Value("BuildHoseOpacity"), Is.EqualTo(0.45f).Within(1e-6f));
        }

        [Test]
        public void Migrate_RemovedOptionsInTheFile_AreDroppedFromTheSavedFile()
        {
            BindFile(Cfg("[General]", "ConfigVersion = 2", "[Visuals]", "ClearHoses = false"));

            Assert.That(File.ReadAllText(_path), Does.Not.Contain("ClearHoses"));
        }

        [Test]
        public void Migrate_FromVersion0_EndsAtTheLatestVersionAndSavesIt()
        {
            BindFile(Cfg("[General]", "ConfigVersion = 0"));

            Assert.That(Value("ConfigVersion"), Is.EqualTo(LatestConfigVersion));
            Assert.That(File.ReadAllText(_path), Does.Match(@"ConfigVersion\s*=\s*" + LatestConfigVersion));
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
        public void Bind_UnknownOldHoseStyleName_KeepsTheBuildDefault()
        {
            BindFile(Cfg("[General]", "ConfigVersion = 3", "[Visuals]", "HoseStyle = Banana"));

            Assert.That(Value("BuildHoseStyle").ToString(), Is.EqualTo("Solid"));
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
