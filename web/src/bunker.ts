import * as THREE from 'three'
import { GLTFLoader } from 'three/examples/jsm/loaders/GLTFLoader.js'
import { applyScreenUV } from './screenUV'

/**
 * Buncarul. Daca exista /models/bunker.glb se foloseste el, altfel se construieste unul procedural (placeholder).
 *
 * Conventia de nume pentru modelul din Blender (obiecte sau nod-uri radacina din glTF):
 *   Screen_Main        mesh plan cu UV 0..1 (16:9): pe el apare Cesium
 *   Terminal_Stats     calculator: statistici si controlul simularii
 *   Terminal_Events    calculator: evenimente
 *   Terminal_Results   calculator: rezultate
 *   Terminal_Control   calculatorul liber: alegi ce controlezi / ce arata ecranul mare
 *   Screen_Secondary   ecranul comandantului (orice orientare): afiseaza starea simularii; UV-urile se recalculeaza singure
 *   Button_SpeedUp / Button_SpeedDown / Button_Pause / Button_Reset   butoane fizice (tasta E): viteza +/-, pauza/reia, repornire
 *   Collider_*         mobilier vizibil care blocheaza trecerea (o cutie per mesh)
 *   Blocker_*          cutie invizibila care blocheaza trecerea
 *   Spawn              empty: pozitia si directia de start (privirea pe -Z local)
 * Unitati: metri; Y in sus (exportul glTF din Blender face conversia).
 */

export type TerminalId = 'stats' | 'events' | 'results' | 'control'

export const TERMINAL_LABELS: Record<TerminalId, string> = {
  stats: 'STATISTICI',
  events: 'EVENIMENTE',
  results: 'REZULTATE',
  control: 'CONTROL',
}

export type ButtonId = 'speedUp' | 'speedDown' | 'pause' | 'reset'

export const BUTTON_LABELS: Record<ButtonId, string> = {
  speedUp: 'Viteză +',
  speedDown: 'Viteză −',
  pause: 'Pauză / Reia',
  reset: 'Repornește scenariul',
}

/** Numele din Blender -> id-ul butonului. */
const BUTTON_NAMES: Record<string, ButtonId> = {
  Button_SpeedUp: 'speedUp',
  Button_SpeedDown: 'speedDown',
  Button_Pause: 'pause',
  Button_Reset: 'reset',
}

export interface Bunker {
  root: THREE.Object3D
  screen: THREE.Mesh | null
  /** Ecranul comandantului (Screen_Secondary), daca exista, si raportul lui latime / inaltime. */
  secondary: THREE.Mesh | null
  secondaryAspect: number
  /** Obiecte pe care se poate apasa E (userData.interact = id terminal, 'screen' sau 'btn:<id>'). */
  interactables: THREE.Object3D[]
  colliders: THREE.Box3[]
  bounds: THREE.Box3
  spawn: THREE.Vector3
  spawnYaw: number
  usedModel: boolean
  /** Verificare model: ce nume s-au gasit, dimensiuni (doar pentru GLB). */
  report: string[]
}

const EYE = 1.65

function labelTexture(text: string, color: string): THREE.CanvasTexture {
  const c = document.createElement('canvas')
  c.width = 256
  c.height = 160
  const g = c.getContext('2d')!
  g.fillStyle = '#06101a'
  g.fillRect(0, 0, 256, 160)
  g.strokeStyle = color
  g.lineWidth = 4
  g.strokeRect(6, 6, 244, 148)
  g.fillStyle = color
  g.font = 'bold 30px system-ui, sans-serif'
  g.textAlign = 'center'
  g.textBaseline = 'middle'
  g.fillText(text, 128, 70)
  g.font = '16px system-ui, sans-serif'
  g.fillStyle = '#7f93aa'
  g.fillText('apasă E', 128, 112)
  const t = new THREE.CanvasTexture(c)
  t.colorSpace = THREE.SRGBColorSpace
  return t
}

const concrete = (c: number) => new THREE.MeshStandardMaterial({ color: c, roughness: 0.95, metalness: 0.02, flatShading: true })

function addBox(parent: THREE.Object3D, w: number, h: number, d: number, x: number, y: number, z: number, mat: THREE.Material): THREE.Mesh {
  const m = new THREE.Mesh(new THREE.BoxGeometry(w, h, d), mat)
  m.position.set(x, y, z)
  parent.add(m)
  return m
}

