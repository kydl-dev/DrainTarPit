using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace DrainTarPit.Tests.Integration
{
    /// <summary>
    /// manifest.json, dependencies, version consistency and the Thunderstore package rules. The version had drifted between
    /// Plugin.cs / manifest.json / CHANGELOG before (bug #4), and a mismatch breaks version checks between players.
    /// </summary>
    [TestFixture]
    public class ManifestTests
    {
        static readonly Regex DependencyFormat = new Regex(@"^[A-Za-z0-9_]+-[A-Za-z0-9_]+-\d+\.\d+\.\d+$");
        static readonly Regex SemVer = new Regex(@"^\d+\.\d+\.\d+$");

        static string ModFile(string name) { return Path.Combine(TestPaths.ModDir, name); }

        static Dictionary<string, object> Manifest()
        {
            var json = File.ReadAllText(ModFile("manifest.json"));
            return (Dictionary<string, object>)new JavaScriptSerializer().DeserializeObject(json);
        }

        static string Str(string key) { return Manifest()[key] as string; }

        static string PluginConst(string name)
        {
            var m = Regex.Match(File.ReadAllText(Path.Combine(TestPaths.ModDir, "src", "Plugin.cs")), "const string " + name + @"\s*=\s*""([^""]*)""");
            Assert.That(m.Success, Is.True, "const " + name + " not found in src/Plugin.cs");
            return m.Groups[1].Value;
        }

        static List<string> Dependencies()
        {
            return ((IEnumerable)Manifest()["dependencies"]).Cast<object>().Select(o => (string)o).ToList();
        }

        [Test]
        public void Manifest_File_IsValidJsonWithAllRequiredFields()
        {
            var m = Manifest();

            foreach (var key in new[] { "name", "version_number", "website_url", "description", "dependencies" })
                Assert.That(m.ContainsKey(key), Is.True, "missing field " + key);
        }

        [Test]
        public void Manifest_Name_MatchesThePluginNameAndThunderstoreCharacterRules()
        {
            Assert.That(Str("name"), Is.EqualTo(PluginConst("ModName")));
            Assert.That(Str("name"), Does.Match(@"^[A-Za-z0-9_]{1,128}$"), "Thunderstore allows letters, digits and underscore only");
        }

        [Test]
        public void Manifest_VersionNumber_IsMajorMinorPatch()
        {
            Assert.That(Str("version_number"), Does.Match(SemVer.ToString()));
        }

        [Test]
        public void Manifest_Version_EqualsPluginModVersion()
        {
            Assert.That(Str("version_number"), Is.EqualTo(PluginConst("ModVersion")), "manifest.json and Plugin.ModVersion must agree");
        }

        [Test]
        public void Manifest_Version_EqualsTheNewestChangelogHeading()
        {
            var heading = File.ReadLines(ModFile("CHANGELOG.md")).FirstOrDefault(l => Regex.IsMatch(l, @"^## \d+\.\d+\.\d+"));

            Assert.That(heading, Is.Not.Null, "no '## x.y.z' heading in CHANGELOG.md");
            Assert.That(Regex.Match(heading, @"\d+\.\d+\.\d+").Value, Is.EqualTo(Str("version_number")));
        }

        // PROJECT_RULES.md rule 3: the mod is unreleased, the version is 1.0.0 and is never bumped until the user says so.
        // When the user releases, change this test together with the rule.
        [Test]
        public void Manifest_Version_WhileTheModIsUnreleased_IsExactlyOneZeroZero()
        {
            Assert.That(Str("version_number"), Is.EqualTo("1.0.0"));
            Assert.That(PluginConst("ModVersion"), Is.EqualTo("1.0.0"));
        }

        [Test]
        public void Manifest_Dependencies_UseAuthorNameVersionFormat()
        {
            foreach (var d in Dependencies()) Assert.That(DependencyFormat.IsMatch(d), Is.True, "bad dependency string: " + d);
        }

        [Test]
        public void Manifest_Dependencies_ContainBepInExPackAndJotunn()
        {
            var deps = Dependencies();

            Assert.That(deps.Any(d => d.IndexOf("BepInExPack_Valheim", StringComparison.OrdinalIgnoreCase) >= 0), Is.True);
            Assert.That(deps.Any(d => d.IndexOf("Jotunn", StringComparison.OrdinalIgnoreCase) >= 0), Is.True);
        }

        [Test]
        public void Manifest_JotunnDependency_IsNotNewerThanTheJotunnTheModIsBuiltAgainst()
        {
            var dep = Dependencies().First(d => d.IndexOf("Jotunn", StringComparison.OrdinalIgnoreCase) >= 0);
            var declared = Version.Parse(dep.Substring(dep.LastIndexOf('-') + 1));
            var dll = Path.Combine(TestPaths.RefDir, "Jotunn.dll");
            Assume.That(File.Exists(dll), "Jotunn.dll not in the reference folder");
            var built = Version.Parse(FileVersionInfo.GetVersionInfo(dll).FileVersion);

            Assert.That(new Version(declared.Major, declared.Minor, declared.Build), Is.LessThanOrEqualTo(new Version(built.Major, built.Minor, built.Build)),
                "manifest asks for a newer Jotunn than the one the mod is compiled against");
        }

        [Test]
        public void Package_RequiredFiles_Exist()
        {
            foreach (var f in new[] { "manifest.json", "README.md", "icon.png", "CHANGELOG.md" })
                Assert.That(File.Exists(ModFile(f)), Is.True, f);
        }

        // ---- Thunderstore package rules (checked against Thunderstore's documentation on 2026-10-19). Release gates, not day-to-day failures. ----

        [Category("ReleaseGate")]
        [Test]
        public void Manifest_Description_IsAtMost250Characters()
        {
            Assert.That(Str("description").Length, Is.LessThanOrEqualTo(250), "Thunderstore rejects a longer description");
        }

        [Category("ReleaseGate")]
        [Test]
        public void Icon_File_Is256By256Png()
        {
            var b = File.ReadAllBytes(ModFile("icon.png"));
            var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

            Assert.That(b.Take(8).ToArray(), Is.EqualTo(png), "not a PNG file");
            int width = (b[16] << 24) | (b[17] << 16) | (b[18] << 8) | b[19];
            int height = (b[20] << 24) | (b[21] << 16) | (b[22] << 8) | b[23];
            Assert.That(new[] { width, height }, Is.EqualTo(new[] { 256, 256 }), "Thunderstore requires exactly 256x256");
        }
    }
}
