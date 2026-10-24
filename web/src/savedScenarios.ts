import type { SetupState } from './draft'

/** Scenarii salvate local (localStorage) + export/import ca fisier. Nimic nu pleaca de pe calculatorul utilizatorului. */

const KEY = 'airsim.scenarios.v1'
const FORMAT = 'airsim-scenario'

export type SavedState = Omit<SetupState, 'seed'>

export interface SavedScenario {
  id: string
  name: string
  savedAt: string
  state: SavedState
}

function readAll(): SavedScenario[] {
  try {
    const raw = localStorage.getItem(KEY)
    const parsed: unknown = raw ? JSON.parse(raw) : []
    return Array.isArray(parsed) ? (parsed as SavedScenario[]).filter((x) => x && typeof x.id === 'string' && x.state) : []
  } catch {
    return []
  }
}

function writeAll(list: SavedScenario[]): void {
  try {
    localStorage.setItem(KEY, JSON.stringify(list))
  } catch {
    throw new Error('Nu s-a putut salva local (spațiu insuficient sau stocarea e blocată). Folosește „Export” ca să păstrezi scenariul într-un fișier.')
  }
}

export function listSaved(): SavedScenario[] {
  return readAll().sort((a, b) => b.savedAt.localeCompare(a.savedAt))
}

function stripSeed(state: SetupState): SavedState {
  const { seed: _seed, ...rest } = state
  void _seed
  return rest
}

/** Salveaza sub numele dat; un nume existent se suprascrie. */
export function saveScenario(name: string, state: SetupState): SavedScenario {
  const trimmed = name.trim()
  if (!trimmed) throw new Error('Dă un nume scenariului.')
  const list = readAll()
  const existing = list.find((x) => x.name === trimmed)
  const item: SavedScenario = {
    id: existing?.id ?? `${Date.now().toString(36)}${Math.random().toString(36).slice(2, 6)}`,
    name: trimmed,
    savedAt: new Date().toISOString(),
    state: stripSeed(state),
  }
  writeAll([...list.filter((x) => x.id !== item.id), item])
  return item
}

export function deleteScenario(id: string): void {
  writeAll(readAll().filter((x) => x.id !== id))
}

export function exportScenario(name: string, state: SetupState): string {
  return JSON.stringify({ format: FORMAT, version: 1, name: name.trim() || state.scenario.name, state: stripSeed(state) })
}

/** Valideaza minimal un fisier importat; serverul valideaza oricum scenariul complet la pornire. */
export function parseImported(text: string): { name: string; state: SavedState } {
  let data: unknown
  try {
    data = JSON.parse(text)
  } catch {
    throw new Error('Fișierul nu e JSON valid.')
  }
  const d = data as { format?: string; name?: string; state?: SavedState }
  const sc = d?.state?.scenario
  if (d?.format !== FORMAT || !sc || !Array.isArray(sc.waves) || !Array.isArray(sc.defenses) || !Array.isArray(sc.launchSites) || !Array.isArray(sc.targets)) {
    throw new Error('Fișierul nu e un scenariu AirSim.')
  }
  return { name: d.name || sc.name || 'Importat', state: d.state as SavedState }
}
