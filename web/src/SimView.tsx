import { useEffect, useState } from 'react'
import { Trans, useTranslation } from 'react-i18next'
import { localName } from './i18n/dataNames'
import { api } from './api'
import CesiumView from './CesiumView'
import EventsPanel from './EventsPanel'
import { formatSimTime } from './format'
import { isNarrowNow, useEventLog, useMediaQuery, useSummary } from './hooks'
import LanguageSwitch from './i18n/LanguageSwitch'
import Icon from './Icons'
import type { IconName } from './Icons'
import ResultsModal from './ResultsModal'
import type { SimClient } from './SimClient'
import { VARIANTS, scenarioStartMs } from './solar'
import type { Variant } from './solar'
import { DEFENSE_COLORS, ROLE_COLORS, UNIT_COLORS, defenseLabel, roleLabel, unitLabel } from './roles'
import type { ConnectionState, CountryRole, DefenseKind, ReplayCheck, Scenario, SimStatus, UnitKind } from './types'

const SPEEDS = [1, 10, 50, 100, 500]

type PanelKey = 'stats' | 'legend' | 'events'
const PANELS_KEY = 'airsim.panels.v1'

function loadPanels(): Record<PanelKey, boolean> {
  // phone: everything starts closed (the panels share one bottom sheet); the stored desktop choice is not used
  if (isNarrowNow()) return { stats: false, legend: false, events: false }
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
  /** The scenario being run (shown on the map). */
  scenario: Scenario
  seed: string
  /** The replayed saved run, or null for a regular simulation. */
  replayId: string | null
  speed: number
  onSpeed: (value: number) => void
  /** Restarts the same configuration (empty seed = automatic). */
  onStart: (seed: string, speed: number) => Promise<void>
  /** Back to setup (the simulation stays as it is). */
  onBack: () => void
  /** Stops the simulation and returns to the main menu. */
  onMenu: () => void
  /** Public mode: the same raid in another time of day (precomputed sibling run), keeping the moment. */
  dayNight?: { current: Variant; available: Variant[]; busy: number | null; onSwitch: (v: Variant) => void }
  /** Public mode: precomputed run (no seed, no saving), with a seek bar. */
  readOnly?: boolean
  durationS?: number
  onSeek?: (seconds: number) => void
}

