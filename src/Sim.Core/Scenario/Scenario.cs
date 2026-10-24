using System;
using System.Collections.Generic;
using Sim.Core.Airspace;

namespace Sim.Core.Scenarios;

public enum UnitKind
{
    Drone,
    BallisticMissile,
    Aircraft,
    /// <summary>Racheta de croaziera: zboara ca o drona (altitudine constanta), dar e tratata separat de aparare.</summary>
    CruiseMissile
}

/// <summary>Punct de lansare (ofensiv). Locatia e in grade WGS84, altitudine in metri.</summary>
public sealed record LaunchSite
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public double LatDeg { get; init; }
    public double LonDeg { get; init; }
    public double AltM { get; init; }
}

/// <summary>Tinta. Kind e liber deocamdata ("city", "military_base", "field"); devine clasa de teren mai tarziu.</summary>
public sealed record Target
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Kind { get; init; } = "field";
    public double LatDeg { get; init; }
    public double LonDeg { get; init; }
    public double AltM { get; init; }

    /// <summary>Raza zonei tintei (m): impacturile din interior iau clasa tintei, restul cad pe camp. 0 = implicit dupa tip (oras 3000, baza 600).</summary>
    public double RadiusM { get; init; }

    /// <summary>Cate impacturi (care ajung la tinta) o distrug. 0 = nu se distruge niciodata. O tinta distrusa poate fi parasita de valurile cu Retarget.</summary>
    public int ImpactsToDestroy { get; init; }
}

/// <summary>
/// Val de lansare: Count unitati din SiteId catre TargetId, lansate la fiecare IntervalSeconds
/// incepand cu StartTimeSeconds. SpreadMeters = dispersia punctului de tintire (deviatie standard).
/// </summary>
public sealed record Wave
{
    public string Id { get; init; } = "";
    public string SiteId { get; init; } = "";
    public string TargetId { get; init; } = "";

    /// <summary>
    /// Mai multe puncte de lansare: fiecare unitate isi alege unul la intamplare (ordinea conteaza pentru reproductibilitate;
    /// un id repetat de mai multe ori = pondere mai mare). Daca lista nu e goala, inlocuieste SiteId.
    /// </summary>
    public List<string>? SiteIds { get; init; }

    /// <summary>La fel pentru tinte: fiecare unitate isi alege una din lista (ex. mai multe puncte intr-un oras). Inlocuieste TargetId.</summary>
    public List<string>? TargetIds { get; init; }

    /// <summary>Probabilitatea ca o unitate sa nu ajunga la tinta (defectiune, lipsa de combustibil): cade la un punct aleator al traseului (5%-95%).</summary>
    public double FailureRate { get; init; }

    /// <summary>Rezistenta la bruiaj (0-1): 0 = complet sensibila (GPS civil), 1 = imuna (ghidare inertiala / antene CRPA / racheta balistica).</summary>
    public double EwResistance { get; init; }

    /// <summary>Momeala: nu face pagube reale, dar apararea nu o deosebeste de o arma. Folosita doar la statistici (tiruri irosite pe momeli).</summary>
    public bool Decoy { get; init; }

    /// <summary>Costul pe care apararea CREDE ca il are unitatea (USD): o momeala ieftina care arata ca o arma scumpa atrage tiruri scumpe. 0 = costul real.</summary>
    public double PerceivedCostUsd { get; init; }

    /// <summary>
    /// Redirectionare in zbor: cand unitatea e la cel mult RetargetAtKm de tinta si tinta e deja distrusa (Target.ImpactsToDestroy),
    /// alege la intamplare alta tinta nedistrusa din lista valului, aflata la cel mult RetargetRangeKm de pozitia ei. O singura data per unitate.
    /// </summary>
    public bool Retarget { get; init; }
    public double RetargetAtKm { get; init; } = 40.0;
    public double RetargetRangeKm { get; init; } = 150.0;

    public UnitKind Kind { get; init; } = UnitKind.Drone;
    public int Count { get; init; } = 1;

