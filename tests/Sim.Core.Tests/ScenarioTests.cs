using System.IO;
using System.Linq;
using Sim.Core.Scenarios;
using Xunit;

namespace Sim.Core.Tests;

public class ScenarioTests
{
    private const string BaseJson = """
    {
      "name": "test",
      "dtSeconds": 0.1,
      "durationSeconds": 600,
      "launchSites": [ { "id": "L1", "name": "Site 1", "latDeg": 45.0, "lonDeg": 30.0, "altM": 0 } ],
      "targets":     [ { "id": "T1", "name": "Target 1", "kind": "city", "latDeg": 44.0, "lonDeg": 26.0, "altM": 80 } ],
      "waves": [
        { "id": "W1", "siteId": "L1", "targetId": "T1", "kind": "drone", "count": 200,
          "startTimeSeconds": 0, "intervalSeconds": 0.5, "speedMps": 55, "altitudeM": 400, "spreadMeters": 300 }
      ]
    }
    """;

    [Fact]
    public void Loads_camelCase_json_with_string_enums()
    {
        var s = ScenarioLoader.FromJson(BaseJson);

        Assert.Equal("test", s.Name);
        Assert.Single(s.LaunchSites);
        Assert.Single(s.Targets);
        Assert.Equal(UnitKind.Drone, s.Waves.Single().Kind);
        Assert.Equal(200, s.Waves.Single().Count);
    }

    [Fact]
    public void Hash_ignores_whitespace_and_property_order_in_the_file()
    {
        // acelasi continut, formatat altfel si cu proprietati in alta ordine
        const string reordered = """
        { "waves": [ { "count": 200, "id": "W1", "targetId": "T1", "siteId": "L1", "kind": "drone",
                       "startTimeSeconds": 0, "intervalSeconds": 0.5, "speedMps": 55, "altitudeM": 400, "spreadMeters": 300 } ],
          "targets": [ { "id": "T1", "kind": "city", "name": "Target 1", "latDeg": 44.0, "lonDeg": 26.0, "altM": 80 } ],
          "launchSites": [ { "id": "L1", "name": "Site 1", "latDeg": 45.0, "lonDeg": 30.0, "altM": 0 } ],
          "durationSeconds": 600, "dtSeconds": 0.1, "name": "test" }
        """;

        Assert.Equal(
            ScenarioLoader.Hash(ScenarioLoader.FromJson(BaseJson)),
            ScenarioLoader.Hash(ScenarioLoader.FromJson(reordered)));
    }

    [Fact]
    public void Hash_changes_when_content_changes()
    {
        var original = ScenarioLoader.FromJson(BaseJson);
        var changed = original with { Waves = original.Waves.Select(w => w with { Count = 201 }).ToList() };

        Assert.NotEqual(ScenarioLoader.Hash(original), ScenarioLoader.Hash(changed));
    }

    [Fact]
    public void Canonical_json_roundtrips_to_same_hash()
    {
        var original = ScenarioLoader.FromJson(BaseJson);
        var again = ScenarioLoader.FromJson(ScenarioLoader.ToJson(original));

        Assert.Equal(ScenarioLoader.Hash(original), ScenarioLoader.Hash(again));
    }

    [Fact]
    public void Validation_rejects_unknown_references()
    {
        var s = ScenarioLoader.FromJson(BaseJson);
        var broken = s with { Waves = s.Waves.Select(w => w with { SiteId = "NOPE", TargetId = "ALSO_NOPE" }).ToList() };

        var errors = ScenarioLoader.Validate(broken);
        Assert.Contains(errors, e => e.Contains("SiteId"));
        Assert.Contains(errors, e => e.Contains("TargetId"));
    }

    [Fact]
    public void Validation_rejects_duplicate_ids_and_bad_values()
    {
        var s = ScenarioLoader.FromJson(BaseJson);
        var broken = s with
        {
            DtSeconds = 0,
            LaunchSites = s.LaunchSites.Concat(s.LaunchSites).ToList(),
            Waves = s.Waves.Select(w => w with { Count = 0 }).ToList()
        };

        var errors = ScenarioLoader.Validate(broken);
        Assert.Contains(errors, e => e.Contains("DtSeconds"));
        Assert.Contains(errors, e => e.Contains("duplicat"));
        Assert.Contains(errors, e => e.Contains("Count"));
    }

    [Fact]
    public void FromJson_throws_on_invalid_scenario()
    {
        const string bad = """{ "dtSeconds": -1 }""";
        Assert.Throws<InvalidDataException>(() => ScenarioLoader.FromJson(bad));
    }

    [Fact]
    public void RunMeta_is_determined_by_scenario_and_seed()
    {
        var s = ScenarioLoader.FromJson(BaseJson);
        var a = RunMeta.Create(s, 42);
        var b = RunMeta.Create(s, 42);
        var c = RunMeta.Create(s, 43);

        Assert.Equal(a.RunId, b.RunId);
        Assert.Equal(a.ScenarioHash, b.ScenarioHash);
        Assert.NotEqual(a.RunId, c.RunId);
        Assert.Equal(EngineInfo.Version, a.EngineVersion);
    }
}
