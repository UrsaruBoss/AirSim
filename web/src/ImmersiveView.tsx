import { useEffect, useRef, useState } from 'react'
import * as THREE from 'three'
import { Cartesian3, Rectangle } from 'cesium'
import type { Viewer } from 'cesium'
import { loadBunker, TERMINAL_LABELS } from './bunker'
import type { Bunker, TerminalId } from './bunker'
import CesiumView from './CesiumView'
import EventsPanel from './EventsPanel'
import { useEventLog, useSummary } from './hooks'
import Icon from './Icons'
import ResultsModal from './ResultsModal'
import type { SimClient } from './SimClient'
import { ControlTerminal, StatsTerminal } from './TerminalPanels'
import type { FocusTarget } from './TerminalPanels'
import type { ConnectionState, Scenario, SimStatus } from './types'

// raport 2.5:1 (ecranul din model are 6.25 x 2.5 m); aceleasi valori si in index.css (.imm-cesium)
const SCREEN_W = 1800
const SCREEN_H = 720
const REACH_M = 2.6
const WALK_MPS = 2.6
const RADIUS = 0.3

type Overlay = TerminalId | null

interface Props {
  client: SimClient
  connection: ConnectionState
  status: SimStatus | null
  scenario: Scenario
  speed: number
  onSpeed: (v: number) => void
  onRestart: () => Promise<void>
  onMenu: () => void
  onTo2D: () => void
}

function overviewRectangle(scenario: Scenario): Rectangle {
  const pts = [...scenario.launchSites, ...scenario.targets, ...scenario.defenses]
  if (pts.length === 0) return Rectangle.fromDegrees(-12, 33, 52, 68)
  const lats = pts.map((p) => p.latDeg)
  const lons = pts.map((p) => p.lonDeg)
  const padLat = Math.max((Math.max(...lats) - Math.min(...lats)) * 0.2, 1)
  const padLon = Math.max((Math.max(...lons) - Math.min(...lons)) * 0.2, 1)
  return Rectangle.fromDegrees(
    Math.max(Math.min(...lons) - padLon, -180),
    Math.max(Math.min(...lats) - padLat, -85),
    Math.min(Math.max(...lons) + padLon, 180),
    Math.min(Math.max(...lats) + padLat, 85),
  )
}

function makeScreenTexture() {
  const canvas = document.createElement('canvas')
  canvas.width = SCREEN_W
  canvas.height = SCREEN_H
  const ctx = canvas.getContext('2d')!
  ctx.fillStyle = '#0a1018'
  ctx.fillRect(0, 0, SCREEN_W, SCREEN_H)
  const tex = new THREE.CanvasTexture(canvas)
  tex.colorSpace = THREE.SRGBColorSpace
  return { canvas, ctx, tex }
}

