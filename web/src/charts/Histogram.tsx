import { useMemo, useRef, useState } from 'react'
import { compact, niceMax, ticks, useWidth } from './utils'

interface Props {
  /** Number of trials in each bin; bin i covers [i * binWidth, (i + 1) * binWidth). */
  counts: number[]
  binWidth: number
  color: string
  mean: number
  p5: number
  p95: number
  /** Name of the mean marker (e.g. "mean"). */
  meanLabel: string
  tipLabel: (range: string, pct: string) => string
  ariaLabel: string
  height?: number
}

const M = { l: 36, r: 10, t: 14, b: 22 }
const MAX_BAR = 24
const GAP = 2

export default function Histogram({ counts, binWidth, color, mean, p5, p95, meanLabel, tipLabel, ariaLabel, height = 170 }: Props) {
  const ref = useRef<HTMLDivElement>(null)
  const width = useWidth(ref)
  const [hover, setHover] = useState<number | null>(null)

  const trials = useMemo(() => counts.reduce((a, c) => a + c, 0) || 1, [counts])
  const pct = useMemo(() => counts.map((c) => (c / trials) * 100), [counts, trials])
  const top = niceMax(Math.max(1, ...pct))
  const iw = Math.max(10, width - M.l - M.r)
  const ih = height - M.t - M.b
  const slot = iw / counts.length
  const bar = Math.min(MAX_BAR, slot - GAP)
  const xOf = (v: number) => M.l + Math.min(1, Math.max(0, v / (binWidth * counts.length))) * iw
  const sy = (v: number) => M.t + ih - (v / top) * ih

  const xTickEvery = Math.max(1, Math.ceil(counts.length / 6))
  const range = (i: number) => `${+(i * binWidth).toFixed(1)}–${+((i + 1) * binWidth).toFixed(1)}`

  return (
    <div ref={ref} className="chart" role="img" aria-label={ariaLabel}>
      {width > 0 && (
        <svg width={width} height={height}>
          {ticks(top, 2).map((v) => (
            <g key={v}>
              <line className="grid" x1={M.l} x2={M.l + iw} y1={sy(v)} y2={sy(v)} />
              <text x={M.l - 6} y={sy(v) + 4} textAnchor="end">
                {compact(v)}%
              </text>
            </g>
          ))}

          {/* 5-95% interval as a wash behind the columns, the mean as a 2px marker */}
          <rect x={xOf(p5)} y={M.t} width={Math.max(2, xOf(p95) - xOf(p5))} height={ih} fill={color} opacity={0.1} />

          {pct.map((p, i) => {
            const cx = M.l + slot * i + slot / 2
            const h = (p / top) * ih
            const r = Math.min(4, bar / 2, h)
            // square at the baseline, 4px rounded at the data end (top)
            const d = `M${cx - bar / 2},${M.t + ih} v${-(h - r)} a${r},${r} 0 0 1 ${r},${-r} h${bar - 2 * r} a${r},${r} 0 0 1 ${r},${r} v${h - r} z`
            return (
              <g key={i} onPointerMove={() => setHover(i)} onPointerLeave={() => setHover(null)}>
                <rect x={M.l + slot * i} y={M.t} width={slot} height={ih} fill="transparent" />
                {h > 0.5 && <path d={d} fill={color} opacity={hover === null || hover === i ? 1 : 0.55} />}
                {i % xTickEvery === 0 && (
                  <text x={M.l + slot * i} y={height - 6} textAnchor="start">
                    {+(i * binWidth).toFixed(1)}
                  </text>
                )}
              </g>
            )
          })}

          <line x1={xOf(mean)} x2={xOf(mean)} y1={M.t - 4} y2={M.t + ih} stroke={color} strokeWidth={2} />
          <text x={xOf(mean)} y={M.t - 6} textAnchor={xOf(mean) > width - 70 ? 'end' : xOf(mean) < M.l + 40 ? 'start' : 'middle'} className="end-value">
            {meanLabel} {+mean.toFixed(1)}
          </text>
        </svg>
      )}

      {hover !== null && width > 0 && (
        <div className="chart-tip" style={{ left: Math.min(M.l + slot * hover + slot / 2 + 10, Math.max(0, width - 180)), top: 20 }}>
          <div className="tip-row">
            <span className="tip-key" style={{ background: color }} />
            <strong>{tipLabel(range(hover), `${+pct[hover].toFixed(1)}%`)}</strong>
          </div>
        </div>
      )}
    </div>
  )
}
