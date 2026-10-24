import { useMemo, useState } from 'react'
import type { CSSProperties } from 'react'
import { CLASS_LABEL, formatEstimate } from './format'
import Icon from './Icons'
import { DEFENSE_COLORS, DEFENSE_LABELS } from './roles'
import type { DefenseKind, DefenseStat, RunSummary, SimStatus } from './types'

type Tab = 'summary' | 'defense' | 'terrain'
type SortKey = 'name' | 'shots' | 'hits' | 'rate' | 'magazineStart'

interface Props {
  summary: RunSummary
  status: SimStatus | null
  partial: boolean
  onClose: () => void
}

const rate = (d: DefenseStat) => (d.kind !== 'ew' && d.shots > 0 ? d.hits / d.shots : -1)

export default function ResultsModal({ summary, status, partial, onClose }: Props) {
  const [tab, setTab] = useState<Tab>('summary')
  const [search, setSearch] = useState('')
  const [kinds, setKinds] = useState<Set<DefenseKind>>(new Set())
  const [onlyActive, setOnlyActive] = useState(true)
  const [sort, setSort] = useState<{ key: SortKey; desc: boolean }>({ key: 'shots', desc: true })

  const defenses = useMemo(() => summary.defenses.filter((d) => d.kind !== 'radar' && d.kind !== 'airbase'), [summary])
  const kindCounts = useMemo(() => {
    const m = new Map<DefenseKind, number>()
    for (const d of defenses) m.set(d.kind, (m.get(d.kind) ?? 0) + 1)
    return [...m.entries()]
  }, [defenses])

  const needle = search.trim().toLowerCase()
  const rows = useMemo(() => {
    const list = defenses.filter(
      (d) =>
        (kinds.size === 0 || kinds.has(d.kind)) &&
        (!onlyActive || d.shots > 0 || d.hits > 0 || d.destroyed) &&
        (!needle || d.name.toLowerCase().includes(needle)),
    )
    const val = (d: DefenseStat): number | string => (sort.key === 'rate' ? rate(d) : sort.key === 'name' ? d.name.toLowerCase() : d[sort.key])
    return list.sort((a, b) => {
      const x = val(a)
      const y = val(b)
      const c = typeof x === 'string' && typeof y === 'string' ? x.localeCompare(y) : Number(x) - Number(y)
      return sort.desc ? -c : c
    })
  }, [defenses, kinds, onlyActive, needle, sort])

  const toggleKind = (k: DefenseKind) =>
    setKinds((prev) => {
      const next = new Set(prev)
      if (next.has(k)) next.delete(k)
      else next.add(k)
      return next
    })

  const sortBy = (key: SortKey) => setSort((s) => (s.key === key ? { key, desc: !s.desc } : { key, desc: key !== 'name' }))
  const arrow = (key: SortKey) => (sort.key === key ? (sort.desc ? ' ▼' : ' ▲') : '')

  const destroyed = summary.defenses.filter((d) => d.destroyed)
  const cards: { label: string; value: string; color?: string }[] = [
    { label: 'Lansate', value: `${summary.launched} / ${summary.total}` },
    { label: 'Doborâte', value: String(summary.intercepted), color: '#3ddc97' },
    { label: 'Impacturi', value: String(summary.impacted), color: '#ff6b6b' },
    ...((summary.jammed ?? 0) > 0 ? [{ label: 'Bruiate', value: String(summary.jammed), color: '#2dd4bf' }] : []),
    ...((summary.failed ?? 0) > 0 ? [{ label: 'Căzute pe traseu', value: String(summary.failed), color: '#c4a574' }] : []),
    { label: 'Victime (est.)', value: formatEstimate(summary.killed) },
    { label: 'Răniți (est.)', value: formatEstimate(summary.injured) },
    ...(status ? [{ label: 'Tiruri', value: `${status.shotsFired}` }] : []),
    ...(status && status.interceptorsLaunched > 0 ? [{ label: 'Interceptoare', value: `${status.interceptorsLaunched} (pierdute ${status.interceptorsLost})` }] : []),
  ]

  return (
    <div className="modal-back" onClick={onClose}>
      <div className="modal" onClick={(e) => e.stopPropagation()}>
        <div className="title">
          <span>Rezultat{partial ? ' (parțial)' : ''}</span>
          <button className="icon-btn" onClick={onClose} title="Închide">
            <Icon name="close" size={18} />
          </button>
        </div>

        <div className="tabs">
          {([['summary', 'Sumar'], ['defense', `Apărare (${defenses.length})`], ['terrain', 'Pe tipuri de țintă']] as [Tab, string][]).map(([k, label]) => (
            <button key={k} className={tab === k ? 'on' : ''} onClick={() => setTab(k)}>
              {label}
            </button>
          ))}
        </div>

        <div className="modal-body">
          {tab === 'summary' && (
            <>
              <div className="cards">
                {cards.map((c) => (
                  <div className="card" key={c.label} style={c.color ? ({ '--c': c.color } as CSSProperties) : undefined}>
                    <div className="card-value">{c.value}</div>
                    <div className="card-label">{c.label}</div>
                  </div>
                ))}
              </div>

              {summary.violationsByCountry.length > 0 && (
                <div className="block">
                  <h4>Încălcări de spațiu aerian</h4>
                  <div className="tags">
                    {summary.violationsByCountry.map((v) => (
                      <span className="tag" key={v.code}>
                        {v.code} ×{v.count}
                      </span>
                    ))}
                  </div>
                </div>
              )}

              {destroyed.length > 0 && (
                <div className="block">
                  <h4>Sisteme distruse ({destroyed.length})</h4>
                  <div className="tags">
                    {destroyed.map((d) => (
                      <span className="tag" key={d.id}>
                        {d.name}
                      </span>
                    ))}
                  </div>
                </div>
              )}
            </>
          )}

          {tab === 'defense' && (
            <>
              <div className="toolbar">
                <label className="search">
                  <Icon name="search" size={15} />
                  <input value={search} onChange={(e) => setSearch(e.target.value)} placeholder="caută sistem…" />
                </label>
                <label className="check">
                  <input type="checkbox" checked={onlyActive} onChange={(e) => setOnlyActive(e.target.checked)} /> doar cele active
                </label>
              </div>
              <div className="chips wrap">
                {kindCounts.map(([k, n]) => (
                  <button key={k} className={`chip text ${kinds.has(k) ? 'on' : ''}`} style={{ '--c': DEFENSE_COLORS[k] } as CSSProperties} onClick={() => toggleKind(k)}>
                    {DEFENSE_LABELS[k].split(' (')[0]} <span>{n}</span>
                  </button>
                ))}
              </div>
              <div className="table-wrap">
                <table className="data">
                  <thead>
                    <tr>
                      <th onClick={() => sortBy('name')}>Sistem{arrow('name')}</th>
                      <th onClick={() => sortBy('shots')}>Trageri{arrow('shots')}</th>
                      <th onClick={() => sortBy('hits')}>Lovituri{arrow('hits')}</th>
                      <th onClick={() => sortBy('rate')}>Rată{arrow('rate')}</th>
                      <th onClick={() => sortBy('magazineStart')}>Muniție{arrow('magazineStart')}</th>
                    </tr>
                  </thead>
                  <tbody>
                    {rows.map((d) => (
                      <tr key={d.id} className={d.destroyed ? 'dead' : ''}>
                        <td>
                          <span className="swatch" style={{ background: DEFENSE_COLORS[d.kind] }} /> {d.name}
                          {d.kind === 'patrol' && d.returns > 0 ? ` (↺${d.returns})` : ''}
                          {d.destroyed ? ' ✕' : ''}
                        </td>
                        <td>{d.kind === 'ew' ? '—' : d.shots}</td>
                        <td>{d.hits}</td>
                        <td>{rate(d) >= 0 ? `${Math.round(rate(d) * 100)}%` : '—'}</td>
                        <td>{d.magazineStart}</td>
                      </tr>
                    ))}
                    {rows.length === 0 && (
                      <tr>
                        <td colSpan={5} className="muted">
                          Niciun sistem pentru filtrele alese.
                        </td>
                      </tr>
                    )}
                  </tbody>
                </table>
              </div>
            </>
          )}

          {tab === 'terrain' && (
            <table className="data">
              <thead>
                <tr>
                  <th>Țintă</th>
                  <th>Impacturi</th>
                  <th>Morți</th>
                  <th>Răniți</th>
                </tr>
              </thead>
              <tbody>
                {summary.classes.map((c) => (
                  <tr key={c.class}>
                    <td>{CLASS_LABEL[c.class] ?? c.class}</td>
                    <td>{c.impacts}</td>
                    <td>{formatEstimate(c.killed)}</td>
                    <td>{formatEstimate(c.injured)}</td>
                  </tr>
                ))}
                <tr className="total">
                  <td>Total</td>
                  <td>{summary.impacted}</td>
                  <td>{formatEstimate(summary.killed)}</td>
                  <td>{formatEstimate(summary.injured)}</td>
                </tr>
              </tbody>
            </table>
          )}
        </div>

        <div className="muted small modal-foot">
          Medie (interval 5–95%, {summary.trials} simulări Monte Carlo). Estimare cu parametri ilustrativi, nu o predicție reală.
        </div>
      </div>
    </div>
  )
}
