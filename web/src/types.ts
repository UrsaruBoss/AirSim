// The types mirror the DTOs in Sim.Server/Models/Dtos.cs and the scenario in Sim.Core (JSON camelCase).

export type RunState = 'idle' | 'running' | 'paused' | 'finished'

export interface SimStatus {
  state: RunState
  scenarioId: string
  scenarioName: string
  seed: string
  runId: string
  epoch: number
  speed: number
  simTimeS: number
  tick: number
  totalDrones: number
  flying: number
  impacted: number
  crashed: number
  jammed: number
  violations: number
  detected: number
  intercepted: number
  shotsFired: number
  magazineLeft: number
  interceptorsLaunched: number
  interceptorsLost: number
  savedRunId: string
}

export interface SimEvent {
  tick: number
  timeS: number
  type: string
  id: number
  latDeg: number
  lonDeg: number
  altM: number
  /** The country code (only for airspaceViolation). */
  code: string | null
}

export interface ScenarioInfo {
  id: string
  name: string
  valid: boolean
  error: string | null
  totalUnits: number
}

export interface LaunchSite {
  id: string
  name: string
  latDeg: number
  lonDeg: number
  altM: number
}

export interface Target {
  id: string
  name: string
  kind: string
  latDeg: number
  lonDeg: number
  altM: number
  radiusM?: number
  impactsToDestroy?: number
}

export type DefenseKind = 'radar' | 'sam' | 'aaGun' | 'abm' | 'patrol' | 'interceptorBase' | 'airbase' | 'ew'

export type UnitKind = 'drone' | 'ballisticMissile' | 'aircraft' | 'cruiseMissile'

export interface Wave {
  id: string
  siteId: string
  targetId: string
  siteIds?: string[]
  targetIds?: string[]
  failureRate?: number
  kind: UnitKind
  count: number
  /** Id of the catalog type (only for waves created in the editor). */
  unit?: string
  unitType?: string
  costUsd?: number
  rangeKm?: number
  speedMps?: number
  startTimeSeconds?: number
  intervalSeconds?: number
  spreadMeters?: number
}

export interface Defense {
  id: string
  name: string
  kind: DefenseKind
  latDeg: number
  lonDeg: number
  altM: number
  rangeKm: number
  magazine: number
  pk: number
  reloadSeconds: number
  patrolRadiusKm: number
  /** Id of the catalog type (only for systems created in the editor). */
  type?: string
  typeId?: string
  costUsd?: number
}

export type CountryRole = 'neutral' | 'defender' | 'defenderAlly' | 'attacker' | 'attackerAlly'

export interface Scenario {
  name: string
  airspaceMap: string
  countries: Record<string, CountryRole>
  dtSeconds: number
  durationSeconds: number
  /** ISO 8601 UTC instant at which the scenario starts; absent = no time of day (neutral model). */
  startTimeUtc?: string
  /** Weather preset: clear | overcast | rain | fog | storm (absent = clear). */
  weather?: string
  /** Public warning system (default on): the first detection gives people time to reach shelter. false = off. */
  publicWarning?: boolean
  defenseIntervalSeconds?: number
  cheapestFirst?: boolean
  localTracking?: boolean
  launchSites: LaunchSite[]
  targets: Target[]
  waves: Wave[]
  defenses: Defense[]
}

/** Decoded snapshot: the drones in flight, in typed arrays (no per-drone objects). */
export interface Snapshot {
  tick: number
  timeS: number
  count: number
  ids: Int32Array
  lat: Float32Array
  lon: Float32Array
  alt: Float32Array
  /** Defensive units in the air: patrol aircraft (id = unit index) and interceptors (id >= 1000000). */
  friendlyCount: number
  friendlyIds: Int32Array
  friendlyLat: Float32Array
  friendlyLon: Float32Array
  friendlyAlt: Float32Array
}

export type ConnectionState = 'connecting' | 'connected' | 'reconnecting' | 'disconnected'

export interface Estimate {
  mean: number
  p5: number
  p95: number
}

export interface DefenseStat {
  id: string
  name: string
  kind: DefenseKind
  detected: number
  shots: number
  hits: number
  misses: number
  lost: number
  returns: number
  magazineStart: number
  destroyed?: boolean
}

export interface ClassStat {
  class: string
  impacts: number
  killed: Estimate
  injured: Estimate
}

