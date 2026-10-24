import { useEffect, useMemo, useState } from 'react'
import type { Dispatch, SetStateAction } from 'react'
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
import { DEFENSE_COLORS, DEFENSE_LABELS, UNIT_COLORS, UNIT_LABELS } from './roles'
import type { Alliance, Catalog, CountryRole, DefenseKind, DefenseType, ScenarioInfo } from './types'

const ROLE_SHORT: Record<CountryRole, string> = {
  defender: 'Apărător',
  defenderAlly: 'Aliat apărător',
  attacker: 'Atacator',
  attackerAlly: 'Aliat atacator',
  neutral: 'Neutru',
}

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
        // fara lista de tari: se pot folosi doar aliantele
      })
    return () => {
      cancelled = true
    }
  }, [map])

  return list
}

const fold = (t: string) => t.normalize('NFD').replace(/[\u0300-\u036f]/g, '').toLowerCase()

function SearchField({ value, onChange, placeholder }: { value: string; onChange: (v: string) => void; placeholder: string }) {
  return (
    <div className="search small-search">
      <input value={value} onChange={(e) => onChange(e.target.value)} placeholder={placeholder} />
      {value && (
        <button className="icon-btn" onClick={() => onChange('')} title="Șterge căutarea">
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
  /** Modul immersive: butonul de pornire duce in buncar. */
  immersive?: boolean
}

/** Numele scurt al unui scenariu: partea dinaintea primului ":" (descrierea apare sub selector). */
const shortName = (name: string) => name.split(':')[0].trim()

export default function Setup({ client, connected, scenarios, catalog, alliances, state, setState, onBack, onStart, immersive = false }: Props) {
  const { scenario } = state
  const countries = useCountries(scenario.airspaceMap)
  const countryName = useMemo(() => new Map(countries.map((c) => [c.iso2, c.name])), [countries])

  const [tab, setTab] = useState<'defense' | 'offense'>('defense')
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

  // Esc opreste plasarea
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') setPlacing(null)
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [])

  const formTypeId = form.typeId || catalog?.offense[0]?.id || ''

  // ---- scenariu prestabilit ----

  const presets = scenarios.filter((s) => s.valid && (showTests || PRESET_IDS.includes(s.id)))
  const selectedPreset = presets.find((p) => p.id === state.presetId)

  // ---- scenarii salvate local ----

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
      setMessage(`Salvat: ${saveName || state.scenario.name}`)
    })

  const loadSaved = (x: SavedScenario) => {
    setPlacing(null)
    setSaveName(x.name)
    setState((s) => ({ ...s, ...x.state, seed: s.seed }))
    setMessage(`Încărcat: ${x.name}`)
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
    a.download = `${name.replace(/[^\w.-]+/g, '_') || 'scenariu'}.airsim.json`
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
      setMessage(`Importat: ${name}. Apasă „Salvează” ca să-l păstrezi în browser.`)
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e))
    }
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
      // editorul lucreaza mereu pe harta lumii, ca sa poti adauga orice tara; rolurile din preset (coduri iso2) raman valabile
      const loaded = fetched.airspaceMap ? { ...fetched, airspaceMap: 'world' } : fetched
      setState((s) => ({ ...s, presetId: id, scenario: loaded, addedDefenses: [], addedWaves: [] }))
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e))
    } finally {
      setLoading(false)
    }
  }

  // ---- tari si aliante ----

  const setRole = (iso: string, role: CountryRole | null) =>
    setState((s) => {
      const next = { ...s.scenario.countries }
      if (role === null) delete next[iso]
      else next[iso] = role
      return { ...s, scenario: { ...s.scenario, countries: next } }
    })

  /** force = true: suprascrie si statele marcate deja ca apărător / atacator ("toți apărători", "toți atacatori"). */
  const applyAlliance = (a: Alliance, role: CountryRole, force = false) =>
    setState((s) => {
      const next = { ...s.scenario.countries }
      for (const iso of a.members) {
        if (countries.length > 0 && !countryName.has(iso)) continue // nu e pe harta
        if (!force && (next[iso] === 'defender' || next[iso] === 'attacker')) continue // statele principale raman
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

  // ---- plasare pe harta ----

  const onMapClick = (lat: number, lon: number) => {
    if (!placing) return
    setMessage('')

    if (placing.kind === 'defense') {
      const type = catalog?.defense.find((t) => t.id === placing.typeId)
      if (!type) return
      if (budget && (type.costUsd ?? 0) > defenderLeft) {
        setMessage('Buget de apărare insuficient.')
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
      ? (catalog?.defense.find((t) => t.id === placing.typeId)?.name ?? '')
      : placing.kind === 'site'
        ? 'punct de lansare'
        : targetKind === 'city'
          ? 'țintă (oraș)'
          : 'țintă (bază militară)'

  const hasAirbase = scenario.defenses.some((d) => d.kind === 'airbase')

  // ---- stergere ----

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
    if (user) return setMessage(`Punctul de lansare e folosit de valul ${user.id}; șterge întâi valul.`)
    setState((s) => ({ ...s, scenario: { ...s.scenario, launchSites: s.scenario.launchSites.filter((x) => x.id !== id) } }))
    setForm((f) => ({ ...f, siteIds: f.siteIds.filter((x) => x !== id) }))
  }

  const removeTarget = (id: string) => {
    const user = scenario.waves.find((w) => waveTargetIds(w).includes(id))
    if (user) return setMessage(`Ținta e folosită de valul ${user.id}; șterge întâi valul.`)
    setState((s) => ({ ...s, scenario: { ...s.scenario, targets: s.scenario.targets.filter((x) => x.id !== id) } }))
    setForm((f) => ({ ...f, targetIds: f.targetIds.filter((x) => x !== id) }))
  }

  // ---- valuri ----

  const toggle = (list: string[], id: string) => (list.includes(id) ? list.filter((x) => x !== id) : [...list, id])

  const addWave = () => {
    setMessage('')
    const type = catalog?.offense.find((t) => t.id === formTypeId)
    if (!type) return setMessage('Alege un tip de unitate.')
    if (!Number.isInteger(form.count) || form.count < 1) return setMessage('Numărul de unități trebuie să fie un întreg ≥ 1.')
    if (form.siteIds.length === 0) return setMessage('Alege cel puțin un punct de lansare.')
    if (form.targetIds.length === 0) return setMessage('Alege cel puțin o țintă.')

    const dist = maxPairDistanceKm(form.siteIds, form.targetIds, scenario)
    if (type.rangeKm && dist * 1.01 > type.rangeKm) {
      return setMessage(`${type.name} are raza ${type.rangeKm} km, dar o pereche lansare → țintă e la ${Math.round(dist)} km.`)
    }
    const cost = form.count * (type.costUsd ?? 0)
    if (budget && cost > attackerLeft) return setMessage(`Buget de atac insuficient (${formatMoney(cost)} necesari, ${formatMoney(Math.max(0, attackerLeft))} rămași).`)

    setState((s) => {
      const w = makeWave(type, { ...form, typeId: type.id }, s.scenario)
      return { ...s, scenario: { ...s.scenario, waves: [...s.scenario.waves, w] }, addedWaves: [...s.addedWaves, w.id] }
    })
  }

  const unitName = (id: string | undefined) => catalog?.offense.find((t) => t.id === id)?.name ?? id ?? '?'

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
  const allCountries: Alliance | null = countries.length > 0 ? { id: 'all', name: 'Toate țările de pe hartă', members: countries.map((x) => x.iso2), note: '' } : null

  const costOf = (t: DefenseType) => (t.costUsd ? formatMoney(t.costUsd) : '—')

  return (
    <div className="app">
      <CesiumView
        client={client}
        scenario={scenario}
        fitKey={state.presetId}
        onMapClick={onMapClick}
        placing={placing !== null}
      />

      {/* ---------- stanga: scenariu, mod, state ---------- */}
      <aside className="panel setup-left">
        <div className="title">
          Configurare
          <button onClick={onBack}>← Meniu</button>
        </div>

        <section>
          <h3>Scenariu</h3>
          <select value={state.presetId} onChange={(e) => void loadPreset(e.target.value)} disabled={loading}>
            <option value={BLANK_ID}>— de la zero —</option>
            {presets.map((p) => (
              <option key={p.id} value={p.id}>
                {shortName(p.name)} ({p.totalUnits})
              </option>
            ))}
          </select>
          {selectedPreset && <div className="muted small">{selectedPreset.name}</div>}
          <label className="check">
            <input type="checkbox" checked={showTests} onChange={(e) => setShowTests(e.target.checked)} /> arată și scenariile de test
          </label>
          <div className="muted small">Scenariul prestabilit e doar punctul de start: poți adăuga și șterge orice. Schimbarea lui renunță la modificări.</div>
        </section>

        <section>
          <h3>Scenarii salvate</h3>
          <div className="row save-row">
            <input className="grow" value={saveName} onChange={(e) => setSaveName(e.target.value)} placeholder="nume scenariu" />
            <button onClick={saveCurrent}>Salvează</button>
          </div>
          {saved.length > 0 && (
            <ul className="items">
              {saved.map((x) => (
                <li key={x.id}>
                  <span className="grow">{x.name}</span>
                  <button onClick={() => loadSaved(x)}>Încarcă</button>
                  <button className="x" onClick={() => removeSaved(x.id)} title="Șterge">
                    ✕
                  </button>
                </li>
              ))}
            </ul>
          )}
          <div className="row">
            <button onClick={exportCurrent}>Export fișier</button>
            <label className="file-btn">
              Import fișier
              <input type="file" accept="application/json,.json" onChange={(e) => void importFile(e.target.files?.[0])} hidden />
            </label>
          </div>
          <div className="muted small">Se păstrează doar în acest browser (localStorage). Exportul îți dă un fișier pe care îl poți muta pe alt calculator.</div>
        </section>

        <section>
          <h3>Mod</h3>
          <div className="seg">
            <button className={!budget ? 'on' : ''} onClick={() => setState((s) => ({ ...s, mode: 'sandbox' }))}>
              Sandbox
            </button>
            <button className={budget ? 'on' : ''} onClick={() => setState((s) => ({ ...s, mode: 'budget' }))}>
              Cu buget
            </button>
          </div>
          {budget ? (
            <>
              <div className="two">
                <label className="field">
                  Buget apărare (mil $)
                  <input type="number" min={0} value={state.budgetDefenderM} onChange={(e) => setState((s) => ({ ...s, budgetDefenderM: Number(e.target.value) }))} />
                </label>
                <label className="field">
                  Buget atac (mil $)
                  <input type="number" min={0} value={state.budgetAttackerM} onChange={(e) => setState((s) => ({ ...s, budgetAttackerM: Number(e.target.value) }))} />
                </label>
              </div>
              <div className="muted small">
                Rămas: apărare {formatMoney(Math.max(0, defenderLeft))} · atac {formatMoney(Math.max(0, attackerLeft))}. Se plătesc doar elementele pe care le adaugi; ce vine din scenariu e gratuit.
              </div>
            </>
          ) : (
            <div className="muted small">Fără limite; costurile se afișează doar informativ.</div>
          )}
        </section>

        <section>
          <h3>State și aliați</h3>
          {[...(allCountries ? [allCountries] : []), ...alliances].map((a) => (
            <div className="alliance" key={a.id} title={a.note}>
              <span>{a.name}</span>
              <span className="btns">
                <button onClick={() => applyAlliance(a, 'defender', true)} title="Toate devin apărători (suprascrie)">
                  toți apărători
                </button>
                <button onClick={() => applyAlliance(a, 'attacker', true)} title="Toate devin atacatori (suprascrie)">
                  toți atacatori
                </button>
              </span>
              <span className="btns">
                <button onClick={() => applyAlliance(a, 'defenderAlly')}>aliat apărare</button>
                <button onClick={() => applyAlliance(a, 'attackerAlly')}>aliat atac</button>
                <button onClick={() => removeAlliance(a, a.id === 'all')}>scoate</button>
              </span>
            </div>
          ))}
          <div className="muted small">Aliații nu schimbă statele marcate ca apărător / atacator. Membrii fără hartă sunt ignorați (harta „world” le acoperă pe toate).</div>

          <SearchField value={filters.countries} onChange={setFilter('countries')} placeholder="caută în țările alese…" />
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
                        {ROLE_SHORT[r]}
                      </option>
                    ))}
                  </select>
                  <button className="x" onClick={() => setRole(iso, null)} title="Scoate">
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
              placeholder={countries.length === 0 ? 'se încarcă țările…' : `caută țară (${unassigned.length} disponibile)…`}
              disabled={countries.length === 0}
            />
            <select value={addRole} onChange={(e) => setAddRole(e.target.value as CountryRole)}>
              {ROLE_ORDER.map((r) => (
                <option key={r} value={r}>
                  {ROLE_SHORT[r]}
                </option>
              ))}
            </select>
          </div>
          {q && (
            <ul className="items pick-list">
              {matches.length === 0 && <li className="muted">nicio țară găsită</li>}
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

      {/* ---------- dreapta: aparare / atac ---------- */}
      <aside className="panel setup-right">
        <div className="seg">
          <button className={tab === 'defense' ? 'on' : ''} onClick={() => setTab('defense')}>
            Apărare ({scenario.defenses.length})
          </button>
          <button className={tab === 'offense' ? 'on' : ''} onClick={() => setTab('offense')}>
            Atac ({totalUnits})
          </button>
        </div>

        {!catalog && <div className="muted">Se încarcă catalogul…</div>}

        {catalog && tab === 'defense' && (
          <>
            <div className="muted small">
              {budget
                ? `Cheltuit: ${formatMoney(c.defenderAdded)} din ${formatMoney(state.budgetDefenderM * 1e6)}`
                : `Cost total apărare: ${formatMoney(c.defenderTotal)}`}
            </div>
            <div className="muted small">Alege un sistem, apoi dă click pe hartă (poți plasa mai multe). Esc oprește plasarea.</div>

            <SearchField value={filters.types} onChange={setFilter('types')} placeholder="caută sistem în catalog…" />
            {defenseGroups
              .map((g) => ({ ...g, types: g.types.filter((t) => has(t.name, filters.types)) }))
              .filter((g) => g.types.length > 0)
              .map((g) => (
              <div key={g.kind} className="group">
                <h4>
                  <span className="swatch" style={{ background: DEFENSE_COLORS[g.kind] }} /> {DEFENSE_LABELS[g.kind]}
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
                      title={needsBase ? 'Avioanele de patrulă cer o bază aeriană.' : tooPricey ? 'Buget insuficient.' : ''}
                      onClick={() => setPlacing(armed ? null : { kind: 'defense', typeId: t.id })}
                    >
                      <span className="grow">{t.name}</span>
                      <span className="muted">{t.rangeKm ? `${t.rangeKm} km · ` : ''}{costOf(t)}</span>
                    </button>
                  )
                })}
              </div>
            ))}

            <div className="list-head">
              <h4>În scenariu ({scenario.defenses.length})</h4>
              <button
                onClick={() => setState((s) => ({ ...s, scenario: { ...s.scenario, defenses: [] }, addedDefenses: [] }))}
                disabled={scenario.defenses.length === 0}
              >
                Șterge tot
              </button>
            </div>
            <SearchField value={filters.defs} onChange={setFilter('defs')} placeholder="caută în scenariu…" />
            <ul className="items">
              {scenario.defenses.filter((d) => has(d.name || d.id, filters.defs)).map((d) => (
                <li key={d.id} className={state.addedDefenses.includes(d.id) ? 'new' : ''}>
                  <span className="swatch" style={{ background: DEFENSE_COLORS[d.kind] }} />
                  <span className="grow">{d.name || d.id}</span>
                  <span className="muted">{defenseCost(d) > 0 ? formatMoney(defenseCost(d)) : ''}</span>
                  <button className="x" onClick={() => removeDefense(d.id)} title="Șterge">
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
                ? `Cheltuit: ${formatMoney(c.attackerAdded)} din ${formatMoney(state.budgetAttackerM * 1e6)}`
                : `Cost total atac: ${formatMoney(c.attackerTotal)}`}
            </div>

            <div className="group">
              <div className="list-head">
                <h4>Puncte de lansare ({scenario.launchSites.length})</h4>
                <button className={placing?.kind === 'site' ? 'armed' : ''} onClick={() => setPlacing(placing?.kind === 'site' ? null : { kind: 'site' })}>
                  + pe hartă
                </button>
              </div>
              <SearchField value={filters.sites} onChange={setFilter('sites')} placeholder="caută punct de lansare…" />
              <ul className="items">
                {scenario.launchSites.filter((s) => has(s.name, filters.sites)).map((s) => (
                  <li key={s.id}>
                    <span className="swatch" style={{ background: '#1e90ff' }} />
                    <span className="grow">{s.name}</span>
                    <button className="x" onClick={() => removeSite(s.id)} title="Șterge">
                      ✕
                    </button>
                  </li>
                ))}
              </ul>
            </div>

            <div className="group">
              <div className="list-head">
                <h4>Ținte ({scenario.targets.length})</h4>
                <span className="row">
                  <select value={targetKind} onChange={(e) => setTargetKind(e.target.value as 'city' | 'military_base')}>
                    <option value="city">oraș</option>
                    <option value="military_base">bază / obiectiv</option>
                  </select>
                  <button className={placing?.kind === 'target' ? 'armed' : ''} onClick={() => setPlacing(placing?.kind === 'target' ? null : { kind: 'target' })}>
                    + pe hartă
                  </button>
                </span>
              </div>
              <SearchField value={filters.targets} onChange={setFilter('targets')} placeholder="caută țintă…" />
              <ul className="items">
                {scenario.targets.filter((t) => has(t.name, filters.targets)).map((t) => (
                  <li key={t.id}>
                    <span className="swatch" style={{ background: '#ff4500' }} />
                    <span className="grow">{t.name}</span>
                    <button className="x" onClick={() => removeTarget(t.id)} title="Șterge">
                      ✕
                    </button>
                  </li>
                ))}
              </ul>
            </div>

            <div className="group">
              <h4>Val nou</h4>
              <label className="field">
                Unitate
                <select value={formTypeId} onChange={(e) => setForm({ ...form, typeId: e.target.value })}>
                  {catalog.offense.map((t) => (
                    <option key={t.id} value={t.id}>
                      {t.name}
                      {t.costUsd ? ` — ${formatMoney(t.costUsd)}/buc` : ''}
                    </option>
                  ))}
                </select>
              </label>
              <div className="two">
                <label className="field">
                  Număr
                  <input type="number" min={1} value={form.count} onChange={(e) => setForm({ ...form, count: Number(e.target.value) })} />
                </label>
                <label className="field">
                  Start (min)
                  <input type="number" min={0} value={form.startMin} onChange={(e) => setForm({ ...form, startMin: Number(e.target.value) })} />
                </label>
                <label className="field">
                  Interval (s)
                  <input type="number" min={0} step={0.5} value={form.intervalS} onChange={(e) => setForm({ ...form, intervalS: Number(e.target.value) })} />
                </label>
                <label className="field">
                  Dispersie (m)
                  <input type="number" min={0} value={form.spreadM} onChange={(e) => setForm({ ...form, spreadM: Number(e.target.value) })} />
                </label>
              </div>
              <div className="pick">
                <div>
                  <div className="muted small">Din:</div>
                  <SearchField value={filters.formSites} onChange={setFilter('formSites')} placeholder="caută…" />
                  {scenario.launchSites.length === 0 && <div className="muted small">adaugă puncte de lansare</div>}
                  {scenario.launchSites.filter((s) => has(s.name, filters.formSites)).map((s) => (
                    <label key={s.id} className="check">
                      <input type="checkbox" checked={form.siteIds.includes(s.id)} onChange={() => setForm({ ...form, siteIds: toggle(form.siteIds, s.id) })} /> {s.name}
                    </label>
                  ))}
                </div>
                <div>
                  <div className="muted small">Spre:</div>
                  <SearchField value={filters.formTargets} onChange={setFilter('formTargets')} placeholder="caută…" />
                  {scenario.targets.length === 0 && <div className="muted small">adaugă ținte</div>}
                  {scenario.targets.filter((t) => has(t.name, filters.formTargets)).map((t) => (
                    <label key={t.id} className="check">
                      <input type="checkbox" checked={form.targetIds.includes(t.id)} onChange={() => setForm({ ...form, targetIds: toggle(form.targetIds, t.id) })} /> {t.name}
                    </label>
                  ))}
                </div>
              </div>
              <button className="primary" onClick={addWave}>
                Adaugă valul
              </button>
            </div>

            <div className="list-head">
              <h4>Valuri ({scenario.waves.length})</h4>
              <button onClick={() => setState((s) => ({ ...s, scenario: { ...s.scenario, waves: [] }, addedWaves: [] }))} disabled={scenario.waves.length === 0}>
                Șterge tot
              </button>
            </div>
            <SearchField value={filters.waves} onChange={setFilter('waves')} placeholder="caută val…" />
            <ul className="items">
              {scenario.waves.filter((w) => has(`${w.id} ${unitName(w.unit ?? w.unitType)}`, filters.waves)).map((w) => (
                <li key={w.id} className={state.addedWaves.includes(w.id) ? 'new' : ''}>
                  <span className="swatch" style={{ background: UNIT_COLORS[w.kind] }} title={UNIT_LABELS[w.kind]} />
                  <span className="grow">
                    {w.count}× {unitName(w.unit ?? w.unitType)}
                  </span>
                  <span className="muted">{waveCost(w) > 0 ? formatMoney(waveCost(w)) : ''}</span>
                  <button className="x" onClick={() => removeWave(w.id)} title="Șterge">
                    ✕
                  </button>
                </li>
              ))}
            </ul>
          </>
        )}
      </aside>

      {/* ---------- jos: start ---------- */}
      <div className="panel setup-bar">
        {placing && (
          <div className="hint">
            Plasezi: <strong>{placingLabel}</strong> — click pe hartă · Esc oprește
          </div>
        )}
        {(message || error) && <div className="error">{error || message}</div>}
        <div className="row">
          <span className="muted">
            {scenario.defenses.length} sisteme de apărare · {totalUnits} unități de atac
          </span>
          <input
            className="seed"
            value={state.seed}
            onChange={(e) => setState((s) => ({ ...s, seed: e.target.value }))}
            placeholder="seed (automat)"
            inputMode="numeric"
          />
          <button className="primary" onClick={() => void start()} disabled={!connected || starting || totalUnits === 0}>
            {starting ? 'Pornește…' : immersive ? '▶ Intră în buncăr' : '▶ Pornește simularea'}
          </button>
        </div>
        {totalUnits === 0 && <div className="muted small">Adaugă cel puțin un val de atac (fila Atac).</div>}
      </div>
    </div>
  )
}