function buildProcedural(): Bunker {
  const root = new THREE.Group()
  const W = 10
  const D = 10
  const H = 3.2
  const wall = concrete(0x5b616b)
  const floorMat = concrete(0x3a3e45)
  const ceilMat = concrete(0x474c55)

  // camera
  const plane = (w: number, h: number, mat: THREE.Material, pos: [number, number, number], rot: [number, number, number]) => {
    const m = new THREE.Mesh(new THREE.PlaneGeometry(w, h), mat)
    m.position.set(...pos)
    m.rotation.set(...rot)
    root.add(m)
  }
  plane(W, D, floorMat, [0, 0, 0], [-Math.PI / 2, 0, 0])
  plane(W, D, ceilMat, [0, H, 0], [Math.PI / 2, 0, 0])
  plane(W, H, wall, [0, H / 2, -D / 2], [0, 0, 0]) // nord
  plane(W, H, wall, [0, H / 2, D / 2], [0, Math.PI, 0]) // sud
  plane(D, H, wall, [-W / 2, H / 2, 0], [0, Math.PI / 2, 0]) // vest
  plane(D, H, wall, [W / 2, H / 2, 0], [0, -Math.PI / 2, 0]) // est

  // grinzi de tavan, ca sa nu fie o cutie goala
  for (const z of [-3, 0, 3]) addBox(root, W, 0.25, 0.35, 0, H - 0.125, z, concrete(0x353941))
  // plinta
  addBox(root, W, 0.12, 0.06, 0, 0.06, -D / 2 + 0.03, concrete(0x2a2d33))

  // lumini
  root.add(new THREE.HemisphereLight(0x9fb4d0, 0x1a1d22, 0.55))
  const lamps = [
    [-2.5, -2.5],
    [2.5, -2.5],
    [-2.5, 2.5],
    [2.5, 2.5],
  ]
  for (const [x, z] of lamps) {
    const p = addBox(root, 1.4, 0.06, 0.35, x, H - 0.3, z, new THREE.MeshBasicMaterial({ color: 0xdfe9ff }))
    p.name = 'lamp'
    const l = new THREE.PointLight(0xcfdcff, 5, 9, 1.6)
    l.position.set(x, H - 0.5, z)
    root.add(l)
  }

  const colliders: THREE.Box3[] = []
  const interactables: THREE.Object3D[] = []
  const boxOf = (x: number, z: number, w: number, d: number) => colliders.push(new THREE.Box3(new THREE.Vector3(x - w / 2, 0, z - d / 2), new THREE.Vector3(x + w / 2, 1.2, z + d / 2)))

  // ecranul mare (perete nord)
  const frame = addBox(root, 4.5, 2.6, 0.18, 0, 1.9, -D / 2 + 0.09, concrete(0x15181d))
  frame.name = 'ScreenFrame'
  const screen = new THREE.Mesh(new THREE.PlaneGeometry(4.16, 1.664), new THREE.MeshBasicMaterial({ color: 0x0a1018 }))
  screen.name = 'Screen_Main'
  screen.position.set(0, 1.9, -D / 2 + 0.2)
  screen.userData.interact = 'screen'
  root.add(screen)
  interactables.push(screen)

  // masa centrala
  const top = addBox(root, 3.6, 0.1, 1.5, 0, 0.9, -0.8, concrete(0x6a5a45))
  top.name = 'Table_Center'
  for (const [dx, dz] of [[-1.6, -0.55], [1.6, -0.55], [-1.6, 0.55], [1.6, 0.55]]) addBox(root, 0.12, 0.85, 0.12, dx, 0.425, -0.8 + dz, concrete(0x2e2a24))
  boxOf(0, -0.8, 3.7, 1.6)

  // butoane pe marginea mesei dinspre comandant: viteza -, viteza +, pauza, repornire
  const buttonSpec: { id: ButtonId; x: number; color: number }[] = [
    { id: 'speedDown', x: -0.45, color: 0xffb454 },
    { id: 'speedUp', x: -0.15, color: 0x3ddc97 },
    { id: 'pause', x: 0.15, color: 0x4c9aff },
    { id: 'reset', x: 0.45, color: 0xff6b6b },
  ]
  for (const bt of buttonSpec) {
    const m = addBox(root, 0.2, 0.05, 0.12, bt.x, 0.975, -0.2, new THREE.MeshBasicMaterial({ color: bt.color, toneMapped: false }))
    m.name = `Button_${bt.id}`
    m.userData.interact = `btn:${bt.id}`
    interactables.push(m)
  }

  // birouri + calculatoare: stanga si dreapta
  const spec: { id: TerminalId; side: -1 | 1; z: number; color: string }[] = [
    { id: 'stats', side: -1, z: -2.6, color: '#4c9aff' },
    { id: 'events', side: -1, z: 1.4, color: '#ffb454' },
    { id: 'results', side: 1, z: -2.6, color: '#3ddc97' },
    { id: 'control', side: 1, z: 1.4, color: '#ff6b6b' },
  ]
  for (const t of spec) {
    const x = t.side * 4.2
    const g = new THREE.Group()
    g.name = `Terminal_${t.id[0].toUpperCase()}${t.id.slice(1)}`
    g.position.set(x, 0, t.z)
    const desk = addBox(g, 0.9, 0.06, 2.0, 0, 0.78, 0, concrete(0x4d5560))
    addBox(g, 0.85, 0.74, 1.9, 0, 0.37, 0, concrete(0x353b44))
    // monitor, orientat spre centrul camerei
    const mon = new THREE.Group()
    mon.rotation.y = t.side === -1 ? Math.PI / 2 : -Math.PI / 2
    mon.position.set(-t.side * 0.15, 0, 0)
    addBox(mon, 0.7, 0.45, 0.05, 0, 1.18, 0, concrete(0x101317))
    addBox(mon, 0.08, 0.2, 0.08, 0, 0.9, -0.03, concrete(0x101317))
    const face = new THREE.Mesh(new THREE.PlaneGeometry(0.64, 0.4), new THREE.MeshBasicMaterial({ map: labelTexture(TERMINAL_LABELS[t.id], t.color), toneMapped: false }))
    face.position.set(0, 1.18, 0.03)
    mon.add(face)
    g.add(mon)
    // scaun
    addBox(g, 0.5, 0.06, 0.5, -t.side * 0.9, 0.48, 0.0, concrete(0x22262c))
    root.add(g)
    for (const o of [desk, face]) {
      o.userData.interact = t.id
      interactables.push(o)
    }
    boxOf(x, t.z, 1.0, 2.1)
  }

  const bounds = new THREE.Box3(new THREE.Vector3(-W / 2 + 0.4, 0, -D / 2 + 0.55), new THREE.Vector3(W / 2 - 0.4, H, D / 2 - 0.4))
  return { root, screen, secondary: null, secondaryAspect: 2, interactables, colliders, bounds, spawn: new THREE.Vector3(0, EYE, 3.6), spawnYaw: 0, usedModel: false, report: [] }
}

