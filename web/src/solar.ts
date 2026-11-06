import type { Scenario } from './types'

const RAD = Math.PI / 180
const J2000_MS = Date.UTC(2000, 0, 1, 12, 0, 0)

/**
 * Sun elevation above the horizon (degrees) at a place and a UTC instant.
 * Low-precision astronomical formula (error well under 0.1 degree), a pure function of its inputs;
 * the C# engine uses the same formula, so the UI and the model agree on what "night" means.
 */
export function sunElevationDeg(latDeg: number, lonDeg: number, utcMs: number): number {
  const sun = sunPosition(utcMs)
  return elevationFor(sun, latDeg * RAD, lonDeg * RAD)
}

/** Declination of the sun and the Greenwich hour angle at an instant (radians). */
export interface SunPosition {
  decRad: number
  ghaRad: number
}

export function sunPosition(utcMs: number): SunPosition {
  const d = (utcMs - J2000_MS) / 86_400_000
  const g = (357.529 + 0.98560028 * d) * RAD
  const q = 280.459 + 0.98564736 * d
  const lambda = (q + 1.915 * Math.sin(g) + 0.02 * Math.sin(2 * g)) * RAD
  const eps = (23.439 - 0.00000036 * d) * RAD
  const ra = Math.atan2(Math.cos(eps) * Math.sin(lambda), Math.cos(lambda))
  const decRad = Math.asin(Math.sin(eps) * Math.sin(lambda))
  const gmstDeg = (18.697374558 + 24.06570982441908 * d) * 15
  return { decRad, ghaRad: gmstDeg * RAD - ra }
}

export function elevationFor(sun: SunPosition, latRad: number, lonRad: number): number {
  const ha = sun.ghaRad + lonRad
  const sinEl = Math.sin(latRad) * Math.sin(sun.decRad) + Math.cos(latRad) * Math.cos(sun.decRad) * Math.cos(ha)
  return Math.asin(Math.max(-1, Math.min(1, sinEl))) / RAD
}

/** Unit vector (Earth-fixed, x to lon 0/lat 0, z to the north pole) towards the point opposite the sun: the centre of the night. */
export function antisolarUnit(utcMs: number): [number, number, number] {
  const sun = sunPosition(utcMs)
  const lat = -sun.decRad
  const lon = Math.PI - sun.ghaRad
  return [Math.cos(lat) * Math.cos(lon), Math.cos(lat) * Math.sin(lon), Math.sin(lat)]
}

/** Start of the scenario as UTC milliseconds, or null when the scenario has no time of day (the model stays neutral). */
export function scenarioStartMs(scenario: Pick<Scenario, 'startTimeUtc'> | null | undefined): number | null {
  const raw = scenario?.startTimeUtc
  if (!raw) return null
  const ms = Date.parse(raw)
  return Number.isFinite(ms) ? ms : null
}

export type DayPhase = 'day' | 'twilight' | 'night'

/** Day above 0 degrees, civil twilight down to -6, night below. */
export function dayPhase(elevationDeg: number): DayPhase {
  return elevationDeg > 0 ? 'day' : elevationDeg > -6 ? 'twilight' : 'night'
}

/** Centroid of the targets (or of the launch sites when there are no targets); used for the "local time" of the scenario. */
export function scenarioCenter(scenario: Scenario): { latDeg: number; lonDeg: number } | null {
  const pts = scenario.targets.length > 0 ? scenario.targets : scenario.launchSites
  if (pts.length === 0) return null
  let lat = 0
  let lon = 0
  for (const p of pts) {
    lat += p.latDeg
    lon += p.lonDeg
  }
  return { latDeg: lat / pts.length, lonDeg: lon / pts.length }
}

/** Day used by the time-of-day presets: close to the equinox, so day and night are about equally long everywhere. */
export const PRESET_DATE = '2026-03-20'

/**
 * UTC instant (ISO 8601, whole seconds) at which the mean solar time at a longitude is `hour` (0..24) on a date.
 * Mean solar time, not the political time zone: it is what decides day and night, and it is the same rule the C# side uses.
 */
export function startUtcForLocalSolarHour(lonDeg: number, hour: number, date: string = PRESET_DATE): string {
  const ms = Date.parse(`${date}T00:00:00Z`) + (hour - lonDeg / 15) * 3_600_000
  return new Date(Math.round(ms / 1000) * 1000).toISOString().replace('.000Z', 'Z')
}

/** Mean solar time (0..24 h) at a longitude at a UTC instant. */
export function localSolarHour(lonDeg: number, utcMs: number): number {
  const h = ((utcMs / 3_600_000 + lonDeg / 15) % 24 + 24) % 24
  return h
}

export const VARIANTS = ['day', 'night'] as const
export type Variant = (typeof VARIANTS)[number]

/** "ru-vs-ua.night" -> { baseId: "ru-vs-ua", variant: "night" }; an id without a day/night suffix is its own base. */
export function splitVariantId(id: string): { baseId: string; variant: Variant | null } {
  for (const v of VARIANTS) {
    const suffix = `.${v}`
    if (id.length > suffix.length && id.endsWith(suffix)) return { baseId: id.slice(0, -suffix.length), variant: v }
  }
  return { baseId: id, variant: null }
}
