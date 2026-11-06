using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Sim.Core.Airspace;
using Sim.Core.Events;
using Sim.Core.Scenarios;
using Xunit;

namespace Sim.Core.Tests;

public class AirspaceTests
{
    // ---------- synthetic geography: rectangles on the equator ----------
    //   AA: lon 0..10   BB: lon 10..20   CC: lon 20..30   (lat -5..5)
    //   DD: lon 40..60 with a hole lon 45..55, lat -2..2
    //   EE: MultiPolygon, two squares (lon 70..71 and 72..73, lat 0..1)

    private static string N(double v) => v.ToString(CultureInfo.InvariantCulture);

    private static string Ring(double w, double s, double e, double n) =>
        $"[[{N(w)},{N(s)}],[{N(e)},{N(s)}],[{N(e)},{N(n)}],[{N(w)},{N(n)}],[{N(w)},{N(s)}]]";

    private static string Feature(string iso, string type, string coordinates) =>
        "{\"type\":\"Feature\",\"properties\":{\"iso2\":\"" + iso + "\",\"name\":\"Tara " + iso + "\"}," +
        "\"geometry\":{\"type\":\"" + type + "\",\"coordinates\":" + coordinates + "}}";

    private static string Collection(params string[] features) =>
        "{\"type\":\"FeatureCollection\",\"features\":[" + string.Join(",", features) + "]}";

    private static readonly string FixtureJson = Collection(
        Feature("AA", "Polygon", "[" + Ring(0, -5, 10, 5) + "]"),
        Feature("BB", "Polygon", "[" + Ring(10, -5, 20, 5) + "]"),
        Feature("CC", "Polygon", "[" + Ring(20, -5, 30, 5) + "]"),
        Feature("DD", "Polygon", "[" + Ring(40, -5, 60, 5) + "," + Ring(45, -2, 55, 2) + "]"),
        Feature("EE", "MultiPolygon", "[[" + Ring(70, 0, 71, 1) + "],[" + Ring(72, 0, 73, 1) + "]]"));

    private static CountryMap FixtureMap() => CountryMap.FromGeoJson(FixtureJson);

    private static CountryRaster FixtureRaster(CountryMap map) => CountryRaster.Build(map, 0.05);

    // ---------- map and raster ----------

    [Fact]
    public void GeoJson_is_parsed_into_countries()
    {
        var map = FixtureMap();

        Assert.Equal(5, map.Countries.Count);
        Assert.Equal(0, map.IndexOf("AA"));
        Assert.Equal(4, map.IndexOf("EE"));
        Assert.Equal(-1, map.IndexOf("ZZ"));
        Assert.Equal("Tara BB", map.Countries[1].Name);
        Assert.Equal(0.0, map.West);
        Assert.Equal(73.0, map.East);
        Assert.Equal(-5.0, map.South);
        Assert.Equal(5.0, map.North);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("{\"type\":\"FeatureCollection\",\"features\":[]}")]
    public void Invalid_geojson_is_rejected(string json)
    {
        Assert.Throws<InvalidDataException>(() => CountryMap.FromGeoJson(json));
    }

    [Fact]
    public void Duplicate_country_codes_are_rejected()
    {
        string json = Collection(
            Feature("AA", "Polygon", "[" + Ring(0, 0, 1, 1) + "]"),
            Feature("AA", "Polygon", "[" + Ring(2, 0, 3, 1) + "]"));

        Assert.Throws<InvalidDataException>(() => CountryMap.FromGeoJson(json));
    }

    [Fact]
    public void Raster_finds_the_country_at_a_point()
    {
        var map = FixtureMap();
        var raster = FixtureRaster(map);

        Assert.Equal(map.IndexOf("AA"), raster.IndexAt(0, 5));
        Assert.Equal(map.IndexOf("BB"), raster.IndexAt(0, 15));
        Assert.Equal(map.IndexOf("CC"), raster.IndexAt(0, 25));
        Assert.Equal(map.IndexOf("AA"), raster.IndexAt(4.5, 0.5));
        Assert.Equal(map.IndexOf("CC"), raster.IndexAt(-4.5, 29.5));
    }

