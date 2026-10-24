import { HubConnectionBuilder } from '@microsoft/signalr'
import type { HubConnection } from '@microsoft/signalr'
import { MessagePackHubProtocol } from '@microsoft/signalr-protocol-msgpack'
import { decodeSnapshot } from './snapshot'
import type { ConnectionState, SimEvent, SimStatus, Snapshot } from './types'

type Listener<T> = (value: T) => void

class Emitter<T> {
  private listeners = new Set<Listener<T>>()

  /** Intoarce functia de dezabonare. */
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
 * Conexiunea catre hub-ul SignalR (MessagePack). Datele de frecventa mare (snapshot-urile)
 * ajung prin Emitter, nu prin starea React, ca sa nu re-randam UI-ul la fiecare mesaj.
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

    // conexiunea initiala nu se reia automat: incercam din nou cat timp serverul nu raspunde
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
