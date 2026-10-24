using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Sim.Core.Catalog;
using Sim.Core.Events;
using Sim.Core.Scenarios;
using Xunit;

namespace Sim.Core.Tests;

public class CatalogTests
{
    private const string OffenseJson = """
        { "types": [
            { "id": "shahed", "name": "Shahed", "kind": "drone", "speedMps": 51, "altitudeM": 300, "warheadKg": 50, "costUsd": 35000, "rangeKm": 2000, "source": "x", "notes": "y" },
            { "id": "lancet", "name": "Lancet", "kind": "drone", "speedMps": 30, "warheadKg": 3, "rangeKm": 40 },
            { "id": "cruise", "name": "Cruise", "kind": "cruiseMissile", "speedMps": 240, "altitudeM": 50, "warheadKg": 450 },
            { "id": "bm", "name": "BM", "kind": "ballisticMissile", "speedMps": 1500, "warheadKg": 480 }
        ] }
        """;

    private const string DefenseJson = """
        { "types": [
            { "id": "radar", "name": "Radar generic", "kind": "radar", "rangeKm": 100 },
            { "id": "sam", "name": "SAM generic", "kind": "sam", "rangeKm": 50, "magazine": 10, "pk": 1.0, "reloadSeconds": 0,
              "engages": ["drone", "ballisticMissile"], "pkByKind": { "ballisticMissile": 0.0 }, "shotCostUsd": 1000 }
        ] }
        """;

    private static UnitCatalog Catalog() => UnitCatalog.FromJson(OffenseJson, DefenseJson);

    private static string ScenarioJson(string wave, string defense = "") => $$"""
        {
          "name": "cat-test", "dtSeconds": 0.1, "durationSeconds": 6000,
          "launchSites": [ { "id": "L1", "name": "L1", "latDeg": 44.0, "lonDeg": 24.0 } ],
          "targets": [ { "id": "T1", "name": "T1", "kind": "city", "latDeg": 44.0, "lonDeg": 26.0, "altM": 50 } ],
          "waves": [ {{wave}} ],
          "defenses": [ {{defense}} ]
        }
        """;

    // ---------- catalog ----------

    [Fact]
    public void Catalog_parses_types_by_id()
    {
        var c = Catalog();

        Assert.Equal(4, c.OffenseTypes.Count);
        Assert.Equal(2, c.DefenseTypes.Count);
        Assert.True(c.Offense.ContainsKey("shahed"));
        Assert.True(c.Defense.ContainsKey("sam"));
    }

    [Fact]
    public void Missing_catalog_files_give_an_empty_catalog()
    {
        var c = UnitCatalog.FromJson(null, "");

        Assert.Empty(c.OffenseTypes);
        Assert.Empty(c.DefenseTypes);
    }

    [Theory]
    [InlineData("{ \"types\": [ { \"id\": \"a\", \"kind\": \"drone\" }, { \"id\": \"a\", \"kind\": \"drone\" } ] }", "duplicat")]
    [InlineData("{ \"types\": [ { \"id\": \"a\" } ] }", "kind")]
    [InlineData("{ \"types\": [ { \"kind\": \"drone\" } ] }", "id")]
    [InlineData("{ \"types\": [ { \"id\": \"../x\", \"kind\": \"drone\" } ] }", "id")]
    [InlineData("{ \"nope\": [] }", "types")]
    [InlineData("not json", "JSON")]
    public void Invalid_catalogs_are_rejected(string json, string expected)
    {
        var ex = Assert.Throws<InvalidDataException>(() => UnitCatalog.FromJson(json, null));

        Assert.Contains(expected, ex.Message);
    }

    // ---------- rezolvarea referintelor ----------

