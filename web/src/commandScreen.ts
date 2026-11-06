import * as THREE from 'three'
import { formatSimTime } from './format'
import type { SimStatus } from './types'

const STATE_LABEL: Record<string, string> = { idle: 'OPRIT', running: 'RULEAZĂ', paused: 'PAUZĂ', finished: 'TERMINAT' }
const STATE_COLOR: Record<string, string> = { idle: '#7f93aa', running: '#3ddc97', paused: '#ffb454', finished: '#4c9aff' }

/** Ecranul comandantului (Screen_Secondary): stare, viteza, timp si contoare principale. Se redeseneaza cand se schimba datele. */
export function makeCommandScreen() {
  const canvas = document.createElement('canvas')
  canvas.width = 1024
  canvas.height = 512
  const ctx = canvas.getContext('2d')!
  const tex = new THREE.CanvasTexture(canvas)
  tex.colorSpace = THREE.SRGBColorSpace

  const setAspect = (aspect: number) => {
    const a = Math.min(Math.max(aspect, 0.5), 4)
    canvas.height = Math.max(256, Math.round(canvas.width / a))
  }

  const draw = (status: SimStatus | null, speed: number) => {
    const w = canvas.width
    const h = canvas.height
    const k = h / 512
    const state = status?.state ?? 'idle'
    ctx.fillStyle = '#06101a'
    ctx.fillRect(0, 0, w, h)
    ctx.strokeStyle = '#1d3b57'
    ctx.lineWidth = 4
    ctx.strokeRect(8, 8, w - 16, h - 16)

    ctx.textBaseline = 'alphabetic'
    ctx.fillStyle = '#7f93aa'
    ctx.font = `${26 * k}px system-ui, sans-serif`
    ctx.fillText('COMANDANT', 36, 56 * k)

    ctx.fillStyle = STATE_COLOR[state] ?? '#7f93aa'
    ctx.font = `bold ${64 * k}px system-ui, sans-serif`
    ctx.fillText(STATE_LABEL[state] ?? state.toUpperCase(), 36, 130 * k)

    ctx.fillStyle = '#e6eef7'
    ctx.font = `${44 * k}px system-ui, sans-serif`
    ctx.fillText(`${speed}×`, w - 36 - ctx.measureText(`${speed}×`).width, 130 * k)
    ctx.fillStyle = '#7f93aa'
    ctx.font = `${24 * k}px system-ui, sans-serif`
    ctx.fillText('viteză', w - 36 - ctx.measureText('viteză').width, 56 * k)

    ctx.fillStyle = '#e6eef7'
    ctx.font = `${52 * k}px ui-monospace, monospace`
    ctx.fillText(status && state !== 'idle' ? formatSimTime(status.simTimeS) : '--:--:--', 36, 210 * k)

    const rows: [string, string, string][] = status && state !== 'idle'
      ? [
          ['În zbor', String(status.flying), '#e6eef7'],
          ['Doborâte', String(status.intercepted), '#3ddc97'],
          ['Impacturi', String(status.impacted), '#ff6b6b'],
          ['Bruiate', String(status.jammed), '#2dd4bf'],
        ]
      : []
    const cw = (w - 72) / Math.max(rows.length, 1)
    rows.forEach(([label, value, color], i) => {
      const x = 36 + i * cw
      ctx.fillStyle = '#7f93aa'
      ctx.font = `${24 * k}px system-ui, sans-serif`
      ctx.fillText(label, x, 300 * k)
      ctx.fillStyle = color
      ctx.font = `bold ${72 * k}px system-ui, sans-serif`
      ctx.fillText(value, x, 380 * k)
    })
    if (rows.length === 0) {
      ctx.fillStyle = '#7f93aa'
      ctx.font = `${30 * k}px system-ui, sans-serif`
      ctx.fillText('Scenariul nu rulează. Folosește butonul de repornire.', 36, 330 * k)
    }
    if (status && state !== 'idle') {
      ctx.fillStyle = '#7f93aa'
      ctx.font = `${22 * k}px system-ui, sans-serif`
      ctx.fillText(`Total ${status.totalDrones} · tiruri ${status.shotsFired} · încălcări ${status.violations}`, 36, 462 * k)
    }
    tex.needsUpdate = true
  }

  return { canvas, tex, draw, setAspect }
}
