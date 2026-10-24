using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Sim.Core.Airspace;

/// <summary>
/// Geografia (harta + raster, partajabila intre rulari) plus rolurile tarilor dintr-un scenariu.
/// Imutabila dupa constructie: poate fi citita fara blocari.
/// </summary>
public sealed class AirspaceModel
{
    private readonly CountryRaster _raster;
    private readonly CountryRole[] _roles;
    private readonly bool[] _violationForAttackers;

    public CountryMap Map { get; }

    /// <param name="roles">Cod ISO2 -> rol. Tarile nementionate sunt Neutral. Un cod care nu exista in harta e eroare.</param>
    public AirspaceModel(CountryMap map, CountryRaster raster, IReadOnlyDictionary<string, CountryRole> roles)
    {
        Map = map;
        _raster = raster;
        _roles = new CountryRole[map.Countries.Count]; // implicit Neutral (0)
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
                $"Coduri de tara necunoscute in harta: {string.Join(", ", unknown.OrderBy(c => c, System.StringComparer.Ordinal))}.");

        for (int i = 0; i < _roles.Length; i++)
            _violationForAttackers[i] = AirspacePolicy.IsViolationForAttackers(_roles[i]);
    }

    /// <summary>Indexul tarii in punctul dat, sau -1 (mare / in afara hartii).</summary>
    public int CountryIndexAt(double latDeg, double lonDeg) => _raster.IndexAt(latDeg, lonDeg);

    public string CodeOf(int countryIndex) => Map.Countries[countryIndex].Iso2;

    public CountryRole RoleOf(int countryIndex) => _roles[countryIndex];

    public bool IsViolationForAttackers(int countryIndex) => _violationForAttackers[countryIndex];
}
