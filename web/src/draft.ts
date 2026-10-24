// Starea editorului de scenarii si calculele pure din jurul ei (costuri, durata, payload-ul trimis serverului).

import type { CountryRole, Defense, DefenseType, LaunchSite, OffenseType, Scenario, Target, Wave } from './types'

export type Mode = 'sandbox' | 'budget'

export const BLANK_ID = '__blank__'

/** Scenariile prestabilite afisate implicit; restul (exemple / teste) sunt ascunse in spatele unei casete. */
export const PRESET_IDS = ['ru-vs-ua', 'ua-vs-ru', 'kp-vs-kr', 'cn-vs-tw', 'ir-vs-il', 'ir-vs-il-proxies', 'il-vs-ir', 'kr-vs-kp', 'in-vs-pk', 'pk-vs-in', 'ru-vs-baltic', 'ru-vs-natose']

export interface SetupState {
  presetId: string
  scenario: Scenario
  /** Id-urile elementelor adaugate de utilizator (doar acestea se platesc din buget). */
  addedDefenses: string[]
  addedWaves: string[]
  mode: Mode
  /** Bugete in milioane USD. */
  budgetDefenderM: number
  budgetAttackerM: number
  seed: string
}

export function blankScenario(): Scenario {
  return {
    name: 'Scenariu personalizat',
    airspaceMap: 'world',
    countries: {},
    dtSeconds: 0.5,
    durationSeconds: 3600,
    defenseIntervalSeconds: 1,
    cheapestFirst: true,
    localTracking: true,
    launchSites: [],
    targets: [],
    waves: [],
    defenses: [],
  }
}

export function initialSetup(): SetupState {
  return {
    presetId: BLANK_ID,
    scenario: blankScenario(),
    addedDefenses: [],
    addedWaves: [],
    mode: 'sandbox',
    budgetDefenderM: 1000,
    budgetAttackerM: 500,
    seed: '',
  }
}

export function haversineKm(aLat: number, aLon: number, bLat: number, bLon: number): number {
  const rad = Math.PI / 180
  const dLat = (bLat - aLat) * rad
  const dLon = (bLon - aLon) * rad
  const h = Math.sin(dLat / 2) ** 2 + Math.cos(aLat * rad) * Math.cos(bLat * rad) * Math.sin(dLon / 2) ** 2
  return 2 * 6371.0088 * Math.asin(Math.min(1, Math.sqrt(h)))
}

/** Primul `prefix + numar` care nu exista deja. */
export function uniqueId(prefix: string, existing: readonly string[]): string {
  const used = new Set(existing)
  let n = existing.length + 1
  while (used.has(`${prefix}${n}`)) n++
  return `${prefix}${n}`
}

function withoutInfo<T extends { id: string; name: string }>(type: T) {
  const rest: Record<string, unknown> = { ...type }
  delete rest.id
  delete rest.name
  delete rest.notes
  delete rest.source
  return rest
}

export function makeDefense(type: DefenseType, latDeg: number, lonDeg: number, scenario: Scenario): Defense {
  const sameType = scenario.defenses.filter((d) => d.type === type.id).length
  return {
    ...withoutInfo(type),
    type: type.id,
    id: uniqueId('U', scenario.defenses.map((d) => d.id)),
    name: `${type.name} ${sameType + 1}`,
    kind: type.kind,
    latDeg: round4(latDeg),
    lonDeg: round4(lonDeg),
    altM: 0,
    rangeKm: type.rangeKm ?? 0,
    magazine: type.magazine ?? 0,
    pk: type.pk ?? 0,
    reloadSeconds: type.reloadSeconds ?? 0,
    patrolRadiusKm: type.patrolRadiusKm ?? 0,
    costUsd: type.costUsd ?? 0,
  }
}

export function makeLaunchSite(latDeg: number, lonDeg: number, scenario: Scenario): LaunchSite {
  const id = uniqueId('L', scenario.launchSites.map((s) => s.id))
  return { id, name: `Lansare ${id.slice(1)}`, latDeg: round4(latDeg), lonDeg: round4(lonDeg), altM: 0 }
}

export function makeTarget(kind: 'city' | 'military_base', latDeg: number, lonDeg: number, scenario: Scenario): Target {
  const id = uniqueId('T', scenario.targets.map((t) => t.id))
  const base = kind === 'city'
  return {
    id,
    name: `${base ? 'Oraș' : 'Bază'} ${id.slice(1)}`,
    kind,
    latDeg: round4(latDeg),
    lonDeg: round4(lonDeg),
    altM: 0,
    radiusM: base ? 0 : 800,
    impactsToDestroy: base ? 0 : 10,
  }
}

export interface WaveForm {
  typeId: string
  count: number
  siteIds: string[]
  targetIds: string[]
  startMin: number
  intervalS: number
  spreadM: number
}

