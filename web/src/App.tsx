import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import i18n from './i18n'
import { api } from './api'
import { initialSetup, toPayload } from './draft'
import type { SetupState } from './draft'
import Compare from './Compare'
import Home from './Home'
import { useConnection, useSimStatus } from './hooks'
import Runs from './Runs'
import Setup from './Setup'
import SimView from './SimView'
import Gallery from './Gallery'
import { playback } from './playback'
import { splitVariantId } from './solar'
import type { Variant } from './solar'
import { SimClient } from './SimClient'
import type { Alliance, Catalog, PrecomputedInfo, Scenario, ScenarioInfo } from './types'

type View = 'home' | 'setup' | 'sim' | 'runs' | 'compare' | 'gallery'

/** Finds out the server mode (local / public) before creating the client: in public mode there is no SignalR hub. */
export default function App() {
  const { t } = useTranslation()
  const [mode, setMode] = useState<'loading' | 'local' | 'public'>('loading')

  useEffect(() => {
    api
      .mode()
      .then((m) => {
        playback.enabled = m === 'public'
        setMode(m)
      })
      .catch(() => setMode('local')) // old or stopped server: stay in local mode (the client keeps retrying the connection)
  }, [])

  if (mode === 'loading')
    return (
      <div className="home">
        <div className="home-inner muted">{t('common.loading')}</div>
      </div>
    )
  return <AppInner publicMode={mode === 'public'} />
}

function AppInner({ publicMode }: { publicMode: boolean }) {
  const [client] = useState<SimClient>(() => (publicMode ? playback : new SimClient()))
  const connection = useConnection(client)
  const status = useSimStatus(client)

  const [view, setView] = useState<View>('home')
  const [setup, setSetup] = useState<SetupState>(initialSetup)
  const [speed, setSpeed] = useState(100)
  // the scenario drawn in the simulation view + the replayed run, if any
  const [simScenario, setSimScenario] = useState<Scenario | null>(null)
  const [replayId, setReplayId] = useState<string | null>(null)

  const [scenarios, setScenarios] = useState<ScenarioInfo[]>([])
  const [catalog, setCatalog] = useState<Catalog | null>(null)
  const [alliances, setAlliances] = useState<Alliance[]>([])

  // static resources (scenarios, catalog, alliances) are reloaded when the server becomes available
  useEffect(() => {
    if (connection !== 'connected' || publicMode) return
    api.scenarios().then(setScenarios).catch(() => undefined)
    api.catalog().then(setCatalog).catch(() => undefined)
    api
      .alliances()
      .then((r) => setAlliances(r.alliances))
      .catch(() => undefined)
  }, [connection, publicMode])

  const startRun = async (seed: string, runSpeed: number) => {
    if (publicMode) {
      playback.restart()
      playback.setSpeed(runSpeed)
      return
    }
    if (replayId) await api.replay(replayId)
    else await api.startCustom(toPayload(setup), seed ? Number(seed) : null)
    await api.setSpeed(runSpeed)
  }

  const onReplay = async (runId: string, scenario: Scenario) => {
    await api.replay(runId)
    await api.setSpeed(speed)
    setReplayId(runId)
    setSimScenario(scenario)
    setView('sim')
  }

  const onStartFromSetup = async () => {
    const trimmed = setup.seed.trim()
    if (trimmed && !/^\d{1,15}$/.test(trimmed)) {
      throw new Error(i18n.t('errors.seed'))
    }
    setReplayId(null)
    await api.startCustom(toPayload(setup), trimmed ? Number(trimmed) : null)
    await api.setSpeed(speed)
    setSimScenario(setup.scenario)
    setView('sim')
  }

  // public mode: the open run may have a day / night sibling ("id.day" / "id.night"); the viewer can switch between them
  const [variantRuns, setVariantRuns] = useState<Partial<Record<Variant, PrecomputedInfo>>>({})
  const [currentVariant, setCurrentVariant] = useState<Variant | null>(null)
  const [switching, setSwitching] = useState<number | null>(null)

  const switchVariant = async (v: Variant) => {
    const target = variantRuns[v]
    if (!target || switching !== null || v === currentVariant) return
    const t = status?.simTimeS ?? 0
    const wasPaused = status?.state === 'paused'
    setSwitching(0)
    try {
      await playback.load(target, (fraction) => setSwitching(fraction))
      setSimScenario(await api.scenario(target.id))
      setCurrentVariant(v)
      playback.seek(t) // same moment of the raid, other time of day
      if (wasPaused) playback.pause()
    } catch {
      /* the previous run is still loaded if the download failed before playback.load replaced it */
    } finally {
      setSwitching(null)
    }
  }

  const onOpenPrecomputed = async (info: PrecomputedInfo) => {
    const scenario = await api.scenario(info.id)
    const { baseId, variant } = splitVariantId(info.id)
    setCurrentVariant(variant)
    setVariantRuns(variant ? { [variant]: info } : {})
    if (variant) {
      playback
        .list()
        .then((list) => {
          const siblings: Partial<Record<Variant, PrecomputedInfo>> = {}
          for (const it of list) {
            const sp = splitVariantId(it.id)
            if (sp.baseId === baseId && sp.variant) siblings[sp.variant] = it
          }
          setVariantRuns(siblings)
        })
        .catch(() => undefined)
    }
    playback.setSpeed(speed)
    setSimScenario(scenario)
    setView('sim')
  }

  if (view === 'gallery') {
    return <Gallery onOpen={onOpenPrecomputed} onBack={() => setView('home')} />
  }

  if (view === 'setup') {
    return (
      <Setup
        client={client}
        connected={connection === 'connected'}
        scenarios={scenarios}
        catalog={catalog}
        alliances={alliances}
        state={setup}
        setState={setSetup}
        onBack={() => setView('home')}
        onStart={onStartFromSetup}
      />
    )
  }

  if (view === 'runs') {
    return <Runs connected={connection === 'connected'} onBack={() => setView('home')} onReplay={onReplay} />
  }

  if (view === 'compare') {
    return (
      <Compare
        connected={connection === 'connected'}
        scenarios={scenarios}
        currentScenario={setup.scenario.waves.length > 0 ? toPayload(setup) : null}
        currentName={setup.scenario.name}
        onBack={() => setView('home')}
      />
    )
  }

  if (view === 'sim') {
    return (
      <SimView
        client={client}
        connection={connection}
        status={status}
        scenario={simScenario ?? setup.scenario}
        replayId={replayId}
        seed={replayId ? '' : setup.seed}
        speed={speed}
        onSpeed={setSpeed}
        onStart={startRun}
        onBack={() => setView(publicMode ? 'gallery' : replayId ? 'runs' : 'setup')}
        readOnly={publicMode}
        durationS={playback.durationS}
        dayNight={publicMode && currentVariant && Object.keys(variantRuns).length > 1 ? { current: currentVariant, available: Object.keys(variantRuns) as Variant[], busy: switching, onSwitch: (v) => void switchVariant(v) } : undefined}
        onSeek={(t) => void api.seek(t).catch(() => undefined)}
        onMenu={() => {
          void api.stop().catch(() => undefined)
          setReplayId(null)
          setView('home')
        }}
      />
    )
  }

  return (
    <Home
      connection={connection}
      status={status}
      onNormal={() => {
        setView('setup')
      }}
      onRuns={() => setView('runs')}
      onCompare={() => setView('compare')}
      onResume={() => setView('sim')}
      publicMode={publicMode}
      onGallery={() => setView('gallery')}
    />
  )
}