    [Fact]
    public void Wave_takes_its_values_from_the_catalog_and_the_scenario_overrides_them()
    {
        var s = ScenarioJson("""{ "id": "W1", "unit": "shahed", "siteId": "L1", "targetId": "T1", "count": 5, "speedMps": 99 }""");

        var wave = Assert.Single(ScenarioLoader.FromJson(s, Catalog()).Waves);

        Assert.Equal("W1", wave.Id);                 // id-ul valului, nu al tipului
        Assert.Equal("shahed", wave.UnitType);
        Assert.Equal(UnitKind.Drone, wave.Kind);
        Assert.Equal(50, wave.WarheadKg);            // din catalog
        Assert.Equal(300, wave.AltitudeM);
        Assert.Equal(35000, wave.CostUsd);
        Assert.Equal(2000, wave.RangeKm);
        Assert.Equal(99, wave.SpeedMps);             // suprascris de scenariu
        Assert.Equal(5, wave.Count);
    }

    [Fact]
    public void Defense_takes_its_values_from_the_catalog_with_overrides_and_its_own_position()
    {
        var s = ScenarioJson(
            """{ "id": "W1", "unit": "shahed", "siteId": "L1", "targetId": "T1" }""",
            """{ "id": "S1", "type": "sam", "latDeg": 44.0, "lonDeg": 25.0, "magazine": 3 }, { "id": "S2", "type": "sam", "name": "Custom", "latDeg": 44.0, "lonDeg": 25.0 }""");

        var scenario = ScenarioLoader.FromJson(s, Catalog());
        var s1 = scenario.Defenses[0];
        var s2 = scenario.Defenses[1];

        Assert.Equal("SAM generic", s1.Name);        // numele vine din tip
        Assert.Equal("Custom", s2.Name);             // dar scenariul il poate schimba
        Assert.Equal("sam", s1.TypeId);
        Assert.Equal(DefenseKind.Sam, s1.Kind);
        Assert.Equal(3, s1.Magazine);                // suprascris
        Assert.Equal(10, s2.Magazine);               // din catalog
        Assert.Equal(1000, s1.ShotCostUsd);
        Assert.Equal(new[] { UnitKind.Drone, UnitKind.BallisticMissile }, s1.Engages!.ToArray());
        Assert.Equal(0.0, s1.PkByKind!["ballisticMissile"]);
        Assert.Equal(44.0, s1.LatDeg);
    }

    [Fact]
    public void Unknown_type_or_missing_catalog_is_rejected()
    {
        string unknown = ScenarioJson("""{ "id": "W1", "unit": "nope", "siteId": "L1", "targetId": "T1" }""");
        string known = ScenarioJson("""{ "id": "W1", "unit": "shahed", "siteId": "L1", "targetId": "T1" }""");

        Assert.Contains("nope", Assert.Throws<InvalidDataException>(() => ScenarioLoader.FromJson(unknown, Catalog())).Message);
        Assert.Contains("catalog", Assert.Throws<InvalidDataException>(() => ScenarioLoader.FromJson(known)).Message);
    }

    [Fact]
    public void Resolved_scenario_is_self_contained_and_keeps_its_identity()
    {
        string json = ScenarioJson("""{ "id": "W1", "unit": "shahed", "siteId": "L1", "targetId": "T1", "count": 3 }""",
            """{ "id": "S1", "type": "sam", "latDeg": 44.0, "lonDeg": 25.0 }""");
        var resolved = ScenarioLoader.FromJson(json, Catalog());

        // salvat si reincarcat FARA catalog => aceleasi valori, acelasi hash
        var reloaded = ScenarioLoader.FromJson(ScenarioLoader.ToJson(resolved));

        Assert.Equal(ScenarioLoader.Hash(resolved), ScenarioLoader.Hash(reloaded));
    }

    [Fact]
    public void Changing_the_catalog_changes_the_scenario_hash()
    {
        string json = ScenarioJson("""{ "id": "W1", "unit": "shahed", "siteId": "L1", "targetId": "T1" }""");
        var changed = UnitCatalog.FromJson(OffenseJson.Replace("\"warheadKg\": 50", "\"warheadKg\": 60"), DefenseJson);

        Assert.NotEqual(ScenarioLoader.Hash(ScenarioLoader.FromJson(json, Catalog())), ScenarioLoader.Hash(ScenarioLoader.FromJson(json, changed)));
    }

