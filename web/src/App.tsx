import { useEffect, useState } from 'react'
import { api } from './api'
import { initialSetup, toPayload } from './draft'
import type { SetupState } from './draft'
import Compare from './Compare'
import Home from './Home'
import ImmersiveView from './ImmersiveView'
import { useConnection, useSimStatus } from './hooks'
import Runs from './Runs'
import Setup from './Setup'
import SimView from './SimView'
import { SimClient } from './SimClient'
import type { Alliance, Catalog, Scenario, ScenarioInfo } from './types'

type View = 'home' | 'setup' | 'sim' | 'runs' | 'compare' | 'immersive'

export default function App() {
  const [client] = useState(() => new SimClient())
  const connection = useConnection(client)
  const status = useSimStatus(client)

  const [view, setView] = useState<View>('home')
  const [setup, setSetup] = useState<SetupState>(initialSetup)
  const [speed, setSpeed] = useState(100)
  // scenariul desenat in vizualizarea simularii + rularea reluata (replay), daca e cazul
  const [simScenario, setSimScenario] = useState<Scenario | null>(null)
  const [replayId, setReplayId] = useState<string | null>(null)
  // porneste din meniul Immersive: dupa configurare se intra in buncar, nu in vizualizarea 2D
  const [immersive, setImmersive] = useState(false)

  const [scenarios, setScenarios] = useState<ScenarioInfo[]>([])
  const [catalog, setCatalog] = useState<Catalog | null>(null)
  const [alliances, setAlliances] = useState<Alliance[]>([])

  // resursele statice (scenarii, catalog, aliante) se reincarca cand serverul devine disponibil
  useEffect(() => {
    if (connection !== 'connected') return
    api.scenarios().then(setScenarios).catch(() => undefined)
    api.catalog().then(setCatalog).catch(() => undefined)
    api
      .alliances()
      .then((r) => setAlliances(r.alliances))
      .catch(() => undefined)
  }, [connection])

  const startRun = async (seed: string, runSpeed: number) => {
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
      throw new Error('Seed-ul trebuie să fie un număr întreg pozitiv.')
    }
    setReplayId(null)
    await api.startCustom(toPayload(setup), trimmed ? Number(trimmed) : null)
    await api.setSpeed(speed)
    setSimScenario(setup.scenario)
    setView(immersive ? 'immersive' : 'sim')
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
        immersive={immersive}
      />
    )
  }

  if (view === 'immersive') {
    return (
      <ImmersiveView
        client={client}
        connection={connection}
        status={status}
        scenario={simScenario ?? setup.scenario}
        speed={speed}
        onSpeed={(v) => {
          setSpeed(v)
          void api.setSpeed(v).catch(() => undefined)
        }}
        onRestart={() => startRun(setup.seed.trim(), speed)}
        onMenu={() => {
          void api.stop().catch(() => undefined)
          setReplayId(null)
          setImmersive(false)
          setView('home')
        }}
        onTo2D={() => setView('sim')}
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
        onBack={() => setView(replayId ? 'runs' : immersive ? 'immersive' : 'setup')}
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
        setImmersive(false)
        setView('setup')
      }}
      onImmersive={() => {
        setImmersive(true)
        setView('setup')
      }}
      onRuns={() => setView('runs')}
      onCompare={() => setView('compare')}
      onResume={() => setView(immersive ? 'immersive' : 'sim')}
    />
  )
}
