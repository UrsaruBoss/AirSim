import { useRef } from 'react'
import { niceMax, useWidth } from './utils'

export interface TornadoRow {
  label: string
  /** Change of the metric when the factor is scaled down / up (relative to the baseline), and how reliable each change is (Z). */
  low: number
  high: number
  zLow: number
  zHigh: number
}

interface Props {
  rows: TornadoRow[]
  lowLabel: string
  highLabel: string
  lowColor: string
  highColor: string
  ariaLabel: string
}

const ROW_H = 38
const BAR_H = 10
const GAP = 2
const TIP_W = 40
/** |Z| below this: the change is within the noise of the paired runs. */
const SIGNIFICANT = 2

const signed = (v: number) => `${v > 0 ? '+' : v < 0 ? '−' : ''}${Math.abs(Math.round(v * 10) / 10)}`

/** A bar from the zero line to v (positive: right, negative: left), rounded at the data end only. */
function barPath(zero: number, v: number, y: number): string {
  const w = Math.abs(v)
  if (w < 0.5) return `M${zero},${y} h${v >= 0 ? 1 : -1} v${BAR_H} h${v >= 0 ? -1 : 1} z`
  const r = Math.min(4, w / 2, BAR_H / 2)
  const s = v >= 0 ? 1 : -1
  return `M${zero},${y} h${s * (w - r)} a${r},${r} 0 0 ${s > 0 ? 1 : 0} ${s * r},${r} v${BAR_H - 2 * r} a${r},${r} 0 0 ${s > 0 ? 1 : 0} ${-s * r},${r} h${-s * (w - r)} z`
}

/** One-at-a-time sensitivity: two bars per input (scaled down / up) around a common zero line, longest swing on top. */
export default function TornadoChart({ rows, lowLabel, highLabel, lowColor, highColor, ariaLabel }: Props) {
  const ref = useRef<HTMLDivElement>(null)
  const width = useWidth(ref)
  const labelW = width < 420 ? 112 : 168
  const half = Math.max(20, (width - labelW - TIP_W) / 2 - TIP_W)
  const max = niceMax(Math.max(1e-9, ...rows.flatMap((r) => [Math.abs(r.low), Math.abs(r.high)])))
  const zero = labelW + TIP_W + half
  const x = (v: number) => (v / max) * half

  return (
    <div ref={ref} className="chart" role="img" aria-label={ariaLabel}>
      {width > 0 && (
        <svg width={width} height={rows.length * ROW_H + 6}>
          <line x1={zero} x2={zero} y1={0} y2={rows.length * ROW_H + 6} stroke="currentColor" strokeOpacity={0.35} />
          {rows.map((r, i) => {
            const y = 4 + i * ROW_H
            const bars = [
              { v: r.low, z: r.zLow, color: lowColor, label: lowLabel, yy: y + 4 },
              { v: r.high, z: r.zHigh, color: highColor, label: highLabel, yy: y + 4 + BAR_H + GAP },
            ]
            return (
              <g key={r.label}>
                <text x={0} y={y + ROW_H / 2 + 1} className="row-label">
                  {r.label}
                </text>
                {bars.map((b) => {
                  const w = x(b.v)
                  const faded = Math.abs(b.z) < SIGNIFICANT
                  return (
                    <g key={b.label} opacity={faded ? 0.4 : 1}>
                      <title>{`${r.label} ${b.label}: ${signed(b.v)} (Z ${Math.round(b.z * 10) / 10})`}</title>
                      <path d={barPath(zero, w, b.yy)} fill={b.color} />
                      <text
                        x={zero + w + (w >= 0 ? 6 : -6)}
                        y={b.yy + BAR_H - 1}
                        textAnchor={w >= 0 ? 'start' : 'end'}
                        className="end-value"
                        style={{ fontSize: 11 }}
                      >
                        {signed(b.v)}
                      </text>
                    </g>
                  )
                })}
              </g>
            )
          })}
        </svg>
      )}
    </div>
  )
}
