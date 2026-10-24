import type { IconName } from './Icons'

/** Categoriile de evenimente: filtrele din panou si iconita/culoarea fiecarui rand. */
export interface EventCategory {
  key: string
  label: string
  icon: IconName
  color: string
  types: string[]
  /** Pornita implicit in filtru? Lansarile si detectarile sunt foarte numeroase. */
  defaultOn: boolean
}

export const EVENT_CATEGORIES: EventCategory[] = [
  { key: 'impact', label: 'Impact', icon: 'burst', color: '#ff6b6b', types: ['impact'], defaultOn: true },
  { key: 'intercepted', label: 'Doborâre', icon: 'check', color: '#3ddc97', types: ['intercepted'], defaultOn: true },
  { key: 'missed', label: 'Ratat / interceptor pierdut', icon: 'close', color: '#8d9bb0', types: ['missed', 'interceptorLost'], defaultOn: true },
  { key: 'violation', label: 'Încălcare spațiu aerian', icon: 'alert', color: '#ffb454', types: ['airspaceViolation'], defaultOn: true },
  { key: 'jammed', label: 'Bruiat', icon: 'bolt', color: '#2dd4bf', types: ['jammed'], defaultOn: true },
  { key: 'crashed', label: 'Căzut pe traseu', icon: 'down', color: '#c4a574', types: ['crashed'], defaultOn: true },
  { key: 'defense', label: 'Sistem distrus', icon: 'shieldOff', color: '#f472b6', types: ['defenseDestroyed'], defaultOn: true },
  { key: 'retarget', label: 'Redirecționat', icon: 'shuffle', color: '#a78bfa', types: ['retargeted'], defaultOn: true },
  { key: 'patrol', label: 'Patrulă (întoarcere / reîncărcare)', icon: 'undo', color: '#38bdf8', types: ['patrolReturning', 'patrolRearmed'], defaultOn: true },
  { key: 'detected', label: 'Detectat', icon: 'radar', color: '#e2e8f0', types: ['detected'], defaultOn: false },
  { key: 'launched', label: 'Lansare (atac / interceptor)', icon: 'launch', color: '#facc15', types: ['launched', 'interceptorLaunched'], defaultOn: false },
]

export const CATEGORY_OF_TYPE: Record<string, EventCategory> = Object.fromEntries(
  EVENT_CATEGORIES.flatMap((c) => c.types.map((t) => [t, c] as const)),
)

export const EVENT_LABEL: Record<string, string> = {
  launched: 'Lansare',
  impact: 'Impact',
  airspaceViolation: 'Încălcare',
  detected: 'Detectat',
  intercepted: 'Doborât',
  missed: 'Ratat',
  interceptorLaunched: 'Interceptor lansat',
  interceptorLost: 'Interceptor pierdut',
  patrolReturning: 'Patrulă → bază',
  patrolRearmed: 'Patrulă reîncărcată',
  defenseDestroyed: 'Sistem distrus',
  crashed: 'Căzut pe traseu',
  jammed: 'Bruiat',
  retargeted: 'Redirecționat',
}

export function defaultFilter(): Set<string> {
  return new Set(EVENT_CATEGORIES.filter((c) => c.defaultOn).map((c) => c.key))
}
