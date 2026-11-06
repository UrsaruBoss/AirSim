import { useMemo, useRef, useState } from 'react'
import type { KeyboardEvent, PointerEvent } from 'react'
import { formatSimTime } from '../format'
import { clock, compact, niceMax, ticks, timeTicks, useWidth } from './utils'

export interface LineSeries {
  key: string
  label: string
  color: string
  /** Value at each x (same length as x). */
  values: number[]
  /** Value at time 0 (default 0: cumulative counts start from nothing). */
  origin?: number
}

interface Props {
  /** Time (seconds) of each sample; a zero point is added, so cumulative series start at 0. */
  x: number[]
  series: LineSeries[]
  /** Fixed upper bound of the y axis (otherwise the largest value, rounded up). */
  yMax?: number
  yFormat?: (v: number) => string
  height?: number
  ariaLabel: string
}

const M = { l: 42, r: 40, t: 8, b: 24 }

export default function LineChart({ x, series, yMax, yFormat = compact, height = 220, ariaLabel }: Props) {
  const ref = useRef<HTMLDivElement>(null)
  const width = useWidth(ref)
  const [hover, setHover] = useState<number | null>(null)

  const xs = useMemo(() => [0, ...x], [x])
  const ys = useMemo(() => series.map((s) => [s.origin ?? 0, ...s.values]), [series])
  const xMax = xs[xs.length - 1] || 1
  const top = yMax ?? niceMax(Math.max(1, ...ys.flat()))

  const iw = Math.max(10, width - M.l - M.r)
  const ih = height - M.t - M.b
  const sx = (v: number) => M.l + (v / xMax) * iw
  const sy = (v: number) => M.t + ih - (v / top) * ih

  const move = (e: PointerEvent<HTMLDivElement>) => {
    const rect = e.currentTarget.getBoundingClientRect()
    const t = ((e.clientX - rect.left - M.l) / iw) * xMax
    let best = 0
    for (let i = 1; i < xs.length; i++) if (Math.abs(xs[i] - t) < Math.abs(xs[best] - t)) best = i
    setHover(best)
  }
  const key = (e: KeyboardEvent<HTMLDivElement>) => {
    if (e.key === 'ArrowLeft') setHover((h) => Math.max(0, (h ?? xs.length) - 1))
    else if (e.key === 'ArrowRight') setHover((h) => Math.min(xs.length - 1, (h ?? -1) + 1))
    else if (e.key === 'Escape') setHover(null)
  }

  // end-of-line values: only the ones that do not collide (a label is never nudged away from its line)
  const endLabels = useMemo(() => {
    const items = series.map((s, i) => ({ s, y: sy(ys[i][ys[i].length - 1]) })).sort((a, b) => a.y - b.y)
    const placed: typeof items = []
    for (const it of items) if (placed.every((p) => Math.abs(p.y - it.y) >= 14)) placed.push(it)
    return placed
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [series, ys, top, height])

  const tipLeft = hover !== null ? sx(xs[hover]) : 0
  const flip = tipLeft > width - 190

  return (
    <div
      ref={ref}
      className="chart"
      tabIndex={0}
      role="img"
      aria-label={ariaLabel}
      onPointerMove={move}
      onPointerLeave={() => setHover(null)}
      onKeyDown={key}
      onFocus={() => setHover((h) => h ?? xs.length - 1)}
      onBlur={() => setHover(null)}
    >
      {width > 0 && (
        <svg width={width} height={height}>
          {ticks(top).map((v) => (
            <g key={v}>
              <line className="grid" x1={M.l} x2={M.l + iw} y1={sy(v)} y2={sy(v)} />
              <text x={M.l - 6} y={sy(v) + 4} textAnchor="end">
                {yFormat(v)}
              </text>
            </g>
          ))}
          {timeTicks(xMax).map((t) => (
            <text key={t} x={sx(t)} y={height - 6} textAnchor={t === 0 ? 'start' : 'middle'}>
              {clock(t)}
            </text>
          ))}

          {series.map((s, i) => (
            <polyline
              key={s.key}
              fill="none"
              stroke={s.color}
              strokeWidth={2}
              strokeLinejoin="round"
              strokeLinecap="round"
              points={xs.map((t, j) => `${sx(t).toFixed(1)},${sy(ys[i][j]).toFixed(1)}`).join(' ')}
            />
          ))}

          {endLabels.map(({ s, y }) => (
            <text key={s.key} className="end-value" x={M.l + iw + 6} y={y + 4}>
              {yFormat(s.values[s.values.length - 1])}
            </text>
          ))}

          {hover !== null && (
            <g>
              <line className="crosshair" x1={sx(xs[hover])} x2={sx(xs[hover])} y1={M.t} y2={M.t + ih} />
              {series.map((s, i) => (
                <circle key={s.key} cx={sx(xs[hover])} cy={sy(ys[i][hover])} r={4} fill={s.color} className="dot-ring" />
              ))}
            </g>
          )}
        </svg>
      )}

      {hover !== null && width > 0 && (
        <div className="chart-tip" style={{ left: flip ? undefined : tipLeft + 12, right: flip ? width - tipLeft + 12 : undefined, top: 6 }}>
          <div className="tip-time">{formatSimTime(xs[hover])}</div>
          {series.map((s, i) => (
            <div className="tip-row" key={s.key}>
              <span className="tip-key" style={{ background: s.color }} />
              <strong>{yFormat(ys[i][hover])}</strong>
              <span>{s.label}</span>
            </div>
          ))}
        </div>
      )}
    </div>
  )
}
