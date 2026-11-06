import { useState } from 'react'
import type { ReactNode } from 'react'
import { useTranslation } from 'react-i18next'

export interface LegendItem {
  label: string
  color: string
}

export interface TableData {
  head: string[]
  rows: (string | number)[][]
}

interface Props {
  title: string
  subtitle?: string
  /** Two or more series: always a legend (a single series is named by the title). */
  legend?: LegendItem[]
  /** The same numbers as a table (every chart has a table twin; tooltips never gate a value). */
  table: TableData
  children: ReactNode
}

export default function ChartCard({ title, subtitle, legend, table, children }: Props) {
  const { t } = useTranslation()
  const [asTable, setAsTable] = useState(false)

  return (
    <section className="chart-card">
      <header className="chart-head">
        <div>
          <h4>{title}</h4>
          {subtitle && <div className="chart-sub">{subtitle}</div>}
        </div>
        <button className="chart-toggle" onClick={() => setAsTable((v) => !v)} aria-pressed={asTable}>
          {asTable ? t('charts.showChart') : t('charts.showTable')}
        </button>
      </header>

      {legend && legend.length > 1 && (
        <ul className="chart-legend">
          {legend.map((l) => (
            <li key={l.label}>
              <span className="swatch" style={{ background: l.color }} />
              {l.label}
            </li>
          ))}
        </ul>
      )}

      {asTable ? (
        <div className="chart-table-wrap">
          <table className="chart-table">
            <thead>
              <tr>
                {table.head.map((h) => (
                  <th key={h}>{h}</th>
                ))}
              </tr>
            </thead>
            <tbody>
              {table.rows.map((r, i) => (
                <tr key={i}>
                  {r.map((c, j) => (
                    <td key={j}>{c}</td>
                  ))}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : (
        children
      )}
    </section>
  )
}
