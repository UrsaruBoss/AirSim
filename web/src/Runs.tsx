import { useEffect, useState } from 'react'
import { api } from './api'
import type { SavedRun, Scenario } from './types'

interface Props {
  connected: boolean
  onBack: () => void
  /** Porneste replay-ul; scenariul e necesar ca sa fie desenat pe harta. */
  onReplay: (runId: string, scenario: Scenario) => Promise<void>
}

export default function Runs({ connected, onBack, onReplay }: Props) {
  const [runs, setRuns] = useState<SavedRun[] | null>(null)
  const [busy, setBusy] = useState('')
  const [error, setError] = useState('')

  useEffect(() => {
    if (!connected) return
    api
      .runs()
      .then(setRuns)
      .catch((e) => setError(e instanceof Error ? e.message : String(e)))
  }, [connected])

  const replay = async (run: SavedRun) => {
    setBusy(run.runId)
    setError('')
    try {
      const scenario = await api.runScenario(run.runId)
      await onReplay(run.runId, scenario)
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e))
      setBusy('')
    }
  }

  return (
    <div className="page">
      <div className="page-inner">
        <div className="page-head">
          <button className="back" onClick={onBack}>
            ← Meniu
          </button>
          <h2>Rulări salvate</h2>
        </div>
        <p className="muted small">
          Replay pornește din nou același scenariu cu același seed. Motorul e determinist, deci desfășurarea e identică; la final se
          verifică hash-ul log-ului față de cel salvat.
        </p>

        {error && <div className="error">{error}</div>}
        {runs === null && !error && <div className="muted">Se încarcă…</div>}
        {runs !== null && runs.length === 0 && (
          <div className="muted">Nicio rulare salvată încă. O rulare se salvează automat la final sau cu butonul „Salvează”.</div>
        )}

        <ul className="runs">
          {runs?.map((r) => (
            <li key={r.runId}>
              <div className="grow">
                <strong>{r.scenarioName || r.runId}</strong>
                <div className="muted small">
                  seed {r.seed} · {new Date(r.createdUtc).toLocaleString('ro-RO')} · motor {r.engineVersion}
                </div>
                {!r.engineMatches && <div className="warn-text small">Versiune de motor diferită: rezultatul poate diferi de cel salvat.</div>}
              </div>
              <button className="primary" disabled={!connected || busy !== ''} onClick={() => void replay(r)}>
                {busy === r.runId ? 'Pornește…' : '▶ Replay'}
              </button>
            </li>
          ))}
        </ul>
      </div>
    </div>
  )
}