export default function ImmersiveView({ client, connection, status, scenario, speed, onSpeed, onRestart, onMenu, onTo2D }: Props) {
  const mountRef = useRef<HTMLDivElement>(null)
  const [screen] = useState(makeScreenTexture)
  const [viewer, setViewer] = useState<Viewer | null>(null)
  const [overlay, setOverlay] = useState<Overlay>(null)
  const [fullscreen, setFullscreen] = useState(false)
  const [prompt, setPrompt] = useState('')
  const [locked, setLocked] = useState(false)
  const [loaded, setLoaded] = useState<null | boolean>(null)
  const [report, setReport] = useState<string[]>([])
  const [focusName, setFocusName] = useState('')

  const overlayRef = useRef<Overlay>(null)
  const fullRef = useRef(false)
  const targetRef = useRef<string>('')
  useEffect(() => {
    overlayRef.current = overlay
    fullRef.current = fullscreen
  })

  const log = useEventLog(client, status?.epoch, 3000)
  const summary = useSummary(status)

  // ---- Cesium -> textura ecranului mare ----
  useEffect(() => {
    if (!viewer) return
    let last = 0
    const copy = () => {
      if (fullRef.current) return
      const now = performance.now()
      if (now - last < 60) return
      last = now
      screen.ctx.drawImage(viewer.scene.canvas, 0, 0, SCREEN_W, SCREEN_H)
      screen.tex.needsUpdate = true
    }
    viewer.scene.postRender.addEventListener(copy)
    return () => {
      if (!viewer.isDestroyed()) viewer.scene.postRender.removeEventListener(copy)
    }
  }, [viewer, screen])

  // ---- scena three.js ----
  useEffect(() => {
    const mount = mountRef.current
    if (!mount) return
    let disposed = false

    const renderer = new THREE.WebGLRenderer({ antialias: true })
    renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2))
    renderer.setSize(mount.clientWidth, mount.clientHeight)
    renderer.outputColorSpace = THREE.SRGBColorSpace
    mount.appendChild(renderer.domElement)
    renderer.domElement.className = 'three-canvas'

    const scene = new THREE.Scene()
    scene.background = new THREE.Color(0x05070a)
    scene.fog = new THREE.Fog(0x05070a, 12, 30)
    const camera = new THREE.PerspectiveCamera(72, mount.clientWidth / mount.clientHeight, 0.05, 60)
    camera.rotation.order = 'YXZ'

    let bunker: Bunker | null = null
    let yaw = 0
    let pitch = 0
    const pos = new THREE.Vector3(0, 1.65, 3.6)
    const keys = new Set<string>()
    const ray = new THREE.Raycaster()
    ray.far = REACH_M
    const clock = new THREE.Clock()
    let frame = 0
    let lastPrompt = ''

    void loadBunker().then((b) => {
      if (disposed) return
      bunker = b
      scene.add(b.root)
      if (b.screen) b.screen.material = new THREE.MeshBasicMaterial({ map: screen.tex, toneMapped: false })
      pos.copy(b.spawn)
      yaw = b.spawnYaw
      setReport(b.report)
      setLoaded(b.usedModel)
    })

    const resolve = () => {
      if (!bunker) return
      const b = bunker.bounds
      pos.x = Math.min(Math.max(pos.x, b.min.x), b.max.x)
      pos.z = Math.min(Math.max(pos.z, b.min.z), b.max.z)
      for (const box of bunker.colliders) {
        if (pos.x > box.min.x - RADIUS && pos.x < box.max.x + RADIUS && pos.z > box.min.z - RADIUS && pos.z < box.max.z + RADIUS) {
          const dl = pos.x - (box.min.x - RADIUS)
          const dr = box.max.x + RADIUS - pos.x
          const du = pos.z - (box.min.z - RADIUS)
          const dd = box.max.z + RADIUS - pos.z
          const m = Math.min(dl, dr, du, dd)
          if (m === dl) pos.x = box.min.x - RADIUS
          else if (m === dr) pos.x = box.max.x + RADIUS
          else if (m === du) pos.z = box.min.z - RADIUS
          else pos.z = box.max.z + RADIUS
        }
      }
    }

    const interactTarget = (): string => {
      if (!bunker || bunker.interactables.length === 0) return ''
      ray.setFromCamera(new THREE.Vector2(0, 0), camera)
      const hit = ray.intersectObjects(bunker.interactables, false)[0]
      return hit ? String(hit.object.userData.interact ?? '') : ''
    }

    const animate = () => {
      frame = requestAnimationFrame(animate)
      const dt = Math.min(clock.getDelta(), 0.05)
      const active = document.pointerLockElement === renderer.domElement && overlayRef.current === null && !fullRef.current
      if (active) {
        const f = (keys.has('KeyW') || keys.has('ArrowUp') ? 1 : 0) - (keys.has('KeyS') || keys.has('ArrowDown') ? 1 : 0)
        const r = (keys.has('KeyD') || keys.has('ArrowRight') ? 1 : 0) - (keys.has('KeyA') || keys.has('ArrowLeft') ? 1 : 0)
        if (f !== 0 || r !== 0) {
          const len = Math.hypot(f, r)
          const speed = WALK_MPS * (keys.has('ShiftLeft') ? 1.7 : 1) * dt
          pos.x += ((-Math.sin(yaw) * f + Math.cos(yaw) * r) / len) * speed
          pos.z += ((-Math.cos(yaw) * f - Math.sin(yaw) * r) / len) * speed
          resolve()
        }
      }
      camera.position.copy(pos)
      camera.rotation.set(pitch, yaw, 0)

      const target = active ? interactTarget() : ''
      if (target !== lastPrompt) {
        lastPrompt = target
        targetRef.current = target
        setPrompt(target)
      }
      renderer.render(scene, camera)
    }
    animate()

    const onMove = (e: MouseEvent) => {
      if (document.pointerLockElement !== renderer.domElement) return
      yaw -= e.movementX * 0.0022
      pitch = Math.max(-1.35, Math.min(1.35, pitch - e.movementY * 0.0022))
    }
    const onKeyDown = (e: KeyboardEvent) => {
      if ((e.target as HTMLElement | null)?.tagName === 'INPUT') return
      keys.add(e.code)
      if (e.code === 'KeyE' && overlayRef.current === null && !fullRef.current && targetRef.current) {
        document.exitPointerLock()
        if (targetRef.current === 'screen') setFullscreen(true)
        else setOverlay(targetRef.current as TerminalId)
      }
    }
    const onKeyUp = (e: KeyboardEvent) => keys.delete(e.code)
    const onLock = () => setLocked(document.pointerLockElement === renderer.domElement)
    const onResize = () => {
      renderer.setSize(mount.clientWidth, mount.clientHeight)
      camera.aspect = mount.clientWidth / mount.clientHeight
      camera.updateProjectionMatrix()
    }
    const onClick = () => {
      if (overlayRef.current === null && !fullRef.current && document.pointerLockElement !== renderer.domElement) {
        void renderer.domElement.requestPointerLock()
      }
    }
    const onBlur = () => keys.clear()

    renderer.domElement.addEventListener('click', onClick)
    document.addEventListener('mousemove', onMove)
    document.addEventListener('pointerlockchange', onLock)
    window.addEventListener('keydown', onKeyDown)
    window.addEventListener('keyup', onKeyUp)
    window.addEventListener('resize', onResize)
    window.addEventListener('blur', onBlur)

    return () => {
      disposed = true
      cancelAnimationFrame(frame)
      if (document.pointerLockElement === renderer.domElement) document.exitPointerLock()
      renderer.domElement.removeEventListener('click', onClick)
      document.removeEventListener('mousemove', onMove)
      document.removeEventListener('pointerlockchange', onLock)
      window.removeEventListener('keydown', onKeyDown)
      window.removeEventListener('keyup', onKeyUp)
      window.removeEventListener('resize', onResize)
      window.removeEventListener('blur', onBlur)
      scene.traverse((o) => {
        const m = o as THREE.Mesh
        if (m.isMesh) {
          m.geometry.dispose()
          const mat = m.material
          for (const x of Array.isArray(mat) ? mat : [mat]) x.dispose()
        }
      })
      renderer.dispose()
      renderer.domElement.remove()
    }
  }, [screen])

  // Esc inchide fereastra / ecranul complet
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.code !== 'Escape') return
      if (overlay !== null) setOverlay(null)
      else if (fullscreen) setFullscreen(false)
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [overlay, fullscreen])

  const flyTo = (t: FocusTarget) => {
    if (viewer && !viewer.isDestroyed()) viewer.camera.flyTo({ destination: Cartesian3.fromDegrees(t.lonDeg, t.latDeg, 120_000), duration: 2 })
    setFocusName(t.name)
    setOverlay(null)
  }
  const overview = () => {
    if (viewer && !viewer.isDestroyed()) viewer.camera.flyTo({ destination: overviewRectangle(scenario), duration: 2 })
    setFocusName('')
    setOverlay(null)
  }

  const promptLabel = prompt === 'screen' ? 'Ecranul mare (mod ecran complet)' : prompt ? TERMINAL_LABELS[prompt as TerminalId] : ''
  const connected = connection === 'connected'

  return (
    <div className="immersive">
      <div className="three-mount" ref={mountRef} />

      {/* Cesium randat in afara ecranului; apare pe ecranul mare prin textura (sau pe tot ecranul, la cerere) */}
      <div className={fullscreen ? 'cesium-full' : 'cesium-off'}>
        <CesiumView client={client} scenario={scenario} fitKey="immersive" onViewer={setViewer} />
      </div>
      {fullscreen && <div className="full-hint">Esc — înapoi în buncăr</div>}

      <div className="imm-hud">
        <button onClick={onMenu} title="Oprește simularea și întoarce-te la meniu">
          <Icon name="home" /> Meniu principal
        </button>
        <button onClick={onTo2D}>
          <Icon name="layers" /> Vizualizare 2D
        </button>
      </div>

      {loaded === null && <div className="imm-center muted">Se încarcă buncărul…</div>}
      {loaded !== null && !locked && overlay === null && !fullscreen && (
        <div className="imm-center">
          <strong>Click pentru a intra</strong>
          <div className="muted small">W A S D — mers · mouse — privire · Shift — alergi · E — folosești · Esc — eliberezi mouse-ul</div>
          {report.map((l) => (
            <div key={l} className="small" style={{ color: l.startsWith('LIPSE') || l.startsWith('Niciun') ? '#f59e0b' : undefined }}>
              {l}
            </div>
          ))}
          {loaded === false && <div className="muted small">Model placeholder; pune `bunker.glb` în `web/public/models/` ca să-l folosești pe al tău.</div>}
        </div>
      )}
      {locked && <div className="crosshair" />}
      {locked && promptLabel && (
        <div className="imm-prompt">
          <kbd>E</kbd> {promptLabel}
        </div>
      )}
      {focusName && locked && <div className="imm-focus">Ecran: {focusName}</div>}

      {overlay === 'stats' && (
        <StatsTerminal status={status} speed={speed} onSpeed={onSpeed} onRestart={onRestart} connected={connected} onClose={() => setOverlay(null)} />
      )}
      {overlay === 'events' && (
        <div className="modal-back" onClick={() => setOverlay(null)}>
          <div className="modal" onClick={(e) => e.stopPropagation()}>
            <EventsPanel events={log} large onClose={() => setOverlay(null)} />
          </div>
        </div>
      )}
      {overlay === 'results' &&
        (summary ? (
          <ResultsModal summary={summary} status={status} partial={status?.state !== 'finished'} onClose={() => setOverlay(null)} />
        ) : (
          <div className="modal-back" onClick={() => setOverlay(null)}>
            <div className="modal narrow" onClick={(e) => e.stopPropagation()}>
              <div className="title">
                <span>Rezultate</span>
                <button className="icon-btn" onClick={() => setOverlay(null)}>
                  <Icon name="close" size={18} />
                </button>
              </div>
              <div className="muted">Rezultatele apar când simularea e în pauză sau terminată (folosește calculatorul de statistici).</div>
            </div>
          </div>
        ))}
      {overlay === 'control' && <ControlTerminal scenario={scenario} focus={focusName} onFocus={flyTo} onOverview={overview} onClose={() => setOverlay(null)} />}
    </div>
  )
}
