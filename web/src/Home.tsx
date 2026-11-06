import { useTranslation } from 'react-i18next'
import { localName } from './i18n/dataNames'
import LanguageSwitch from './i18n/LanguageSwitch'
import type { ConnectionState, SimStatus } from './types'

interface Props {
  connection: ConnectionState
  status: SimStatus | null
  onNormal: () => void
  onRuns: () => void
  onCompare: () => void
  onResume: () => void
  /** Public mode: only the gallery of precomputed scenarios. */
  publicMode?: boolean
  onGallery?: () => void
}

export default function Home({ connection, status, onNormal, onRuns, onCompare, onResume, publicMode = false, onGallery }: Props) {
  const { t } = useTranslation()
  const active = status !== null && (status.state === 'running' || status.state === 'paused')

  return (
    <div className="home">
      <div className="top-right">
        <LanguageSwitch />
      </div>
      <div className="home-inner">
        <h1>{t('home.title')}</h1>
        <p className="muted">{t('home.subtitle')}</p>
        <span className={`badge ${connection === 'connected' ? 'ok' : 'warn'}`}>{t(`connection.${connection}`)}</span>

        {publicMode ? (
          <div className="modes modes-1">
            <button className="mode" onClick={onGallery}>
              <strong>{t('home.gallery.title')}</strong>
              <span>{t('home.gallery.desc')}</span>
            </button>
          </div>
        ) : (
          <div className="modes">
            <button className="mode" onClick={onNormal}>
              <strong>{t('home.normal.title')}</strong>
              <span>{t('home.normal.desc')}</span>
            </button>
            <button className="mode" onClick={onRuns}>
              <strong>{t('home.runs.title')}</strong>
              <span>{t('home.runs.desc')}</span>
            </button>
            <button className="mode" onClick={onCompare}>
              <strong>{t('home.compare.title')}</strong>
              <span>{t('home.compare.desc')}</span>
            </button>
          </div>
        )}

        {active && (
          <button className="resume" onClick={onResume}>
            {t('home.resume', { name: localName('scenario', status?.scenarioName ?? '') })}
          </button>
        )}
      </div>
    </div>
  )
}
