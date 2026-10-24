using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sim.Core.Catalog;

/// <summary>
/// Catalogul de tipuri: unitati ofensive (offense.json) si sisteme defensive (defense.json), cate un obiect JSON per tip.
/// Formatul fisierelor: { "types": [ { "id": "...", "name": "...", "kind": "...", ...campuri ale Wave / Defense... } ] }.
/// Campurile "id", "notes" si "source" sunt informative si nu se copiaza in scenariu.
/// Catalogul nu are logica: tipurile devin Wave / Defense la incarcarea scenariului (vezi ScenarioLoader).
/// </summary>
public sealed class UnitCatalog
{
    public static UnitCatalog Empty { get; } = new(Array.Empty<JsonObject>(), Array.Empty<JsonObject>());

    public IReadOnlyList<JsonObject> OffenseTypes { get; }
    public IReadOnlyList<JsonObject> DefenseTypes { get; }

    public IReadOnlyDictionary<string, JsonObject> Offense { get; }
    public IReadOnlyDictionary<string, JsonObject> Defense { get; }

    private UnitCatalog(IReadOnlyList<JsonObject> offense, IReadOnlyList<JsonObject> defense)
    {
        OffenseTypes = offense;
        DefenseTypes = defense;
        Offense = offense.ToDictionary(t => t["id"]!.GetValue<string>(), StringComparer.Ordinal);
        Defense = defense.ToDictionary(t => t["id"]!.GetValue<string>(), StringComparer.Ordinal);
    }

    /// <summary>Citeste offense.json si defense.json din folder (fisierele lipsa = catalog gol pentru partea respectiva).</summary>
    public static UnitCatalog FromDirectory(string dir)
    {
        string? Read(string name)
        {
            string path = Path.Combine(dir, name);
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }

        return FromJson(Read("offense.json"), Read("defense.json"));
    }

    public static UnitCatalog FromJson(string? offenseJson, string? defenseJson) =>
        new(Parse(offenseJson, "offense"), Parse(defenseJson, "defense"));

    private static IReadOnlyList<JsonObject> Parse(string? json, string file)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Array.Empty<JsonObject>();

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Catalog {file}: JSON invalid: {ex.Message}", ex);
        }

        if (root is not JsonObject obj || obj["types"] is not JsonArray types)
            throw new InvalidDataException($"Catalog {file}: se asteapta {{ \"types\": [ ... ] }}.");

        var result = new List<JsonObject>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (int i = 0; i < types.Count; i++)
        {
            if (types[i] is not JsonObject type)
                throw new InvalidDataException($"Catalog {file}: types[{i}] nu e un obiect.");

            string? id = type["id"] is JsonValue v && v.TryGetValue(out string? s) ? s : null;
            if (!IsSafeId(id))
                throw new InvalidDataException($"Catalog {file}: types[{i}] are id lipsa sau invalid (litere, cifre, '-', '_').");
            if (!seen.Add(id!))
                throw new InvalidDataException($"Catalog {file}: id duplicat '{id}'.");
            if (type["kind"] is not JsonValue)
                throw new InvalidDataException($"Catalog {file}: '{id}' nu are 'kind'.");

            result.Add(type);
        }

        return result;
    }

    private static bool IsSafeId(string? id) =>
        !string.IsNullOrWhiteSpace(id)
        && id.Length < 100
        && id.All(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.');
}