export function makeWave(type: OffenseType, form: WaveForm, scenario: Scenario): Wave {
  return {
    ...withoutInfo(type),
    unit: type.id,
    id: uniqueId('V', scenario.waves.map((w) => w.id)),
    kind: type.kind,
    count: form.count,
    siteId: form.siteIds[0],
    targetId: form.targetIds[0],
    siteIds: form.siteIds.length > 1 ? form.siteIds : undefined,
    targetIds: form.targetIds.length > 1 ? form.targetIds : undefined,
    startTimeSeconds: Math.round(form.startMin * 60),
    intervalSeconds: form.intervalS,
    spreadMeters: form.spreadM,
    costUsd: type.costUsd ?? 0,
    rangeKm: type.rangeKm ?? 0,
    speedMps: type.speedMps,
  }
}

function round4(v: number): number {
  return Math.round(v * 10000) / 10000
}

// ---- costuri ----

export const defenseCost = (d: Defense): number => d.costUsd ?? 0
export const waveCost = (w: Wave): number => w.count * (w.costUsd ?? 0)

export function sum<T>(items: readonly T[], f: (item: T) => number): number {
  return items.reduce((acc, item) => acc + f(item), 0)
}

export interface Costs {
  /** Costul elementelor adaugate de utilizator. */
  defenderAdded: number
  attackerAdded: number
  /** Costul intregului scenariu. */
  defenderTotal: number
  attackerTotal: number
}

export function costs(state: SetupState): Costs {
  const { scenario } = state
  const addedD = new Set(state.addedDefenses)
  const addedW = new Set(state.addedWaves)
  return {
    defenderAdded: sum(scenario.defenses.filter((d) => addedD.has(d.id)), defenseCost),
    attackerAdded: sum(scenario.waves.filter((w) => addedW.has(w.id)), waveCost),
    defenderTotal: sum(scenario.defenses, defenseCost),
    attackerTotal: sum(scenario.waves, waveCost),
  }
}

export const formatMoney = (usd: number): string =>
  usd >= 1e9 ? `${(usd / 1e9).toFixed(2)} mld $` : usd >= 1e6 ? `${(usd / 1e6).toFixed(1)} mil $` : `${Math.round(usd / 1e3)} mii $`

// ---- valuri ----

export function waveSiteIds(w: Wave): string[] {
  return w.siteIds && w.siteIds.length > 0 ? w.siteIds : [w.siteId]
}

export function waveTargetIds(w: Wave): string[] {
  return w.targetIds && w.targetIds.length > 0 ? w.targetIds : [w.targetId]
}

/** Cea mai lunga pereche lansare -> tinta (km), ca la validarea de pe server. */
export function maxPairDistanceKm(siteIds: readonly string[], targetIds: readonly string[], scenario: Scenario): number {
  let max = 0
  for (const sid of siteIds) {
    const s = scenario.launchSites.find((x) => x.id === sid)
    if (!s) continue
    for (const tid of targetIds) {
      const t = scenario.targets.find((x) => x.id === tid)
      if (t) max = Math.max(max, haversineKm(s.latDeg, s.lonDeg, t.latDeg, t.lonDeg))
    }
  }
  return max
}

/** Durata minima ca toate valurile sa ajunga: ultima lansare + zborul cel mai lung, cu rezerva. */
export function neededDurationS(scenario: Scenario): number {
  let end = 0
  for (const w of scenario.waves) {
    const speed = w.speedMps && w.speedMps > 0 ? w.speedMps : 50
    const flight = (maxPairDistanceKm(waveSiteIds(w), waveTargetIds(w), scenario) * 1000) / speed
    end = Math.max(end, (w.startTimeSeconds ?? 0) + Math.max(0, w.count - 1) * (w.intervalSeconds ?? 0) + flight)
  }
  return Math.ceil(end * 1.15 + 900)
}

/**
 * Scenariul trimis serverului. Elementele adaugate din catalog pleaca minimal (tip + pozitie), iar serverul le rezolva din catalog,
 * ca orice scenariu; restul (din scenariul prestabilit) pleaca asa cum au fost primite.
 */
export function toPayload(state: SetupState): Scenario {
  const { scenario } = state
  const addedD = new Set(state.addedDefenses)
  const addedW = new Set(state.addedWaves)

  const defenses = scenario.defenses.map((d) =>
    addedD.has(d.id)
      ? ({ type: d.type, id: d.id, name: d.name, latDeg: d.latDeg, lonDeg: d.lonDeg, altM: d.altM } as unknown as Defense)
      : d,
  )

  const waves = scenario.waves.map((w) =>
    addedW.has(w.id)
      ? ({
          unit: w.unit,
          id: w.id,
          siteId: w.siteId,
          targetId: w.targetId,
          siteIds: w.siteIds,
          targetIds: w.targetIds,
          count: w.count,
          startTimeSeconds: w.startTimeSeconds,
          intervalSeconds: w.intervalSeconds,
          spreadMeters: w.spreadMeters,
        } as unknown as Wave)
      : w,
  )

  return {
    ...scenario,
    durationSeconds: Math.max(scenario.durationSeconds || 0, neededDurationS(scenario)),
    defenses,
    waves,
  }
}

export function rolesInUse(countries: Record<string, CountryRole>): [string, CountryRole][] {
  return Object.entries(countries).sort(([a], [b]) => a.localeCompare(b))
}
