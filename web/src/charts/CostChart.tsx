import { useRef } from 'react'
import { formatMoney } from '../draft'
import { useWidth } from './utils'

interface Props {
  rows: { label: string; usd: number; color: string }[]
  ariaLabel: string
}

const LABEL_W = 120
const BAR_H = 20
const ROW_H = 34

/** Two bars on one scale, the value at the tip of each. */
export default function CostChart({ rows, ariaLabel }: Props) {
  const ref = useRef<HTMLDivElement>(null)
  const width = useWidth(ref)
  const max = Math.max(1, ...rows.map((r) => r.usd))
  const iw = Math.max(10, width - LABEL_W - 80)

  return (
    <div ref={ref} className="chart" role="img" aria-label={ariaLabel}>
      {width > 0 && (
        <svg width={width} height={rows.length * ROW_H + 6}>
          {rows.map((r, i) => {
            const y = 3 + i * ROW_H
            const w = Math.max(r.usd > 0 ? 2 : 0, (r.usd / max) * iw)
            const rr = Math.min(4, w / 2)
            const d = `M${LABEL_W},${y} h${w - rr} a${rr},${rr} 0 0 1 ${rr},${rr} v${BAR_H - 2 * rr} a${rr},${rr} 0 0 1 ${-rr},${rr} h${-(w - rr)} z`
            return (
              <g key={r.label}>
                <text x={0} y={y + BAR_H / 2 + 4} className="row-label">
                  {r.label}
                </text>
                {w > 0 && <path d={d} fill={r.color} />}
                <text x={LABEL_W + w + 8} y={y + BAR_H / 2 + 4} className="end-value">
                  {formatMoney(r.usd)}
                </text>
              </g>
            )
          })}
        </svg>
      )}
    </div>
  )
}