    [Fact]
    public void Raster_returns_minus_one_outside_any_country()
    {
        var raster = FixtureRaster(FixtureMap());

        Assert.Equal(-1, raster.IndexAt(0, 35));   // gap between CC and DD
        Assert.Equal(-1, raster.IndexAt(50, 5));   // far outside the map (north)
        Assert.Equal(-1, raster.IndexAt(0, -20));  // west
        Assert.Equal(-1, raster.IndexAt(0, 500));  // east
    }

    [Fact]
    public void Raster_respects_holes()
    {
        var map = FixtureMap();
        var raster = FixtureRaster(map);

        Assert.Equal(-1, raster.IndexAt(0, 50));               // inside the hole
        Assert.Equal(map.IndexOf("DD"), raster.IndexAt(0, 42)); // in inel, in afara golului
        Assert.Equal(map.IndexOf("DD"), raster.IndexAt(4, 50)); // deasupra golului
    }

    [Fact]
    public void Raster_handles_multipolygon_parts()
    {
        var map = FixtureMap();
        var raster = FixtureRaster(map);

        Assert.Equal(map.IndexOf("EE"), raster.IndexAt(0.5, 70.5));
        Assert.Equal(map.IndexOf("EE"), raster.IndexAt(0.5, 72.5));
        Assert.Equal(-1, raster.IndexAt(0.5, 71.5)); // between the two parts
    }

    [Fact]
    public void Raster_border_is_accurate_to_about_one_cell()
    {
        var map = FixtureMap();
        var raster = FixtureRaster(map);

        // the AA/BB border is at lon 10; one cell (0.05) away on each side must be correct
        Assert.Equal(map.IndexOf("AA"), raster.IndexAt(0, 9.94));
        Assert.Equal(map.IndexOf("BB"), raster.IndexAt(0, 10.06));
    }

    [Fact]
    public void Raster_rejects_invalid_cell_size_and_huge_grids()
    {
        var map = FixtureMap();

        Assert.Throws<ArgumentOutOfRangeException>(() => CountryRaster.Build(map, 0));
        Assert.Throws<ArgumentException>(() => CountryRaster.Build(map, 0.0001)); // above the cell limit
    }

    // ---------- roles and policy ----------

    [Theory]
    [InlineData(CountryRole.Neutral, true)]
    [InlineData(CountryRole.DefenderAlly, true)]
    [InlineData(CountryRole.Defender, false)]
    [InlineData(CountryRole.Attacker, false)]
    [InlineData(CountryRole.AttackerAlly, false)]
    public void Policy_says_which_roles_are_violations_for_attackers(CountryRole role, bool expected)
    {
        Assert.Equal(expected, AirspacePolicy.IsViolationForAttackers(role));
    }

    [Fact]
    public void Unknown_country_codes_in_roles_are_rejected()
    {
        var map = FixtureMap();
        var roles = new Dictionary<string, CountryRole> { ["AA"] = CountryRole.Attacker, ["ZZ"] = CountryRole.Defender };

        var ex = Assert.Throws<InvalidDataException>(() => new AirspaceModel(map, FixtureRaster(map), roles));
        Assert.Contains("ZZ", ex.Message);
    }

    [Fact]
    public void Unlisted_countries_default_to_neutral()
    {
        var map = FixtureMap();
        var model = new AirspaceModel(map, FixtureRaster(map), new Dictionary<string, CountryRole> { ["CC"] = CountryRole.Defender });

        Assert.Equal(CountryRole.Neutral, model.RoleOf(map.IndexOf("AA")));
        Assert.Equal(CountryRole.Defender, model.RoleOf(map.IndexOf("CC")));
        Assert.True(model.IsViolationForAttackers(map.IndexOf("AA")));
        Assert.False(model.IsViolationForAttackers(map.IndexOf("CC")));
        Assert.Equal("BB", model.CodeOf(map.IndexOf("BB")));
    }