    /// <summary>Id-ul tipului din catalog din care provine valul (informativ; valorile sunt deja rezolvate in camp).</summary>
    public string UnitType { get; init; } = "";

    /// <summary>Costul unei unitati (USD), pentru raportul de cost.</summary>
    public double CostUsd { get; init; }

    /// <summary>Raza de actiune a tipului (km). 0 = necunoscuta / fara limita; altfel scenariul nu poate trimite unitatea mai departe.</summary>
    public double RangeKm { get; init; }
    public double StartTimeSeconds { get; init; }
    public double IntervalSeconds { get; init; }
    public double SpeedMps { get; init; } = 50.0;
    public double AltitudeM { get; init; } = 500.0;
    public double SpreadMeters { get; init; }

    /// <summary>Altitudinea maxima a traiectoriei balistice (m). 0 = automat: 25% din distanta, intre 20 si 300 km. Ignorat la celelalte tipuri.</summary>
    public double ApogeeM { get; init; }

    /// <summary>Masa sarcinii explozive (kg); determina raza letala la estimarea victimelor.</summary>
    public double WarheadKg { get; init; } = 40.0;
}

public enum DefenseKind
{
    /// <summary>Detecteaza (cu limita orizontului radar); nu trage.</summary>
    Radar,
    /// <summary>Racheta sol-aer: trage in drone / avioane detectate de un radar.</summary>
    Sam,
    /// <summary>Tun antiaerian: isi vede singur tintele din raza (fara radar), tavan implicit 4000 m.</summary>
    AaGun,
    /// <summary>Aparare anti-balistica: trage in rachete balistice detectate de un radar.</summary>
    Abm,
    /// <summary>
    /// Avion de vanatoare. Sta in asteptare pe un cerc (centru = Lat/Lon, raza PatrolRadiusKm, altitudine AltM), e trimis pe tintele detectate
    /// din raza ScrambleRangeKm, trage in raza armei (RangeKm), iar cand ramane fara munitie sau combustibil se intoarce la cea mai apropiata
    /// baza aeriana, se reincarca (TurnaroundSeconds) si revine. Cere cel putin o unitate Airbase in scenariu.
    /// </summary>
    Patrol,
    /// <summary>Baza de drone interceptoare: lanseaza (Magazine) drone care urmaresc tinte detectate din raza RangeKm.</summary>
    InterceptorBase,
    /// <summary>Baza aeriana: doar pozitie. Avioanele de patrula se intorc aici sa se reincarce.</summary>
    Airbase,
    /// <summary>
    /// Razboi electronic (bruiaj / falsificare GPS): fiecare unitate aflata in raza (RangeKm) este pierduta cu o rata de JamRatePerMinute
    /// (inmultita cu sensibilitatea ei, vezi Wave.EwResistance). Nu are nevoie de radar si nu foloseste munitie. Afecteaza implicit drone si rachete de croaziera.
    /// </summary>
    Ew
}

