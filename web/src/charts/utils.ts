import { useEffect, useState } from 'react'
import type { RefObject } from 'react'

/** Width of an element, kept up to date (the charts lay themselves out in pixels, so text is never stretched). */
export function useWidth(ref: RefObject<HTMLElement | null>): number {
  const [w, setW] = useState(0)
  useEffect(() => {
    const el = ref.current
    if (!el) return
    const update = () => setW(Math.floor(el.getBoundingClientRect().width))
    update()
    const ro = new ResizeObserver(update)
    ro.observe(el)
    return () => ro.disconnect()
  }, [ref])
  return w
}

/** A "nice" upper bound for an axis (1, 2, 2.5, 5, 10 x 10^n). */
export function niceMax(v: number): number {
  if (!(v > 0)) return 1
  const exp = Math.floor(Math.log10(v))
  const f = v / 10 ** exp
  const nice = f <= 1 ? 1 : f <= 2 ? 2 : f <= 2.5 ? 2.5 : f <= 5 ? 5 : 10
  return nice * 10 ** exp
}

/** Evenly spaced ticks from 0 to top (inclusive). */
export function ticks(top: number, count = 4): number[] {
  return Array.from({ length: count + 1 }, (_, i) => (top * i) / count)
}

/** Time ticks (seconds) at a round step so that there are about `target` of them. */
export function timeTicks(endS: number, target = 5): number[] {
  const steps = [60, 300, 600, 900, 1800, 3600, 7200, 10800, 21600, 43200, 86400]
  const step = steps.find((s) => endS / s <= target) ?? steps[steps.length - 1]
  const out: number[] = []
  for (let t = 0; t <= endS + 1e-6; t += step) out.push(t)
  return out
}

/** Compact number: 1,284 / 12.9K / 3.4M. */
export function compact(v: number): string {
  const a = Math.abs(v)
  if (a >= 1e9) return `${+(v / 1e9).toFixed(1)}B`
  if (a >= 1e6) return `${+(v / 1e6).toFixed(1)}M`
  if (a >= 1e4) return `${+(v / 1e3).toFixed(1)}K`
  return String(Math.round(v * 10) / 10)
}

/** Axis time label: h:mm. */
export function clock(seconds: number): string {
  const m = Math.round(seconds / 60)
  return `${Math.floor(m / 60)}:${String(m % 60).padStart(2, '0')}`
}
