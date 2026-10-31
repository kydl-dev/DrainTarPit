using System.IO;
using System.Linq;
using Mono.Cecil;

namespace DrainTarPit.Tests.Integration
{
    /// <summary>Game fields the mod reads by name (Traverse / AccessTools strings, not patch parameters): they must still exist in the current game DLLs.</summary>
    [TestFixture]
    public class GameFieldTests
    {
        [Category("ReleaseGate")]
[TestCase("Player", "m_placeRotationDegrees")]
        [TestCase("CharacterAnimEvent", "m_character")]
        [TestCase("ZoneSystem", "m_waterLevel")]
        [TestCase("LiquidVolume", "m_depths")]
        [TestCase("LiquidVolume", "m_heights")]
        [TestCase("LiquidVolume", "m_liquidType")]
        [TestCase("LiquidVolume", "m_width")]
        public void GameField_UsedByTheMod_ExistsInReferenceAssembly(string type, string field)
        {
            var path = Path.Combine(TestPaths.RefDir, "assembly_valheim.dll");
            var module = ModuleDefinition.ReadModule(path);
            var t = module.GetType(type);

            Assert.That(t, Is.Not.Null, "type " + type);
            Assert.That(t.Fields.Any(x => x.Name == field), Is.True, type + "." + field);
        }
    }
}