/// <summary>
/// Sistem defensiv. RangeKm = raza de detectie (radar), de angajare (SAM / tun / ABM / patrula) sau de lansare (baza de interceptoare).
/// Magazine = rachete / proiectile / interceptoare; Pk = probabilitatea de lovire per tir;
/// ReloadSeconds = timpul minim intre doua tiruri / lansari ale aceleiasi unitati.
/// </summary>
public sealed record Defense
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public DefenseKind Kind { get; init; } = DefenseKind.Sam;
    public double LatDeg { get; init; }
    public double LonDeg { get; init; }
    public double AltM { get; init; }
    public double RangeKm { get; init; } = 20.0;
    public int Magazine { get; init; }
    public double Pk { get; init; } = 0.7;
    public double ReloadSeconds { get; init; } = 2.0;

    /// <summary>Id-ul tipului din catalog din care provine unitatea (informativ; valorile sunt deja rezolvate in camp).</summary>
    public string TypeId { get; init; } = "";

    /// <summary>Costul unui tir / unei lansari (USD), pentru raportul de cost.</summary>
    public double ShotCostUsd { get; init; }

    /// <summary>Costul de achizitie al sistemului (USD, ilustrativ), folosit la modul cu buget. 0 = necunoscut.</summary>
    public double CostUsd { get; init; }

    /// <summary>Pk specific pe tip de tinta (cheie: numele tipului, ex. "ballisticMissile"); tipurile nementionate folosesc Pk.</summary>
    public SortedDictionary<string, double>? PkByKind { get; init; }

    /// <summary>Tipurile de tinte pe care le poate angaja. Gol / lipsa = implicit dupa tip (SAM, tun, patrula, interceptoare: drone + avioane; ABM: rachete balistice).</summary>
    public List<UnitKind>? Engages { get; init; }

    /// <summary>Intervalul de altitudini al tintelor angajabile (m). MaxAltM = 0: nelimitat (la tun: 4000 m).</summary>
    public double MinAltM { get; init; }
    public double MaxAltM { get; init; }

    /// <summary>Patrula: raza cercului de patrulare (km, 0 = stationeaza) si viteza avionului (m/s).</summary>
    public double PatrolRadiusKm { get; init; }
    public double PatrolSpeedMps { get; init; } = 200.0;

    /// <summary>Patrula: cat zboara (s) inainte sa se intoarca la baza, cat sta la sol la reincarcare (s) si pana unde (km de la centru) accepta sa urmareasca o tinta detectata.</summary>
    public double EnduranceSeconds { get; init; } = 3600.0;
    public double TurnaroundSeconds { get; init; } = 600.0;
    public double ScrambleRangeKm { get; init; } = 100.0;

    /// <summary>Baza de interceptoare: viteza dronelor interceptoare (m/s) si durata maxima de zbor (s).</summary>
    public double InterceptorSpeedMps { get; init; } = 120.0;
    public double MaxFlightSeconds { get; init; } = 600.0;

    /// <summary>Razboi electronic: rata de pierdere pe minut (λ) pentru o unitate complet sensibila aflata in raza. Probabilitatea de pierdere intr-un minut = 1 - exp(-λ).</summary>
    public double JamRatePerMinute { get; init; }

    /// <summary>Razboi electronic: scaderea eficientei cu distanta. Rata efectiva = JamRatePerMinute * (1 - JamFalloff * distanta / RangeKm); 0 = constanta in toata raza, 1 = zero la marginea razei.</summary>
    public double JamFalloff { get; init; }

    /// <summary>Razboi electronic: cate unitati poate bruia simultan. Peste aceasta valoare, eficienta se imparte (capacitate / unitati in raza). 0 = fara limita.</summary>
    public int JamCapacity { get; init; }

    // ---- Retea de radare (doar cand Scenario.LocalTracking = true) ----

    /// <summary>Distanta maxima (km) pana la un radar de la care unitatea primeste piste (legatura de date). Implicit 150 km.</summary>
    public double LinkRangeKm { get; init; } = 150.0;

    /// <summary>Raza (km) in care unitatea isi vede singura tintele, cu radarul propriu, fara retea (Pantsir, Tor, grupuri mobile). 0 = fara radar propriu.</summary>
    public double SelfTrackKm { get; init; }

    // ---- Vulnerabilitate (contra-jocul atacatorului): un impact in apropiere poate distruge sistemul ----

    /// <summary>
    /// Cat explozibil (kg) e nevoie, in medie, ca sa distruga sistemul: o lovitura de W kg il distruge cu probabilitatea 1 - exp(-W / HardnessKg).
    /// 0 = implicit dupa tip (radar 40, SAM 80, tun 60, ABM 100, baza de interceptoare 50, baza aeriana 5000); avioanele de patrula nu pot fi distruse.
    /// </summary>
    public double HardnessKg { get; init; }

    /// <summary>Marimea sistemului (m): un impact cu raza letala R lovește daca e la cel mult SizeM + R. 0 = implicit (baza aeriana 600, ABM 40, restul 30).</summary>
    public double SizeM { get; init; }

    // ---- Doctrina de angajare (toate optionale; valorile implicite = comportamentul simplu: trage in tot ce poate) ----

    /// <summary>
    /// Tintele "principale" ale sistemului: le angajeaza mereu (indiferent de cost), pot folosi si munitia rezervata si primesc
    /// pana la MaxRoundsPerTarget tiruri. Restul sunt "tinte de oportunitate", supuse regulilor de mai jos.
    /// Gol / lipsa = implicit: ABM = rachete balistice; restul = tot ce nu e drona (avion, racheta de croaziera, racheta balistica).
    /// </summary>
    public List<UnitKind>? PrimaryKinds { get; init; }

    /// <summary>
    /// Cost maxim acceptat: un tir de ShotCostUsd se foloseste pe o tinta de oportunitate doar daca ShotCostUsd &lt;= MaxCostRatio * costul tintei.
    /// 0 = fara limita. (Nu se aplica daca ShotCostUsd sau costul tintei sunt necunoscute, adica 0.)
    /// </summary>
    public double MaxCostRatio { get; init; }

    /// <summary>"Scapatoarele": o tinta de oportunitate prea ieftina totusi se angajeaza daca mai are cel mult atatia km pana la impact. 0 = niciodata.</summary>
    public double LeakerDistanceKm { get; init; }

    /// <summary>Ultimele ReserveRounds tiruri se pastreaza doar pentru tintele principale.</summary>
    public int ReserveRounds { get; init; }

    /// <summary>Cate tiruri (maxim) se trag intr-o tinta principala: tirurile se trag pe rand, se opreste la prima lovitura (trage-priveste-trage).</summary>
    public int MaxRoundsPerTarget { get; init; } = 1;

    /// <summary>Cum alege tinta: cea mai aproape de impact, sau intai tipul cel mai periculos (balistic &gt; croaziera &gt; avion &gt; drona), apoi cea mai aproape de impact.</summary>
    public TargetPriority Priority { get; init; } = TargetPriority.Imminent;
}

