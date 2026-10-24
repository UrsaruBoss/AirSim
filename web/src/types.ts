// Tipurile oglindesc DTO-urile din Sim.Server/Models/Dtos.cs si scenariul din Sim.Core (JSON camelCase).

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
  /** Codul tarii (doar la airspaceViolation). */
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
  /** Id-ul tipului din catalog (doar la valurile create in editor). */
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
  /** Id-ul tipului din catalog (doar la sistemele create in editor). */
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
  defenseIntervalSeconds?: number
  cheapestFirst?: boolean
  localTracking?: boolean
  launchSites: LaunchSite[]
  targets: Target[]
  waves: Wave[]
  defenses: Defense[]
}

/** Snapshot decodat: dronele in zbor, in array-uri tipate (fara obiecte per drona). */
export interface Snapshot {
  tick: number
  timeS: number
  count: number
  ids: Int32Array
  lat: Float32Array
  lon: Float32Array
  alt: Float32Array
  /** Unitati defensive in aer: avioane de patrula (id = indexul unitatii) si interceptoare (id >= 1000000). */
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

/** Rezultatul rularii (GET /api/sim/summary). */
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
}

/** Tip din catalog (data/catalog/*.json): campurile exacte depind de tip, deci doar cele folosite de interfata sunt tipate. */
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
