import i18n from './i18n'
import { SimClient } from './SimClient'
import { decodeSnapshot } from './snapshot'
import type { PrecomputedInfo, RunSummary, SimEvent, SimStatus, Snapshot } from './types'

/**
 * Player for the precomputed runs (public mode). It has the same interface as SimClient
 * (snapshots / events / statuses / connection), so the map and the panels do not know there is no live simulation.
 * The stored frames are at a fixed step (stepS); snapshots at ~10 Hz are composed from them, like on the live server,
 * interpolating by id between the two neighboring frames, and CesiumView interpolates further between arrivals.
 */

interface Frame {
  t: number
  off: number
  len: number
  /** flying, impacted, violations, detected, intercepted, shotsFired, magazineLeft, interceptorsLaunched, interceptorsLost, crashed, jammed */
  c: number[]
}

interface DataFile {
  frames: Frame[]
  events: SimEvent[]
  summary: RunSummary
}

interface Layer {
  count: number
  ids: Int32Array
  lat: Float32Array
  lon: Float32Array
  alt: Float32Array
}

const TICK_MS = 100

const droneLayer = (s: Snapshot): Layer => ({ count: s.count, ids: s.ids, lat: s.lat, lon: s.lon, alt: s.alt })
const friendlyLayer = (s: Snapshot): Layer => ({ count: s.friendlyCount, ids: s.friendlyIds, lat: s.friendlyLat, lon: s.friendlyLon, alt: s.friendlyAlt })

function indexOf(layer: Layer): Map<number, number> {
  const map = new Map<number, number>()
  for (let i = 0; i < layer.count; i++) map.set(layer.ids[i], i)
  return map
}

/**
 * Entities present in both frames are interpolated; those that appear or disappear between frames are shown
 * only for half of the interval (new ones after the midpoint, vanished ones until the midpoint).
 */
function mix(a: Layer, b: Layer, alpha: number, ia: Map<number, number>, ib: Map<number, number>): Layer {
  const max = a.count + b.count
  const ids = new Int32Array(max)
  const lat = new Float32Array(max)
  const lon = new Float32Array(max)
  const alt = new Float32Array(max)
  let n = 0

  for (let i = 0; i < b.count; i++) {
    const id = b.ids[i]
    const j = ia.get(id)
    if (j !== undefined) {
      ids[n] = id
      lat[n] = a.lat[j] + (b.lat[i] - a.lat[j]) * alpha
      lon[n] = a.lon[j] + (b.lon[i] - a.lon[j]) * alpha
      alt[n] = a.alt[j] + (b.alt[i] - a.alt[j]) * alpha
      n++
    } else if (alpha >= 0.5) {
      ids[n] = id
      lat[n] = b.lat[i]
      lon[n] = b.lon[i]
      alt[n] = b.alt[i]
      n++
    }
  }
  if (alpha < 0.5) {
    for (let j = 0; j < a.count; j++) {
      const id = a.ids[j]
      if (ib.has(id)) continue
      ids[n] = id
      lat[n] = a.lat[j]
      lon[n] = a.lon[j]
      alt[n] = a.alt[j]
      n++
    }
  }
  return { count: n, ids: ids.subarray(0, n), lat: lat.subarray(0, n), lon: lon.subarray(0, n), alt: alt.subarray(0, n) }
}

export class PlaybackClient extends SimClient {
  /** Set once, after the server has answered that it runs in public mode. */
  enabled = false

  private info: PrecomputedInfo | null = null
  private data: DataFile | null = null
  private bin: Uint8Array | null = null

  private state: SimStatus['state'] = 'idle'
  private epoch = 0
  private speed = 100
  private t = 0
  private evIdx = 0
  private timer: number | null = null
  private lastWall = 0
  private dirty = false

  private cache = new Map<number, Snapshot>()
  private pair: { k: number; d: [Map<number, number>, Map<number, number>]; f: [Map<number, number>, Map<number, number>] } | null = null

