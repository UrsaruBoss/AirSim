import { useEffect, useState } from 'react'
import type { SimClient } from './SimClient'
import { api } from './api'
import type { ConnectionState, RunSummary, SimEvent, SimStatus } from './types'

/** Porneste conexiunea cat timp componenta e montata si expune starea ei. */
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

/** Ultimele `max` evenimente, cele mai noi primele. Log-ul apartine unei rulari (epoch); alta rulare => gol. */
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

/** Statisticile rularii: se incarca cand simularea e in pauza sau terminata (cand log-ul nu se mai schimba). */
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
        // fara statistici: panoul ramane ascuns
      })
    return () => {
      cancelled = true
    }
  }, [key])

  return key && store?.key === key ? store.data : null
}
