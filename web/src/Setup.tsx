import { useEffect, useMemo, useState } from 'react'
import type { Dispatch, SetStateAction } from 'react'
import { Trans, useTranslation } from 'react-i18next'
import { localName } from './i18n/dataNames'
import { api } from './api'
import { deleteScenario, exportScenario, listSaved, parseImported, saveScenario } from './savedScenarios'
import type { SavedScenario } from './savedScenarios'
import CesiumView from './CesiumView'
import {
  BLANK_ID,
  PRESET_IDS,
  blankScenario,
  costs,
  defenseCost,
  formatMoney,
  makeDefense,
  makeLaunchSite,
  makeTarget,
  makeWave,
  maxPairDistanceKm,
  waveCost,
  waveSiteIds,
  waveTargetIds,
} from './draft'
import type { SetupState, WaveForm } from './draft'
import type { SimClient } from './SimClient'
import { DEFENSE_COLORS, UNIT_COLORS, defenseLabel, roleShortLabel, unitLabel } from './roles'
import LanguageSwitch from './i18n/LanguageSwitch'
import { useMediaQuery } from './hooks'
import { dayPhase, localSolarHour, scenarioCenter, scenarioStartMs, startUtcForLocalSolarHour, sunElevationDeg } from './solar'
import type { Alliance, Catalog, CountryRole, DefenseKind, DefenseType, ScenarioInfo } from './types'

const ROLE_ORDER: CountryRole[] = ['defender', 'defenderAlly', 'attacker', 'attackerAlly', 'neutral']
const KIND_ORDER: DefenseKind[] = ['radar', 'sam', 'abm', 'aaGun', 'ew', 'patrol', 'interceptorBase', 'airbase']

interface CountryInfo {
  iso2: string
  name: string
}

const countryCache = new Map<string, CountryInfo[]>()

function useCountries(map: string): CountryInfo[] {
  const [list, setList] = useState<CountryInfo[]>(() => countryCache.get(map) ?? [])

  useEffect(() => {
    if (!map || countryCache.has(map)) return
    let cancelled = false
    fetch(`/api/airspace/${encodeURIComponent(map)}`)
      .then((r) => r.json())
      .then((g: { features?: { properties?: { iso2?: string; name?: string } }[] }) => {
        const items = (g.features ?? [])
          .flatMap((f) => (f.properties?.iso2 && f.properties.name ? [{ iso2: f.properties.iso2, name: f.properties.name }] : []))
          .sort((a, b) => a.name.localeCompare(b.name))
        countryCache.set(map, items)
        if (!cancelled) setList(items)
      })
      .catch(() => {
        // no country list: only alliances can be used
      })
    return () => {
      cancelled = true
    }
  }, [map])

  return list
}

const fold = (t: string) => t.normalize('NFD').replace(/[\u0300-\u036f]/g, '').toLowerCase()

function SearchField({ value, onChange, placeholder }: { value: string; onChange: (v: string) => void; placeholder: string }) {
  const { t } = useTranslation()
  return (
    <div className="search small-search">
      <input value={value} onChange={(e) => onChange(e.target.value)} placeholder={placeholder} />
      {value && (
        <button className="icon-btn" onClick={() => onChange('')} title={t('setup.clearSearch')}>
          ✕
        </button>
      )}
    </div>
  )
}

type Placing = { kind: 'defense'; typeId: string } | { kind: 'site' } | { kind: 'target' } | null

interface Props {
  client: SimClient
  connected: boolean
  scenarios: ScenarioInfo[]
  catalog: Catalog | null
  alliances: Alliance[]
  state: SetupState
  setState: Dispatch<SetStateAction<SetupState>>
  onBack: () => void
  onStart: () => Promise<void>
}

/** Short name of a scenario: the part before the first ":" (the description appears under the selector). */
const shortName = (name: string) => name.split(':')[0].trim()

/** Mean solar time (hours) of the presets: dawn, midday, dusk, night. */
const TIME_PRESETS = [6, 12, 18, 1]
const WEATHER_PRESETS = ['clear', 'overcast', 'rain', 'fog', 'storm']

