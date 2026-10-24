import type { Estimate } from './types'

export function formatSimTime(totalSeconds: number): string {
  const s = Math.floor(totalSeconds)
  const h = Math.floor(s / 3600)
  const m = Math.floor((s % 3600) / 60)
  const sec = s % 60
  return `${h}:${String(m).padStart(2, '0')}:${String(sec).padStart(2, '0')}`
}

export function formatEstimate(e: Estimate): string {
  const round = (v: number) => (v >= 100 ? Math.round(v) : Math.round(v * 10) / 10)
  return `${round(e.mean)} (${round(e.p5)}–${round(e.p95)})`
}

export const CLASS_LABEL: Record<string, string> = {
  city: 'Oraș',
  military_base: 'Bază militară',
  field: 'Câmp',
}