  override async start(): Promise<void> {
    this.connection.emit('connected')
  }

  override async stop(): Promise<void> {
    this.halt()
  }

  get durationS(): number {
    return this.info?.durationS ?? 0
  }

  async list(): Promise<PrecomputedInfo[]> {
    const res = await fetch('/api/precomputed')
    if (!res.ok) throw new Error(i18n.t('playback.listFailed'))
    return (await res.json()) as PrecomputedInfo[]
  }

  /** Downloads the run (data + frames) and starts it from the beginning. */
  async load(info: PrecomputedInfo, onProgress?: (fraction: number) => void): Promise<void> {
    const v = encodeURIComponent(info.key)
    const [dataRes, binRes] = await Promise.all([fetch(`/api/precomputed/${info.id}/data?v=${v}`), fetch(`/api/precomputed/${info.id}/bin?v=${v}`)])
    if (!dataRes.ok || !binRes.ok) throw new Error(i18n.t('playback.unavailable'))

    const data = (await dataRes.json()) as DataFile

    const bin = new Uint8Array(info.binBytes)
    let got = 0
    const reader = binRes.body?.getReader()
    if (reader) {
      for (;;) {
        const { done, value } = await reader.read()
        if (done) break
        if (got + value.length > bin.length) throw new Error(i18n.t('playback.corrupt'))
        bin.set(value, got)
        got += value.length
        onProgress?.(Math.min(1, got / bin.length))
      }
    } else {
      bin.set(new Uint8Array(await binRes.arrayBuffer()))
    }

    this.halt()
    this.info = info
    this.data = data
    this.bin = bin
    this.begin(0, 'running')
  }

  restart(): SimStatus {
    if (!this.data) throw new Error(i18n.t('playback.noRun'))
    return this.begin(0, 'running')
  }

  seek(seconds: number): SimStatus {
    if (!this.data || !this.info) throw new Error(i18n.t('playback.noRun'))
    const t = Math.min(Math.max(0, seconds), this.info.durationS)
    return this.begin(t, this.state === 'running' ? 'running' : 'paused')
  }

  pause(): SimStatus {
    if (this.state !== 'running') throw new Error(i18n.t('playback.notRunning'))
    this.state = 'paused'
    return this.publishStatus()
  }

  resume(): SimStatus {
    if (this.state !== 'paused') throw new Error(i18n.t('playback.notPaused'))
    this.state = 'running'
    this.lastWall = performance.now()
    return this.publishStatus()
  }

  halt(): SimStatus {
    if (this.timer !== null) window.clearInterval(this.timer)
    this.timer = null
    this.state = 'idle'
    this.epoch++
    this.cache.clear()
    this.pair = null
    this.t = 0
    this.evIdx = 0
    return this.publishStatus()
  }

  setSpeed(value: number): SimStatus {
    this.speed = value
    return this.publishStatus()
  }

  summary(): RunSummary {
    if (!this.data || this.state !== 'finished') throw new Error('Rezultatele apar la final.')
    return this.data.summary
  }

  // ---------- internal ----------

  /** New epoch = the map and the event list are cleared; the events up to t are resent on the next tick. */
  private begin(t: number, state: SimStatus['state']): SimStatus {
    if (this.timer !== null) window.clearInterval(this.timer)
    this.epoch++
    this.t = t
    this.evIdx = 0
    this.state = state
    this.dirty = true
    this.cache.clear()
    this.pair = null
    this.lastWall = performance.now()
    this.timer = window.setInterval(() => this.tick(), TICK_MS)
    return this.publishStatus()
  }

