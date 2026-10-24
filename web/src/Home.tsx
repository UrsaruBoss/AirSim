import type { ConnectionState, SimStatus } from './types'

const CONNECTION_LABEL: Record<ConnectionState, string> = {
  connecting: 'se conectează…',
  connected: 'conectat',
  reconnecting: 'reconectare…',
  disconnected: 'server indisponibil',
}

interface Props {
  connection: ConnectionState
  status: SimStatus | null
  onNormal: () => void
  onRuns: () => void
  onCompare: () => void
  onImmersive: () => void
  onResume: () => void
}

export default function Home({ connection, status, onNormal, onRuns, onCompare, onImmersive, onResume }: Props) {
  const active = status !== null && (status.state === 'running' || status.state === 'paused')

  return (
    <div className="home">
      <div className="home-inner">
        <h1>AirSim</h1>
        <p className="muted">Simulator de atac și apărare aeriană (drone și rachete)</p>
        <span className={`badge ${connection === 'connected' ? 'ok' : 'warn'}`}>{CONNECTION_LABEL[connection]}</span>

        <div className="modes">
          <button className="mode" onClick={onNormal}>
            <strong>Simulare normală</strong>
            <span>Alegi țările și aliații, plasezi apărarea și forțele de atac pe hartă, apoi rulezi simularea.</span>
          </button>
          <button className="mode" onClick={onRuns}>
            <strong>Rulări salvate</strong>
            <span>Reiei (replay) o rulare salvată, identică datorită seed-ului.</span>
          </button>
          <button className="mode" onClick={onCompare}>
            <strong>Compară scenarii</strong>
            <span>Două scenarii, aceleași seed-uri, diferențe cu semnificație statistică.</span>
          </button>
          <button className="mode" onClick={onImmersive}>
            <strong>Immersive</strong>
            <span>Intri într-un buncăr în prim-plan: ecran mare cu harta, calculatoare cu statistici și control.</span>
          </button>
        </div>

        {active && (
          <button className="resume" onClick={onResume}>
            Revino la simularea în curs ({status?.scenarioName})
          </button>
        )}
      </div>
    </div>
  )
}
