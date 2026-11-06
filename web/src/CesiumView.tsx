import { useEffect, useRef, useState } from 'react'
import {
  Cartesian2,
  Cartesian3,
  Cartographic,
  Color,
  ColorGeometryInstanceAttribute,
  EllipsoidGeometry,
  GeometryInstance,
  Matrix3,
  Matrix4,
  PerInstanceColorAppearance,
  Primitive,
  Quaternion,
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
import { antisolarUnit, scenarioStartMs } from './solar'
import { DEFENSE_COLORS, ROLE_COLORS, UNIT_COLORS } from './roles'
import type { CountryRole, Scenario, UnitKind } from './types'

interface Track {
  point: PointPrimitive
  /** Position at the previous snapshot and at the current one (ECEF); interpolated in between. */
  from: Cartesian3
  to: Cartesian3
  /** Colour at full opacity, altitude (m) at the last snapshot and the opacity currently applied (low flyers fade when altFade is on). */
  base?: Color
  altM?: number
  alpha?: number
}

/** Opacity from altitude: 0.4 at ground level up to 1.0 from 1000 m, in steps of 0.05 (so the colour is rebuilt only when it visibly changes). */
const altAlpha = (altM: number) => Math.round((0.4 + 0.6 * Math.min(1, Math.max(0, altM) / 1000)) * 20) / 20

function applyFade(track: Track, on: boolean): void {
  if (!track.base) return
  const a = on ? altAlpha(track.altM ?? 0) : 1
  if (a === (track.alpha ?? 1)) return
  track.alpha = a
  track.point.color = track.base.withAlpha(a)
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
    // with an Ion token: Cesium terrain and imagery
    Ion.defaultAccessToken = token
    options.terrain = Terrain.fromWorldTerrain()
  } else {
    // without a token: free dark tiles (require attribution, shown by Cesium in the corner)
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
  // the clock is driven by the simulation (see the day/night effect), never by real time
  viewer.clock.shouldAnimate = false
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

// default view when the scenario has no points yet (empty editor)
const EUROPE = Rectangle.fromDegrees(-12, 33, 52, 68)

interface Props {
  client: SimClient
  scenario: Scenario | null
  /** If given, a left click on the globe reports the coordinates (the editor places systems / points). */
  onMapClick?: (latDeg: number, lonDeg: number) => void
  /** Crosshair cursor while something is being placed. */
  placing?: boolean
  /** The camera flies to the scenario only when this key changes (default: on any change of the scenario). */
  fitKey?: string
  /** Receives the Cesium viewer when it is ready (null on unmount). */
  onViewer?: (viewer: Viewer | null) => void
  /** Sun lighting of the globe, driven by the scenario's start time + simulated time (ignored without startTimeUtc). */
  lighting?: boolean
  /** Offensive units become more transparent the lower they fly (opacity from altitude). */
  altFade?: boolean
}

export default function CesiumView({ client, scenario, onMapClick, placing = false, fitKey, onViewer, lighting = false, altFade = false }: Props) {
  const containerRef = useRef<HTMLDivElement>(null)
  const clickRef = useRef(onMapClick)
  const clickable = onMapClick !== undefined
  const [viewer, setViewer] = useState<Viewer | null>(null)
  const wavesRef = useRef<WaveKinds>({ ends: [], kinds: [] })

  // offensive ids are assigned in wave order: the unit type is derived from them
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
  const altFadeRef = useRef(altFade)
  useEffect(() => {
    clickRef.current = onMapClick
    viewerCbRef.current = onViewer
    altFadeRef.current = altFade
  })

  useEffect(() => {
    viewerCbRef.current?.(viewer)
    return () => viewerCbRef.current?.(null)
  }, [viewer])

  // day/night: the Cesium clock follows the simulated time; the night side is shaded by translucent spherical caps
  // (not by globe.enableLighting: that one is pitch black at night; an imagery layer hid the ground-clamped overlays)
  const startMs = scenarioStartMs(scenario)
  const lit = lighting && startMs !== null
  const startMsRef = useRef<number | null>(null)
  const litRef = useRef(false)
  useEffect(() => {
    startMsRef.current = startMs
    litRef.current = lit
    if (!viewer || viewer.isDestroyed()) return
    if (startMs !== null) viewer.clock.currentTime = JulianDate.fromDate(new Date(startMs))
  }, [viewer, lit, startMs])

  useEffect(() => {
    if (!viewer || !lit) return
    // Night = translucent spherical caps slightly above the ground, centred on the point opposite the sun.
    // The cap angle is measured from that point: 90 deg = the terminator, below it the sun is under the horizon.
    const caps: { angleDeg: number; alpha: number }[] = [96, 93, 90, 87, 84, 78].map((angleDeg) => ({ angleDeg, alpha: 0.1 }))
    const radii = new Cartesian3(6378137 + 9000, 6378137 + 9000, 6356752.3 + 9000)
    const primitives = caps.map((cap) => {
      const prim = new Primitive({
        geometryInstances: new GeometryInstance({
          geometry: new EllipsoidGeometry({
            radii,
            minimumCone: 0,
            maximumCone: CesiumMath.toRadians(cap.angleDeg),
            slicePartitions: 72,
            stackPartitions: 48,
            vertexFormat: PerInstanceColorAppearance.VERTEX_FORMAT,
          }),
          attributes: { color: ColorGeometryInstanceAttribute.fromColor(new Color(0.01, 0.02, 0.08, cap.alpha)) },
        }),
        appearance: new PerInstanceColorAppearance({ translucent: true, closed: true, flat: true }),
        asynchronous: false,
        modelMatrix: Matrix4.IDENTITY.clone(),
      })
      viewer.scene.primitives.add(prim)
      return prim
    })

    const z = new Cartesian3(0, 0, 1)
    const axis = new Cartesian3()
    const target = new Cartesian3()
    const rot = new Matrix3()
    const quat = new Quaternion()
    const orient = () => {
      const ms = JulianDate.toDate(viewer.clock.currentTime).getTime()
      const [x, y, zz] = antisolarUnit(ms)
      Cartesian3.fromElements(x, y, zz, target)
      const dot = Cartesian3.dot(z, target)
      if (dot > 0.999999) Matrix3.clone(Matrix3.IDENTITY, rot)
      else if (dot < -0.999999) Matrix3.fromRotationX(Math.PI, rot)
      else {
        Cartesian3.cross(z, target, axis)
        Cartesian3.normalize(axis, axis)
        Quaternion.fromAxisAngle(axis, Math.acos(dot), quat)
        Matrix3.fromQuaternion(quat, rot)
      }
      for (const prim of primitives) Matrix4.fromRotationTranslation(rot, Cartesian3.ZERO, prim.modelMatrix)
    }
    orient()
    const off = viewer.scene.preRender.addEventListener(orient)

    return () => {
      off()
      if (!viewer.isDestroyed()) for (const prim of primitives) viewer.scene.primitives.remove(prim)
    }
  }, [viewer, lit])

  // click on the globe => coordinates (for the editor); the current callback is read from a ref
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

  // 1) the Cesium viewer: created on mount, destroyed on unmount
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

  // 2) drones + impacts: primitives (not entities) and interpolation between snapshots
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
    let fadeOn = altFadeRef.current

    const reset = () => {
      droneLayer.removeAll()
      impactLayer.removeAll()
      friendlyLayer.removeAll()
      tracks.clear()
      friendlyTracks.clear()
      if (startMsRef.current !== null) viewer.clock.currentTime = JulianDate.fromDate(new Date(startMsRef.current))
    }

    const offSnapshots = client.snapshots.on((snap) => {
      const now = performance.now()
      intervalMs = Math.min(1000, Math.max(30, intervalMs * 0.7 + (now - lastArrival) * 0.3))
      lastArrival = now

      if (litRef.current && startMsRef.current !== null) {
        viewer.clock.currentTime = JulianDate.fromDate(new Date(startMsRef.current + snap.timeS * 1000))
      }

      const seen = new Set<number>()
      for (let i = 0; i < snap.count; i++) {
        const id = snap.ids[i]
        seen.add(id)

        const track = tracks.get(id)
        if (track) {
          Cartesian3.clone(track.to, track.from)
          Cartesian3.fromDegrees(snap.lon[i], snap.lat[i], snap.alt[i], undefined, track.to)
          track.altM = snap.alt[i]
          applyFade(track, fadeOn)
        } else {
          const position = Cartesian3.fromDegrees(snap.lon[i], snap.lat[i], snap.alt[i])
          const kind = kindOf(wavesRef.current, id)
          const base = Color.fromCssColorString(UNIT_COLORS[kind])
          const point = droneLayer.add({
            position,
            pixelSize: kind === 'ballisticMissile' ? 7 : kind === 'aircraft' || kind === 'cruiseMissile' ? 6 : 4,
            color: base,
            disableDepthTestDistance: ALWAYS_VISIBLE,
          })
          const created: Track = { point, from: position.clone(), to: position, base, altM: snap.alt[i], alpha: 1 }
          applyFade(created, fadeOn)
          tracks.set(id, created)
        }
      }

      for (const [id, track] of tracks) {
        if (!seen.has(id)) {
          droneLayer.remove(track.point)
          tracks.delete(id)
        }
      }

      // defensive units in the air (patrol aircraft, interceptors)
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

    // new run (epoch) or Stop => clear the scene
    const offStatuses = client.statuses.on((status) => {
      if (status.epoch !== lastEpoch || status.state === 'idle') {
        lastEpoch = status.epoch
        reset()
      }
    })

    // on every frame: position = interpolation between the last two snapshots
    const offPreRender = viewer.scene.preRender.addEventListener(() => {
      const alpha = Math.min(1, (performance.now() - lastArrival) / intervalMs)
      if (altFadeRef.current !== fadeOn) {
        fadeOn = altFadeRef.current
        for (const track of tracks.values()) applyFade(track, fadeOn)
      }
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

  // 3) countries colored by role (map from /api/airspace/{map}, roles from the scenario)
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
          // world map: countries without a role are not drawn (hundreds of terrain-clamped polygons would slow the globe down)
          if (!role && many) {
            ds.entities.remove(entity)
            continue
          }
          const base = Color.fromCssColorString(role ? ROLE_COLORS[role] : '#64748b')
          entity.polygon.material = base.withAlpha(role ? 0.28 : 0.04) as never
          entity.polygon.outline = false as never

          // outlines of "terrain-clamped" polygons are not rendered => terrain-clamped polylines
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
        // map unavailable: the simulation works without it
      })

    return () => {
      cancelled = true
      if (source && !viewer.isDestroyed()) viewer.dataSources.remove(source, true)
    }
  }, [viewer, airspaceMap, countries])

  // 4) launch bases and targets from the scenario + zoom to the area
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

  // 5) camera: flies to the scenario when the key changes (or the scenario, if there is no key)
  const fitDependency = fitKey ?? scenario
  useEffect(() => {
    if (!viewer || !scenario) return
    viewer.camera.flyTo({ destination: scenarioRectangle(scenario) ?? EUROPE, duration: 1.5 })
    // only fitDependency triggers the flight; the current scenario is read from the closure
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [viewer, fitDependency])

  return <div ref={containerRef} className="cesium-container" />
}