    [Fact]
    public void Engages_and_pk_order_do_not_change_the_hash()
    {
        string a = ScenarioJson("""{ "id": "W1", "unit": "shahed", "siteId": "L1", "targetId": "T1" }""",
            """{ "id": "S1", "kind": "sam", "latDeg": 44.0, "lonDeg": 25.0, "engages": ["drone", "aircraft"], "pkByKind": { "drone": 0.9, "aircraft": 0.5 } }""");
        string b = ScenarioJson("""{ "id": "W1", "unit": "shahed", "siteId": "L1", "targetId": "T1" }""",
            """{ "id": "S1", "kind": "sam", "latDeg": 44.0, "lonDeg": 25.0, "engages": ["aircraft", "drone", "drone"], "pkByKind": { "aircraft": 0.5, "drone": 0.9 } }""");

        Assert.Equal(ScenarioLoader.Hash(ScenarioLoader.FromJson(a, Catalog())), ScenarioLoader.Hash(ScenarioLoader.FromJson(b, Catalog())));
    }

    // ---------- raza de actiune ----------

    [Fact]
    public void Waves_beyond_the_unit_range_are_rejected()
    {
        // tinta la ~160 km; Lancet are 40 km, Shahed 2000 km
        string lancet = ScenarioJson("""{ "id": "W1", "unit": "lancet", "siteId": "L1", "targetId": "T1" }""");
        string shahed = ScenarioJson("""{ "id": "W1", "unit": "shahed", "siteId": "L1", "targetId": "T1" }""");

        Assert.Contains("raza de actiune", Assert.Throws<InvalidDataException>(() => ScenarioLoader.FromJson(lancet, Catalog())).Message);
        Assert.Single(ScenarioLoader.FromJson(shahed, Catalog()).Waves);
    }

    // ---------- tipuri noi si Pk pe tip de tinta ----------

    private static Simulation Run(string json)
    {
        var sim = new Simulation(ScenarioLoader.FromJson(json, Catalog()), 1);
        sim.Run();
        return sim;
    }

    private const string RadarDef = """{ "id": "R1", "type": "radar", "latDeg": 44.0, "lonDeg": 25.0 }""";
    private const string SamDef = """{ "id": "S1", "type": "sam", "latDeg": 44.0, "lonDeg": 25.0 }""";

    [Fact]
    public void Pk_by_kind_overrides_the_default_pk()
    {
        // SAM-ul din catalog: Pk 1.0, dar 0.0 contra rachetelor balistice
        var drones = Run(ScenarioJson("""{ "id": "W1", "unit": "shahed", "siteId": "L1", "targetId": "T1", "count": 5 }""", RadarDef + "," + SamDef));
        var ballistic = Run(ScenarioJson("""{ "id": "W1", "unit": "bm", "siteId": "L1", "targetId": "T1", "count": 5 }""", RadarDef + "," + SamDef));

        Assert.Equal(5, drones.InterceptedCount);
        Assert.Equal(0, ballistic.InterceptedCount);
        Assert.True(ballistic.ShotsFired > 0); // il angajeaza (e in lista engages), dar rateaza mereu
        Assert.Equal(ballistic.ShotsFired, ballistic.Events.Events.Count(e => e.Type == SimEventType.Missed));
    }

    [Fact]
    public void Cruise_missiles_fly_like_drones_and_default_sams_engage_them()
    {
        var radar = """{ "id": "R1", "kind": "radar", "latDeg": 44.0, "lonDeg": 25.0, "rangeKm": 100 }""";
        var sam = """{ "id": "S1", "kind": "sam", "latDeg": 44.0, "lonDeg": 25.0, "rangeKm": 50, "magazine": 10, "pk": 1.0, "reloadSeconds": 0 }""";
        var abm = """{ "id": "A1", "kind": "abm", "latDeg": 44.0, "lonDeg": 25.0, "rangeKm": 50, "magazine": 10, "pk": 1.0, "reloadSeconds": 0 }""";
        string wave = """{ "id": "W1", "unit": "cruise", "siteId": "L1", "targetId": "T1", "count": 4 }""";

        var none = Run(ScenarioJson(wave));
        var withSam = Run(ScenarioJson(wave, radar + "," + sam));
        var withAbm = Run(ScenarioJson(wave, radar + "," + abm));

        Assert.Equal(4, none.ImpactCount);
        Assert.Equal(4, withSam.InterceptedCount);
        Assert.Equal(0, withAbm.ShotsFired);
    }

