import type { Snapshot } from './types'

const HEADER_SIZE = 24
const RECORD_SIZE = 16

/**
 * Decodeaza snapshot-ul binar produs de Sim.Server/Services/SnapshotWriter.cs (little-endian):
 *   0: int64 tick | 8: float64 timp | 16: int32 N | 20: int32 M | 24: N x drone, apoi M x unitate defensiva, fiecare { int32 id, f32 lat, f32 lon, f32 alt }
 */
export function decodeSnapshot(bytes: Uint8Array): Snapshot {
  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength)

  const tick = Number(view.getBigInt64(0, true))
  const timeS = view.getFloat64(8, true)
  const count = view.getInt32(16, true)

  const friendlyCount = view.getInt32(20, true)

  const expected = HEADER_SIZE + (count + friendlyCount) * RECORD_SIZE
  if (count < 0 || friendlyCount < 0 || bytes.byteLength < expected) {
    throw new Error(`Snapshot invalid: ${bytes.byteLength} octeti pentru ${count} drone`)
  }

  const ids = new Int32Array(count)
  const lat = new Float32Array(count)
  const lon = new Float32Array(count)
  const alt = new Float32Array(count)

  for (let i = 0; i < count; i++) {
    const o = HEADER_SIZE + i * RECORD_SIZE
    ids[i] = view.getInt32(o, true)
    lat[i] = view.getFloat32(o + 4, true)
    lon[i] = view.getFloat32(o + 8, true)
    alt[i] = view.getFloat32(o + 12, true)
  }

  const friendlyIds = new Int32Array(friendlyCount)
  const friendlyLat = new Float32Array(friendlyCount)
  const friendlyLon = new Float32Array(friendlyCount)
  const friendlyAlt = new Float32Array(friendlyCount)

  for (let i = 0; i < friendlyCount; i++) {
    const o = HEADER_SIZE + (count + i) * RECORD_SIZE
    friendlyIds[i] = view.getInt32(o, true)
    friendlyLat[i] = view.getFloat32(o + 4, true)
    friendlyLon[i] = view.getFloat32(o + 8, true)
    friendlyAlt[i] = view.getFloat32(o + 12, true)
  }

  return { tick, timeS, count, ids, lat, lon, alt, friendlyCount, friendlyIds, friendlyLat, friendlyLon, friendlyAlt }
}
