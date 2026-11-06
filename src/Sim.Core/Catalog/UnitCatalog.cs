using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sim.Core.Catalog;

/// <summary>
/// The type catalog: offensive units (offense.json) and defensive systems (defense.json), one JSON object per type.
/// File format: { "types": [ { "id": "...", "name": "...", "kind": "...", ...Wave / Defense fields... } ] }.
/// The "id", "notes" and "source" fields are informational and are not copied into the scenario.
/// The catalog has no logic: types become Wave / Defense when the scenario is loaded (see ScenarioLoader).
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

    /// <summary>Reads offense.json and defense.json from the folder (a missing file = an empty catalog for that side).</summary>
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
            throw new InvalidDataException($"Catalog {file}: invalid JSON: {ex.Message}", ex);
        }

        if (root is not JsonObject obj || obj["types"] is not JsonArray types)
            throw new InvalidDataException($"Catalog {file}: expected {{ \"types\": [ ... ] }}.");

        var result = new List<JsonObject>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (int i = 0; i < types.Count; i++)
        {
            if (types[i] is not JsonObject type)
                throw new InvalidDataException($"Catalog {file}: types[{i}] is not an object.");

            string? id = type["id"] is JsonValue v && v.TryGetValue(out string? s) ? s : null;
            if (!IsSafeId(id))
                throw new InvalidDataException($"Catalog {file}: types[{i}] has a missing or invalid id (letters, digits, '-', '_').");
            if (!seen.Add(id!))
                throw new InvalidDataException($"Catalog {file}: duplicate id '{id}'.");
            if (type["kind"] is not JsonValue)
                throw new InvalidDataException($"Catalog {file}: '{id}' has no 'kind'.");

            result.Add(type);
        }

        return result;
    }

    private static bool IsSafeId(string? id) =>
        !string.IsNullOrWhiteSpace(id)
        && id.Length < 100
        && id.All(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.');
}
