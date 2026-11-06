import react from '@vitejs/plugin-react'
import { defineConfig, loadEnv } from 'vite'
import { viteStaticCopy } from 'vite-plugin-static-copy'

// The .NET backend runs on :5080 by default (Sim.Server/Properties/launchSettings.json); the address can be changed with VITE_SERVER_URL in .env.
// The proxy makes the frontend talk to it on the same origin (no CORS problems).

const cesiumBuild = 'node_modules/cesium/Build/Cesium'

export default defineConfig(({ mode }) => {
  const backend = loadEnv(mode, '.', '').VITE_SERVER_URL || 'http://localhost:5080'
  return {
    plugins: [
      react(),
      // Cesium loads its Workers/Assets/Widgets at runtime from CESIUM_BASE_URL (/cesium/Workers/...).
      // The plugin keeps the source path, so stripBase: 4 cuts node_modules/cesium/Build/Cesium.
      viteStaticCopy({
        targets: [
          { src: `${cesiumBuild}/Workers`, dest: 'cesium', rename: { stripBase: 4 } },
          { src: `${cesiumBuild}/ThirdParty`, dest: 'cesium', rename: { stripBase: 4 } },
          { src: `${cesiumBuild}/Assets`, dest: 'cesium', rename: { stripBase: 4 } },
          { src: `${cesiumBuild}/Widgets`, dest: 'cesium', rename: { stripBase: 4 } },
        ],
      }),
    ],
    define: {
      CESIUM_BASE_URL: JSON.stringify('/cesium'),
    },
    server: {
      port: 5173,
      proxy: {
        '/api': backend,
        '/hubs': { target: backend, ws: true },
      },
    },
  }
})
