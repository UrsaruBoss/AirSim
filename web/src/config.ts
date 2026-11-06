/** Setari din .env (vezi .env.example). Se citesc la pornirea Vite: dupa o schimbare, reporneste `npm run dev`. */
const flag = (v: unknown) => v === 'true' || v === '1'

export const SPEEDS = [1, 10, 50, 100, 500]

export const config = {
  /** Panou de debug in Immersive (reincarca modelul, reset pozitie / scenariu / buncar) + taste F7 / F8 / F9. */
  debug: flag(import.meta.env.VITE_DEBUG),
  /** Viteza cu care porneste simularea cand intri in Immersive. */
  immersiveSpeed: SPEEDS.includes(Number(import.meta.env.VITE_IMMERSIVE_SPEED)) ? Number(import.meta.env.VITE_IMMERSIVE_SPEED) : 50,
  /** Adresa serverului .NET (doar de afisat: proxy-ul Vite il foloseste din vite.config.ts). */
  serverUrl: (import.meta.env.VITE_SERVER_URL as string | undefined) || 'http://localhost:5080',
}
