import { useTranslation } from 'react-i18next'
import { LANGUAGES } from './index'

/** Small EN / RO toggle, shown in the top-right corner of every view. */
export default function LanguageSwitch() {
  const { i18n } = useTranslation()
  const current = i18n.language?.startsWith('ro') ? 'ro' : 'en'
  return (
    <div className="lang-switch" role="group" aria-label="Language">
      {LANGUAGES.map((l) => (
        <button key={l.code} className={current === l.code ? 'on' : ''} onClick={() => void i18n.changeLanguage(l.code)}>
          {l.label}
        </button>
      ))}
    </div>
  )
}
