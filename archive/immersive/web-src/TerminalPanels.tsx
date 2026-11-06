import { useMemo, useState } from 'react'
import { api } from './api'
import { formatSimTime } from './format'
import Icon from './Icons'
import { DEFENSE_COLORS } from './roles'
import type { Scenario, SimStatus } from './types'

const SPEEDS = [1, 10, 50, 100, 500]

const STATE_LABEL: Record<string, string> = { idle: 'oprit', running: 'rulează', paused: 'pauză', finished: 'terminat' }

export function StatsTerminal({
  status,
  speed,
  onSpeed,
  onRestart,
  connected,
  onClose,
}: {
  status: SimStatus | null
  speed: number
  onSpeed: (v: number) => void
  onRestart: () => Promise<void>
  connected: boolean
  onClose: () => void
}) {
  const [error, setError] = useState('')
  const state = status?.state ?? 'idle'
  const active = state === 'running' || state === 'paused'
  const run = async (fn: () => Promise<unknown>) => {
    setError('')
    try {
      await fn()
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e))
    }
  }

  return (
    <div className="modal-back" onClick={onClose}>
      <div className="modal narrow" onClick={(e) => e.stopPropagation()}>
        <div className="title">
          <span>
            Statistici · <strong>{STATE_LABEL[state] ?? state}</strong>
          </span>
          <button className="icon-btn" onClick={onClose} title="Închide (Esc)">
            <Icon name="close" size={18} />
          </button>
        </div>

        <div className="transport inline">
          <button className="t-btn primary" onClick={() => void run(onRestart)} disabled={!connected} title={active ? 'Repornește' : 'Start'}>
            <Icon name={active ? 'restart' : 'play'} size={20} />
          </button>
          {state === 'paused' ? (
            <button className="t-btn" onClick={() => void run(api.resume)} title="Reia">
              <Icon name="play" size={20} />
            </button>
          ) : (
            <button className="t-btn" onClick={() => void run(api.pause)} disabled={state !== 'running'} title="Pauză">
              <Icon name="pause" size={20} />
            </button>
          )}
          <button className="t-btn" onClick={() => void run(api.stop)} disabled={state === 'idle'} title="Stop">
            <Icon name="stop" size={20} />
          </button>
          <button className="t-btn" onClick={() => void run(api.save)} disabled={state !== 'paused' && state !== 'finished'} title="Salvează">
            <Icon name="save" size={20} />
          </button>
          <span className="t-sep" />
          <div className="speeds">
            {SPEEDS.map((v) => (
              <button key={v} className={speed === v ? 'on' : ''} onClick={() => onSpeed(v)}>
                {v}×
              </button>
            ))}
          </div>
        </div>
        {error && <div className="error">{error}</div>}

        {status && state !== 'idle' ? (
          <dl>
            <dt>Timp simulat</dt>
            <dd>{formatSimTime(status.simTimeS)}</dd>
            <dt>Drone total</dt>
            <dd>{status.totalDrones}</dd>
            <dt>În zbor</dt>
            <dd>{status.flying}</dd>
            <dt>Impact</dt>
            <dd>{status.impacted}</dd>
            <dt>Doborâte</dt>
            <dd>{status.intercepted}</dd>
            <dt>Bruiate</dt>
            <dd>{status.jammed}</dd>
            <dt>Căzute pe traseu</dt>
            <dd>{status.crashed}</dd>
            <dt>Detectate</dt>
            <dd>{status.detected}</dd>
            <dt>Tiruri</dt>
            <dd>
              {status.shotsFired} (rămase {status.magazineLeft})
            </dd>
            <dt>Încălcări</dt>
            <dd>{status.violations}</dd>
            <dt>Seed</dt>
            <dd>{status.seed}</dd>
          </dl>
        ) : (
          <div className="muted">Apasă Start pentru a rula scenariul.</div>
        )}
      </div>
    </div>
  )
}

export interface FocusTarget {
  name: string
  latDeg: number
  lonDeg: number
}

type ControlTab = 'defense' | 'sites' | 'targets'

const fold = (t: string) => t.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase()

/** Calculatorul liber: alegi ce urmareste ecranul mare. (Controlul efectiv al unitatilor vine intr-o etapa urmatoare.) */
export function ControlTerminal({
  scenario,
  focus,
  onFocus,
  onOverview,
  onClose,
}: {
  scenario: Scenario
  focus: string
  onFocus: (t: FocusTarget) => void
  onOverview: () => void
  onClose: () => void
}) {
  const [tab, setTab] = useState<ControlTab>('defense')
  const [q, setQ] = useState('')

  const items = useMemo(() => {
    const needle = fold(q.trim())
    const base: (FocusTarget & { id: string; color: string; sub: string })[] =
      tab === 'defense'
        ? scenario.defenses.map((d) => ({ id: d.id, name: d.name || d.id, latDeg: d.latDeg, lonDeg: d.lonDeg, color: DEFENSE_COLORS[d.kind], sub: d.kind }))
        : tab === 'sites'
          ? scenario.launchSites.map((s) => ({ id: s.id, name: s.name, latDeg: s.latDeg, lonDeg: s.lonDeg, color: '#1e90ff', sub: 'lansare' }))
          : scenario.targets.map((t) => ({ id: t.id, name: t.name, latDeg: t.latDeg, lonDeg: t.lonDeg, color: '#ff4500', sub: t.kind }))
    return (needle ? base.filter((x) => fold(x.name).includes(needle)) : base).slice(0, 300)
  }, [scenario, tab, q])

  return (
    <div className="modal-back" onClick={onClose}>
      <div className="modal" onClick={(e) => e.stopPropagation()}>
        <div className="title">
          <span>Control · ecranul mare{focus ? `: ${focus}` : ''}</span>
          <button className="icon-btn" onClick={onClose} title="Închide (Esc)">
            <Icon name="close" size={18} />
          </button>
        </div>
        <div className="muted small">Alege un sistem, un punct de lansare sau o țintă: ecranul mare zboară acolo. Controlul direct al unităților urmează.</div>
        <div className="tabs">
          {([['defense', `Apărare (${scenario.defenses.length})`], ['sites', `Lansări (${scenario.launchSites.length})`], ['targets', `Ținte (${scenario.targets.length})`]] as [ControlTab, string][]).map(([k, label]) => (
            <button key={k} className={tab === k ? 'on' : ''} onClick={() => setTab(k)}>
              {label}
            </button>
          ))}
          <span className="grow" />
          <button onClick={onOverview}>Vedere generală</button>
        </div>
        <label className="search">
          <Icon name="search" size={15} />
          <input value={q} onChange={(e) => setQ(e.target.value)} placeholder="caută…" autoFocus />
        </label>
        <div className="table-wrap">
          <ul className="items big">
            {items.map((x) => (
              <li key={x.id}>
                <button className="pick-item" onClick={() => onFocus(x)}>
                  <span>
                    <span className="swatch" style={{ background: x.color }} /> {x.name}
                  </span>
                  <span className="muted">{x.sub}</span>
                </button>
              </li>
            ))}
            {items.length === 0 && <li className="muted">Nimic găsit.</li>}
          </ul>
        </div>
      </div>
    </div>
  )
}
