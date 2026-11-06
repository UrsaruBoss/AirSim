import { useEffect, useState } from 'react'
import type { SimClient } from './SimClient'
import { api } from './api'
import type { ConnectionState, RunSummary, SimEvent, SimStatus } from './types'

/** Starts the connection while the component is mounted and exposes its state. */
export function useConnection(client: SimClient): ConnectionState {
  const [state, setState] = useState<ConnectionState>('connecting')

  useEffect(() => {
    const off = client.connection.on(setState)
    void client.start()
    return () => {
      off()
      void client.stop()
    }
  }, [client])

  return state
}

export function useSimStatus(client: SimClient): SimStatus | null {
  const [status, setStatus] = useState<SimStatus | null>(null)

  useEffect(() => client.statuses.on(setStatus), [client])

  return status
}

interface LogStore {
  epoch: number | undefined
  items: SimEvent[]
}

/** The last `max` events, newest first. The log belongs to one run (epoch); another run => empty. */
export function useEventLog(client: SimClient, epoch: number | undefined, max = 200): SimEvent[] {
  const [store, setStore] = useState<LogStore>({ epoch, items: [] })

  useEffect(
    () =>
      client.events.on((batch) => {
        setStore((prev) => {
          const base = prev.epoch === epoch ? prev.items : []
          const items = [...batch].reverse().concat(base)
          return { epoch, items: items.length > max ? items.slice(0, max) : items }
        })
      }),
    [client, epoch, max],
  )

  return store.epoch === epoch ? store.items : []
}

interface SummaryStore {
  key: string
  data: RunSummary
}

/** Run statistics: loaded when the simulation is paused or finished (when the log no longer changes). */
export function useSummary(status: SimStatus | null): RunSummary | null {
  const [store, setStore] = useState<SummaryStore | null>(null)

  const settled = status !== null && (status.state === 'paused' || status.state === 'finished')
  const key = settled ? `${status.epoch}:${status.state}:${status.tick}` : ''

  useEffect(() => {
    if (!key) return
    let cancelled = false
    api
      .summary()
      .then((data) => {
        if (!cancelled) setStore({ key, data })
      })
      .catch(() => {
        // no statistics: the panel stays hidden
      })
    return () => {
      cancelled = true
    }
  }, [key])

  return key && store?.key === key ? store.data : null
}

/** The breakpoint under which the phone layout is used (kept in sync with the CSS media queries). */
export const NARROW_QUERY = '(max-width: 800px)'

export const isNarrowNow = (): boolean => typeof window !== 'undefined' && window.matchMedia(NARROW_QUERY).matches

/** Tracks a CSS media query (e.g. the phone layout). */
export function useMediaQuery(query: string = NARROW_QUERY): boolean {
  const [matches, setMatches] = useState(() => typeof window !== 'undefined' && window.matchMedia(query).matches)

  useEffect(() => {
    const mq = window.matchMedia(query)
    const onChange = () => setMatches(mq.matches)
    onChange()
    mq.addEventListener('change', onChange)
    return () => mq.removeEventListener('change', onChange)
  }, [query])

  return matches
}
