import { playback } from './playback'
import type { Alliance, Catalog, CompareJob, CompareSide, ReplayCheck, ReplayStart, RunSummary, SavedRun, Scenario, ScenarioInfo, SimStatus } from './types'

async function request<T>(path: string, method = 'GET', body?: unknown): Promise<T> {
  const res = await fetch(path, {
    method,
    headers: body === undefined ? undefined : { 'Content-Type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body),
  })

  const data: unknown = await res.json().catch(() => ({}))
  if (!res.ok) {
    const message =
      typeof data === 'object' && data !== null && 'error' in data && typeof data.error === 'string'
        ? data.error
        : res.statusText
    throw new Error(message)
  }
  return data as T
}

export const api = {
  scenarios: () => request<ScenarioInfo[]>('/api/scenarios'),
  scenario: (id: string) => request<Scenario>(`/api/scenarios/${encodeURIComponent(id)}`),
  catalog: () => request<Catalog>('/api/catalog'),
  alliances: () => request<{ alliances: Alliance[] }>('/api/alliances'),
  airspace: (map: string) => request<unknown>(`/api/airspace/${encodeURIComponent(map)}`),
  seek: (seconds: number) => Promise.resolve().then(() => playback.seek(seconds)),
  mode: () => request<{ mode: 'local' | 'public' }>('/api/mode').then((r) => r.mode),
  summary: () => (playback.enabled ? Promise.resolve().then(() => playback.summary()) : request<RunSummary>('/api/sim/summary')),
  status: () => request<SimStatus>('/api/sim/status'),
  start: (scenario: string, seed: number | null) => request<SimStatus>('/api/sim/start', 'POST', { scenario, seed }),
  startCustom: (scenario: unknown, seed: number | null) => request<SimStatus>('/api/sim/start-custom', 'POST', { scenario, seed }),
  pause: () => (playback.enabled ? Promise.resolve().then(() => playback.pause()) : request<SimStatus>('/api/sim/pause', 'POST')),
  resume: () => (playback.enabled ? Promise.resolve().then(() => playback.resume()) : request<SimStatus>('/api/sim/resume', 'POST')),
  stop: () => (playback.enabled ? Promise.resolve().then(() => playback.halt()) : request<SimStatus>('/api/sim/stop', 'POST')),
  setSpeed: (value: number) => (playback.enabled ? Promise.resolve().then(() => playback.setSpeed(value)) : request<SimStatus>('/api/sim/speed', 'POST', { value })),
  runs: () => request<SavedRun[]>('/api/runs'),
  runScenario: (id: string) => request<Scenario>(`/api/runs/${encodeURIComponent(id)}/scenario`),
  replay: (id: string) => request<ReplayStart>(`/api/runs/${encodeURIComponent(id)}/replay`, 'POST'),
  replayCheck: () => request<ReplayCheck>('/api/sim/replay-check'),
  compareStart: (a: CompareSide, b: CompareSide, runs: number, seed: number | null) =>
    request<CompareJob>('/api/compare', 'POST', { a, b, runs, seed }),
  compare: (id: string) => request<CompareJob>(`/api/compare/${encodeURIComponent(id)}`),
  save: () => request<{ runId: string }>('/api/sim/save', 'POST'),
}