public enum TargetPriority
{
    /// <summary>Tinta care ajunge prima la impact.</summary>
    Imminent,
    /// <summary>Tipul cel mai periculos intai; la egalitate, cea mai apropiata de impact.</summary>
    Threat
}

public sealed record Scenario
{
    public string Name { get; init; } = "";
    public double DtSeconds { get; init; } = 0.1;
    public double DurationSeconds { get; init; } = 3600.0;

    /// <summary>Numele hartii de tari (fisier data/countries/&lt;nume&gt;.geojson). Gol = fara spatiu aerian.</summary>
    public string AirspaceMap { get; init; } = "";

    /// <summary>
    /// Cod ISO2 -> rol. SortedDictionary (ordine ordinala) ca forma canonica si hash-ul sa nu depinda de ordinea din fisier.
    /// Tarile nementionate sunt Neutral.
    /// </summary>
    public SortedDictionary<string, CountryRole> Countries { get; init; } = new(StringComparer.Ordinal);

    public List<LaunchSite> LaunchSites { get; init; } = new();
    public List<Target> Targets { get; init; } = new();
    public List<Wave> Waves { get; init; } = new();

    public List<Defense> Defenses { get; init; } = new();

    /// <summary>Cat de des (in secunde simulate) detecteaza si trage sistemele defensive. Fiecare unitate trage cel mult o data per ciclu.</summary>
    public double DefenseIntervalSeconds { get; init; } = 1.0;

    /// <summary>
    /// Ordinea in care trag unitatile intr-un ciclu: false = ordinea din lista; true = cele mai ieftine intai (cost per tir crescator,
    /// cost necunoscut la urma), ca straturile ieftine sa incerce inaintea celor scumpe.
    /// </summary>
    public bool CheapestFirst { get; init; }

    /// <summary>
    /// false = detectia e globala si permanenta (orice radar care a vazut o tinta o arata tuturor, pentru totdeauna).
    /// true = retea: un sistem care are nevoie de pista trage doar daca un radar INTACT din raza lui de legatura (LinkRangeKm) vede tinta in acest moment,
    /// sau daca tinta e in raza radarului propriu (SelfTrackKm). Radarele distruse sau depasite de tinta nu mai conteaza.
    /// </summary>
    public bool LocalTracking { get; init; }
}