    [Fact]
    public void Pk_by_kind_is_validated()
    {
        string badKey = ScenarioJson("""{ "id": "W1", "unit": "shahed", "siteId": "L1", "targetId": "T1" }""",
            """{ "id": "S1", "kind": "sam", "latDeg": 44.0, "lonDeg": 25.0, "pkByKind": { "spaceship": 0.5 } }""");
        string badValue = ScenarioJson("""{ "id": "W1", "unit": "shahed", "siteId": "L1", "targetId": "T1" }""",
            """{ "id": "S1", "kind": "sam", "latDeg": 44.0, "lonDeg": 25.0, "pkByKind": { "drone": 1.5 } }""");

        Assert.Contains("spaceship", Assert.Throws<InvalidDataException>(() => ScenarioLoader.FromJson(badKey, Catalog())).Message);
        Assert.Contains("0 si 1", Assert.Throws<InvalidDataException>(() => ScenarioLoader.FromJson(badValue, Catalog())).Message);
    }

    // ---------- fisierele livrate ----------

    private static string? FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "data", "catalog")) && Directory.Exists(Path.Combine(dir.FullName, "scenarios")))
                return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }

    [Fact]
    public void Shipped_catalog_and_scenarios_are_valid()
    {
        string? root = FindRepoRoot();
        if (root is null)
            return; // rulat in afara depozitului: nu avem fisierele

        var catalog = UnitCatalog.FromDirectory(Path.Combine(root, "data", "catalog"));
        Assert.NotEmpty(catalog.OffenseTypes);
        Assert.NotEmpty(catalog.DefenseTypes);

        foreach (string file in Directory.EnumerateFiles(Path.Combine(root, "scenarios"), "*.json"))
        {
            var scenario = ScenarioLoader.FromFile(file, catalog); // arunca daca e invalid
            Sim.Core.Airspace.AirspaceModel? airspace = string.IsNullOrEmpty(scenario.AirspaceMap) ? null : TestAirspace(root, scenario);
            var sim = new Simulation(scenario, 1, airspace);
            for (int i = 0; i < 50; i++)
                sim.Step();
        }
    }

    [Fact]
    public void Shipped_types_have_costs_for_the_budget_mode_and_alliances_are_well_formed()
    {
        string? root = FindRepoRoot();
        if (root is null)
            return;

        var catalog = UnitCatalog.FromDirectory(Path.Combine(root, "data", "catalog"));
        foreach (var t in catalog.DefenseTypes)
            Assert.True((t["costUsd"]?.GetValue<double>() ?? 0) > 0, $"defense '{t["id"]}' nu are costUsd");
        foreach (var t in catalog.OffenseTypes)
            Assert.True((t["costUsd"]?.GetValue<double>() ?? 0) > 0, $"offense '{t["id"]}' nu are costUsd");

        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "data", "alliances.json")));
        foreach (var a in doc.RootElement.GetProperty("alliances").EnumerateArray())
        {
            Assert.False(string.IsNullOrWhiteSpace(a.GetProperty("name").GetString()));
            foreach (var m in a.GetProperty("members").EnumerateArray())
                Assert.Matches("^[A-Z]{2}$", m.GetString()!);
        }
    }

    private static Sim.Core.Airspace.AirspaceModel TestAirspace(string root, Scenario scenario)
    {
        var map = Sim.Core.Airspace.CountryMap.FromFile(Path.Combine(root, "data", "countries", scenario.AirspaceMap + ".geojson"));
        var raster = Sim.Core.Airspace.CountryRaster.Build(map, 0.5); // grosier: doar sa nu dureze
        return new Sim.Core.Airspace.AirspaceModel(map, raster, scenario.Countries);
    }
}
