using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DrainTarPit.Tests.Integration
{
    public sealed class PatchRow
    {
        public string Id, GameType, Assembly, Method, Kind, Via, ModPatch;
        public string[] Parameters;
        public bool Required;

        public string ParameterList { get { return string.Join(",", Parameters); } }
        public override string ToString() { return Id + " " + GameType + "." + Method + "(" + ParameterList + ")" + (Required ? "" : " [optional]"); }
    }

    /// <summary>Reads the machine-readable table in _agent/PATCH_INVENTORY.md (between the patch-table markers).</summary>
    internal static class PatchInventory
    {
        public static string FilePath { get { return Path.Combine(TestPaths.AgentDir, "PATCH_INVENTORY.md"); } }

        public static string Text { get { return File.ReadAllText(FilePath); } }

        public static List<PatchRow> Load()
        {
            var rows = new List<PatchRow>();
            bool inside = false;
            foreach (var raw in File.ReadAllLines(FilePath))
            {
                var line = raw.Trim();
                if (line.StartsWith("<!-- patch-table:begin")) { inside = true; continue; }
                if (line.StartsWith("<!-- patch-table:end")) break;
                if (!inside || !line.StartsWith("|")) continue;

                var cells = line.Trim('|').Split('|').Select(c => c.Trim()).ToArray();
                if (cells.Length != 9) throw new FormatException("Patch table row must have 9 columns, found " + cells.Length + ": " + line);
                if (cells[0] == "ID" || cells[0].StartsWith("---")) continue;

                rows.Add(new PatchRow
                {
                    Id = cells[0],
                    GameType = cells[1],
                    Assembly = cells[2],
                    Method = cells[3],
                    Parameters = cells[4] == "-" || cells[4].Length == 0 ? new string[0] : cells[4].Split(',').Select(p => p.Trim()).ToArray(),
                    Kind = cells[5],
                    Via = cells[6],
                    ModPatch = cells[7],
                    Required = string.Equals(cells[8], "yes", StringComparison.OrdinalIgnoreCase),
                });
            }
            return rows;
        }

        public static IEnumerable<PatchRow> RequiredRows() { return Load().Where(r => r.Required); }
        public static IEnumerable<PatchRow> OptionalRows() { return Load().Where(r => !r.Required); }
    }
}
