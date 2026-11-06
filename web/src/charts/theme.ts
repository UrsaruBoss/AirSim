/**
 * Chart colors: the dark steps of the reference categorical palette (validated against the modal surface, see dataviz skill).
 * One color per entity everywhere it appears (an "impact" is the same orange in every chart); chart text never wears them.
 */
export const COLORS = {
  intercepted: '#3987e5',
  impacts: '#d95926',
  jammed: '#199e70',
  crashed: '#c98500',
  killed: '#d55181',
  injured: '#008300',
  magazines: '#9085e9',
  attack: '#e66767',
  defense: '#3987e5',
  /** Context / "nothing happened yet": recessive gray. */
  rest: '#5b6678',
} as const

/** Label color inside a filled mark: near-black on the light fills, white on the rest. */
export const inkOn = (fill: string): string => (fill === COLORS.crashed ? '#0b0b0b' : '#ffffff')
