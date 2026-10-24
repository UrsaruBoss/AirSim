import { useEffect, useRef, useState } from 'react'
import {
  Cartesian2,
  Cartesian3,
  Cartographic,
  Color,
  GeoJsonDataSource,
  JulianDate,
  HorizontalOrigin,
  ImageryLayer,
  Ion,
  LabelStyle,
  Math as CesiumMath,
  PointPrimitiveCollection,
  Rectangle,
  ScreenSpaceEventHandler,
  ScreenSpaceEventType,
  Terrain,
  UrlTemplateImageryProvider,
  Viewer,
} from 'cesium'
import type { Entity, PointPrimitive } from 'cesium'
import type { SimClient } from './SimClient'
import { DEFENSE_COLORS, ROLE_COLORS, UNIT_COLORS } from './roles'
import type { CountryRole, Scenario, UnitKind } from './types'

interface Track {
  point: PointPrimitive
  /** Pozitia la snapshot-ul anterior si la cel curent (ECEF), intre ele se interpoleaza. */
  from: Cartesian3
  to: Cartesian3
}

const ALWAYS_VISIBLE = Number.POSITIVE_INFINITY
const INTERCEPTOR_ID_BASE = 1_000_000

interface WaveKinds {
  ends: number[]
  kinds: UnitKind[]
}

function kindOf(waves: WaveKinds, id: number): UnitKind {
  const index = waves.ends.findIndex((end) => id < end)
  return index >= 0 ? waves.kinds[index] : 'drone'
}

function createViewer(container: HTMLElement): Viewer {
  const token = import.meta.env.VITE_CESIUM_ION_TOKEN as string | undefined

  const options: Viewer.ConstructorOptions = {
    animation: false,
    timeline: false,
    baseLayerPicker: false,
    geocoder: false,
    homeButton: false,
    navigationHelpButton: false,
    fullscreenButton: false,
    infoBox: false,
    selectionIndicator: false,
  }

  if (token) {
    // cu token Ion: teren si imagistica Cesium
    Ion.defaultAccessToken = token
    options.terrain = Terrain.fromWorldTerrain()
  } else {
    // fara token: tile-uri intunecate gratuite (necesita atribuire, afisata de Cesium in colt)
    options.baseLayer = new ImageryLayer(
      new UrlTemplateImageryProvider({
        url: 'https://{s}.basemaps.cartocdn.com/dark_all/{z}/{x}/{y}.png',
        subdomains: ['a', 'b', 'c', 'd'],
        maximumLevel: 18,
        credit: '© OpenStreetMap contributors © CARTO',
      }),
    )
  }

  const viewer = new Viewer(container, options)
  viewer.scene.globe.enableLighting = false
  return viewer
}

function scenarioRectangle(scenario: Scenario): Rectangle | null {
  const points = [...scenario.launchSites, ...scenario.targets]
  if (points.length === 0) return null

  const lats = points.map((p) => p.latDeg)
  const lons = points.map((p) => p.lonDeg)
  const padLat = Math.max((Math.max(...lats) - Math.min(...lats)) * 0.25, 1)
  const padLon = Math.max((Math.max(...lons) - Math.min(...lons)) * 0.25, 1)

  return Rectangle.fromDegrees(
    Math.max(Math.min(...lons) - padLon, -180),
    Math.max(Math.min(...lats) - padLat, -85),
    Math.min(Math.max(...lons) + padLon, 180),
    Math.min(Math.max(...lats) + padLat, 85),
  )
}

// vedere implicita cand scenariul nu are inca puncte (editor gol)
const EUROPE = Rectangle.fromDegrees(-12, 33, 52, 68)

interface Props {
  client: SimClient
  scenario: Scenario | null
  /** Daca e dat, un click stanga pe glob raporteaza coordonatele (editorul plaseaza sisteme / puncte). */
  onMapClick?: (latDeg: number, lonDeg: number) => void
  /** Cursor cruce cat timp se plaseaza ceva. */
  placing?: boolean
  /** Camera zboara spre scenariu doar cand se schimba cheia (implicit: la orice schimbare a scenariului). */
  fitKey?: string
  /** Primeste viewer-ul Cesium cand e gata (null la demontare); folosit de modul immersive ca sa-l afiseze pe ecranul din buncar. */
  onViewer?: (viewer: Viewer | null) => void
}

