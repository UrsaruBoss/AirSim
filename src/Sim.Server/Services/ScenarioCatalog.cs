using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Sim.Core.Catalog;
using Sim.Core.Scenarios;
using Sim.Server.Models;

namespace Sim.Server.Services;

public static class ScenarioCatalog
{
    /// <summary>Scenario ids are plain file names: no path separators or "..".</summary>
    public static bool IsSafeId(string? id) =>
        !string.IsNullOrWhiteSpace(id)
        && id.Length < 100
        && !id.Contains("..")
        && id.All(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.');

    public static string PathFor(string scenariosDir, string id) => Path.Combine(scenariosDir, id + ".json");

    public static IReadOnlyList<ScenarioInfo> List(string scenariosDir, Func<UnitCatalog> catalog)
    {
        if (!Directory.Exists(scenariosDir))
            return Array.Empty<ScenarioInfo>();

        var result = new List<ScenarioInfo>();
        foreach (string file in Directory.EnumerateFiles(scenariosDir, "*.json").OrderBy(f => f, StringComparer.Ordinal))
        {
            string id = Path.GetFileNameWithoutExtension(file);
            try
            {
                var scenario = ScenarioLoader.FromFile(file, catalog());
                result.Add(new ScenarioInfo(id, scenario.Name, true, null, scenario.Waves.Sum(w => w.Count)));
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException)
            {
                result.Add(new ScenarioInfo(id, id, false, ex.Message, 0));
            }
        }
        return result;
    }
}
