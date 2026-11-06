import { HubConnectionBuilder } from '@microsoft/signalr'
import type { HubConnection } from '@microsoft/signalr'
import { MessagePackHubProtocol } from '@microsoft/signalr-protocol-msgpack'
import { decodeSnapshot } from './snapshot'
import type { ConnectionState, SimEvent, SimStatus, Snapshot } from './types'

type Listener<T> = (value: T) => void

class Emitter<T> {
  private listeners = new Set<Listener<T>>()

  /** Returns the unsubscribe function. */
  on(listener: Listener<T>): () => void {
    this.listeners.add(listener)
    return () => {
      this.listeners.delete(listener)
    }
  }

  emit(value: T): void {
    for (const listener of this.listeners) listener(value)
  }
}

const sleep = (ms: number) => new Promise<void>((resolve) => setTimeout(resolve, ms))

/**
 * The connection to the SignalR hub (MessagePack). High-frequency data (snapshots)
 * flows through an Emitter, not through React state, so the UI is not re-rendered on every message.
 */
export class SimClient {
  readonly snapshots = new Emitter<Snapshot>()
  readonly events = new Emitter<SimEvent[]>()
  readonly statuses = new Emitter<SimStatus>()
  readonly connection = new Emitter<ConnectionState>()

  private hub: HubConnection | null = null
  private stopped = true

  async start(): Promise<void> {
    this.stopped = false

    const hub = new HubConnectionBuilder()
      .withUrl('/hubs/sim')
      .withHubProtocol(new MessagePackHubProtocol())
      .withAutomaticReconnect()
      .build()
    this.hub = hub

    hub.on('snapshot', (data: Uint8Array | ArrayBuffer) => {
      const bytes = data instanceof Uint8Array ? data : new Uint8Array(data)
      this.snapshots.emit(decodeSnapshot(bytes))
    })
    hub.on('events', (events: SimEvent[]) => this.events.emit(events))
    hub.on('status', (status: SimStatus) => this.statuses.emit(status))

    hub.onreconnecting(() => this.connection.emit('reconnecting'))
    hub.onreconnected(() => this.connection.emit('connected'))
    hub.onclose(() => {
      if (!this.stopped) this.connection.emit('disconnected')
    })

    // the initial connection is not retried automatically: keep trying while the server does not answer
    this.connection.emit('connecting')
    while (!this.stopped && this.hub === hub) {
      try {
        await hub.start()
        this.connection.emit('connected')
        return
      } catch {
        if (this.stopped || this.hub !== hub) return
        this.connection.emit('disconnected')
        await sleep(2000)
      }
    }
  }

  async stop(): Promise<void> {
    this.stopped = true
    const hub = this.hub
    this.hub = null
    if (hub) await hub.stop()
  }
}
