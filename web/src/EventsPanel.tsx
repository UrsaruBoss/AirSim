import { useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import type { CSSProperties } from 'react'
import { CATEGORY_OF_TYPE, EVENT_CATEGORIES, categoryLabel, defaultFilter, eventLabel } from './events'
import { formatSimTime } from './format'
import Icon from './Icons'
import type { SimEvent } from './types'

interface Props {
  events: SimEvent[]
  /** Large variant (central window): search + more rows. */
  large?: boolean
  onExpand?: () => void
  onClose: () => void
}

function rowText(e: SimEvent): string {
  const code = e.code ? ` (${e.code})` : ''
  return `#${e.id}${code}  ${e.latDeg.toFixed(3)}, ${e.lonDeg.toFixed(3)}`
}

export default function EventsPanel({ events, large = false, onExpand, onClose }: Props) {
  const { t } = useTranslation()
  const [filter, setFilter] = useState<Set<string>>(defaultFilter)
  const [search, setSearch] = useState('')

  const counts = useMemo(() => {
    const m = new Map<string, number>()
    for (const e of events) {
      const c = CATEGORY_OF_TYPE[e.type]
      if (c) m.set(c.key, (m.get(c.key) ?? 0) + 1)
    }
    return m
  }, [events])

  const needle = search.trim().toLowerCase()
  const shown = useMemo(() => {
    const out: SimEvent[] = []
    const limit = large ? 600 : 80
    for (const e of events) {
      const c = CATEGORY_OF_TYPE[e.type]
      if (!c || !filter.has(c.key)) continue
      if (needle && !`${eventLabel(e.type)} ${rowText(e)} ${formatSimTime(e.timeS)}`.toLowerCase().includes(needle)) continue
      out.push(e)
      if (out.length >= limit) break
    }
    return out
  }, [events, filter, needle, large])

  const toggle = (key: string) =>
    setFilter((prev) => {
      const next = new Set(prev)
      if (next.has(key)) next.delete(key)
      else next.add(key)
      return next
    })

  return (
    <div className={`events ${large ? 'large' : ''}`}>
      <div className="title">
        <span>{t('events.title')}</span>
        <span className="tools">
          {onExpand && (
            <button className="icon-btn" onClick={onExpand} title={t('events.expand')}>
              <Icon name="expand" size={16} />
            </button>
          )}
          <button className="icon-btn" onClick={onClose} title={t('events.hide')}>
            <Icon name="close" size={16} />
          </button>
        </span>
      </div>

      <div className="chips">
        {EVENT_CATEGORIES.map((c) => (
          <button
            key={c.key}
            className={`chip ${filter.has(c.key) ? 'on' : ''}`}
            style={{ '--c': c.color } as CSSProperties}
            onClick={() => toggle(c.key)}
            title={`${categoryLabel(c.key)} (${counts.get(c.key) ?? 0})`}
          >
            <Icon name={c.icon} size={14} />
            {large && <span>{counts.get(c.key) ?? 0}</span>}
          </button>
        ))}
      </div>

      {large && (
        <label className="search">
          <Icon name="search" size={15} />
          <input value={search} onChange={(e) => setSearch(e.target.value)} placeholder={t('events.search')} />
        </label>
      )}

      <div className="lines">
        {shown.length === 0 && <div className="muted">{events.length === 0 ? '—' : t('events.none')}</div>}
        {shown.map((e, i) => {
          const c = CATEGORY_OF_TYPE[e.type]
          return (
            <div key={`${e.tick}-${e.type}-${e.id}-${i}`} className="ev" style={{ '--c': c?.color ?? '#8d9bb0' } as CSSProperties} title={eventLabel(e.type)}>
              <span className="ev-icon">{c && <Icon name={c.icon} size={14} />}</span>
              <span className="ev-time">{formatSimTime(e.timeS)}</span>
              <span className="ev-text">
                {large && <strong>{eventLabel(e.type)} </strong>}
                {rowText(e)}
              </span>
            </div>
          )
        })}
      </div>
    </div>
  )
}
