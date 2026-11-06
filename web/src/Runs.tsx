import { useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { localName } from './i18n/dataNames'
import { api } from './api'
import i18n, { currentLocale } from './i18n'
import LanguageSwitch from './i18n/LanguageSwitch'
import type { SavedRun, Scenario } from './types'

interface Props {
  connected: boolean
  onBack: () => void
  /** Starts the replay; the scenario is needed to draw it on the map. */
  onReplay: (runId: string, scenario: Scenario) => Promise<void>
}

export default function Runs({ connected, onBack, onReplay }: Props) {
  const { t } = useTranslation()
  const [runs, setRuns] = useState<SavedRun[] | null>(null)
  const [busy, setBusy] = useState('')
  const [error, setError] = useState('')
  const [query, setQuery] = useState('')

  useEffect(() => {
    if (!connected) return
    api
      .runs()
      .then(setRuns)
      .catch((e) => setError(e instanceof Error ? e.message : String(e)))
  }, [connected])

  // search over scenario name, seed and run id; the date is not searched (it is formatted per locale)
  const filtered = useMemo(() => {
    if (!runs) return null
    const q = query.trim().toLowerCase()
    if (!q) return runs
    return runs.filter((r) => `${r.scenarioName} ${r.seed} ${r.runId}`.toLowerCase().includes(q))
  }, [runs, query])

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

  // the language is part of the render (dates), so re-render when it changes
  void i18n.language

  return (
    <div className="page">
      <div className="page-inner">
        <div className="page-head">
          <button className="back" onClick={onBack}>
            {t('common.menu')}
          </button>
          <h2>{t('runs.title')}</h2>
          <LanguageSwitch />
        </div>
        <p className="muted small">{t('runs.intro')}</p>

        <div className="runs-toolbar">
          <input
            className="runs-search"
            type="search"
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            placeholder={t('runs.searchPlaceholder')}
            aria-label={t('runs.searchPlaceholder')}
          />
          {runs !== null && filtered !== null && (
            <span className="muted small count">{t('runs.count', { shown: filtered.length, total: runs.length })}</span>
          )}
        </div>

        {error && <div className="error">{error}</div>}
        {runs === null && !error && <div className="muted">{t('common.loading')}</div>}
        {runs !== null && runs.length === 0 && <div className="muted">{t('runs.none')}</div>}
        {runs !== null && runs.length > 0 && filtered?.length === 0 && <div className="muted">{t('runs.noMatch')}</div>}

        <ul className="runs">
          {filtered?.map((r) => (
            <li key={r.runId}>
              <div className="grow">
                <strong className="run-title" title={localName('scenario', r.scenarioName) || r.runId}>
                  {localName('scenario', r.scenarioName) || r.runId}
                </strong>
                <div className="muted small">
                  {t('runs.meta', { seed: r.seed, date: new Date(r.createdUtc).toLocaleString(currentLocale()), engine: r.engineVersion })}
                </div>
                {!r.engineMatches && <div className="warn-text small">{t('runs.engineMismatch')}</div>}
              </div>
              <button className="primary" disabled={!connected || busy !== ''} onClick={() => void replay(r)}>
                {busy === r.runId ? t('runs.starting') : t('runs.replay')}
              </button>
            </li>
          ))}
        </ul>
      </div>
    </div>
  )
}
