// Display names that come from the server data (scenarios, alliances, catalog, comparison metrics).
// The data files are in English; for Romanian we look the English text up in data.ro.json and fall back to the original.
import i18n from './index'
import ro from './data.ro.json'

type Kind = keyof typeof ro

export function localName(kind: Kind, name: string): string {
  if (!i18n.language?.startsWith('ro')) return name
  return (ro[kind] as Record<string, string>)[name] ?? name
}
