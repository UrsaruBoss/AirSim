import { useEffect, useRef, useState } from 'react'
import { api } from './api'
import type { CompareJob, CompareSide, ComparisonRow, ScenarioInfo } from './types'

const CURRENT = '__current__'

interface Props {
  connected: boolean
  scenarios: ScenarioInfo[]
  /** Configurarea din editor (null daca nu exista una), trimisa ca scenariu custom. */
  currentScenario: unknown | null
  currentName: string
  onBack: () => void
}

function fmt(v: number): string {
  const a = Math.abs(v)
  if (a >= 1000) return Math.round(v).toLocaleString('ro-RO')
  if (a >= 100) return v.toFixed(0)
  if (a >= 10) return v.toFixed(1)
  return v.toFixed(2)
}

function fmtDiff(r: ComparisonRow): string {
  const sign = r.diff > 0 ? '+' : ''
  return sign + fmt(r.diff)
}

export default function Compare({ connected, scenarios, currentScenario, currentName, onBack }: Props) {
  const visible = scenarios.filter((s) => s.valid && !s.id.startsWith('test'))
  const [aChoice, setA] = useState('')
  const [noRetA, setNoRetA] = useState(false)
  const [noRetB, setNoRetB] = useState(false)
  const [b, setB] = useState(currentScenario ? CURRENT : '')
  const [runs, setRuns] = useState(10)
  const [seed, setSeed] = useState('')
  const [job, setJob] = useState<CompareJob | null>(null)
  const [error, setError] = useState('')
  const timer = useRef<number | null>(null)

  useEffect(
    () => () => {
      if (timer.current !== null) window.clearTimeout(timer.current)
    },
    [],
  )

  const a = aChoice || visible[0]?.id || ''

  const side = (v: string, noRetarget: boolean): CompareSide => ({ ...(v === CURRENT ? { scenario: currentScenario } : { id: v }), noRetarget })

  const poll = (id: string) => {
    timer.current = window.setTimeout(async () => {
      try {
        const j = await api.compare(id)
        setJob(j)
        if (j.state === 'running') poll(id)
      } catch (e) {
        setError(e instanceof Error ? e.message : String(e))
      }
    }, 1500)
  }

  const start = async () => {
    setError('')
    setJob(null)
    const trimmed = seed.trim()
    if (trimmed && !/^\d{1,15}$/.test(trimmed)) return setError('Seed-ul trebuie să fie un număr întreg pozitiv.')
    if (!Number.isInteger(runs) || runs < 1 || runs > 100) return setError('Numărul de rulări trebuie să fie între 1 și 100.')
    if (!a || !b) return setError('Alege ambele scenarii.')
    try {
      const j = await api.compareStart(side(a, noRetA), side(b, noRetB), runs, trimmed ? Number(trimmed) : null)
      setJob(j)
      poll(j.id)
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e))
    }
  }

  const options = (
    <>
      {currentScenario !== null && <option value={CURRENT}>Configurarea din editor ({currentName})</option>}
      {visible.map((s) => (
        <option key={s.id} value={s.id}>
          {s.name}
        </option>
      ))}
    </>
  )

  const running = job?.state === 'running'
  const pct = job && job.total > 0 ? (job.done / job.total) * 100 : 0

  return (
    <div className="page">
      <div className="page-inner">
        <div className="page-head">
          <button className="back" onClick={onBack}>
            ← Meniu
          </button>
          <h2>Compară scenarii</h2>
        </div>
        <p className="muted small">
          Ambele scenarii rulează cu aceleași seed-uri, iar diferența se calculează pe perechi (același seed în A și în B). Z = diferența medie / eroarea
          standard; |Z| &gt; 2 înseamnă o diferență probabil reală. Pentru o variantă modificată: ajustează în editor, apoi alege „Configurarea din
          editor” ca B.
        </p>

        <div className="two">
          <label className="field">
            Scenariu A
            <select value={a} onChange={(e) => setA(e.target.value)} disabled={running}>
              {options}
            </select>
            <span className="check">
              <input type="checkbox" checked={noRetA} onChange={(e) => setNoRetA(e.target.checked)} disabled={running} /> fără redirecționare în zbor
            </span>
          </label>
          <label className="field">
            Scenariu B
            <select value={b} onChange={(e) => setB(e.target.value)} disabled={running}>
              <option value="">— alege —</option>
              {options}
            </select>
            <span className="check">
              <input type="checkbox" checked={noRetB} onChange={(e) => setNoRetB(e.target.checked)} disabled={running} /> fără redirecționare în zbor
            </span>
          </label>
          <label className="field">
            Rulări per scenariu (max 100)
            <input type="number" min={1} max={100} value={runs} onChange={(e) => setRuns(Number(e.target.value))} disabled={running} />
          </label>
          <label className="field">
            Seed de bază
            <input value={seed} onChange={(e) => setSeed(e.target.value)} placeholder="automat" inputMode="numeric" disabled={running} />
          </label>
        </div>

        <button className="primary" onClick={() => void start()} disabled={!connected || running}>
          {running ? 'Rulează…' : '▶ Compară'}
        </button>
        <div className="muted small">O rulare mare durează minute (RU–UA: câteva minute per rulare). Folosește 5–10 rulări pentru o primă idee.</div>

        {error && <div className="error">{error}</div>}
        {job?.state === 'error' && <div className="error">{job.error}</div>}

        {job && job.state === 'running' && (
          <div className="progress">
            <div className="progress-bar" style={{ width: `${pct}%` }} />
            <span className="small">
              {job.done} / {job.total} rulări
            </span>
          </div>
        )}

        {job?.state === 'done' && job.rows && (
          <>
            <div className="muted small">
              A: <strong>{job.nameA}</strong> · B: <strong>{job.nameB}</strong> · {job.runs} rulări · seed {job.seed}..{Number(job.seed) + job.runs - 1} · motor{' '}
              {job.engineVersion}
            </div>
            <table className="cmp">
              <thead>
                <tr>
                  <th>Metrică</th>
                  <th>A</th>
                  <th>B</th>
                  <th>B − A</th>
                  <th>Z</th>
                </tr>
              </thead>
              <tbody>
                {job.rows.map((r) => (
                  <tr key={r.name} className={r.significant ? 'sig' : ''}>
                    <td>{r.name.trim()}</td>
                    <td>{fmt(r.meanA)}</td>
                    <td>{fmt(r.meanB)}</td>
                    <td>{fmtDiff(r)}</td>
                    <td>
                      {r.z.toFixed(1)} {r.significant ? '●' : ''}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
            <div className="muted small">● = diferență semnificativă (|Z| &gt; 2). Valorile sunt medii peste rulări.</div>
          </>
        )}
      </div>
    </div>
  )
}
