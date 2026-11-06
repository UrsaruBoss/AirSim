import { useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { localName } from './i18n/dataNames'
import LanguageSwitch from './i18n/LanguageSwitch'
import { formatSimTime } from './format'
import Icon from './Icons'
import { playback } from './playback'
import { VARIANTS, splitVariantId } from './solar'
import type { Variant } from './solar'
import type { PrecomputedInfo } from './types'

interface Props {
  /** After loading: the id of the chosen scenario; App loads the scenario for the map and switches to the view. */
  onOpen: (info: PrecomputedInfo) => Promise<void>
  onBack: () => void
}

/** Public mode: pick a scenario with a precomputed run. */
export default function Gallery({ onOpen, onBack }: Props) {
  const { t } = useTranslation()
  const [items, setItems] = useState<PrecomputedInfo[] | null>(null)
  const [error, setError] = useState('')
  const [loading, setLoading] = useState<{ id: string; fraction: number } | null>(null)

  useEffect(() => {
    let cancelled = false
    playback
      .list()
      .then((list) => {
        if (!cancelled) setItems(list)
      })
      .catch((e: unknown) => {
        if (!cancelled) setError(e instanceof Error ? e.message : String(e))
      })
    return () => {
      cancelled = true
    }
  }, [])

  // runs "id.day" and "id.night" are shown as one card with two buttons
  const groups = useMemo(() => {
    const map = new Map<string, { baseId: string; single: PrecomputedInfo | null; variants: Partial<Record<Variant, PrecomputedInfo>> }>()
    for (const it of items ?? []) {
      const { baseId, variant } = splitVariantId(it.id)
      const g = map.get(baseId) ?? { baseId, single: null, variants: {} }
      if (variant) g.variants[variant] = it
      else g.single = it
      map.set(baseId, g)
    }
    return [...map.values()]
  }, [items])

  const open = async (info: PrecomputedInfo) => {
    setError('')
    setLoading({ id: info.id, fraction: 0 })
    try {
      await playback.load(info, (fraction) => setLoading({ id: info.id, fraction }))
      await onOpen(info)
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e))
      setLoading(null)
    }
  }

  return (
    <div className="home">
      <div className="top-right">
        <LanguageSwitch />
      </div>
      <div className="home-inner">
        <h1>{t('gallery.title')}</h1>
        <p className="muted">{t('gallery.intro')}</p>
        {error && <div className="error">{error}</div>}
        {items === null && !error && <div className="muted">{t('gallery.loadingList')}</div>}
        {items?.length === 0 && <div className="muted">{t('gallery.empty')}</div>}

        <div className="modes gallery-grid">
          {groups.map((g) => {
            const first = g.single ?? g.variants.day ?? g.variants.night
            if (!first) return null
            const title = localName('scenario', first.name).split(':')[0]
            if (g.single && Object.keys(g.variants).length === 0) {
              const it = g.single
              return (
                <button key={g.baseId} className="mode" onClick={() => void open(it)} disabled={loading !== null}>
                  <strong>{title}</strong>
                  <span>
                    {t('gallery.meta', { units: it.totalUnits, duration: formatSimTime(it.durationS), seed: it.seed, mb: (it.binBytes / 1e6).toFixed(1) })}
                  </span>
                  {loading?.id === it.id && <span className="muted">{t('gallery.downloading', { pct: Math.round(loading.fraction * 100) })}</span>}
                </button>
              )
            }
            return (
              <div key={g.baseId} className="mode">
                <strong>{title}</strong>
                <span>{t('gallery.metaShort', { units: first.totalUnits, seed: first.seed })}</span>
                <div className="variant-btns">
                  {VARIANTS.map((v) => {
                    const it = g.variants[v]
                    if (!it) return null
                    return (
                      <button key={v} className="variant-btn" onClick={() => void open(it)} disabled={loading !== null}>
                        <Icon name={v === 'day' ? 'sun' : 'moon'} size={16} />
                        {loading?.id === it.id
                          ? `${Math.round(loading.fraction * 100)}%`
                          : t('gallery.variant', { name: t(`gallery.${v}`), mb: (it.binBytes / 1e6).toFixed(0) })}
                      </button>
                    )
                  })}
                </div>
              </div>
            )
          })}
        </div>

        <button className="resume" onClick={onBack} disabled={loading !== null}>
          {t('common.back')}
        </button>
      </div>
    </div>
  )
}
