using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Mono.Cecil;

namespace DrainTarPit.Tests.Integration
{
    /// <summary>
    /// Harmony patch targets, checked against the reference game build (ref\ or DTP_REF_DIR) without starting the game: the type, the
    /// method and the exact parameter list of every row of _agent/PATCH_INVENTORY.md must exist. This catches "the game renamed or
    /// changed a method" after an update. It does NOT prove a patch still behaves correctly (that is the runtime smoke test).
    /// </summary>
    [TestFixture]
    [Category("PatchTargets")]
    public class PatchTargetTests
    {
        static readonly Dictionary<string, ModuleDefinition> Modules = new Dictionary<string, ModuleDefinition>();

        public static IEnumerable<PatchRow> Required() { return PatchInventory.RequiredRows(); }
        public static IEnumerable<PatchRow> Optional() { return PatchInventory.OptionalRows(); }

        static ModuleDefinition Module(string assembly)
        {
            ModuleDefinition m;
            if (Modules.TryGetValue(assembly, out m)) return m;
            var path = Path.Combine(TestPaths.RefDir, assembly + ".dll");
            if (!File.Exists(path)) throw new FileNotFoundException("Reference assembly not found: " + path);
            return Modules[assembly] = ModuleDefinition.ReadModule(path);
        }

        static bool TargetExists(PatchRow row, out string detail)
        {
            var type = Module(row.Assembly).GetType(row.GameType);
            if (type == null) { detail = "type " + row.GameType + " not found in " + row.Assembly + ".dll"; return false; }

            var named = type.Methods.Where(m => m.Name == row.Method).ToList();
            if (named.Count == 0) { detail = "no method " + row.GameType + "." + row.Method; return false; }

            var wanted = row.ParameterList;
            if (named.Any(m => string.Join(",", m.Parameters.Select(p => p.ParameterType.FullName)) == wanted)) { detail = "ok"; return true; }

            detail = row.GameType + "." + row.Method + " exists but not with (" + wanted + "). Found: " +
                string.Join(" | ", named.Select(m => "(" + string.Join(",", m.Parameters.Select(p => p.ParameterType.FullName)) + ")"));
            return false;
        }

        [Test]
        public void Inventory_Table_HasRowsAndEveryRowIsComplete()
        {
            var rows = PatchInventory.Load();

            Assert.That(rows, Is.Not.Empty);
            foreach (var r in rows)
            {
                Assert.That(new[] { r.Id, r.GameType, r.Assembly, r.Method, r.Kind, r.Via, r.ModPatch }, Has.None.Empty, r.ToString());
                Assert.That(new[] { "attribute", "TargetMethod", "manual" }, Does.Contain(r.Via), r + ": Targeted via");
            }
            Assert.That(rows.Select(r => r.Id), Is.Unique, "patch ids must be unique");
        }

        [Category("ReleaseGate")]
        [TestCaseSource(nameof(Required))]
        public void RequiredTarget_InReferenceAssembly_ExistsWithTheExactSignature(PatchRow row)
        {
            string detail;

            var ok = TargetExists(row, out detail);

            Assert.That(ok, Is.True, "REQUIRED patch target missing (release blocker unless a tested fallback exists): " + detail);
        }

        [TestCaseSource(nameof(Optional))]
        public void OptionalTarget_InReferenceAssembly_ExistsOrWarns(PatchRow row)
        {
            string detail;

            if (!TargetExists(row, out detail)) Assert.Warn("optional patch target not found (the feature degrades): " + detail);
        }

        [Test]
        public void Inventory_AgainstTheModSource_ListsEveryAttributePatchAndNothingStale()
        {
            // every [HarmonyPatch(typeof(X), "Y" | nameof(X.Y) ...)] in src must be one row (same count per type+method); and rows say "attribute" only when the source has one
            var rx = new Regex(@"\[HarmonyPatch\(\s*typeof\(\s*(?<t>\w+)\s*\)\s*,\s*(?:""(?<m>\w+)""|nameof\(\s*\w+\.(?<m2>\w+)\s*\))");
            var inSource = new List<string>();
            foreach (var file in Directory.GetFiles(Path.Combine(TestPaths.ModDir, "src"), "*.cs", SearchOption.AllDirectories))
                foreach (Match m in rx.Matches(File.ReadAllText(file)))
                    inSource.Add(m.Groups["t"].Value + "." + (m.Groups["m"].Success ? m.Groups["m"].Value : m.Groups["m2"].Value));

            var inTable = PatchInventory.Load().Where(r => r.Via == "attribute").Select(r => r.GameType + "." + r.Method).ToList();

            var notInTable = inSource.GroupBy(x => x).Where(g => g.Count() > inTable.Count(t => t == g.Key)).Select(g => g.Key).ToList();
            var notInSource = inTable.GroupBy(x => x).Where(g => g.Count() > inSource.Count(t => t == g.Key)).Select(g => g.Key).ToList();

            Assert.That(notInTable, Is.Empty, "patches in the source with no row in _agent/PATCH_INVENTORY.md");
            Assert.That(notInSource, Is.Empty, "rows in _agent/PATCH_INVENTORY.md with no [HarmonyPatch] in the source");
        }

        [Category("ReleaseGate")]
        [TestCase("Player", "m_placeRotationDegrees")]
        [TestCase("CharacterAnimEvent", "m_character")]
        [TestCase("ZoneSystem", "m_waterLevel")]
        [TestCase("LiquidVolume", "m_depths")]
        [TestCase("LiquidVolume", "m_heights")]
        [TestCase("LiquidVolume", "m_liquidType")]
        [TestCase("LiquidVolume", "m_width")]
        public void GameField_UsedByAPatch_ExistsInReferenceAssembly(string type, string field)
        {
            var t = Module("assembly_valheim").GetType(type);

            Assert.That(t, Is.Not.Null, "type " + type);
            Assert.That(t.Fields.Any(f => f.Name == field), Is.True, type + "." + field);
        }

        [Test]
        public void ReferenceBuild_Fingerprint_IsTheOneTheInventoryWasCheckedAgainst()
        {
            var path = Path.Combine(TestPaths.RefDir, "assembly_valheim.dll");
            string hash;
            using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "");

            if (PatchInventory.Text.IndexOf(hash, StringComparison.OrdinalIgnoreCase) < 0)
                Assert.Warn("assembly_valheim.dll is a different build (" + hash + ") than the one the inventory was checked against: re-verify, run the smoke tests, update the build block in PATCH_INVENTORY.md");
        }
    }
}
