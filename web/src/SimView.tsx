import { useEffect, useState } from 'react'
import { api } from './api'
import CesiumView from './CesiumView'
import EventsPanel from './EventsPanel'
import { formatSimTime } from './format'
import { useEventLog, useSummary } from './hooks'
import Icon from './Icons'
import type { IconName } from './Icons'
import ResultsModal from './ResultsModal'
import type { SimClient } from './SimClient'
import { DEFENSE_COLORS, DEFENSE_LABELS, ROLE_COLORS, ROLE_LABELS, UNIT_COLORS, UNIT_LABELS } from './roles'
import type { ConnectionState, CountryRole, DefenseKind, ReplayCheck, Scenario, SimStatus, UnitKind } from './types'

const SPEEDS = [1, 10, 50, 100, 500]

const CONNECTION_LABEL: Record<ConnectionState, string> = {
  connecting: 'se conectează…',
  connected: 'conectat',
  reconnecting: 'reconectare…',
  disconnected: 'server indisponibil',
}

const STATE_LABEL: Record<string, string> = {
  idle: 'oprit',
  running: 'rulează',
  paused: 'pauză',
  finished: 'terminat',
}

type PanelKey = 'stats' | 'legend' | 'events'
const PANELS_KEY = 'airsim.panels.v1'

function loadPanels(): Record<PanelKey, boolean> {
  const fallback = { stats: true, legend: true, events: true }
  try {
    const raw = localStorage.getItem(PANELS_KEY)
    return raw ? { ...fallback, ...(JSON.parse(raw) as Partial<Record<PanelKey, boolean>>) } : fallback
  } catch {
    return fallback
  }
}

interface Props {
  client: SimClient
  connection: ConnectionState
  status: SimStatus | null
  /** Scenariul rulat (afisat pe harta). */
  scenario: Scenario
  seed: string
  /** Rularea salvata reluata (replay), sau null la o simulare obisnuita. */
  replayId: string | null
  speed: number
  onSpeed: (value: number) => void
  /** Reporneste aceeasi configuratie (seed gol = automat). */
  onStart: (seed: string, speed: number) => Promise<void>
  /** Inapoi la configurare (simularea ramane cum e). */
  onBack: () => void
  /** Opreste simularea si intoarce la meniul principal. */
  onMenu: () => void
}

