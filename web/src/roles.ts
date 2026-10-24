import type { CountryRole, DefenseKind, UnitKind } from './types'

export const ROLE_COLORS: Record<CountryRole, string> = {
  defender: '#3b82f6',
  defenderAlly: '#22d3ee',
  attacker: '#ef4444',
  attackerAlly: '#fb923c',
  neutral: '#94a3b8',
}

export const ROLE_LABELS: Record<CountryRole, string> = {
  defender: 'Apărător',
  defenderAlly: 'Aliat apărător (încălcare)',
  attacker: 'Atacator',
  attackerAlly: 'Aliat atacator',
  neutral: 'Neutru (încălcare)',
}

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

export const DEFENSE_LABELS: Record<DefenseKind, string> = {
  radar: 'Radar (rază de detecție)',
  sam: 'SAM (rază de angajare)',
  aaGun: 'Tun AA',
  abm: 'ABM (anti-balistic)',
  patrol: 'Avion de patrulă (orbită)',
  interceptorBase: 'Bază drone interceptoare',
  airbase: 'Bază aeriană',
  ew: 'Război electronic (rază de bruiaj)',
}

export const UNIT_COLORS: Record<UnitKind, string> = {
  drone: '#facc15',
  ballisticMissile: '#e879f9',
  aircraft: '#f8fafc',
  cruiseMissile: '#fb923c',
}

export const UNIT_LABELS: Record<UnitKind, string> = {
  drone: 'Dronă',
  ballisticMissile: 'Rachetă balistică',
  aircraft: 'Avion',
  cruiseMissile: 'Rachetă de croazieră',
}
