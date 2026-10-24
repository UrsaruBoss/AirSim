import * as THREE from 'three'
import { GLTFLoader } from 'three/examples/jsm/loaders/GLTFLoader.js'

/**
 * Buncarul. Daca exista /models/bunker.glb se foloseste el, altfel se construieste unul procedural (placeholder).
 *
 * Conventia de nume pentru modelul din Blender (obiecte sau nod-uri radacina din glTF):
 *   Screen_Main        mesh plan cu UV 0..1 (16:9): pe el apare Cesium
 *   Terminal_Stats     calculator: statistici si controlul simularii
 *   Terminal_Events    calculator: evenimente
 *   Terminal_Results   calculator: rezultate
 *   Terminal_Control   calculatorul liber: alegi ce controlezi / ce arata ecranul mare
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

export interface Bunker {
  root: THREE.Object3D
  screen: THREE.Mesh | null
  /** Obiecte pe care se poate apasa E (userData.interact = id terminal sau 'screen'). */
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
  return { root, screen, interactables, colliders, bounds, spawn: new THREE.Vector3(0, EYE, 3.6), spawnYaw: 0, usedModel: false, report: [] }
}

/**
 * Recalculeaza UV-urile ecranului din geometrie (perete vertical aliniat pe X sau Z), ca imaginea sa fie
 * mereu dreapta, indiferent cum a iesit UV-ul din Blender. `inside` = un punct din interiorul incaperii.
 */
function planarScreenUV(mesh: THREE.Mesh, inside: THREE.Vector3) {
  mesh.updateMatrixWorld(true)
  const geo = mesh.geometry.clone()
  const pos = geo.getAttribute('position')
  const pts: THREE.Vector3[] = []
  for (let i = 0; i < pos.count; i++) pts.push(new THREE.Vector3().fromBufferAttribute(pos, i).applyMatrix4(mesh.matrixWorld))
  const box = new THREE.Box3().setFromPoints(pts)
  const size = box.getSize(new THREE.Vector3())
  const normal = new THREE.Vector3(size.x < size.z ? 1 : 0, 0, size.x < size.z ? 0 : 1)
  if (normal.dot(inside.clone().sub(box.getCenter(new THREE.Vector3()))) < 0) normal.negate()
  // privitorul se uita spre -normal; dreapta lui = forward x up
  const right = normal.clone().negate().cross(new THREE.Vector3(0, 1, 0)).normalize()
  let lo = Infinity
  let hi = -Infinity
  for (const p of pts) {
    const d = p.dot(right)
    lo = Math.min(lo, d)
    hi = Math.max(hi, d)
  }
  const uv = new Float32Array(pts.length * 2)
  pts.forEach((p, i) => {
    uv[i * 2] = (p.dot(right) - lo) / Math.max(hi - lo, 1e-6)
    uv[i * 2 + 1] = (p.y - box.min.y) / Math.max(size.y, 1e-6)
  })
  geo.setAttribute('uv', new THREE.Float32BufferAttribute(uv, 2))
  mesh.geometry = geo
}

function fromModel(gltf: { scene: THREE.Group }): Bunker {
  const root = gltf.scene
  const interactables: THREE.Object3D[] = []
  const colliders: THREE.Box3[] = []
  let screen: THREE.Mesh | null = null
  let spawnObj: THREE.Object3D | null = null

  root.updateMatrixWorld(true)
  root.traverse((o) => {
    const n = o.name
    if (n === 'Screen_Main' && (o as THREE.Mesh).isMesh) screen = o as THREE.Mesh
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
  root.traverse((o) => {
    const id = ids[o.name]
    if (!id) return
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
  if (scr) planarScreenUV(scr, whole.getCenter(new THREE.Vector3()))
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
  if (scr) {
    const ss = new THREE.Box3().setFromObject(scr).getSize(new THREE.Vector3())
    const w = Math.max(ss.x, ss.z)
    report.push(`Screen_Main: ${w.toFixed(2)} × ${ss.y.toFixed(2)} m, raport ${(w / ss.y).toFixed(2)}:1 (16:9 = 1.78)`)
  } else report.push('LIPSEȘTE Screen_Main (nu apare Cesium pe perete)')
  report.push(sp ? `Spawn: găsit la (${spawn.x.toFixed(1)}, ${spawn.z.toFixed(1)})` : 'LIPSEȘTE Spawn (start în centrul modelului, privind spre -Z)')
  const found = new Set(interactables.map((i) => i.userData.interact as string))
  for (const t of Object.values(ids)) report.push(found.has(t) ? `Terminal ${t}: ok` : `LIPSEȘTE Terminal_${t[0].toUpperCase()}${t.slice(1)}`)
  report.push(colliders.length ? `Obstacole (Collider_* / Blocker_*): ${colliders.length} cutii` : 'Niciun Collider_* / Blocker_* (poți trece prin mobilier)')
  console.info('[bunker]', report.join('\n'))
  return { root, screen: scr, interactables, colliders, bounds, spawn, spawnYaw, usedModel: true, report }
}

export async function loadBunker(url = '/models/bunker.glb'): Promise<Bunker> {
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