export default function SimView({ client, connection, status, scenario, seed: initialSeed, replayId, speed, onSpeed: setSpeed, onStart, onBack, onMenu }: Props) {
  const log = useEventLog(client, status?.epoch, 3000)
  const summary = useSummary(status)
  const [seed, setSeed] = useState(initialSeed)
  const [error, setError] = useState('')
  const [check, setCheck] = useState<ReplayCheck | null>(null)
  const [panels, setPanels] = useState(loadPanels)
  const [eventsLarge, setEventsLarge] = useState(false)
  // fereastra de rezultate: se deschide singura la final; alegerea utilizatorului tine pana la urmatoarea rulare
  const [resultsView, setResultsView] = useState<{ epoch: number; open: boolean } | null>(null)

  const state = status?.state ?? 'idle'
  const epoch = status?.epoch ?? -1
  const resultsOpen = resultsView?.epoch === epoch ? resultsView.open : state === 'finished'

  const setPanel = (key: PanelKey, value: boolean) =>
    setPanels((prev) => {
      const next = { ...prev, [key]: value }
      try {
        localStorage.setItem(PANELS_KEY, JSON.stringify(next))
      } catch {
        // fara stocare locala: panourile nu se retin intre sesiuni
      }
      return next
    })

  // la finalul unui replay, hash-ul log-ului se compara cu cel salvat
  useEffect(() => {
    if (!replayId || state !== 'finished') return
    let cancelled = false
    api
      .replayCheck()
      .then((c) => {
        if (!cancelled) setCheck(c)
      })
      .catch(() => undefined)
    return () => {
      cancelled = true
    }
  }, [replayId, state, status?.epoch])

  const run = async (action: () => Promise<unknown>) => {
    setError('')
    try {
      await action()
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e))
    }
  }

  const restart = () =>
    run(async () => {
      const trimmed = seed.trim()
      if (trimmed && !/^\d{1,15}$/.test(trimmed)) {
        throw new Error('Seed-ul trebuie să fie un număr întreg pozitiv.')
      }
      await onStart(trimmed, speed)
    })

  const onSpeed = (value: number) => {
    setSpeed(value)
    if (connection === 'connected') void run(() => api.setSpeed(value))
  }

  const connected = connection === 'connected'
  const canSave = state === 'paused' || state === 'finished'
  const progress = status && status.totalDrones > 0 ? ((status.impacted + status.crashed + status.jammed + status.intercepted) / status.totalDrones) * 100 : 0
  const active = state === 'running' || state === 'paused'

  const dock: { key: string; icon: IconName; title: string; on: boolean; toggle: () => void; disabled?: boolean }[] = [
    { key: 'stats', icon: 'chart', title: 'Statistici', on: panels.stats, toggle: () => setPanel('stats', !panels.stats) },
    { key: 'legend', icon: 'layers', title: 'Legendă', on: panels.legend, toggle: () => setPanel('legend', !panels.legend) },
    { key: 'events', icon: 'list', title: 'Evenimente', on: panels.events, toggle: () => setPanel('events', !panels.events) },
    {
      key: 'results',
      icon: 'clipboard',
      title: summary ? 'Rezultate' : 'Rezultatele apar la pauză sau la final',
      on: resultsOpen && summary !== null,
      toggle: () => setResultsView({ epoch, open: !resultsOpen }),
      disabled: summary === null,
    },
  ]

  return (
    <div className="app">
      <CesiumView client={client} scenario={scenario} fitKey="sim" />

      <aside className="panel topbar">
        <div className="title">
          <span>
            AirSim <span className={`badge ${connected ? 'ok' : 'warn'}`}>{CONNECTION_LABEL[connection]}</span>
          </span>
        </div>
        <div className="nav">
          <button onClick={onMenu} title="Meniu principal (oprește simularea)">
            <Icon name="home" /> Meniu principal
          </button>
          <button onClick={onBack} title="Înapoi la configurare">
            <Icon name="sliders" /> Configurare
          </button>
        </div>
        <div className="muted scenario-name">{scenario.name}</div>
        {replayId && <div className="replay-badge">Replay: {replayId}</div>}
        <label className="field seed">
          Seed
          <input value={seed} onChange={(e) => setSeed(e.target.value)} placeholder="automat" inputMode="numeric" disabled={replayId !== null} />
        </label>
        {error && <div className="error">{error}</div>}

        {panels.legend && (
          <div className="legend-block">
            <div className="legend-head">
              <span>Legendă</span>
              <button className="icon-btn" onClick={() => setPanel('legend', false)} title="Ascunde legenda">
                <Icon name="close" size={14} />
              </button>
            </div>
            {scenario.waves?.length > 0 && (
              <ul className="legend">
                {([...new Set(scenario.waves.map((w) => w.kind))] as UnitKind[]).map((kind) => (
                  <li key={kind}>
                    <span className="swatch" style={{ background: UNIT_COLORS[kind] }} />
                    {UNIT_LABELS[kind]}
                  </li>
                ))}
              </ul>
            )}
            {scenario.defenses?.length > 0 && (
              <ul className="legend">
                {([...new Set(scenario.defenses.map((d) => d.kind))] as DefenseKind[]).map((kind) => (
                  <li key={kind}>
                    <span className="swatch" style={{ background: DEFENSE_COLORS[kind] }} />
                    {DEFENSE_LABELS[kind]}
                  </li>
                ))}
              </ul>
            )}
            {scenario.airspaceMap && (
              <ul className="legend">
                {(Object.keys(ROLE_COLORS) as CountryRole[]).map((role) => (
                  <li key={role}>
                    <span className="swatch" style={{ background: ROLE_COLORS[role] }} />
                    {ROLE_LABELS[role]}
                  </li>
                ))}
                <li>
                  <span className="swatch violation" />
                  Încălcare spațiu aerian
                </li>
              </ul>
            )}
          </div>
        )}
      </aside>

      <div className="dock">
        {dock.map((d) => (
          <button key={d.key} className={`dock-btn ${d.on ? 'on' : ''}`} onClick={d.toggle} disabled={d.disabled} title={d.title}>
            <Icon name={d.icon} size={20} />
          </button>
        ))}
      </div>

      {panels.stats && (
        <aside className="panel stats">
          <div className="title">
            <span>
              Stare: <strong>{STATE_LABEL[state] ?? state}</strong>
            </span>
            <button className="icon-btn" onClick={() => setPanel('stats', false)} title="Ascunde">
              <Icon name="close" size={14} />
            </button>
          </div>
          {status && state !== 'idle' ? (
            <>
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
                {status.jammed > 0 && (
                  <>
                    <dt>Bruiate</dt>
                    <dd>{status.jammed}</dd>
                  </>
                )}
                {status.crashed > 0 && (
                  <>
                    <dt>Căzute pe traseu</dt>
                    <dd>{status.crashed}</dd>
                  </>
                )}
                {status.interceptorsLaunched > 0 && (
                  <>
                    <dt>Interceptoare</dt>
                    <dd>
                      {status.interceptorsLaunched} (pierdute {status.interceptorsLost})
                    </dd>
                  </>
                )}
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
              <div className="bar" title={`${progress.toFixed(0)}% au lovit`}>
                <div style={{ width: `${progress}%` }} />
              </div>
              {status.savedRunId && <div className="saved">salvat: runs/{status.savedRunId}</div>}
              {replayId && state === 'finished' && check?.active && check.finished && (
                <div className={check.match ? 'saved' : 'error'}>
                  {check.match ? 'Replay verificat: log identic cu cel salvat ✓' : 'Replay diferit de rularea salvată (altă versiune de motor sau scenariu modificat)'}
                </div>
              )}
            </>
          ) : (
            <div className="muted">Apasă Start pentru a rula configurația.</div>
          )}
        </aside>
      )}

      {panels.events && !eventsLarge && (
        <aside className="panel log">
          <EventsPanel events={log} onExpand={() => setEventsLarge(true)} onClose={() => setPanel('events', false)} />
        </aside>
      )}

      <div className="panel transport">
        <button className="t-btn primary" onClick={restart} disabled={!connected} title={active ? 'Repornește' : 'Start'}>
          <Icon name={active ? 'restart' : 'play'} size={20} />
        </button>
        {state === 'paused' ? (
          <button className="t-btn" onClick={() => run(api.resume)} title="Reia">
            <Icon name="play" size={20} />
          </button>
        ) : (
          <button className="t-btn" onClick={() => run(api.pause)} disabled={state !== 'running'} title="Pauză">
            <Icon name="pause" size={20} />
          </button>
        )}
        <button className="t-btn" onClick={() => run(api.stop)} disabled={state === 'idle'} title="Stop">
          <Icon name="stop" size={20} />
        </button>
        <button className="t-btn" onClick={() => run(api.save)} disabled={!canSave} title="Salvează rularea">
          <Icon name="save" size={20} />
        </button>
        <span className="t-sep" />
        <div className="speeds" title="Viteza simulării">
          {SPEEDS.map((v) => (
            <button key={v} className={speed === v ? 'on' : ''} onClick={() => onSpeed(v)}>
              {v}×
            </button>
          ))}
        </div>
        {status && state !== 'idle' && <span className="t-time">{formatSimTime(status.simTimeS)}</span>}
      </div>

      {eventsLarge && (
        <div className="modal-back" onClick={() => setEventsLarge(false)}>
          <div className="modal" onClick={(e) => e.stopPropagation()}>
            <EventsPanel events={log} large onClose={() => setEventsLarge(false)} />
          </div>
        </div>
      )}

      {resultsOpen && summary && <ResultsModal summary={summary} status={status} partial={state !== 'finished'} onClose={() => setResultsView({ epoch, open: false })} />}
    </div>
  )
}
