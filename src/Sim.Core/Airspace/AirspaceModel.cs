using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Sim.Core.Airspace;

/// <summary>
/// Geography (map + raster, shareable between runs) plus the country roles of one scenario.
/// Immutable after construction: can be read without locking.
/// </summary>
public sealed class AirspaceModel
{
    private readonly CountryRaster _raster;
    private readonly CountryRole[] _roles;
    private readonly bool[] _violationForAttackers;

    public CountryMap Map { get; }

    /// <param name="roles">ISO2 code -> role. Countries not mentioned are Neutral. A code that does not exist in the map is an error.</param>
    public AirspaceModel(CountryMap map, CountryRaster raster, IReadOnlyDictionary<string, CountryRole> roles)
    {
        Map = map;
        _raster = raster;
        _roles = new CountryRole[map.Countries.Count]; // Neutral (0) by default
        _violationForAttackers = new bool[map.Countries.Count];

        var unknown = new List<string>();
        foreach (var (code, role) in roles)
        {
            int index = map.IndexOf(code);
            if (index < 0)
                unknown.Add(code);
            else
                _roles[index] = role;
        }

        if (unknown.Count > 0)
            throw new InvalidDataException(
                $"Unknown country codes in the map: {string.Join(", ", unknown.OrderBy(c => c, System.StringComparer.Ordinal))}.");

        for (int i = 0; i < _roles.Length; i++)
            _violationForAttackers[i] = AirspacePolicy.IsViolationForAttackers(_roles[i]);
    }

    /// <summary>Index of the country at the given point, or -1 (sea / outside the map).</summary>
    public int CountryIndexAt(double latDeg, double lonDeg) => _raster.IndexAt(latDeg, lonDeg);

    public string CodeOf(int countryIndex) => Map.Countries[countryIndex].Iso2;

    public CountryRole RoleOf(int countryIndex) => _roles[countryIndex];

    public bool IsViolationForAttackers(int countryIndex) => _violationForAttackers[countryIndex];
}
