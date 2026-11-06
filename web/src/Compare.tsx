import { useEffect, useRef, useState } from 'react'
import { Trans, useTranslation } from 'react-i18next'
import { localName } from './i18n/dataNames'
import { api } from './api'
import { currentLocale } from './i18n'
import LanguageSwitch from './i18n/LanguageSwitch'
import type { CompareJob, CompareSide, ComparisonRow, ScenarioInfo } from './types'

const CURRENT = '__current__'

interface Props {
  connected: boolean
  scenarios: ScenarioInfo[]
  /** The configuration from the editor (null if there is none), sent as a custom scenario. */
  currentScenario: unknown | null
  currentName: string
  onBack: () => void
}

function fmt(v: number): string {
  const a = Math.abs(v)
  if (a >= 1000) return Math.round(v).toLocaleString(currentLocale())
  if (a >= 100) return v.toFixed(0)
  if (a >= 10) return v.toFixed(1)
  return v.toFixed(2)
}

function fmtDiff(r: ComparisonRow): string {
  const sign = r.diff > 0 ? '+' : ''
  return sign + fmt(r.diff)
}

export default function Compare({ connected, scenarios, currentScenario, currentName, onBack }: Props) {
  const { t } = useTranslation()
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
    if (trimmed && !/^\d{1,15}$/.test(trimmed)) return setError(t('compare.errSeed'))
    if (!Number.isInteger(runs) || runs < 1 || runs > 100) return setError(t('compare.errRuns'))
    if (!a || !b) return setError(t('compare.errPick'))
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
      {currentScenario !== null && <option value={CURRENT}>{t('compare.currentNamed', { name: currentName })}</option>}
      {visible.map((s) => (
        <option key={s.id} value={s.id}>
          {localName('scenario', s.name)}
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
            {t('common.menu')}
          </button>
          <h2>{t('compare.title')}</h2>
          <LanguageSwitch />
        </div>
        <p className="muted small">{t('compare.intro')}</p>

        <div className="cmp-sides">
          <div className="cmp-side">
            <label className="field">
              {t('compare.scenarioA')}
              <select value={a} onChange={(e) => setA(e.target.value)} disabled={running}>
                {options}
              </select>
            </label>
            <label className="check">
              <input type="checkbox" checked={noRetA} onChange={(e) => setNoRetA(e.target.checked)} disabled={running} /> {t('compare.noRetarget')}
            </label>
          </div>
          <div className="cmp-side">
            <label className="field">
              {t('compare.scenarioB')}
              <select value={b} onChange={(e) => setB(e.target.value)} disabled={running}>
                <option value="">{t('compare.pick')}</option>
                {options}
              </select>
            </label>
            <label className="check">
              <input type="checkbox" checked={noRetB} onChange={(e) => setNoRetB(e.target.checked)} disabled={running} /> {t('compare.noRetarget')}
            </label>
          </div>
        </div>

        <div className="cmp-run">
          <label className="field">
            {t('compare.runsPer')}
            <input type="number" min={1} max={100} value={runs} onChange={(e) => setRuns(Number(e.target.value))} disabled={running} />
          </label>
          <label className="field">
            {t('compare.baseSeed')}
            <input value={seed} onChange={(e) => setSeed(e.target.value)} placeholder={t('compare.auto')} inputMode="numeric" disabled={running} />
          </label>
          <button className="primary" onClick={() => void start()} disabled={!connected || running}>
            {running ? t('compare.running') : t('compare.start')}
          </button>
        </div>
        <div className="muted small">{t('compare.hint')}</div>

        {error && <div className="error">{error}</div>}
        {job?.state === 'error' && <div className="error">{job.error}</div>}

        {job && job.state === 'running' && (
          <div className="progress">
            <div className="progress-bar" style={{ width: `${pct}%` }} />
            <span className="small">{t('compare.progress', { done: job.done, total: job.total })}</span>
          </div>
        )}

        {job?.state === 'done' && job.rows && (
          <>
            <div className="muted small">
              <Trans
                i18nKey="compare.summary"
                values={{ nameA: localName('scenario', job.nameA), nameB: localName('scenario', job.nameB), runs: job.runs, from: job.seed, to: Number(job.seed) + job.runs - 1, engine: job.engineVersion }}
                components={{ b: <strong /> }}
              />
            </div>
            <table className="cmp">
              <thead>
                <tr>
                  <th>{t('compare.metric')}</th>
                  <th>A</th>
                  <th>B</th>
                  <th>B − A</th>
                  <th>Z</th>
                </tr>
              </thead>
              <tbody>
                {job.rows.map((r) => (
                  <tr key={r.name} className={r.significant ? 'sig' : ''}>
                    <td>{localName('metric', r.name.trim())}</td>
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
            <div className="muted small">{t('compare.legend')}</div>
          </>
        )}
      </div>
    </div>
  )
}
