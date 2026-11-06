import i18n from './i18n'
import type { IconName } from './Icons'

/** Event categories: the panel filters and the icon/color of each row. */
export interface EventCategory {
  key: string
  icon: IconName
  color: string
  types: string[]
  /** On by default in the filter? Launches and detections are very numerous. */
  defaultOn: boolean
}

export const EVENT_CATEGORIES: EventCategory[] = [
  { key: 'impact', icon: 'burst', color: '#ff6b6b', types: ['impact'], defaultOn: true },
  { key: 'intercepted', icon: 'check', color: '#3ddc97', types: ['intercepted'], defaultOn: true },
  { key: 'missed', icon: 'close', color: '#8d9bb0', types: ['missed', 'interceptorLost'], defaultOn: true },
  { key: 'violation', icon: 'alert', color: '#ffb454', types: ['airspaceViolation'], defaultOn: true },
  { key: 'jammed', icon: 'bolt', color: '#2dd4bf', types: ['jammed'], defaultOn: true },
  { key: 'crashed', icon: 'down', color: '#c4a574', types: ['crashed'], defaultOn: true },
  { key: 'defense', icon: 'shieldOff', color: '#f472b6', types: ['defenseDestroyed'], defaultOn: true },
  { key: 'retarget', icon: 'shuffle', color: '#a78bfa', types: ['retargeted'], defaultOn: true },
  { key: 'patrol', icon: 'undo', color: '#38bdf8', types: ['patrolReturning', 'patrolRearmed'], defaultOn: true },
  { key: 'detected', icon: 'radar', color: '#e2e8f0', types: ['detected'], defaultOn: false },
  { key: 'launched', icon: 'launch', color: '#facc15', types: ['launched', 'interceptorLaunched'], defaultOn: false },
]

export const CATEGORY_OF_TYPE: Record<string, EventCategory> = Object.fromEntries(
  EVENT_CATEGORIES.flatMap((c) => c.types.map((t) => [t, c] as const)),
)

/** Labels follow the UI language: call at render time. */
export const categoryLabel = (key: string): string => i18n.t(`events.category.${key}`)
export const eventLabel = (type: string): string => i18n.t(`events.type.${type}`, { defaultValue: type })

export function defaultFilter(): Set<string> {
  return new Set(EVENT_CATEGORIES.filter((c) => c.defaultOn).map((c) => c.key))
}