    // ---------- scenario ----------

    [Fact]
    public void Scenario_validation_checks_airspace_fields()
    {
        var s = AirScenario(Roles(("AA", CountryRole.Attacker)));

        Assert.Empty(ScenarioLoader.Validate(s));
        Assert.Contains(ScenarioLoader.Validate(s with { AirspaceMap = "" }), e => e.Contains("AirspaceMap"));
        Assert.Contains(ScenarioLoader.Validate(s with { AirspaceMap = "../secret" }), e => e.Contains("AirspaceMap"));
        Assert.Contains(ScenarioLoader.Validate(s with { Countries = Roles(("romania", CountryRole.Defender)) }), e => e.Contains("invalid country code"));
    }

    [Fact]
    public void Countries_roundtrip_through_json_and_do_not_depend_on_file_order()
    {
        const string a = "{ \"airspaceMap\": \"x\", \"countries\": { \"RO\": \"defender\", \"BG\": \"defenderAlly\" }, \"launchSites\": [], \"targets\": [], \"waves\": [] }";
        const string b = "{ \"airspaceMap\": \"x\", \"countries\": { \"BG\": \"defenderAlly\", \"RO\": \"defender\" }, \"launchSites\": [], \"targets\": [], \"waves\": [] }";

        var sa = ScenarioLoader.FromJson(a);
        var sb = ScenarioLoader.FromJson(b);

        Assert.Equal(CountryRole.Defender, sa.Countries["RO"]);
        Assert.Equal(CountryRole.DefenderAlly, sa.Countries["BG"]);
        Assert.Equal(ScenarioLoader.Hash(sa), ScenarioLoader.Hash(sb));
    }

    // ---------- simulation: airspace violations ----------

    private static SortedDictionary<string, CountryRole> Roles(params (string Code, CountryRole Role)[] entries)
    {
        var d = new SortedDictionary<string, CountryRole>(StringComparer.Ordinal);
        foreach (var (code, role) in entries) d[code] = role;
        return d;
    }

    // drones from (0, 5) in AA toward (0, 25) in CC, through BB; 250 m/s => ~2.5 simulated hours
    private static Scenario AirScenario(SortedDictionary<string, CountryRole> countries, int count = 1, double interval = 0) => new()
    {
        Name = "air",
        DtSeconds = 0.5,
        DurationSeconds = 20000,
        AirspaceMap = "fixture",
        Countries = countries,
        LaunchSites = new List<LaunchSite> { new() { Id = "L1", Name = "L1", LatDeg = 0, LonDeg = 5 } },
        Targets = new List<Target> { new() { Id = "T1", Name = "T1", Kind = "city", LatDeg = 0, LonDeg = 25 } },
        Waves = new List<Wave>
        {
            new()
            {
                Id = "W1", SiteId = "L1", TargetId = "T1", Count = count, IntervalSeconds = interval,
                SpeedMps = 250, AltitudeM = 400, SpreadMeters = 0
            }
        }
    };

    private static Simulation RunAir(Scenario s, ulong seed = 1)
    {
        var map = FixtureMap();
        var model = new AirspaceModel(map, FixtureRaster(map), s.Countries);
        var sim = new Simulation(s, seed, model);
        sim.Run();
        return sim;
    }

    private static List<SimEvent> Violations(Simulation sim) =>
        sim.Events.Events.Where(e => e.Type == SimEventType.AirspaceViolation).ToList();