export default function SimView({ client, connection, status, scenario, seed: initialSeed, replayId, speed, onSpeed: setSpeed, onStart, onBack, onMenu, readOnly = false, durationS = 0, onSeek, dayNight }: Props) {
  const { t } = useTranslation()
  const narrow = useMediaQuery()
  const [topOpen, setTopOpen] = useState(false)
  const hasTime = scenarioStartMs(scenario) !== null
  const [sunLight, setSunLight] = useState(() => {
    try {
      return localStorage.getItem('airsim.sun') !== '0'
    } catch {
      return true
    }
  })
  const [altFade, setAltFade] = useState(() => {
    try {
      return localStorage.getItem('airsim.altfade') !== '0'
    } catch {
      return true
    }
  })
  const toggleAltFade = () => {
    setAltFade((v) => {
      try {
        localStorage.setItem('airsim.altfade', v ? '0' : '1')
      } catch {
        /* only a convenience */
      }
      return !v
    })
  }
  const toggleSun = () => {
    setSunLight((v) => {
      try {
        localStorage.setItem('airsim.sun', v ? '0' : '1')
      } catch {
        /* private mode etc.: only a convenience */
      }
      return !v
    })
  }
  const log = useEventLog(client, status?.epoch, 3000)
  const summary = useSummary(status)
  const [seed, setSeed] = useState(initialSeed)
  const [scrub, setScrub] = useState<number | null>(null)
  const [error, setError] = useState('')
  const [check, setCheck] = useState<ReplayCheck | null>(null)
  const [panels, setPanels] = useState(loadPanels)
  const [eventsLarge, setEventsLarge] = useState(false)
  // results window: opens on its own at the end; the user's choice holds until the next run
  const [resultsView, setResultsView] = useState<{ epoch: number; open: boolean } | null>(null)

  const state = status?.state ?? 'idle'
  const epoch = status?.epoch ?? -1
  const resultsOpen = resultsView?.epoch === epoch ? resultsView.open : state === 'finished'

  const setPanel = (key: PanelKey, value: boolean) =>
    setPanels((prev) => {
      // phone: one panel at a time, and nothing is persisted
      if (narrow) return { stats: false, legend: false, events: false, [key]: value }
      const next = { ...prev, [key]: value }
      try {
        localStorage.setItem(PANELS_KEY, JSON.stringify(next))
      } catch {
        // no local storage: the panels are not remembered between sessions
      }
      return next
    })

  // at the end of a replay, the log hash is compared with the saved one
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
        throw new Error(t('sim.errSeed'))
      }
      await onStart(trimmed, speed)
    })

  const onSpeed = (value: number) => {
    setSpeed(value)
    if (connection === 'connected') void run(() => api.setSpeed(value))
  }

  const commitSeek = () => {
    if (scrub !== null) onSeek?.(scrub)
    setScrub(null)
  }

  const connected = connection === 'connected'
  const canSave = state === 'paused' || state === 'finished'
  const progress = status && status.totalDrones > 0 ? ((status.impacted + status.crashed + status.jammed + status.intercepted) / status.totalDrones) * 100 : 0
  const active = state === 'running' || state === 'paused'

  const dock: { key: string; icon: IconName; title: string; on: boolean; toggle: () => void; disabled?: boolean }[] = [
    { key: 'stats', icon: 'chart', title: t('sim.dock.stats'), on: panels.stats, toggle: () => setPanel('stats', !panels.stats) },
    { key: 'legend', icon: 'layers', title: t('sim.dock.legend'), on: panels.legend, toggle: () => setPanel('legend', !panels.legend) },
    { key: 'events', icon: 'list', title: t('sim.dock.events'), on: panels.events, toggle: () => setPanel('events', !panels.events) },
    {
      key: 'results',
      icon: 'clipboard',
      title: summary ? t('sim.dock.results') : t('sim.dock.resultsWait'),
      on: resultsOpen && summary !== null,
      toggle: () => setResultsView({ epoch, open: !resultsOpen }),
      disabled: summary === null,
    },
    { key: 'altfade', icon: 'fade' as IconName, title: t('sim.dock.altFade'), on: altFade, toggle: toggleAltFade },
    ...(hasTime
      ? [{ key: 'sun', icon: (sunLight ? 'sun' : 'moon') as IconName, title: t('sim.dock.lighting'), on: sunLight, toggle: toggleSun }]
      : []),
  ]

  return (
    <div className="app">
      <CesiumView client={client} scenario={scenario} fitKey="sim" lighting={sunLight} altFade={altFade} />

      <aside className="panel topbar">
        <div className="title">
          <span>
            AirSim <span className={`badge ${connected ? 'ok' : 'warn'}`}>{t(`connection.${connection}`)}</span>
          </span>
          <span className="title-actions">
            <LanguageSwitch />
            <button className="icon-btn topbar-toggle" onClick={() => setTopOpen((o) => !o)} aria-expanded={topOpen} title={t('sim.more')}>
              <Icon name={topOpen ? 'close' : 'sliders'} size={18} />
            </button>
          </span>
        </div>
        <div className={`topbar-more ${topOpen ? 'open' : ''}`}>
        <div className="nav">
          <button onClick={onMenu} title={t('sim.mainMenuTip')}>
            <Icon name="home" /> {t('sim.mainMenu')}
          </button>
          <button onClick={onBack} title={t('sim.backSetup')}>
            <Icon name="sliders" /> {readOnly ? t('sim.scenarios') : t('sim.setup')}
          </button>
        </div>
        <div className="muted scenario-name">{localName('scenario', scenario.name)}</div>
        {replayId && <div className="replay-badge">{t('sim.replay', { id: replayId })}</div>}
        {!readOnly && (
        <label className="field seed">
          {t('sim.seed')}
          <input value={seed} onChange={(e) => setSeed(e.target.value)} placeholder={t('sim.auto')} inputMode="numeric" disabled={replayId !== null} />
        </label>
        )}
        {error && <div className="error">{error}</div>}
        </div>

        {panels.legend && (
          <div className="legend-block">
            <div className="legend-head">
              <span>{t('sim.legend')}</span>
              <button className="icon-btn" onClick={() => setPanel('legend', false)} title={t('sim.hideLegend')}>
                <Icon name="close" size={14} />
              </button>
            </div>
            {scenario.waves?.length > 0 && (
              <ul className="legend">
                {([...new Set(scenario.waves.map((w) => w.kind))] as UnitKind[]).map((kind) => (
                  <li key={kind}>
                    <span className="swatch" style={{ background: UNIT_COLORS[kind] }} />
                    {unitLabel(kind)}
                  </li>
                ))}
              </ul>
            )}
            {scenario.defenses?.length > 0 && (
              <ul className="legend">
                {([...new Set(scenario.defenses.map((d) => d.kind))] as DefenseKind[]).map((kind) => (
                  <li key={kind}>
                    <span className="swatch" style={{ background: DEFENSE_COLORS[kind] }} />
                    {defenseLabel(kind)}
                  </li>
                ))}
              </ul>
            )}
            {scenario.airspaceMap && (
              <ul className="legend">
                {(Object.keys(ROLE_COLORS) as CountryRole[]).map((role) => (
                  <li key={role}>
                    <span className="swatch" style={{ background: ROLE_COLORS[role] }} />
                    {roleLabel(role)}
                  </li>
                ))}
                <li>
                  <span className="swatch violation" />
                  {t('sim.violation')}
                </li>
              </ul>
            )}
          </div>
        )}
      </aside>

      <div className="dock">
        {dayNight && (
          <div className="variant-seg">
            {VARIANTS.map((v) => (
              <button
                key={v}
                className={dayNight.current === v ? 'on' : ''}
                disabled={dayNight.busy !== null || !dayNight.available.includes(v)}
                onClick={() => dayNight.onSwitch(v)}
                title={t(`gallery.${v}`)}
              >
                <Icon name={v === 'day' ? 'sun' : 'moon'} size={16} />
                <span className="vs-label">
                  {dayNight.busy !== null && dayNight.current !== v && dayNight.available.includes(v) ? `${Math.round(dayNight.busy * 100)}%` : t(`gallery.${v}`)}
                </span>
              </button>
            ))}
          </div>
        )}
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
              <Trans i18nKey="sim.stateLabel" values={{ state: t(`sim.state.${state}`) }} components={{ b: <strong /> }} />
            </span>
            <button className="icon-btn" onClick={() => setPanel('stats', false)} title={t('sim.hide')}>
              <Icon name="close" size={14} />
            </button>
          </div>
          {status && state !== 'idle' ? (
            <>
              <dl>
                <dt>{t('sim.time')}</dt>
                <dd>{formatSimTime(status.simTimeS)}</dd>
                <dt>{t('sim.dronesTotal')}</dt>
                <dd>{status.totalDrones}</dd>
                <dt>{t('sim.flying')}</dt>
                <dd>{status.flying}</dd>
                <dt>{t('sim.impact')}</dt>
                <dd>{status.impacted}</dd>
                <dt>{t('sim.intercepted')}</dt>
                <dd>{status.intercepted}</dd>
                {status.jammed > 0 && (
                  <>
                    <dt>{t('sim.jammed')}</dt>
                    <dd>{status.jammed}</dd>
                  </>
                )}
                {status.crashed > 0 && (
                  <>
                    <dt>{t('sim.crashed')}</dt>
                    <dd>{status.crashed}</dd>
                  </>
                )}
                {status.interceptorsLaunched > 0 && (
                  <>
                    <dt>{t('sim.interceptors')}</dt>
                    <dd>
                      {t('sim.interceptorsVal', { launched: status.interceptorsLaunched, lost: status.interceptorsLost })}
                    </dd>
                  </>
                )}
                <dt>{t('sim.detected')}</dt>
                <dd>{status.detected}</dd>
                <dt>{t('sim.shots')}</dt>
                <dd>
                  {t('sim.shotsVal', { fired: status.shotsFired, left: status.magazineLeft })}
                </dd>
                <dt>{t('sim.violations')}</dt>
                <dd>{status.violations}</dd>
                <dt>Seed</dt>
                <dd>{status.seed}</dd>
              </dl>
              <div className="bar" title={t('sim.hitPct', { pct: progress.toFixed(0) })}>
                <div style={{ width: `${progress}%` }} />
              </div>
              {status.savedRunId && <div className="saved">{t('sim.saved', { id: status.savedRunId })}</div>}
              {replayId && state === 'finished' && check?.active && check.finished && (
                <div className={check.match ? 'saved' : 'error'}>
                  {check.match ? t('sim.replayOk') : t('sim.replayDiff')}
                </div>
              )}
            </>
          ) : (
            <div className="muted">{t('sim.pressStart')}</div>
          )}
        </aside>
      )}

      {panels.events && !eventsLarge && (
        <aside className="panel log">
          <EventsPanel events={log} onExpand={() => setEventsLarge(true)} onClose={() => setPanel('events', false)} />
        </aside>
      )}

      <div className="panel transport">
        <button className="t-btn primary" onClick={restart} disabled={!connected} title={active ? t('sim.restart') : t('sim.start')}>
          <Icon name={active ? 'restart' : 'play'} size={20} />
        </button>
        {state === 'paused' ? (
          <button className="t-btn" onClick={() => run(api.resume)} title={t('sim.resume')}>
            <Icon name="play" size={20} />
          </button>
        ) : (
          <button className="t-btn" onClick={() => run(api.pause)} disabled={state !== 'running'} title={t('sim.pause')}>
            <Icon name="pause" size={20} />
          </button>
        )}
        <button className="t-btn" onClick={() => run(api.stop)} disabled={state === 'idle'} title="Stop">
          <Icon name="stop" size={20} />
        </button>
        {!readOnly && (
          <button className="t-btn" onClick={() => run(api.save)} disabled={!canSave} title={t('sim.saveRun')}>
            <Icon name="save" size={20} />
          </button>
        )}
        <span className="t-sep" />
        <div className="speeds" title={t('sim.speed')}>
          {SPEEDS.map((v) => (
            <button key={v} className={speed === v ? 'on' : ''} onClick={() => onSpeed(v)}>
              {v}×
            </button>
          ))}
        </div>
        {readOnly && status && state !== 'idle' && durationS > 0 && (
          <input
            type="range"
            className="t-seek"
            min={0}
            max={Math.round(durationS)}
            value={scrub ?? Math.round(status.simTimeS)}
            title={t('sim.seek')}
            onChange={(e) => setScrub(Number(e.target.value))}
            onPointerUp={commitSeek}
            onKeyUp={commitSeek}
          />
        )}
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