function fromModel(gltf: { scene: THREE.Group }): Bunker {
  const root = gltf.scene
  const interactables: THREE.Object3D[] = []
  const colliders: THREE.Box3[] = []
  let screen: THREE.Mesh | null = null
  let secondaryMesh: THREE.Mesh | null = null
  let spawnObj: THREE.Object3D | null = null

  root.updateMatrixWorld(true)
  root.traverse((o) => {
    const n = o.name
    if (n === 'Screen_Main' && (o as THREE.Mesh).isMesh) screen = o as THREE.Mesh
    // "Screen_Seconadry" = greseala de scriere intalnita in model; acceptata si ea
    if ((n === 'Screen_Secondary' || n === 'Screen_Seconadry') && (o as THREE.Mesh).isMesh) secondaryMesh = o as THREE.Mesh
    if (n === 'Spawn') spawnObj = o
    // Collider_* = mobilier vizibil care blocheaza trecerea; Blocker_* = cutie invizibila care blocheaza trecerea.
    // Cate o cutie per mesh, ca un birou facut din mai multe piese sa nu devina un singur bloc mare.
    if (n.startsWith('Collider_') || n.startsWith('Blocker_')) {
      o.traverse((c) => {
        if ((c as THREE.Mesh).isMesh) colliders.push(new THREE.Box3().setFromObject(c))
      })
      if (n.startsWith('Blocker_')) o.visible = false
    }
  })

  const ids: Record<string, TerminalId> = { Terminal_Stats: 'stats', Terminal_Events: 'events', Terminal_Results: 'results', Terminal_Control: 'control' }
  const buttonsFound = new Set<ButtonId>()
  root.traverse((o) => {
    const button = BUTTON_NAMES[o.name]
    const id = ids[o.name] ?? (button ? `btn:${button}` : undefined)
    if (!id) return
    if (button) buttonsFound.add(button)
    o.traverse((c) => {
      if ((c as THREE.Mesh).isMesh) {
        c.userData.interact = id
        interactables.push(c)
      }
    })
  })
  const scr = screen as THREE.Mesh | null
  if (scr) {
    scr.userData.interact = 'screen'
    interactables.push(scr)
  }

  const whole = new THREE.Box3().setFromObject(root)
  const inside = whole.getCenter(new THREE.Vector3())
  const screenAspect = scr ? applyScreenUV(scr, inside) : 0
  const sec = secondaryMesh as THREE.Mesh | null
  const secondaryAspect = sec ? applyScreenUV(sec, inside) : 2
  const bounds = new THREE.Box3(new THREE.Vector3(whole.min.x + 0.4, 0, whole.min.z + 0.4), new THREE.Vector3(whole.max.x - 0.4, whole.max.y, whole.max.z - 0.4))
  const sp = spawnObj as THREE.Object3D | null
  const spawn = sp ? sp.getWorldPosition(new THREE.Vector3()) : new THREE.Vector3(0, 0, 0)
  spawn.y = EYE
  const spawnYaw = sp ? new THREE.Euler().setFromQuaternion(sp.getWorldQuaternion(new THREE.Quaternion()), 'YXZ').y : 0

  // fara lumini in model: o iluminare minima, ca scena sa nu fie neagra
  let hasLight = false
  root.traverse((o) => {
    if ((o as THREE.Light).isLight) hasLight = true
  })
  if (!hasLight) {
    // intensitatile din three sunt fara factorul PI: fara asta un material gri iese ~3x mai intunecat decat in Blender
    root.add(new THREE.HemisphereLight(0xffffff, 0xb4bac6, Math.PI * 1.25))
  }
  const report: string[] = []
  const sz = whole.getSize(new THREE.Vector3())
  report.push(`Model: ${sz.x.toFixed(1)} × ${sz.z.toFixed(1)} m, înălțime ${sz.y.toFixed(2)} m (ochi la ${EYE} m)`)
  if (scr) report.push(`Screen_Main: raport ${screenAspect.toFixed(2)}:1 (captura Cesium are 2.50:1)`)
  else report.push('LIPSEȘTE Screen_Main (nu apare Cesium pe perete)')
  report.push(sec ? `Screen_Secondary: raport ${secondaryAspect.toFixed(2)}:1` : 'Screen_Secondary: lipsește (opțional, ecranul comandantului)')
  report.push(buttonsFound.size === 4 ? 'Butoane: Button_SpeedUp / SpeedDown / Pause / Reset: ok' : `Butoane găsite: ${buttonsFound.size} din 4 (opțional: Button_SpeedUp, Button_SpeedDown, Button_Pause, Button_Reset)`)
  report.push(sp ? `Spawn: găsit la (${spawn.x.toFixed(1)}, ${spawn.z.toFixed(1)})` : 'LIPSEȘTE Spawn (start în centrul modelului, privind spre -Z)')
  const found = new Set(interactables.map((i) => i.userData.interact as string))
  for (const t of Object.values(ids)) report.push(found.has(t) ? `Terminal ${t}: ok` : `LIPSEȘTE Terminal_${t[0].toUpperCase()}${t.slice(1)}`)
  report.push(colliders.length ? `Obstacole (Collider_* / Blocker_*): ${colliders.length} cutii` : 'Niciun Collider_* / Blocker_* (poți trece prin mobilier)')
  console.info('[bunker]', report.join('\n'))
  return { root, screen: scr, secondary: sec, secondaryAspect, interactables, colliders, bounds, spawn, spawnYaw, usedModel: true, report }
}

/** `bust` = ocoleste cache-ul browserului (debug: dupa ce reexporti modelul din Blender). */
export async function loadBunker(base = '/models/bunker.glb', bust = false): Promise<Bunker> {
  const url = bust ? `${base}?t=${Date.now()}` : base
  try {
    const head = await fetch(url, { method: 'HEAD' })
    const type = head.headers.get('content-type') ?? ''
    // serverul de dezvoltare intoarce index.html pentru fisiere lipsa: se accepta doar un raspuns care nu e HTML
    if (head.ok && !type.includes('text/html')) {
      const gltf = await new GLTFLoader().loadAsync(url)
      return fromModel(gltf)
    }
  } catch {
    // fara model: buncarul procedural
  }
  return buildProcedural()
}