    [Fact]
    public void Crossing_a_neutral_country_is_one_violation()
    {
        var sim = RunAir(AirScenario(Roles(("AA", CountryRole.Attacker), ("BB", CountryRole.Neutral), ("CC", CountryRole.Defender))));

        var violations = Violations(sim);
        Assert.Single(violations);
        Assert.Equal("BB", violations[0].Code);
        Assert.Equal(1, sim.ViolationCount);
        Assert.InRange(violations[0].Position.LonDeg, 9.9, 20.1); // entered BB
        Assert.Equal(1, sim.Events.Events.Count(e => e.Type == SimEventType.Impact));
    }

    [Fact]
    public void Crossing_a_defender_ally_is_a_violation()
    {
        var sim = RunAir(AirScenario(Roles(("AA", CountryRole.Attacker), ("BB", CountryRole.DefenderAlly), ("CC", CountryRole.Defender))));

        var violations = Violations(sim);
        Assert.Single(violations);
        Assert.Equal("BB", violations[0].Code);
    }

    [Theory]
    [InlineData(CountryRole.AttackerAlly)]
    [InlineData(CountryRole.Attacker)]
    [InlineData(CountryRole.Defender)]
    public void Crossing_attacker_side_or_defender_country_is_free(CountryRole middle)
    {
        var sim = RunAir(AirScenario(Roles(("AA", CountryRole.Attacker), ("BB", middle), ("CC", CountryRole.Defender))));

        Assert.Empty(Violations(sim));
        Assert.Equal(0, sim.ViolationCount);
    }

    [Fact]
    public void Unlisted_countries_are_neutral_so_crossing_them_is_a_violation()
    {
        // only CC is listed; AA (launch) is ignored at the start, BB is neutral by default
        var sim = RunAir(AirScenario(Roles(("CC", CountryRole.Defender))));

        var violations = Violations(sim);
        Assert.Single(violations);
        Assert.Equal("BB", violations[0].Code);
    }

    [Fact]
    public void Each_drone_is_counted_separately()
    {
        var sim = RunAir(AirScenario(Roles(("AA", CountryRole.Attacker), ("BB", CountryRole.Neutral), ("CC", CountryRole.Defender)), count: 3, interval: 5));

        Assert.Equal(3, sim.ViolationCount);
        Assert.Equal(new[] { 0, 1, 2 }, Violations(sim).Select(v => v.EntityId).OrderBy(i => i).ToArray());
    }

    [Fact]
    public void Airspace_events_are_deterministic_and_part_of_the_hash()
    {
        var neutral = AirScenario(Roles(("AA", CountryRole.Attacker), ("BB", CountryRole.Neutral), ("CC", CountryRole.Defender)));
        var allied = AirScenario(Roles(("AA", CountryRole.Attacker), ("BB", CountryRole.AttackerAlly), ("CC", CountryRole.Defender)));

        Assert.Equal(RunAir(neutral).Events.Hash(), RunAir(neutral).Events.Hash());
        Assert.NotEqual(RunAir(neutral).Events.Hash(), RunAir(allied).Events.Hash());
    }

    [Fact]
    public void Scenario_that_declares_a_map_requires_an_airspace_model()
    {
        var s = AirScenario(Roles(("AA", CountryRole.Attacker)));
        Assert.Throws<ArgumentException>(() => new Simulation(s, 1));
    }

    [Fact]
    public void Scenario_without_a_map_runs_without_airspace_events()
    {
        var s = AirScenario(new SortedDictionary<string, CountryRole>(StringComparer.Ordinal)) with { AirspaceMap = "" };
        var sim = new Simulation(s, 1);
        sim.Run();

        Assert.Equal(0, sim.ViolationCount);
        Assert.Empty(Violations(sim));
    }

    [Fact]
    public void Event_wire_names_are_camel_case()
    {
        Assert.Equal("launched", SimEventType.Launched.WireName());
        Assert.Equal("impact", SimEventType.Impact.WireName());
        Assert.Equal("airspaceViolation", SimEventType.AirspaceViolation.WireName());
    }
}
