import * as THREE from 'three'

/**
 * Recalculeaza UV-urile unui ecran (mesh plan sau cutie subtire) din geometrie, ca imaginea sa apara dreapta si
 * nemirata indiferent cum a iesit UV-ul din Blender. Merge pe ecrane verticale, inclinate sau culcate pe birou.
 *
 * Fata ecranului = grupul de triunghiuri cu aceeasi normala care are cea mai mare arie si priveste spre `inside`
 * (un punct din interiorul incaperii). "Sus" pe ecran = verticala lumii proiectata pe fata (la un ecran culcat: spre -Z).
 * Intoarce raportul latime / inaltime al ecranului.
 */
export function applyScreenUV(mesh: THREE.Mesh, inside: THREE.Vector3): number {
  mesh.updateMatrixWorld(true)
  const geo = mesh.geometry.clone()
  const pos = geo.getAttribute('position')
  const pts: THREE.Vector3[] = []
  for (let i = 0; i < pos.count; i++) pts.push(new THREE.Vector3().fromBufferAttribute(pos, i).applyMatrix4(mesh.matrixWorld))

  const box = new THREE.Box3().setFromPoints(pts)
  const center = box.getCenter(new THREE.Vector3())
  const toRoom = inside.clone().sub(center)

  // grupeaza triunghiurile dupa normala (rotunjita) si aduna ariile
  const index = geo.getIndex()
  const triCount = index ? index.count / 3 : pts.length / 3
  const groups = new Map<string, { n: THREE.Vector3; area: number }>()
  const a = new THREE.Vector3()
  const b = new THREE.Vector3()
  for (let t = 0; t < triCount; t++) {
    const i0 = index ? index.getX(t * 3) : t * 3
    const i1 = index ? index.getX(t * 3 + 1) : t * 3 + 1
    const i2 = index ? index.getX(t * 3 + 2) : t * 3 + 2
    a.subVectors(pts[i1], pts[i0])
    b.subVectors(pts[i2], pts[i0])
    const cross = new THREE.Vector3().crossVectors(a, b)
    const area = cross.length() / 2
    if (area < 1e-9) continue
    const n = cross.normalize()
    const key = `${n.x.toFixed(2)},${n.y.toFixed(2)},${n.z.toFixed(2)}`
    const g = groups.get(key)
    if (g) g.area += area
    else groups.set(key, { n, area })
  }

  let best: { n: THREE.Vector3; area: number } | null = null
  let bestAny: { n: THREE.Vector3; area: number } | null = null
  for (const g of groups.values()) {
    if (!bestAny || g.area > bestAny.area) bestAny = g
    if (g.n.dot(toRoom) > 0 && (!best || g.area > best.area)) best = g
  }
  // un plan cu o singura fata, orientat invers: se intoarce spre camera
  const normal = (best ?? bestAny)?.n.clone() ?? new THREE.Vector3(0, 0, 1)
  if (!best && normal.dot(toRoom) < 0) normal.negate()

  const up = new THREE.Vector3(0, 1, 0)
  const v = up.clone().addScaledVector(normal, -up.dot(normal))
  if (v.length() < 1e-3) v.set(0, 0, -1).addScaledVector(normal, normal.z) // ecran culcat: "sus" spre -Z
  v.normalize()
  const u = new THREE.Vector3().crossVectors(v, normal).normalize()

  let uLo = Infinity
  let uHi = -Infinity
  let vLo = Infinity
  let vHi = -Infinity
  for (const p of pts) {
    const pu = p.dot(u)
    const pv = p.dot(v)
    uLo = Math.min(uLo, pu)
    uHi = Math.max(uHi, pu)
    vLo = Math.min(vLo, pv)
    vHi = Math.max(vHi, pv)
  }
  const w = Math.max(uHi - uLo, 1e-6)
  const h = Math.max(vHi - vLo, 1e-6)
  const uv = new Float32Array(pts.length * 2)
  pts.forEach((p, i) => {
    uv[i * 2] = (p.dot(u) - uLo) / w
    uv[i * 2 + 1] = (p.dot(v) - vLo) / h
  })
  geo.setAttribute('uv', new THREE.Float32BufferAttribute(uv, 2))
  mesh.geometry = geo
  return w / h
}