export default function Setup({ client, connected, scenarios, catalog, alliances, state, setState, onBack, onStart }: Props) {
  // `tr`, not `t`: this file uses `t` as a lambda parameter for catalog types in many places
  const { t: tr } = useTranslation()
  const { scenario } = state
  const countries = useCountries(scenario.airspaceMap)
  const countryName = useMemo(() => new Map(countries.map((c) => [c.iso2, c.name])), [countries])

  const [tab, setTab] = useState<'defense' | 'offense'>('defense')
  // phone layout: one section at a time (the map is its own tab, used for placing)
  const narrow = useMediaQuery()
  const [mTab, setMTab] = useState<'setup' | 'forces' | 'map' | 'start'>('setup')
  const [placing, setPlacing] = useState<Placing>(null)
  const [targetKind, setTargetKind] = useState<'city' | 'military_base'>('city')
  const [showTests, setShowTests] = useState(false)
  const [loading, setLoading] = useState(false)
  const [message, setMessage] = useState('')
  const [error, setError] = useState('')
  const [starting, setStarting] = useState(false)
  const [query, setQuery] = useState('')
  const [filters, setFilters] = useState({ types: '', defs: '', sites: '', targets: '', waves: '', countries: '', formSites: '', formTargets: '' })
  const setFilter = (key: keyof typeof filters) => (value: string) => setFilters((f) => ({ ...f, [key]: value }))
  const has = (text: string, needle: string) => !needle.trim() || fold(text).includes(fold(needle.trim()))
  const [addRole, setAddRole] = useState<CountryRole>('defender')
  const [form, setForm] = useState<WaveForm>({ typeId: '', count: 50, siteIds: [], targetIds: [], startMin: 0, intervalS: 3, spreadM: 200 })

  const budget = state.mode === 'budget'
  const c = costs(state)
  const defenderLeft = state.budgetDefenderM * 1e6 - c.defenderAdded
  const attackerLeft = state.budgetAttackerM * 1e6 - c.attackerAdded

  // phone: choosing something to place jumps to the map
  useEffect(() => {
    if (narrow && placing) setMTab('map')
  }, [narrow, placing])

  // Esc stops placement
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') setPlacing(null)
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [])

  const formTypeId = form.typeId || catalog?.offense[0]?.id || ''

  // ---- preset scenario ----

  const presets = scenarios.filter((s) => s.valid && (showTests || PRESET_IDS.includes(s.id)))
  const selectedPreset = presets.find((p) => p.id === state.presetId)

  // ---- locally saved scenarios ----

  const [saved, setSaved] = useState<SavedScenario[]>(() => listSaved())
  const [saveName, setSaveName] = useState('')

  const attempt = (fn: () => void) => {
    setError('')
    try {
      fn()
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e))
    }
  }

  const saveCurrent = () =>
    attempt(() => {
      saveScenario(saveName || state.scenario.name, state)
      setSaved(listSaved())
      setMessage(tr('setup.saved.savedMsg', { name: saveName || state.scenario.name }))
    })

  const loadSaved = (x: SavedScenario) => {
    setPlacing(null)
    setSaveName(x.name)
    setState((s) => ({ ...s, ...x.state, seed: s.seed }))
    setMessage(tr('setup.saved.loadedMsg', { name: x.name }))
  }

  const removeSaved = (id: string) =>
    attempt(() => {
      deleteScenario(id)
      setSaved(listSaved())
    })

  const exportCurrent = () => {
    const name = (saveName || state.scenario.name).trim()
    const blob = new Blob([exportScenario(name, state)], { type: 'application/json' })
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = `${name.replace(/[^\w.-]+/g, '_') || tr('setup.defaultFile')}.airsim.json`
    a.click()
    URL.revokeObjectURL(url)
  }

  const importFile = async (file: File | undefined) => {
    if (!file) return
    setError('')
    try {
      const { name, state: imported } = parseImported(await file.text())
      setPlacing(null)
      setSaveName(name)
      setState((s) => ({ ...s, ...imported, seed: s.seed }))
      setMessage(tr('setup.saved.importedMsg', { name }))
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e))
    }
  }

  // time of day: presets are expressed as mean solar time at the center of the scenario
  const center = scenarioCenter(state.scenario)
  const startMs = scenarioStartMs(state.scenario)
  let timeChoice = 'none'
  let timeReadout: string | null = null
  if (startMs !== null) {
    const lon = center?.lonDeg ?? 0
    const hour = localSolarHour(lon, startMs)
    const match = TIME_PRESETS.find((h) => Math.abs(hour - h) < 0.05 || Math.abs(hour - h) > 23.95)
    timeChoice = match !== undefined ? String(match) : 'custom'
    const el = sunElevationDeg(center?.latDeg ?? 0, lon, startMs)
    const hh = Math.floor(hour)
    const mm = Math.floor((hour - hh) * 60)
    timeReadout = tr('setup.time.readout', {
      time: `${String(hh).padStart(2, '0')}:${String(mm).padStart(2, '0')}`,
      phase: tr(`setup.time.phase.${dayPhase(el)}`),
      sun: Math.round(el),
    })
  }
  const setTimeOfDay = (choice: string) => {
    if (choice === 'custom') return
    setState((s) => {
      const c = scenarioCenter(s.scenario)
      const startTimeUtc = choice === 'none' ? undefined : startUtcForLocalSolarHour(c?.lonDeg ?? 0, Number(choice))
      return { ...s, scenario: { ...s.scenario, startTimeUtc } }
    })
  }

  const weatherChoice = state.scenario.weather ?? 'clear'
  const setWeather = (choice: string) => {
    setState((s) => ({ ...s, scenario: { ...s.scenario, weather: choice === 'clear' ? undefined : choice } }))
  }

  const loadPreset = async (id: string) => {
    setError('')
    setMessage('')
    setPlacing(null)
    if (id === BLANK_ID) {
      setState((s) => ({ ...s, presetId: id, scenario: blankScenario(), addedDefenses: [], addedWaves: [] }))
      return
    }
    setLoading(true)
    try {
      const fetched = await api.scenario(id)
      // the editor always works on the world map, so any country can be added; the preset's roles (iso2 codes) stay valid
      const loaded = fetched.airspaceMap ? { ...fetched, airspaceMap: 'world' } : fetched
      setState((s) => ({ ...s, presetId: id, scenario: loaded, addedDefenses: [], addedWaves: [] }))
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e))
    } finally {
      setLoading(false)
    }
  }

  // ---- countries and alliances ----

  const setRole = (iso: string, role: CountryRole | null) =>
    setState((s) => {
      const next = { ...s.scenario.countries }
      if (role === null) delete next[iso]
      else next[iso] = role
      return { ...s, scenario: { ...s.scenario, countries: next } }
    })

  /** force = true: also overwrites states already marked as defender / attacker ("all defenders", "all attackers"). */
  const applyAlliance = (a: Alliance, role: CountryRole, force = false) =>
    setState((s) => {
      const next = { ...s.scenario.countries }
      for (const iso of a.members) {
        if (countries.length > 0 && !countryName.has(iso)) continue // not on the map
        if (!force && (next[iso] === 'defender' || next[iso] === 'attacker')) continue // the main states stay as they are
        next[iso] = role
      }
      return { ...s, scenario: { ...s.scenario, countries: next } }
    })

  const removeAlliance = (a: Alliance, force = false) =>
    setState((s) => {
      const next = { ...s.scenario.countries }
      for (const iso of a.members) {
        if (force || next[iso] === 'defenderAlly' || next[iso] === 'attackerAlly') delete next[iso]
      }
      return { ...s, scenario: { ...s.scenario, countries: next } }
    })

  const addCountry = (iso: string) => {
    setRole(iso, addRole)
    setQuery('')
  }

  // ---- placement on the map ----

  const onMapClick = (lat: number, lon: number) => {
    if (!placing) return
    setMessage('')

    if (placing.kind === 'defense') {
      const type = catalog?.defense.find((t) => t.id === placing.typeId)
      if (!type) return
      if (budget && (type.costUsd ?? 0) > defenderLeft) {
        setMessage(tr('setup.mode.noDefBudget'))
        setPlacing(null)
        return
      }
      setState((s) => {
        const d = makeDefense(type, lat, lon, s.scenario)
        return { ...s, scenario: { ...s.scenario, defenses: [...s.scenario.defenses, d] }, addedDefenses: [...s.addedDefenses, d.id] }
      })
    } else if (placing.kind === 'site') {
      setState((s) => ({ ...s, scenario: { ...s.scenario, launchSites: [...s.scenario.launchSites, makeLaunchSite(lat, lon, s.scenario)] } }))
    } else {
      setState((s) => ({ ...s, scenario: { ...s.scenario, targets: [...s.scenario.targets, makeTarget(targetKind, lat, lon, s.scenario)] } }))
    }
  }

  const placingLabel = !placing
    ? ''
    : placing.kind === 'defense'
      ? localName('catalog', catalog?.defense.find((t) => t.id === placing.typeId)?.name ?? '')
      : placing.kind === 'site'
        ? tr('setup.placeLabel.site')
        : targetKind === 'city'
          ? tr('setup.placeLabel.city')
          : tr('setup.placeLabel.base')

  const hasAirbase = scenario.defenses.some((d) => d.kind === 'airbase')

  // ---- deletion ----

  const removeDefense = (id: string) =>
    setState((s) => ({
      ...s,
      scenario: { ...s.scenario, defenses: s.scenario.defenses.filter((d) => d.id !== id) },
      addedDefenses: s.addedDefenses.filter((x) => x !== id),
    }))

  const removeWave = (id: string) =>
    setState((s) => ({
      ...s,
      scenario: { ...s.scenario, waves: s.scenario.waves.filter((w) => w.id !== id) },
      addedWaves: s.addedWaves.filter((x) => x !== id),
    }))

  const removeSite = (id: string) => {
    const user = scenario.waves.find((w) => waveSiteIds(w).includes(id))
    if (user) return setMessage(tr('setup.msg.siteUsed', { id: user.id }))
    setState((s) => ({ ...s, scenario: { ...s.scenario, launchSites: s.scenario.launchSites.filter((x) => x.id !== id) } }))
    setForm((f) => ({ ...f, siteIds: f.siteIds.filter((x) => x !== id) }))
  }

  const removeTarget = (id: string) => {
    const user = scenario.waves.find((w) => waveTargetIds(w).includes(id))
    if (user) return setMessage(tr('setup.msg.targetUsed', { id: user.id }))
    setState((s) => ({ ...s, scenario: { ...s.scenario, targets: s.scenario.targets.filter((x) => x.id !== id) } }))
    setForm((f) => ({ ...f, targetIds: f.targetIds.filter((x) => x !== id) }))
  }

  // ---- waves ----

  const toggle = (list: string[], id: string) => (list.includes(id) ? list.filter((x) => x !== id) : [...list, id])

  const addWave = () => {
    setMessage('')
    const type = catalog?.offense.find((t) => t.id === formTypeId)
    if (!type) return setMessage(tr('setup.msg.pickType'))
    if (!Number.isInteger(form.count) || form.count < 1) return setMessage(tr('setup.msg.badCount'))
    if (form.siteIds.length === 0) return setMessage(tr('setup.msg.pickSite'))
    if (form.targetIds.length === 0) return setMessage(tr('setup.msg.pickTarget'))

    const dist = maxPairDistanceKm(form.siteIds, form.targetIds, scenario)
    if (type.rangeKm && dist * 1.01 > type.rangeKm) {
      return setMessage(tr('setup.msg.range', { name: localName('catalog', type.name), range: type.rangeKm, dist: Math.round(dist) }))
    }
    const cost = form.count * (type.costUsd ?? 0)
    if (budget && cost > attackerLeft) return setMessage(tr('setup.mode.noAttBudget', { need: formatMoney(cost), left: formatMoney(Math.max(0, attackerLeft)) }))

    setState((s) => {
      const w = makeWave(type, { ...form, typeId: type.id }, s.scenario)
      return { ...s, scenario: { ...s.scenario, waves: [...s.scenario.waves, w] }, addedWaves: [...s.addedWaves, w.id] }
    })
  }

  const unitName = (id: string | undefined) => localName('catalog', catalog?.offense.find((t) => t.id === id)?.name ?? id ?? '?')

  // ---- start ----

  const start = async () => {
    setError('')
    setStarting(true)
    try {
      await onStart()
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e))
    } finally {
      setStarting(false)
    }
  }

  const totalUnits = scenario.waves.reduce((n, w) => n + w.count, 0)
  const defenseGroups = KIND_ORDER.map((kind) => ({ kind, types: (catalog?.defense ?? []).filter((t) => t.kind === kind) })).filter((g) => g.types.length > 0)
  const unassigned = countries.filter((x) => !(x.iso2 in scenario.countries))
  const norm = (t: string) => t.normalize('NFD').replace(/[\u0300-\u036f]/g, '').toLowerCase()
  const q = norm(query.trim())
  const matches = q ? unassigned.filter((x) => norm(x.name).includes(q) || x.iso2.toLowerCase() === q).slice(0, 40) : []
  const allCountries: Alliance | null = countries.length > 0 ? { id: 'all', name: tr('setup.countries.all'), members: countries.map((x) => x.iso2), note: '' } : null

  const costOf = (t: DefenseType) => (t.costUsd ? formatMoney(t.costUsd) : '—')

  return (
    <div className="app">
      <CesiumView
        client={client}
        scenario={scenario}
        fitKey={state.presetId}
        onMapClick={onMapClick}
        placing={placing !== null}
        lighting
      />

      {/* ---------- phone: section tabs (hidden on desktop by CSS) ---------- */}
      <nav className="panel m-tabs">
        {(['setup', 'forces', 'map', 'start'] as const).map((k) => (
          <button key={k} className={mTab === k ? 'on' : ''} onClick={() => setMTab(k)}>
            {tr(`setup.m.${k}`)}
          </button>
        ))}
      </nav>
      {mTab === 'map' && placing && (
        <div className="panel m-hint">
          <span>
            <Trans i18nKey="setup.bottom.placing" values={{ what: placingLabel }} components={{ b: <strong /> }} />
          </span>
          <button
            className="primary"
            onClick={() => {
              setPlacing(null)
              setMTab('forces')
            }}
          >
            {tr('setup.m.done')}
          </button>
        </div>
      )}

      {/* ---------- left: scenario, mode, countries ---------- */}
      <aside className={`panel setup-left ${mTab === 'setup' ? 'm-on' : ''}`}>
        <div className="title">
          {tr('setup.title')}
          <span className="title-actions">
            <LanguageSwitch />
            <button onClick={onBack}>{tr('common.menu')}</button>
          </span>
        </div>

        <section>
          <h3>{tr('setup.scenario.heading')}</h3>
          <select className="full" value={state.presetId} onChange={(e) => void loadPreset(e.target.value)} disabled={loading}>
            <option value={BLANK_ID}>{tr('setup.scenario.blank')}</option>
            {presets.map((p) => (
              <option key={p.id} value={p.id}>
                {shortName(localName('scenario', p.name))} ({p.totalUnits})
              </option>
            ))}
          </select>
          {selectedPreset && <div className="muted small">{localName('scenario', selectedPreset.name)}</div>}
          <label className="check">
            <input type="checkbox" checked={showTests} onChange={(e) => setShowTests(e.target.checked)} /> {tr('setup.scenario.showTests')}
          </label>
          <div className="muted small">{tr('setup.scenario.hint')}</div>
        </section>

        <section>
          <h3>{tr('setup.saved.heading')}</h3>
          <div className="row save-row">
            <input className="grow" value={saveName} onChange={(e) => setSaveName(e.target.value)} placeholder={tr('setup.saved.namePlaceholder')} />
            <button onClick={saveCurrent}>{tr('setup.saved.save')}</button>
          </div>
          {saved.length > 0 && (
            <ul className="items">
              {saved.map((x) => (
                <li key={x.id}>
                  <span className="grow">{x.name}</span>
                  <button onClick={() => loadSaved(x)}>{tr('setup.saved.load')}</button>
                  <button className="x" onClick={() => removeSaved(x.id)} title={tr('setup.saved.delete')}>
                    ✕
                  </button>
                </li>
              ))}
            </ul>
          )}
          <div className="subhead">{tr('setup.saved.file')}</div>
          <div className="file-row">
            <button onClick={exportCurrent}>{tr('setup.saved.export')}</button>
            <label className="file-btn">
              {tr('setup.saved.import')}
              <input type="file" accept="application/json,.json" onChange={(e) => void importFile(e.target.files?.[0])} hidden />
            </label>
          </div>
          <div className="muted small">{tr('setup.saved.hint')}</div>
        </section>

        <section>
          <h3>{tr('setup.mode.heading')}</h3>
          <div className="seg">
            <button className={!budget ? 'on' : ''} onClick={() => setState((s) => ({ ...s, mode: 'sandbox' }))}>
              {tr('setup.mode.sandbox')}
            </button>
            <button className={budget ? 'on' : ''} onClick={() => setState((s) => ({ ...s, mode: 'budget' }))}>
              {tr('setup.mode.budget')}
            </button>
          </div>
          {budget ? (
            <>
              <div className="two">
                <label className="field">
                  {tr('setup.mode.defenderBudget')}
                  <input type="number" min={0} value={state.budgetDefenderM} onChange={(e) => setState((s) => ({ ...s, budgetDefenderM: Number(e.target.value) }))} />
                </label>
                <label className="field">
                  {tr('setup.mode.attackerBudget')}
                  <input type="number" min={0} value={state.budgetAttackerM} onChange={(e) => setState((s) => ({ ...s, budgetAttackerM: Number(e.target.value) }))} />
                </label>
              </div>
              <div className="muted small">
                {tr('setup.mode.left', { def: formatMoney(Math.max(0, defenderLeft)), att: formatMoney(Math.max(0, attackerLeft)) })}
              </div>
            </>
          ) : (
            <div className="muted small">{tr('setup.mode.sandboxHint')}</div>
          )}
        </section>

        <section>
          <h3>{tr('setup.time.heading')}</h3>
          <select className="full" value={timeChoice} onChange={(e) => setTimeOfDay(e.target.value)}>
            <option value="none">{tr('setup.time.none')}</option>
            {TIME_PRESETS.map((h) => (
              <option key={h} value={String(h)}>
                {tr(`setup.time.p${h}`)}
              </option>
            ))}
            {timeChoice === 'custom' && <option value="custom">{tr('setup.time.custom')}</option>}
          </select>
          <div className="muted small">{timeReadout ?? tr('setup.time.hint')}</div>
        </section>

        <section>
          <h3>{tr('setup.weather.heading')}</h3>
          <select className="full" value={weatherChoice} onChange={(e) => setWeather(e.target.value)}>
            {WEATHER_PRESETS.map((w) => (
              <option key={w} value={w}>
                {tr(`setup.weather.${w}`)}
              </option>
            ))}
          </select>
          <div className="muted small">{tr(`setup.weather.hint.${weatherChoice}`)}</div>
        </section>

        <section>
          <h3>{tr('setup.warning.heading')}</h3>
          <label className="check">
            <input
              type="checkbox"
              checked={state.scenario.publicWarning !== false}
              onChange={(e) => setState((s) => ({ ...s, scenario: { ...s.scenario, publicWarning: e.target.checked ? undefined : false } }))}
            />{' '}
            {tr('setup.warning.label')}
          </label>
          <div className="muted small">{tr('setup.warning.hint')}</div>
        </section>

        <section>
          <h3>{tr('setup.countries.heading')}</h3>
          {[...(allCountries ? [allCountries] : []), ...alliances].map((a) => (
            <div className="alliance" key={a.id} title={localName('allianceNote', a.note)}>
              <span>{localName('alliance', a.name)}</span>
              <span className="btns">
                <button onClick={() => applyAlliance(a, 'defender', true)} title={tr('setup.countries.allDefendersTip')}>
                  {tr('setup.countries.allDefenders')}
                </button>
                <button onClick={() => applyAlliance(a, 'attacker', true)} title={tr('setup.countries.allAttackersTip')}>
                  {tr('setup.countries.allAttackers')}
                </button>
              </span>
              <span className="btns">
                <button onClick={() => applyAlliance(a, 'defenderAlly')}>{tr('setup.countries.defAlly')}</button>
                <button onClick={() => applyAlliance(a, 'attackerAlly')}>{tr('setup.countries.attAlly')}</button>
                <button onClick={() => removeAlliance(a, a.id === 'all')}>{tr('setup.countries.remove')}</button>
              </span>
            </div>
          ))}
          <div className="muted small">{tr('setup.countries.allianceHint')}</div>

          <SearchField value={filters.countries} onChange={setFilter('countries')} placeholder={tr('setup.countries.searchChosen')} />
          <ul className="items">
            {Object.entries(scenario.countries)
              .filter(([iso]) => has(`${countryName.get(iso) ?? iso} ${iso}`, filters.countries))
              .sort(([a], [b]) => (countryName.get(a) ?? a).localeCompare(countryName.get(b) ?? b))
              .map(([iso, role]) => (
                <li key={iso}>
                  <span className="grow" title={countryName.get(iso) ?? iso}>
                    {countryName.get(iso) ?? iso}
                  </span>
                  <select value={role} onChange={(e) => setRole(iso, e.target.value as CountryRole)}>
                    {ROLE_ORDER.map((r) => (
                      <option key={r} value={r}>
                        {roleShortLabel(r)}
                      </option>
                    ))}
                  </select>
                  <button className="x" onClick={() => setRole(iso, null)} title={tr('setup.countries.removeTip')}>
                    ✕
                  </button>
                </li>
              ))}
          </ul>

          <div className="row">
            <input
              className="grow"
              value={query}
              onChange={(e) => setQuery(e.target.value)}
              placeholder={countries.length === 0 ? tr('setup.countries.loading') : tr('setup.countries.searchCountry', { n: unassigned.length })}
              disabled={countries.length === 0}
            />
            <select value={addRole} onChange={(e) => setAddRole(e.target.value as CountryRole)}>
              {ROLE_ORDER.map((r) => (
                <option key={r} value={r}>
                  {roleShortLabel(r)}
                </option>
              ))}
            </select>
          </div>
          {q && (
            <ul className="items pick-list">
              {matches.length === 0 && <li className="muted">{tr('setup.countries.none')}</li>}
              {matches.map((x) => (
                <li key={x.iso2}>
                  <button className="pick-item" onClick={() => addCountry(x.iso2)}>
                    {x.name} <span className="muted">{x.iso2}</span>
                  </button>
                </li>
              ))}
            </ul>
          )}
        </section>
      </aside>

      {/* ---------- right: defense / attack ---------- */}
      <aside className={`panel setup-right ${mTab === 'forces' ? 'm-on' : ''}`}>
        <div className="seg">
          <button className={tab === 'defense' ? 'on' : ''} onClick={() => setTab('defense')}>
            {tr('setup.tabs.defense', { n: scenario.defenses.length })}
          </button>
          <button className={tab === 'offense' ? 'on' : ''} onClick={() => setTab('offense')}>
            {tr('setup.tabs.offense', { n: totalUnits })}
          </button>
        </div>

        {!catalog && <div className="muted">{tr('setup.loadingCatalog')}</div>}

        {catalog && tab === 'defense' && (
          <>
            <div className="muted small">
              {budget
                ? tr('setup.defense.spent', { spent: formatMoney(c.defenderAdded), total: formatMoney(state.budgetDefenderM * 1e6) })
                : tr('setup.defense.total', { cost: formatMoney(c.defenderTotal) })}
            </div>
            <div className="muted small">{tr('setup.defense.hint')}</div>

            <SearchField value={filters.types} onChange={setFilter('types')} placeholder={tr('setup.defense.searchCatalog')} />
            {defenseGroups
              .map((g) => ({ ...g, types: g.types.filter((t) => has(t.name, filters.types)) }))
              .filter((g) => g.types.length > 0)
              .map((g) => (
              <div key={g.kind} className="group">
                <h4>
                  <span className="swatch" style={{ background: DEFENSE_COLORS[g.kind] }} /> {defenseLabel(g.kind)}
                </h4>
                {g.types.map((t) => {
                  const tooPricey = budget && (t.costUsd ?? 0) > defenderLeft
                  const needsBase = t.kind === 'patrol' && !hasAirbase
                  const armed = placing?.kind === 'defense' && placing.typeId === t.id
                  return (
                    <button
                      key={t.id}
                      className={`type ${armed ? 'armed' : ''}`}
                      disabled={tooPricey || needsBase}
                      title={needsBase ? tr('setup.defense.needBase') : tooPricey ? tr('setup.defense.tooPricey') : ''}
                      onClick={() => setPlacing(armed ? null : { kind: 'defense', typeId: t.id })}
                    >
                      <span className="grow">{localName('catalog', t.name)}</span>
                      <span className="muted">{t.rangeKm ? `${t.rangeKm} km · ` : ''}{costOf(t)}</span>
                    </button>
                  )
                })}
              </div>
            ))}

            <div className="list-head">
              <h4>{tr('setup.defense.inScenario', { n: scenario.defenses.length })}</h4>
              <button
                onClick={() => setState((s) => ({ ...s, scenario: { ...s.scenario, defenses: [] }, addedDefenses: [] }))}
                disabled={scenario.defenses.length === 0}
              >
                {tr('setup.defense.clearAll')}
              </button>
            </div>
            <SearchField value={filters.defs} onChange={setFilter('defs')} placeholder={tr('setup.defense.searchScenario')} />
            <ul className="items">
              {scenario.defenses.filter((d) => has(d.name || d.id, filters.defs)).map((d) => (
                <li key={d.id} className={state.addedDefenses.includes(d.id) ? 'new' : ''}>
                  <span className="swatch" style={{ background: DEFENSE_COLORS[d.kind] }} />
                  <span className="grow">{d.name || d.id}</span>
                  <span className="muted">{defenseCost(d) > 0 ? formatMoney(defenseCost(d)) : ''}</span>
                  <button className="x" onClick={() => removeDefense(d.id)} title={tr('setup.saved.delete')}>
                    ✕
                  </button>
                </li>
              ))}
            </ul>
          </>
        )}

        {catalog && tab === 'offense' && (
          <>
            <div className="muted small">
              {budget
                ? tr('setup.offense.spent', { spent: formatMoney(c.attackerAdded), total: formatMoney(state.budgetAttackerM * 1e6) })
                : tr('setup.offense.total', { cost: formatMoney(c.attackerTotal) })}
            </div>

            <div className="group">
              <div className="list-head">
                <h4>{tr('setup.offense.sites', { n: scenario.launchSites.length })}</h4>
                <button className={placing?.kind === 'site' ? 'armed' : ''} onClick={() => setPlacing(placing?.kind === 'site' ? null : { kind: 'site' })}>
                  {tr('setup.offense.onMap')}
                </button>
              </div>
              <SearchField value={filters.sites} onChange={setFilter('sites')} placeholder={tr('setup.offense.searchSite')} />
              <ul className="items">
                {scenario.launchSites.filter((s) => has(s.name, filters.sites)).map((s) => (
                  <li key={s.id}>
                    <span className="swatch" style={{ background: '#1e90ff' }} />
                    <span className="grow">{s.name}</span>
                    <button className="x" onClick={() => removeSite(s.id)} title={tr('setup.saved.delete')}>
                      ✕
                    </button>
                  </li>
                ))}
              </ul>
            </div>

            <div className="group">
              <div className="list-head">
                <h4>{tr('setup.offense.targets', { n: scenario.targets.length })}</h4>
                <span className="row">
                  <select value={targetKind} onChange={(e) => setTargetKind(e.target.value as 'city' | 'military_base')}>
                    <option value="city">{tr('setup.offense.kindCity')}</option>
                    <option value="military_base">{tr('setup.offense.kindBase')}</option>
                  </select>
                  <button className={placing?.kind === 'target' ? 'armed' : ''} onClick={() => setPlacing(placing?.kind === 'target' ? null : { kind: 'target' })}>
                    {tr('setup.offense.onMap')}
                  </button>
                </span>
              </div>
              <SearchField value={filters.targets} onChange={setFilter('targets')} placeholder={tr('setup.offense.searchTarget')} />
              <ul className="items">
                {scenario.targets.filter((t) => has(t.name, filters.targets)).map((t) => (
                  <li key={t.id}>
                    <span className="swatch" style={{ background: '#ff4500' }} />
                    <span className="grow">{t.name}</span>
                    <button className="x" onClick={() => removeTarget(t.id)} title={tr('setup.saved.delete')}>
                      ✕
                    </button>
                  </li>
                ))}
              </ul>
            </div>

            <div className="group">
              <h4>{tr('setup.offense.newWave')}</h4>
              <label className="field">
                {tr('setup.offense.unit')}
                <select value={formTypeId} onChange={(e) => setForm({ ...form, typeId: e.target.value })}>
                  {catalog.offense.map((t) => (
                    <option key={t.id} value={t.id}>
                      {localName('catalog', t.name)}
                      {t.costUsd ? ` — ${tr('setup.offense.perUnit', { cost: formatMoney(t.costUsd) })}` : ''}
                    </option>
                  ))}
                </select>
              </label>
              <div className="two">
                <label className="field">
                  {tr('setup.offense.count')}
                  <input type="number" min={1} value={form.count} onChange={(e) => setForm({ ...form, count: Number(e.target.value) })} />
                </label>
                <label className="field">
                  {tr('setup.offense.startMin')}
                  <input type="number" min={0} value={form.startMin} onChange={(e) => setForm({ ...form, startMin: Number(e.target.value) })} />
                </label>
                <label className="field">
                  {tr('setup.offense.intervalS')}
                  <input type="number" min={0} step={0.5} value={form.intervalS} onChange={(e) => setForm({ ...form, intervalS: Number(e.target.value) })} />
                </label>
                <label className="field">
                  {tr('setup.offense.spreadM')}
                  <input type="number" min={0} value={form.spreadM} onChange={(e) => setForm({ ...form, spreadM: Number(e.target.value) })} />
                </label>
              </div>
              <div className="pick">
                <div>
                  <div className="muted small">{tr('setup.offense.from')}</div>
                  <SearchField value={filters.formSites} onChange={setFilter('formSites')} placeholder={tr('setup.offense.search')} />
                  {scenario.launchSites.length === 0 && <div className="muted small">{tr('setup.offense.addSites')}</div>}
                  {scenario.launchSites.filter((s) => has(s.name, filters.formSites)).map((s) => (
                    <label key={s.id} className="check">
                      <input type="checkbox" checked={form.siteIds.includes(s.id)} onChange={() => setForm({ ...form, siteIds: toggle(form.siteIds, s.id) })} /> {s.name}
                    </label>
                  ))}
                </div>
                <div>
                  <div className="muted small">{tr('setup.offense.to')}</div>
                  <SearchField value={filters.formTargets} onChange={setFilter('formTargets')} placeholder={tr('setup.offense.search')} />
                  {scenario.targets.length === 0 && <div className="muted small">{tr('setup.offense.addTargets')}</div>}
                  {scenario.targets.filter((t) => has(t.name, filters.formTargets)).map((t) => (
                    <label key={t.id} className="check">
                      <input type="checkbox" checked={form.targetIds.includes(t.id)} onChange={() => setForm({ ...form, targetIds: toggle(form.targetIds, t.id) })} /> {t.name}
                    </label>
                  ))}
                </div>
              </div>
              <button className="primary" onClick={addWave}>
                {tr('setup.offense.addWave')}
              </button>
            </div>

            <div className="list-head">
              <h4>{tr('setup.offense.waves', { n: scenario.waves.length })}</h4>
              <button onClick={() => setState((s) => ({ ...s, scenario: { ...s.scenario, waves: [] }, addedWaves: [] }))} disabled={scenario.waves.length === 0}>
                {tr('setup.defense.clearAll')}
              </button>
            </div>
            <SearchField value={filters.waves} onChange={setFilter('waves')} placeholder={tr('setup.offense.searchWave')} />
            <ul className="items">
              {scenario.waves.filter((w) => has(`${w.id} ${unitName(w.unit ?? w.unitType)}`, filters.waves)).map((w) => (
                <li key={w.id} className={state.addedWaves.includes(w.id) ? 'new' : ''}>
                  <span className="swatch" style={{ background: UNIT_COLORS[w.kind] }} title={unitLabel(w.kind)} />
                  <span className="grow">
                    {w.count}× {unitName(w.unit ?? w.unitType)}
                  </span>
                  <span className="muted">{waveCost(w) > 0 ? formatMoney(waveCost(w)) : ''}</span>
                  <button className="x" onClick={() => removeWave(w.id)} title={tr('setup.saved.delete')}>
                    ✕
                  </button>
                </li>
              ))}
            </ul>
          </>
        )}
      </aside>

      {/* ---------- bottom: start ---------- */}
      <div className={`panel setup-bar ${mTab === 'start' ? 'm-on' : ''}`}>
        {placing && (
          <div className="hint">
            <Trans i18nKey="setup.bottom.placing" values={{ what: placingLabel }} components={{ b: <strong /> }} />
          </div>
        )}
        {(message || error) && <div className="error">{error || message}</div>}
        <div className="row">
          <span className="muted">
            {tr('setup.bottom.counts', { d: scenario.defenses.length, u: totalUnits })}
          </span>
          <input
            className="seed"
            value={state.seed}
            onChange={(e) => setState((s) => ({ ...s, seed: e.target.value }))}
            placeholder={tr('setup.bottom.seedPlaceholder')}
            inputMode="numeric"
          />
          <button className="primary" onClick={() => void start()} disabled={!connected || starting || totalUnits === 0}>
            {starting ? tr('setup.bottom.starting') : tr('setup.bottom.start')}
          </button>
        </div>
        {totalUnits === 0 && <div className="muted small">{tr('setup.bottom.needWave')}</div>}
      </div>
    </div>
  )
}
