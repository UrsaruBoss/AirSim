import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'
import { viteStaticCopy } from 'vite-plugin-static-copy'

// Backend-ul .NET ruleaza pe :5080 (Sim.Server/Properties/launchSettings.json).
// Proxy-ul face ca frontend-ul sa vorbeasca cu el pe acelasi origin (fara probleme de CORS).
const backend = 'http://localhost:5080'

const cesiumBuild = 'node_modules/cesium/Build/Cesium'

export default defineConfig({
  plugins: [
    react(),
    // Cesium isi incarca Workers/Assets/Widgets la runtime de la CESIUM_BASE_URL (/cesium/Workers/...).
    // Pluginul pastreaza calea sursei, deci stripBase: 4 taie node_modules/cesium/Build/Cesium.
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
})
