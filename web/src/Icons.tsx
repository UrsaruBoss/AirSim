import type { ReactNode } from 'react'

/** A small set of SVG icons (24x24, outline), so the interface does not depend on emoji or on a library. */
const PATHS: Record<string, ReactNode> = {
  play: <polygon points="7 4 20 12 7 20" fill="currentColor" stroke="none" />,
  pause: (
    <>
      <rect x="6" y="4" width="4" height="16" fill="currentColor" stroke="none" />
      <rect x="14" y="4" width="4" height="16" fill="currentColor" stroke="none" />
    </>
  ),
  stop: <rect x="6" y="6" width="12" height="12" rx="1" fill="currentColor" stroke="none" />,
  restart: <path d="M4 12a8 8 0 1 0 3-6.2M4 4v5h5" />,
  save: <path d="M5 3h11l3 3v15H5z M8 3v6h8V3 M8 21v-7h8v7" />,
  home: <path d="M3 11l9-8 9 8 M5 10v10h14V10 M10 20v-6h4v6" />,
  sliders: (
    <>
      <path d="M4 6h8 M18 6h2 M4 12h2 M12 12h8 M4 18h10 M20 18h0" />
      <circle cx="15" cy="6" r="2" />
      <circle cx="9" cy="12" r="2" />
      <circle cx="17" cy="18" r="2" />
    </>
  ),
  chart: <path d="M4 20V10 M10 20V4 M16 20v-7 M22 20H2" />,
  layers: <path d="M12 3L3 8l9 5 9-5z M3 13l9 5 9-5" />,
  list: <path d="M8 6h13 M8 12h13 M8 18h13 M3 6h.01 M3 12h.01 M3 18h.01" />,
  expand: <path d="M4 9V4h5 M20 9V4h-5 M4 15v5h5 M20 15v5h-5" />,
  search: (
    <>
      <circle cx="11" cy="11" r="7" />
      <path d="M21 21l-5-5" />
    </>
  ),
  close: <path d="M6 6l12 12 M18 6L6 18" />,
  clipboard: <path d="M9 4h6v3H9z M7 5H5v16h14V5h-2 M9 12h6 M9 16h6" />,
  launch: <path d="M5 19L19 5 M9 5h10v10" />,
  radar: (
    <>
      <circle cx="12" cy="12" r="9" />
      <path d="M12 12l6-6" />
      <circle cx="12" cy="12" r="1.5" fill="currentColor" />
    </>
  ),
  check: <path d="M4 12l5 5L20 6" />,
  burst: <polygon points="12 2 14.5 8.5 21 9 16 13.5 17.5 20 12 16.5 6.5 20 8 13.5 3 9 9.5 8.5" />,
  alert: <path d="M12 3l10 18H2z M12 10v5 M12 18h.01" />,
  bolt: <path d="M13 2L4 14h7l-1 8 9-12h-7z" />,
  down: <path d="M5 5l14 14 M19 9v10H9" />,
  shuffle: <path d="M3 7h13 M13 4l3 3-3 3 M21 17H8 M11 14l-3 3 3 3" />,
  shieldOff: <path d="M12 3l8 3v6c0 5-3.5 8-8 9-4.5-1-8-4-8-9V6z M4 4l16 16" />,
  undo: <path d="M9 14L4 9l5-5 M4 9h10a6 6 0 0 1 0 12h-3" />,
  sun: (
    <>
      <circle cx="12" cy="12" r="4" />
      <path d="M12 2v2 M12 20v2 M2 12h2 M20 12h2 M4.9 4.9l1.4 1.4 M17.7 17.7l1.4 1.4 M4.9 19.1l1.4-1.4 M17.7 6.3l1.4-1.4" />
    </>
  ),
  moon: <path d="M20 14.5A8 8 0 1 1 9.5 4a6.5 6.5 0 0 0 10.5 10.5z" />,
  filter: <path d="M3 4h18l-7 8v6l-4 2v-8z" />,
  fade: (
    <>
      <circle cx="6" cy="17" r="2" opacity="0.35" />
      <circle cx="12" cy="12" r="2" opacity="0.65" />
      <circle cx="18" cy="7" r="2" />
      <path d="M3 21h18" />
    </>
  ),
}

export type IconName = keyof typeof PATHS

export default function Icon({ name, size = 18 }: { name: IconName; size?: number }) {
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth={2}
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
    >
      {PATHS[name]}
    </svg>
  )
}
