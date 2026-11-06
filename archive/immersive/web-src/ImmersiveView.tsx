import { useEffect, useRef, useState } from 'react'
import * as THREE from 'three'
import { Cartesian3, Rectangle } from 'cesium'
import type { Viewer } from 'cesium'
import { api } from './api'
import { BUTTON_LABELS, loadBunker, TERMINAL_LABELS } from './bunker'
import type { Bunker, ButtonId, TerminalId } from './bunker'
import { makeCommandScreen } from './commandScreen'
import { config, SPEEDS } from './config'
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
  /** Debug: reconstruieste tot Immersive (buncar + ecran Cesium). */
  onReset: () => void
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

const STATE_LABEL: Record<string, string> = { idle: 'oprit', running: 'rulează', paused: 'pauză', finished: 'terminat' }

export default function ImmersiveView({ client, connection, status, scenario, speed, onSpeed, onRestart, onMenu, onTo2D, onReset }: Props) {
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
  const [pauseMenu, setPauseMenu] = useState(true) // meniul e si ecranul de intrare (prima apasare = gestul pentru pointer lock)
  const [entered, setEntered] = useState(false)
  const [toast, setToast] = useState('')
  const [modelKey, setModelKey] = useState(0)
  const [debugInfo, setDebugInfo] = useState({ fps: 0, x: 0, z: 0 })
  const [cmd] = useState(makeCommandScreen)

  const overlayRef = useRef<Overlay>(null)
  const fullRef = useRef(false)
  const targetRef = useRef<string>('')
  const statusRef = useRef<SimStatus | null>(null)
  const speedRef = useRef(speed)
  const enteredRef = useRef(false) // a intrat macar o data in scena curenta (altfel Esc nu deschide meniul de pauza)
  const suppressRef = useRef(false) // iesire din pointer lock facuta de noi (E pe calculator), nu de Esc
  const pausedByMenuRef = useRef(false)
  const canvasRef = useRef<HTMLCanvasElement | null>(null)
  const resetPosRef = useRef<() => void>(() => undefined)
  const actionRef = useRef<(id: ButtonId) => void>(() => undefined)
  const pauseOpenRef = useRef<() => void>(() => undefined)
  const reloadRef = useRef<() => void>(() => undefined)
  const resetScenarioRef = useRef<() => void>(() => undefined)
  const toastTimer = useRef<number | undefined>(undefined)
  useEffect(() => {
    overlayRef.current = overlay
    fullRef.current = fullscreen
    statusRef.current = status
    speedRef.current = speed
  })

  const showToast = (text: string) => {
    setToast(text)
    window.clearTimeout(toastTimer.current)
    toastTimer.current = window.setTimeout(() => setToast(''), 1600)
  }
  useEffect(() => () => window.clearTimeout(toastTimer.current), [])

  const safe = (fn: () => Promise<unknown>) => {
    fn().catch((e: unknown) => showToast(e instanceof Error ? e.message : String(e)))
  }

  // butoanele fizice din buncar + tastele de debug: functiile se refac la fiecare randare, scena le apeleaza prin ref
  useEffect(() => {
    actionRef.current = (id) => {
      const state = statusRef.current?.state ?? 'idle'
      if (id === 'speedUp' || id === 'speedDown') {
        const at = SPEEDS.indexOf(speedRef.current)
        const base = at < 0 ? SPEEDS.indexOf(10) : at
        const next = SPEEDS[Math.min(Math.max(base + (id === 'speedUp' ? 1 : -1), 0), SPEEDS.length - 1)]
        onSpeed(next)
        showToast(`Viteză ${next}×`)
      } else if (id === 'pause') {
        if (state === 'running') {
          safe(api.pause)
          showToast('Pauză')
        } else if (state === 'paused') {
          safe(api.resume)
          showToast('Simularea continuă')
        } else showToast('Simularea nu rulează')
      } else {
        safe(onRestart)
        showToast('Scenariu repornit')
      }
    }
    pauseOpenRef.current = () => {
      setPauseMenu(true)
      if (statusRef.current?.state === 'running') {
        pausedByMenuRef.current = true
        safe(api.pause)
      }
    }
    reloadRef.current = () => {
      setLoaded(null)
      setPauseMenu(false)
      setOverlay(null)
      setFullscreen(false)
      setModelKey((k) => k + 1)
    }
    resetScenarioRef.current = () => {
      safe(onRestart)
      showToast('Scenariu repornit')
    }
  })

  // ecranul comandantului se redeseneaza cand se schimba starea sau viteza
  useEffect(() => {
    cmd.draw(status, speed)
  }, [cmd, status, speed, modelKey])

  const resumeIfPausedByMenu = () => {
    if (pausedByMenuRef.current) {
      pausedByMenuRef.current = false
      if (statusRef.current?.state === 'paused') safe(api.resume)
    }
  }
  const continueGame = () => {
    setPauseMenu(false)
    resumeIfPausedByMenu()
    const el = canvasRef.current
    if (el) Promise.resolve(el.requestPointerLock()).catch(() => undefined)
  }

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
    enteredRef.current = false
    setEntered(false)
    setPauseMenu(true)

    const renderer = new THREE.WebGLRenderer({ antialias: true })
    renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2))
    renderer.setSize(mount.clientWidth, mount.clientHeight)
    renderer.outputColorSpace = THREE.SRGBColorSpace
    mount.appendChild(renderer.domElement)
    renderer.domElement.className = 'three-canvas'
    canvasRef.current = renderer.domElement

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
    let frames = 0
    const fpsTimer = window.setInterval(() => {
      if (config.debug) setDebugInfo({ fps: frames * 2, x: Math.round(pos.x * 100) / 100, z: Math.round(pos.z * 100) / 100 })
      frames = 0
    }, 500)

    void loadBunker('/models/bunker.glb', config.debug).then((b) => {
      if (disposed) return
      bunker = b
      scene.add(b.root)
      if (b.screen) b.screen.material = new THREE.MeshBasicMaterial({ map: screen.tex, toneMapped: false })
      if (b.secondary) {
        cmd.setAspect(b.secondaryAspect)
        cmd.draw(statusRef.current, speedRef.current)
        b.secondary.material = new THREE.MeshBasicMaterial({ map: cmd.tex, toneMapped: false })
      }
      pos.copy(b.spawn)
      yaw = b.spawnYaw
      pitch = 0
      setReport(b.report)
      setLoaded(b.usedModel)
    })

    resetPosRef.current = () => {
      if (!bunker) return
      pos.copy(bunker.spawn)
      yaw = bunker.spawnYaw
      pitch = 0
    }

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
      frames++
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
      if (config.debug && (e.code === 'F7' || e.code === 'F8' || e.code === 'F9')) {
        e.preventDefault()
        if (e.code === 'F7') reloadRef.current()
        else if (e.code === 'F8') resetPosRef.current()
        else resetScenarioRef.current()
        return
      }
      keys.add(e.code)
      if (e.code === 'KeyE' && overlayRef.current === null && !fullRef.current && targetRef.current) {
        if (targetRef.current.startsWith('btn:')) {
          actionRef.current(targetRef.current.slice(4) as ButtonId)
          return
        }
        suppressRef.current = true
        document.exitPointerLock()
        if (targetRef.current === 'screen') setFullscreen(true)
        else setOverlay(targetRef.current as TerminalId)
      }
    }
    const onKeyUp = (e: KeyboardEvent) => keys.delete(e.code)
    const onLock = () => {
      const isLocked = document.pointerLockElement === renderer.domElement
      setLocked(isLocked)
      if (isLocked) {
        enteredRef.current = true
        setEntered(true)
        return
      }
      // Esc: meniu de pauza (nu cand am iesit noi din pointer lock pentru un calculator sau ecranul complet)
      if (suppressRef.current) {
        suppressRef.current = false
        return
      }
      if (enteredRef.current && overlayRef.current === null && !fullRef.current) pauseOpenRef.current()
    }
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
      window.clearInterval(fpsTimer)
      resetPosRef.current = () => undefined
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
  }, [screen, cmd, modelKey])

  // Esc inchide fereastra / ecranul complet / meniul de pauza
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.code !== 'Escape') return
      if (overlay !== null) setOverlay(null)
      else if (fullscreen) setFullscreen(false)
      else if (pauseMenu) continueGame()
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [overlay, fullscreen, pauseMenu])

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

  const promptLabel = prompt === 'screen' ? 'Ecranul mare (mod ecran complet)' : prompt.startsWith('btn:') ? BUTTON_LABELS[prompt.slice(4) as ButtonId] : prompt ? TERMINAL_LABELS[prompt as TerminalId] : ''
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
      {locked && <div className="crosshair" />}
      {locked && promptLabel && (
        <div className="imm-prompt">
          <kbd>E</kbd> {promptLabel}
        </div>
      )}
      {focusName && locked && <div className="imm-focus">Ecran: {focusName}</div>}
      {toast && <div className="imm-toast">{toast}</div>}
      {config.debug && (
        <div className="imm-debug">
          <strong>DEBUG</strong> · {debugInfo.fps} fps · x {debugInfo.x} z {debugInfo.z}
          <div>
            server {config.serverUrl} ({connection}) · F7 model · F8 poziție · F9 scenariu
          </div>
        </div>
      )}

      {pauseMenu && (
        <div className="modal-back">
          <div className="modal narrow pause-menu" onClick={(e) => e.stopPropagation()}>
            <div className="title">
              <span>
                {entered ? 'Pauză' : 'Buncăr'} · scenariu <strong>{STATE_LABEL[status?.state ?? 'idle'] ?? status?.state}</strong>
              </span>
            </div>
            <button className="primary" onClick={continueGame} disabled={loaded === null}>
              <Icon name="play" size={18} /> {loaded === null ? 'Se încarcă buncărul…' : entered ? 'Continuă' : 'Intră în buncăr'}
            </button>
            <div className="muted small">Viteză simulare</div>
            <div className="row" style={{ gap: 6 }}>
              {SPEEDS.map((v) => (
                <button key={v} className={v === speed ? 'primary' : ''} onClick={() => onSpeed(v)} disabled={!connected} style={{ flex: 1 }}>
                  x{v}
                </button>
              ))}
            </div>
            <button
              onClick={() => {
                pausedByMenuRef.current = false
                safe(onRestart)
                continueGame()
              }}
              disabled={!connected}
            >
              <Icon name="restart" size={18} /> Repornește scenariul
            </button>
            <button
              onClick={() => {
                resumeIfPausedByMenu()
                onTo2D()
              }}
            >
              <Icon name="layers" size={18} /> Vizualizare 2D
            </button>
            <button onClick={onMenu}>
              <Icon name="home" size={18} /> Meniu principal
            </button>
            {config.debug && (
              <>
                <div className="muted small">Debug</div>
                <button onClick={() => reloadRef.current()}>Reîncarcă modelul (F7)</button>
                <button onClick={() => resetPosRef.current()}>Reset poziție (F8)</button>
                <button onClick={onReset}>Reset Immersive (buncăr + ecran)</button>
              </>
            )}
            <div className="muted small">W A S D — mers · mouse — privire · Shift — alergi · E — folosești · Esc — pauză</div>
            {report.map((l) => (
              <div key={l} className="small" style={{ color: l.startsWith('LIPSE') || l.startsWith('Niciun') ? '#f59e0b' : undefined }}>
                {l}
              </div>
            ))}
            {loaded === false && <div className="muted small">Model placeholder; pune `bunker.glb` în `web/public/models/`.</div>}
          </div>
        </div>
      )}

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
