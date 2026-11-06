import i18n from './i18n'
import type { CountryRole, DefenseKind, UnitKind } from './types'

export const ROLE_COLORS: Record<CountryRole, string> = {
  defender: '#3b82f6',
  defenderAlly: '#22d3ee',
  attacker: '#ef4444',
  attackerAlly: '#fb923c',
  neutral: '#94a3b8',
}

/** Labels follow the UI language: call them at render time (components re-render on language change). */
export const roleLabel = (r: CountryRole): string => i18n.t(`labels.role.${r}`)

export const DEFENSE_COLORS: Record<DefenseKind, string> = {
  radar: '#e2e8f0',
  sam: '#4ade80',
  aaGun: '#facc15',
  abm: '#a78bfa',
  patrol: '#38bdf8',
  interceptorBase: '#f472b6',
  airbase: '#fdba74',
  ew: '#2dd4bf',
}

export const defenseLabel = (k: DefenseKind): string => i18n.t(`labels.defense.${k}`)

export const UNIT_COLORS: Record<UnitKind, string> = {
  drone: '#facc15',
  ballisticMissile: '#e879f9',
  aircraft: '#f8fafc',
  cruiseMissile: '#fb923c',
}

export const unitLabel = (k: UnitKind): string => i18n.t(`labels.unit.${k}`)
export const roleShortLabel = (r: CountryRole): string => i18n.t(`labels.roleShort.${r}`)