export default function CesiumView({ client, scenario, onMapClick, placing = false, fitKey, onViewer }: Props) {
  const containerRef = useRef<HTMLDivElement>(null)
  const clickRef = useRef(onMapClick)
  const clickable = onMapClick !== undefined
  const [viewer, setViewer] = useState<Viewer | null>(null)
  const wavesRef = useRef<WaveKinds>({ ends: [], kinds: [] })

  // id-urile ofensive sunt atribuite in ordinea valurilor: din ele se deduce tipul unitatii
  useEffect(() => {
    let total = 0
    const ends: number[] = []
    const kinds: UnitKind[] = []
    for (const wave of scenario?.waves ?? []) {
      total += wave.count
      ends.push(total)
      kinds.push(wave.kind)
    }
    wavesRef.current = { ends, kinds }
  }, [scenario])

  const viewerCbRef = useRef(onViewer)
  useEffect(() => {
    clickRef.current = onMapClick
    viewerCbRef.current = onViewer
  })

  useEffect(() => {
    viewerCbRef.current?.(viewer)
    return () => viewerCbRef.current?.(null)
  }, [viewer])

  // click pe glob => coordonate (pentru editor); callback-ul curent se citeste din ref
  useEffect(() => {
    if (!viewer || !clickable) return
    const handler = new ScreenSpaceEventHandler(viewer.scene.canvas)
    handler.setInputAction((e: ScreenSpaceEventHandler.PositionedEvent) => {
      const cartesian = viewer.camera.pickEllipsoid(e.position, viewer.scene.globe.ellipsoid)
      if (!cartesian) return
      const c = Cartographic.fromCartesian(cartesian)
      clickRef.current?.(CesiumMath.toDegrees(c.latitude), CesiumMath.toDegrees(c.longitude))
    }, ScreenSpaceEventType.LEFT_CLICK)
    return () => handler.destroy()
  }, [viewer, clickable])

  useEffect(() => {
    if (!viewer) return
    viewer.canvas.style.cursor = placing ? 'crosshair' : ''
  }, [viewer, placing])

  // 1) viewer-ul Cesium: creat la montare, distrus la demontare
  useEffect(() => {
    const container = containerRef.current
    if (!container) return

    const created = createViewer(container)
    setViewer(created)

    return () => {
      setViewer(null)
      if (!created.isDestroyed()) created.destroy()
    }
  }, [])

  // 2) drone + impacturi: primitive (nu entitati) si interpolare intre snapshot-uri
  useEffect(() => {
    if (!viewer) return

    const droneLayer = viewer.scene.primitives.add(new PointPrimitiveCollection()) as PointPrimitiveCollection
    const impactLayer = viewer.scene.primitives.add(new PointPrimitiveCollection()) as PointPrimitiveCollection

    const friendlyLayer = viewer.scene.primitives.add(new PointPrimitiveCollection()) as PointPrimitiveCollection
    const tracks = new Map<number, Track>()
    const friendlyTracks = new Map<number, Track>()
    const scratch = new Cartesian3()
    let lastArrival = performance.now()
    let intervalMs = 100
    let lastEpoch = -1

    const reset = () => {
      droneLayer.removeAll()
      impactLayer.removeAll()
      friendlyLayer.removeAll()
      tracks.clear()
      friendlyTracks.clear()
    }

    const offSnapshots = client.snapshots.on((snap) => {
      const now = performance.now()
      intervalMs = Math.min(1000, Math.max(30, intervalMs * 0.7 + (now - lastArrival) * 0.3))
      lastArrival = now

      const seen = new Set<number>()
      for (let i = 0; i < snap.count; i++) {
        const id = snap.ids[i]
        seen.add(id)

        const track = tracks.get(id)
        if (track) {
          Cartesian3.clone(track.to, track.from)
          Cartesian3.fromDegrees(snap.lon[i], snap.lat[i], snap.alt[i], undefined, track.to)
        } else {
          const position = Cartesian3.fromDegrees(snap.lon[i], snap.lat[i], snap.alt[i])
          const kind = kindOf(wavesRef.current, id)
          const point = droneLayer.add({
            position,
            pixelSize: kind === 'ballisticMissile' ? 7 : kind === 'aircraft' || kind === 'cruiseMissile' ? 6 : 4,
            color: Color.fromCssColorString(UNIT_COLORS[kind]),
            disableDepthTestDistance: ALWAYS_VISIBLE,
          })
          tracks.set(id, { point, from: position.clone(), to: position })
        }
      }

      for (const [id, track] of tracks) {
        if (!seen.has(id)) {
          droneLayer.remove(track.point)
          tracks.delete(id)
        }
      }

      // unitati defensive in aer (avioane de patrula, interceptoare)
      const seenFriendly = new Set<number>()
      for (let i = 0; i < snap.friendlyCount; i++) {
        const id = snap.friendlyIds[i]
        seenFriendly.add(id)

        const track = friendlyTracks.get(id)
        if (track) {
          Cartesian3.clone(track.to, track.from)
          Cartesian3.fromDegrees(snap.friendlyLon[i], snap.friendlyLat[i], snap.friendlyAlt[i], undefined, track.to)
        } else {
          const position = Cartesian3.fromDegrees(snap.friendlyLon[i], snap.friendlyLat[i], snap.friendlyAlt[i])
          const fighter = id < INTERCEPTOR_ID_BASE
          const point = friendlyLayer.add({
            position,
            pixelSize: fighter ? 8 : 4,
            color: fighter ? Color.DEEPSKYBLUE : Color.LIME,
            outlineColor: Color.BLACK,
            outlineWidth: 1,
            disableDepthTestDistance: ALWAYS_VISIBLE,
          })
          friendlyTracks.set(id, { point, from: position.clone(), to: position })
        }
      }

      for (const [id, track] of friendlyTracks) {
        if (!seenFriendly.has(id)) {
          friendlyLayer.remove(track.point)
          friendlyTracks.delete(id)
        }
      }
    })

    const offEvents = client.events.on((batch) => {
      for (const e of batch) {
        if (e.type === 'airspaceViolation') {
          impactLayer.add({
            position: Cartesian3.fromDegrees(e.lonDeg, e.latDeg, e.altM),
            pixelSize: 7,
            color: Color.ORANGE,
            outlineColor: Color.WHITE,
            outlineWidth: 1,
            disableDepthTestDistance: ALWAYS_VISIBLE,
          })
          continue
        }
        if (e.type === 'intercepted' || e.type === 'missed') {
          const hit = e.type === 'intercepted'
          impactLayer.add({
            position: Cartesian3.fromDegrees(e.lonDeg, e.latDeg, e.altM),
            pixelSize: hit ? 6 : 4,
            color: hit ? Color.LIMEGREEN : Color.GRAY,
            outlineColor: Color.WHITE,
            outlineWidth: hit ? 1 : 0,
            disableDepthTestDistance: ALWAYS_VISIBLE,
          })
          continue
        }
        if (e.type !== 'impact') continue
        impactLayer.add({
          position: Cartesian3.fromDegrees(e.lonDeg, e.latDeg, e.altM),
          pixelSize: 3,
          color: Color.RED.withAlpha(0.75),
          disableDepthTestDistance: ALWAYS_VISIBLE,
        })
      }
    })

    // rulare noua (epoch) sau Stop => curata scena
    const offStatuses = client.statuses.on((status) => {
      if (status.epoch !== lastEpoch || status.state === 'idle') {
        lastEpoch = status.epoch
        reset()
      }
    })

    // la fiecare cadru: pozitia = interpolare intre ultimele doua snapshot-uri
    const offPreRender = viewer.scene.preRender.addEventListener(() => {
      const alpha = Math.min(1, (performance.now() - lastArrival) / intervalMs)
      for (const track of tracks.values()) {
        Cartesian3.lerp(track.from, track.to, alpha, scratch)
        track.point.position = scratch
      }
      for (const track of friendlyTracks.values()) {
        Cartesian3.lerp(track.from, track.to, alpha, scratch)
        track.point.position = scratch
      }
    })

    return () => {
      offSnapshots()
      offEvents()
      offStatuses()
      offPreRender()
      if (!viewer.isDestroyed()) {
        viewer.scene.primitives.remove(droneLayer)
        viewer.scene.primitives.remove(impactLayer)
        viewer.scene.primitives.remove(friendlyLayer)
      }
    }
  }, [viewer, client])

  // 3) tari colorate dupa rol (harta din /api/airspace/{map}, roluri din scenariu)
  const airspaceMap = scenario?.airspaceMap ?? ''
  const countries = scenario?.countries
  useEffect(() => {
    if (!viewer || !airspaceMap) return
    let cancelled = false
    let source: GeoJsonDataSource | null = null

    GeoJsonDataSource.load(`/api/airspace/${encodeURIComponent(airspaceMap)}`, { clampToGround: true })
      .then(async (ds) => {
        if (cancelled || viewer.isDestroyed()) return
        source = ds
        const now = JulianDate.now()
        const many = ds.entities.values.length > 120
        for (const entity of [...ds.entities.values]) {
          if (!entity.polygon) continue
          const code = entity.properties?.iso2?.getValue(now) as string | undefined
          const role: CountryRole | undefined = code ? countries?.[code] : undefined
          // harta lumii: tarile fara rol nu se deseneaza (sute de poligoane lipite de teren ar incetini glob-ul)
          if (!role && many) {
            ds.entities.remove(entity)
            continue
          }
          const base = Color.fromCssColorString(role ? ROLE_COLORS[role] : '#64748b')
          entity.polygon.material = base.withAlpha(role ? 0.28 : 0.04) as never
          entity.polygon.outline = false as never

          // contururile poligoanelor "lipite de teren" nu se randeaza => polilinii lipite de teren
          const hierarchy = entity.polygon.hierarchy?.getValue(now) as { positions: Cartesian3[]; holes: { positions: Cartesian3[] }[] } | undefined
          if (!hierarchy) continue
          const rings = [hierarchy.positions, ...hierarchy.holes.map((h) => h.positions)]
          for (const positions of rings) {
            ds.entities.add({
              polyline: {
                positions: [...positions, positions[0]],
                width: role ? 2 : 1,
                clampToGround: true,
                material: base.withAlpha(role ? 0.9 : 0.25) as never,
              },
            })
          }
        }
        await viewer.dataSources.add(ds)
      })
      .catch(() => {
        // harta indisponibila: simularea merge si fara ea
      })

    return () => {
      cancelled = true
      if (source && !viewer.isDestroyed()) viewer.dataSources.remove(source, true)
    }
  }, [viewer, airspaceMap, countries])

  // 4) baze de lansare si tinte din scenariu + zoom pe zona
  useEffect(() => {
    if (!viewer || !scenario) return

    const entities: Entity[] = []

    for (const site of scenario.launchSites) {
      entities.push(
        viewer.entities.add({
          position: Cartesian3.fromDegrees(site.lonDeg, site.latDeg, site.altM),
          point: { pixelSize: 10, color: Color.DODGERBLUE, outlineColor: Color.WHITE, outlineWidth: 1, disableDepthTestDistance: ALWAYS_VISIBLE },
          label: {
            text: site.name,
            font: '13px sans-serif',
            style: LabelStyle.FILL_AND_OUTLINE,
            fillColor: Color.WHITE,
            outlineColor: Color.BLACK,
            outlineWidth: 3,
            horizontalOrigin: HorizontalOrigin.LEFT,
            pixelOffset: new Cartesian2(12, -12),
            disableDepthTestDistance: ALWAYS_VISIBLE,
          },
        }),
      )
    }

    for (const target of scenario.targets) {
      entities.push(
        viewer.entities.add({
          position: Cartesian3.fromDegrees(target.lonDeg, target.latDeg, target.altM),
          point: { pixelSize: 12, color: Color.ORANGERED, outlineColor: Color.WHITE, outlineWidth: 1, disableDepthTestDistance: ALWAYS_VISIBLE },
          label: {
            text: target.name,
            font: '13px sans-serif',
            style: LabelStyle.FILL_AND_OUTLINE,
            fillColor: Color.WHITE,
            outlineColor: Color.BLACK,
            outlineWidth: 3,
            horizontalOrigin: HorizontalOrigin.LEFT,
            pixelOffset: new Cartesian2(12, -12),
            disableDepthTestDistance: ALWAYS_VISIBLE,
          },
        }),
      )
    }

    for (const defense of scenario.defenses ?? []) {
      const color = Color.fromCssColorString(DEFENSE_COLORS[defense.kind])
      entities.push(
        viewer.entities.add({
          position: Cartesian3.fromDegrees(defense.lonDeg, defense.latDeg, defense.altM),
          point: { pixelSize: 8, color, outlineColor: Color.BLACK, outlineWidth: 1, disableDepthTestDistance: ALWAYS_VISIBLE },
          ellipse: defense.kind === 'airbase' ? undefined : {
            semiMajorAxis: (defense.kind === 'patrol' && defense.patrolRadiusKm > 0 ? defense.patrolRadiusKm : defense.rangeKm) * 1000,
            semiMinorAxis: (defense.kind === 'patrol' && defense.patrolRadiusKm > 0 ? defense.patrolRadiusKm : defense.rangeKm) * 1000,
            material: color.withAlpha(0.08),
            outline: true,
            outlineColor: color.withAlpha(0.7),
            height: 0,
          },
          label: {
            text: defense.name || defense.id,
            font: '12px sans-serif',
            style: LabelStyle.FILL_AND_OUTLINE,
            fillColor: color,
            outlineColor: Color.BLACK,
            outlineWidth: 3,
            horizontalOrigin: HorizontalOrigin.LEFT,
            pixelOffset: new Cartesian2(10, 10),
            disableDepthTestDistance: ALWAYS_VISIBLE,
          },
        }),
      )
    }

    return () => {
      if (viewer.isDestroyed()) return
      for (const entity of entities) viewer.entities.remove(entity)
    }
  }, [viewer, scenario])

  // 5) camera: zboara spre scenariu cand se schimba cheia (sau scenariul, daca nu exista cheie)
  const fitDependency = fitKey ?? scenario
  useEffect(() => {
    if (!viewer || !scenario) return
    viewer.camera.flyTo({ destination: scenarioRectangle(scenario) ?? EUROPE, duration: 1.5 })
    // doar fitDependency declanseaza zborul; scenariul curent se citeste din inchidere
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [viewer, fitDependency])

  return <div ref={containerRef} className="cesium-container" />
}
