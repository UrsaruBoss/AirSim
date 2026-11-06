import { useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { formatMoney } from '../draft'
import { formatSimTime } from '../format'
import { unitLabel } from '../roles'
import type { RunSummary } from '../types'
import ChartCard from './ChartCard'
import CostChart from './CostChart'
import Histogram from './Histogram'
import LineChart from './LineChart'
import StackedBars from './StackedBars'
import TornadoChart from './TornadoChart'
import { COLORS } from './theme'

/** Up to ~24 evenly spaced rows of a time series, for the table view. */
function sample<T>(items: T[], max = 24): { item: T; index: number }[] {
  const step = Math.max(1, Math.ceil(items.length / max))
  const out: { item: T; index: number }[] = []
  for (let i = step - 1; i < items.length; i += step) out.push({ item: items[i], index: i })
  if (items.length > 0 && out[out.length - 1]?.index !== items.length - 1) out.push({ item: items[items.length - 1], index: items.length - 1 })
  return out
}

export default function ChartsTab({ summary }: { summary: RunSummary }) {
  const { t } = useTranslation()
  const { series, waves, casualtyHistogram: hist, cost, warning, sensitivity } = summary
  const [sensMetric, setSensMetric] = useState('Impacts')

  const x = useMemo(() => (series ? series.intercepted.map((_, i) => (i + 1) * series.binSeconds) : []), [series])

  const lines = useMemo(() => {
    if (!series) return []
    const all = [
      { key: 'intercepted', label: t('charts.intercepted'), color: COLORS.intercepted, values: series.intercepted },
      { key: 'impacts', label: t('charts.impacts'), color: COLORS.impacts, values: series.impacts },
      { key: 'jammed', label: t('charts.jammed'), color: COLORS.jammed, values: series.jammed },
      { key: 'crashed', label: t('charts.crashed'), color: COLORS.crashed, values: series.crashed },
    ]
    // a series that stays at zero for the whole run is left out (it would only add a legend entry)
    return all.filter((s) => s.key === 'intercepted' || s.key === 'impacts' || s.values[s.values.length - 1] > 0)
  }, [series, t])

  const magazines = useMemo(() => {
    if (!series || series.magazineTotal <= 0) return null
    return series.shots.map((s) => Math.max(0, 100 - (s / series.magazineTotal) * 100))
  }, [series])

  if (!series) return <div className="muted">{t('charts.none')}</div>

  const legendFor = (rows: { label: string; color: string }[]) => rows.map((r) => ({ label: r.label, color: r.color }))

  return (
    <div className="chart-grid">
      <ChartCard
        title={t('charts.timeTitle')}
        subtitle={t('charts.timeSub')}
        legend={legendFor(lines)}
        table={{
          head: [t('charts.time'), ...lines.map((l) => l.label)],
          rows: sample(x).map(({ item, index }) => [formatSimTime(item), ...lines.map((l) => l.values[index])]),
        }}
      >
        <LineChart x={x} series={lines} ariaLabel={t('charts.timeTitle')} />
      </ChartCard>

      {waves && waves.length > 0 && (
        <ChartCard
          title={t('charts.wavesTitle')}
          subtitle={t('charts.wavesSub')}
          legend={[
            { label: t('charts.intercepted'), color: COLORS.intercepted },
            { label: t('charts.impacts'), color: COLORS.impacts },
            { label: t('charts.jammed'), color: COLORS.jammed },
            { label: t('charts.crashed'), color: COLORS.crashed },
            { label: t('charts.rest'), color: COLORS.rest },
          ]}
          table={{
            head: [t('charts.wave'), t('charts.size'), t('charts.launched'), t('charts.intercepted'), t('charts.impacts'), t('charts.jammed'), t('charts.crashed')],
            rows: waves.map((w) => [`${w.id} · ${unitLabel(w.kind)}`, w.count, w.launched, w.intercepted, w.impacted, w.jammed, w.crashed]),
          }}
        >
          <StackedBars
            ariaLabel={t('charts.wavesTitle')}
            restLabel={t('charts.rest')}
            restColor={COLORS.rest}
            rows={waves.map((w) => ({
              label: `${w.id} · ${unitLabel(w.kind)}`,
              sub: t('charts.launchedOf', { launched: w.launched, count: w.count }),
              total: w.count,
              segments: [
                { key: 'intercepted', label: t('charts.intercepted'), color: COLORS.intercepted, value: w.intercepted },
                { key: 'impacts', label: t('charts.impacts'), color: COLORS.impacts, value: w.impacted },
                { key: 'jammed', label: t('charts.jammed'), color: COLORS.jammed, value: w.jammed },
                { key: 'crashed', label: t('charts.crashed'), color: COLORS.crashed, value: w.crashed },
              ],
            }))}
          />
        </ChartCard>
      )}

      {magazines && (
        <ChartCard
          title={t('charts.ammoTitle')}
          subtitle={t('charts.ammoSub', { total: series.magazineTotal })}
          table={{
            head: [t('charts.time'), t('charts.ammoLeft')],
            rows: sample(x).map(({ item, index }) => [formatSimTime(item), `${Math.round(magazines[index])}%`]),
          }}
        >
          <LineChart
            x={x}
            yMax={100}
            yFormat={(v) => `${Math.round(v)}%`}
            height={170}
            ariaLabel={t('charts.ammoTitle')}
            series={[{ key: 'ammo', label: t('charts.ammoLeft'), color: COLORS.magazines, values: magazines, origin: 100 }]}
          />
        </ChartCard>
      )}

      {cost && (cost.attackUsd > 0 || cost.defenseUsd > 0) && (
        <ChartCard
          title={t('charts.costTitle')}
          subtitle={cost.attackUsd > 0 ? t('charts.costRatio', { ratio: (cost.defenseUsd / cost.attackUsd).toFixed(2) }) : t('charts.costSub')}
          table={{
            head: [t('charts.side'), 'USD'],
            rows: [
              [t('charts.attackCost'), formatMoney(cost.attackUsd)],
              [t('charts.defenseCost'), formatMoney(cost.defenseUsd)],
            ],
          }}
        >
          <CostChart
            ariaLabel={t('charts.costTitle')}
            rows={[
              { label: t('charts.attackCost'), usd: cost.attackUsd, color: COLORS.attack },
              { label: t('charts.defenseCost'), usd: cost.defenseUsd, color: COLORS.defense },
            ]}
          />
        </ChartCard>
      )}

      {warning && warning.impacts > 0 && (
        <ChartCard
          title={t('charts.warnTitle')}
          subtitle={warning.enabled ? t('charts.warnSub') : t('charts.warnOff')}
          table={{
            head: [t('charts.warnMetric'), t('charts.warnValue')],
            rows: [
              [t('charts.warnImpacts'), warning.impacts],
              [t('charts.warnUndetected'), `${warning.undetected} (${Math.round((100 * warning.undetected) / warning.impacts)}%)`],
              [t('charts.warnMean'), formatSimTime(warning.meanWarningS)],
              [t('charts.warnSheltered'), `${warning.meanShelteredPct.toFixed(0)}%`],
            ],
          }}
        >
          <div className="warn-tiles">
            <div className="warn-tile">
              <div className="warn-value">{formatSimTime(warning.meanWarningS)}</div>
              <div className="warn-label">{t('charts.warnMean')}</div>
            </div>
            <div className="warn-tile">
              <div className="warn-value">{Math.round((100 * warning.undetected) / warning.impacts)}%</div>
              <div className="warn-label">{t('charts.warnUndetected')}</div>
            </div>
            <div className="warn-tile">
              <div className="warn-value">{warning.enabled ? `${warning.meanShelteredPct.toFixed(0)}%` : '—'}</div>
              <div className="warn-label">{t('charts.warnSheltered')}</div>
            </div>
          </div>
        </ChartCard>
      )}

      {sensitivity && sensitivity.rows.length > 0 && (() => {
        const pct = Math.round(sensitivity.delta * 100)
        const rows = sensitivity.rows.filter((r) => r.metric === sensMetric)
        const factorName = (r: { factorId: string; factor: string }) => t(`charts.factor.${r.factorId}`, { defaultValue: r.factor })
        const metricName = (m: string) => t(`charts.sensMetric.${m}`, { defaultValue: m })
        return (
          <ChartCard
            title={t('charts.sensTitle')}
            subtitle={t('charts.sensSub', { pct, runs: sensitivity.runs })}
            legend={[
              { label: `−${pct}%`, color: COLORS.crashed },
              { label: `+${pct}%`, color: COLORS.magazines },
            ]}
            table={{
              head: [t('charts.sensFactor'), `−${pct}%`, `+${pct}%`],
              rows: rows.map((r) => [factorName(r), `${r.diffLow.toFixed(1)} (Z ${r.zLow.toFixed(1)})`, `${r.diffHigh.toFixed(1)} (Z ${r.zHigh.toFixed(1)})`]),
            }}
          >
            <div className="sens-metrics" role="group" aria-label={t('charts.sensMetricLabel')}>
              {sensitivity.metrics.map((m) => (
                <button key={m} className="chart-toggle" aria-pressed={m === sensMetric} onClick={() => setSensMetric(m)}>
                  {metricName(m)}
                </button>
              ))}
            </div>
            <div className="chart-sub">{t('charts.sensBaseline', { metric: metricName(sensMetric), value: rows[0] ? +rows[0].baseline.toFixed(1) : 0 })}</div>
            <TornadoChart
              ariaLabel={t('charts.sensTitle')}
              lowLabel={`−${pct}%`}
              highLabel={`+${pct}%`}
              lowColor={COLORS.crashed}
              highColor={COLORS.magazines}
              rows={rows.map((r) => ({ label: factorName(r), low: r.diffLow, high: r.diffHigh, zLow: r.zLow, zHigh: r.zHigh }))}
            />
          </ChartCard>
        )
      })()}

      {hist && (
        <div className="chart-pair">
          <ChartCard
            title={t('charts.killedTitle')}
            subtitle={t('charts.casualtySub', { trials: summary.trials })}
            table={{
              head: [t('charts.range'), t('charts.trials')],
              rows: hist.killed.map((c, i) => [`${+(i * hist.binWidth).toFixed(1)}–${+((i + 1) * hist.binWidth).toFixed(1)}`, c]),
            }}
          >
            <Histogram
              counts={hist.killed}
              binWidth={hist.binWidth}
              color={COLORS.killed}
              mean={summary.killed.mean}
              p5={summary.killed.p5}
              p95={summary.killed.p95}
              meanLabel={t('charts.mean')}
              tipLabel={(range, pct) => t('charts.histTip', { range, pct })}
              ariaLabel={t('charts.killedTitle')}
            />
          </ChartCard>
          <ChartCard
            title={t('charts.injuredTitle')}
            subtitle={t('charts.casualtySub', { trials: summary.trials })}
            table={{
              head: [t('charts.range'), t('charts.trials')],
              rows: hist.injured.map((c, i) => [`${+(i * hist.binWidth).toFixed(1)}–${+((i + 1) * hist.binWidth).toFixed(1)}`, c]),
            }}
          >
            <Histogram
              counts={hist.injured}
              binWidth={hist.binWidth}
              color={COLORS.injured}
              mean={summary.injured.mean}
              p5={summary.injured.p5}
              p95={summary.injured.p95}
              meanLabel={t('charts.mean')}
              tipLabel={(range, pct) => t('charts.histTip', { range, pct })}
              ariaLabel={t('charts.injuredTitle')}
            />
          </ChartCard>
        </div>
      )}
    </div>
  )
}
