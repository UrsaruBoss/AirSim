import { useRef, useState } from 'react'
import { useWidth } from './utils'
import { inkOn } from './theme'

export interface Segment {
  key: string
  label: string
  color: string
  value: number
}

export interface BarRow {
  label: string
  sub?: string
  /** The full length of the bar (the wave size); the segments add up to at most this. */
  total: number
  segments: Segment[]
}

interface Props {
  rows: BarRow[]
  /** The part of a bar not covered by segments (still in flight / not yet launched). */
  restLabel: string
  restColor: string
  ariaLabel: string
}

const LABEL_W = 132
const BAR_H = 18
const ROW_H = 34
const GAP = 2

export default function StackedBars({ rows, restLabel, restColor, ariaLabel }: Props) {
  const ref = useRef<HTMLDivElement>(null)
  const width = useWidth(ref)
  const [tip, setTip] = useState<{ x: number; y: number; row: BarRow; seg: Segment | null; value: number } | null>(null)

  const max = Math.max(1, ...rows.map((r) => r.total))
  const iw = Math.max(10, width - LABEL_W - 56)
  const scale = iw / max
  const height = rows.length * ROW_H + 6

  return (
    <div ref={ref} className="chart" role="img" aria-label={ariaLabel}>
      {width > 0 && (
        <svg width={width} height={height}>
          {rows.map((row, i) => {
            const y = 3 + i * ROW_H
            let x = LABEL_W
            const covered = row.segments.reduce((a, s) => a + s.value, 0)
            const rest = Math.max(0, row.total - covered)
            const parts = [...row.segments.filter((s) => s.value > 0), ...(rest > 0 ? [{ key: 'rest', label: restLabel, color: restColor, value: rest }] : [])]
            return (
              <g key={row.label + i}>
                <text x={0} y={y + BAR_H / 2 + 4} className="row-label">
                  {row.label.length > 20 ? `${row.label.slice(0, 19)}…` : row.label}
                  <title>{row.label}</title>
                </text>
                {parts.map((p, j) => {
                  const w = p.value * scale
                  const px = x
                  x += w
                  const last = j === parts.length - 1
                  const drawW = Math.max(1, w - (last ? 0 : GAP))
                  const r = last ? Math.min(4, drawW / 2) : 0
                  // square at the baseline (left), 4px rounded at the data end (right) of the last segment
                  const d = `M${px},${y} h${drawW - r} a${r},${r} 0 0 1 ${r},${r} v${BAR_H - 2 * r} a${r},${r} 0 0 1 ${-r},${r} h${-(drawW - r)} z`
                  const text = String(p.value)
                  const fits = p.key !== 'rest' && drawW >= text.length * 7 + 10
                  return (
                    <g
                      key={p.key}
                      className="bar-seg"
                      onPointerMove={(e) => {
                        const box = (e.currentTarget.ownerSVGElement as SVGSVGElement).getBoundingClientRect()
                        setTip({ x: e.clientX - box.left, y: e.clientY - box.top, row, seg: p.key === 'rest' ? null : (p as Segment), value: p.value })
                      }}
                      onPointerLeave={() => setTip(null)}
                    >
                      <path d={d} fill={p.color} opacity={p.key === 'rest' ? 0.5 : 1} />
                      {fits && (
                        <text x={px + drawW / 2} y={y + BAR_H / 2 + 4} textAnchor="middle" style={{ fill: inkOn(p.color) }} className="seg-value">
                          {text}
                        </text>
                      )}
                    </g>
                  )
                })}
                <text x={LABEL_W + row.total * scale + 8} y={y + BAR_H / 2 + 4} className="end-value">
                  {row.total}
                </text>
              </g>
            )
          })}
        </svg>
      )}

      {tip && (
        <div className="chart-tip" style={{ left: Math.min(tip.x + 12, Math.max(0, width - 200)), top: tip.y + 12 }}>
          <div className="tip-time">
            {tip.row.label}
            {tip.row.sub ? ` · ${tip.row.sub}` : ''}
          </div>
          <div className="tip-row">
            <span className="tip-key" style={{ background: tip.seg?.color ?? restColor }} />
            <strong>{tip.value}</strong>
            <span>{tip.seg?.label ?? restLabel}</span>
          </div>
        </div>
      )}
    </div>
  )
}