  private tick(): void {
    if (!this.data || !this.info) return
    const now = performance.now()
    const wallDt = (now - this.lastWall) / 1000
    this.lastWall = now

    const wasRunning = this.state === 'running'
    if (wasRunning) {
      this.t += wallDt * this.speed
      if (this.t >= this.info.durationS) {
        this.t = this.info.durationS
        this.state = 'finished'
      }
    }

  // when paused, emit only once after a seek/start (dirty), so the positions and events up to t appear
    if (wasRunning || this.dirty) {
      this.dirty = false
      this.snapshots.emit(this.compose(this.t))

      const events = this.data.events
      const from = this.evIdx
      while (this.evIdx < events.length && events[this.evIdx].timeS <= this.t) this.evIdx++
      if (this.evIdx > from) this.events.emit(events.slice(from, this.evIdx))

      this.statuses.emit(this.buildStatus())
    }

    if (this.state === 'finished' && this.timer !== null) {
      window.clearInterval(this.timer)
      this.timer = null
    }
  }

  private frameAt(t: number): number {
    const frames = this.data!.frames
    let lo = 0
    let hi = frames.length - 1
    while (lo < hi) {
      const mid = (lo + hi + 1) >> 1
      if (frames[mid].t <= t) lo = mid
      else hi = mid - 1
    }
    return lo
  }

  private snap(k: number): Snapshot {
    let s = this.cache.get(k)
    if (!s) {
      const f = this.data!.frames[k]
      s = decodeSnapshot(this.bin!.subarray(f.off, f.off + f.len))
      if (this.cache.size > 4) this.cache.delete(this.cache.keys().next().value as number)
      this.cache.set(k, s)
    }
    return s
  }

  private compose(t: number): Snapshot {
    const frames = this.data!.frames
    const k = this.frameAt(t)
    const a = this.snap(k)
    const next = Math.min(k + 1, frames.length - 1)
    if (next === k) return { ...a, timeS: t }

    const b = this.snap(next)
    const alpha = Math.min(1, Math.max(0, (t - frames[k].t) / (frames[next].t - frames[k].t)))

    if (!this.pair || this.pair.k !== k) {
      this.pair = {
        k,
        d: [indexOf(droneLayer(a)), indexOf(droneLayer(b))],
        f: [indexOf(friendlyLayer(a)), indexOf(friendlyLayer(b))],
      }
    }
    const d = mix(droneLayer(a), droneLayer(b), alpha, this.pair.d[0], this.pair.d[1])
    const f = mix(friendlyLayer(a), friendlyLayer(b), alpha, this.pair.f[0], this.pair.f[1])

    return {
      tick: Math.floor(t / (this.info?.dt || 1)),
      timeS: t,
      count: d.count,
      ids: d.ids,
      lat: d.lat,
      lon: d.lon,
      alt: d.alt,
      friendlyCount: f.count,
      friendlyIds: f.ids,
      friendlyLat: f.lat,
      friendlyLon: f.lon,
      friendlyAlt: f.alt,
    }
  }

  private buildStatus(): SimStatus {
    const info = this.info
    const data = this.data
    const c = info && data && this.state !== 'idle' ? data.frames[this.frameAt(this.t)].c : new Array<number>(11).fill(0)
    return {
      state: this.state,
      scenarioId: info?.id ?? '',
      scenarioName: this.state === 'idle' ? '' : (info?.name ?? ''),
      seed: this.state === 'idle' ? '' : (info?.seed ?? ''),
      runId: info ? `pre-${info.key}` : '',
      epoch: this.epoch,
      speed: this.speed,
      simTimeS: this.t,
      tick: Math.floor(this.t / (info?.dt || 1)),
      totalDrones: this.state === 'idle' ? 0 : (info?.totalUnits ?? 0),
      flying: c[0],
      impacted: c[1],
      violations: c[2],
      detected: c[3],
      intercepted: c[4],
      shotsFired: c[5],
      magazineLeft: c[6],
      interceptorsLaunched: c[7],
      interceptorsLost: c[8],
      crashed: c[9],
      jammed: c[10],
      savedRunId: '',
    }
  }

  private publishStatus(): SimStatus {
    const status = this.buildStatus()
    this.statuses.emit(status)
    return status
  }
}

export const playback = new PlaybackClient()