/** The result of a run (GET /api/sim/summary). */
export interface RunSummary {
  total: number
  launched: number
  detected: number
  intercepted: number
  missed: number
  impacted: number
  violations: number
  classes: ClassStat[]
  killed: Estimate
  injured: Estimate
  violationsByCountry: { code: string; count: number }[]
  defenses: DefenseStat[]
  trials: number
  failed?: number
  jammed?: number
  /** Time series, per-wave outcomes, casualty distribution and cost (absent in runs made by an older engine). */
  series?: RunSeries
  waves?: WaveStat[]
  casualtyHistogram?: CasualtyHistogram
  cost?: CostStat
  warning?: WarningStat
  /** One-at-a-time sensitivity of the scenario (only in precomputed runs). */
  sensitivity?: SensitivityReport
}

export interface SensitivityRow {
  factorId: string
  factor: string
  metric: string
  baseline: number
  meanLow: number
  meanHigh: number
  diffLow: number
  diffHigh: number
  zLow: number
  zHigh: number
  swing: number
}

/** Each input factor scaled by (1 - delta) and (1 + delta), one at a time, with the same seeds. */
export interface SensitivityReport {
  runs: number
  delta: number
  metrics: string[]
  rows: SensitivityRow[]
}

/** Public warning: how early the impacts were announced and how many exposed people were sheltered. */
export interface WarningStat {
  enabled: boolean
  impacts: number
  undetected: number
  meanWarningS: number
  meanShelteredPct: number
}

/** Cumulative counts at the end of each bin of binSeconds. */
export interface RunSeries {
  binSeconds: number
  launched: number[]
  detected: number[]
  intercepted: number[]
  impacts: number[]
  jammed: number[]
  crashed: number[]
  shots: number[]
  magazineTotal: number
}

export interface WaveStat {
  id: string
  kind: UnitKind
  count: number
  launched: number
  intercepted: number
  jammed: number
  crashed: number
  impacted: number
}

/** Bin i covers [i * binWidth, (i + 1) * binWidth); values are numbers of Monte Carlo trials. */
export interface CasualtyHistogram {
  binWidth: number
  killed: number[]
  injured: number[]
}

export interface CostStat {
  attackUsd: number
  defenseUsd: number
}

/** A type from the catalog (data/catalog/*.json): the exact fields depend on the type, so only those used by the interface are typed. */
export interface OffenseType {
  id: string
  name: string
  kind: UnitKind
  rangeKm?: number
  costUsd?: number
  speedMps?: number
  warheadKg?: number
  [key: string]: unknown
}

export interface DefenseType {
  id: string
  name: string
  kind: DefenseKind
  rangeKm?: number
  costUsd?: number
  magazine?: number
  pk?: number
  reloadSeconds?: number
  patrolRadiusKm?: number
  [key: string]: unknown
}

export interface Catalog {
  offense: OffenseType[]
  defense: DefenseType[]
}

export interface Alliance {
  id: string
  name: string
  members: string[]
  note: string
}

export interface SavedRun {
  runId: string
  seed: string
  scenarioName: string
  engineVersion: string
  engineMatches: boolean
  createdUtc: string
  hasEvents: boolean
}

export interface ReplayStart {
  status: SimStatus
  scenarioMatches: boolean
  engineMatches: boolean
  savedEngineVersion: string
}

export interface ReplayCheck {
  active: boolean
  runId: string
  finished: boolean
  match: boolean
  expected: string
  actual: string
}

export interface CompareSide {
  id?: string
  scenario?: unknown
  noRetarget?: boolean
}

export interface ComparisonRow {
  name: string
  meanA: number
  meanB: number
  diff: number
  diffSd: number
  stdError: number
  z: number
  significant: boolean
}

export interface CompareJob {
  id: string
  state: 'running' | 'done' | 'error'
  done: number
  total: number
  error: string
  nameA: string
  nameB: string
  runs: number
  seed: string
  engineVersion: string
  rows: ComparisonRow[] | null
}

/** A precomputed run (public mode): comes from /api/precomputed. */
export interface PrecomputedInfo {
  id: string
  name: string
  /** Changes when the run is regenerated (scenario, seed, engine version or step); used as ?v= for the cache. */
  key: string
  seed: string
  durationS: number
  totalUnits: number
  stepS: number
  dt: number
  binBytes: number
}
