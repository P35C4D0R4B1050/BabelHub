import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { defineConfig, devices } from '@playwright/test';

// Pruebas de la herramienta de apoyo `tools/pruebas-humanas/index.html` (web-base T32b a T34b). No necesitan el entorno de `tests/e2e/entorno/`:
// la app es un solo archivo estático, que se sirve con su propio servidor (`tools/pruebas-humanas/servir.mjs`) en un puerto aparte.
//   cd tests/e2e && npx playwright test --config pruebas-humanas/playwright.config.ts
// Los datos de ejemplo de estas pruebas viven solo aquí; la app nunca los trae precargados. WebKit (iPhone) se omite con E2E_SIN_WEBKIT=1. Las salidas (trazas) van a la carpeta temporal del sistema, nunca al repositorio.
const puerto = Number(process.env.PRUEBAS_HUMANAS_PUERTO ?? 8091);
const conWebkit = process.env.E2E_SIN_WEBKIT !== '1';

export default defineConfig({
  outputDir: process.env.PRUEBAS_HUMANAS_SALIDA ?? join(tmpdir(), 'babelhub-pruebas-humanas-resultados'),
  testDir: '.',
  testMatch: '**/*.spec.ts',
  fullyParallel: true,
  retries: 0,
  // Los recorridos largos en WebKit tardan con muchos procesos a la vez; pocos trabajadores y más margen evitan falsos fallos por lentitud.
  workers: 3,
  timeout: 120_000,
  reporter: [['list']],
  use: { baseURL: `http://localhost:${puerto}`, locale: 'es-PE', timezoneId: 'America/Lima', trace: 'retain-on-failure', actionTimeout: 20_000 },
  webServer: { command: `node ../../../tools/pruebas-humanas/servir.mjs ${puerto}`, url: `http://localhost:${puerto}/`, reuseExistingServer: true, timeout: 20_000 },
  projects: [
    { name: 'escritorio', use: { ...devices['Desktop Chrome'] } },
    { name: 'telefono', use: { ...devices['Pixel 5'] } },
    ...(conWebkit ? [{ name: 'iphone', use: { ...devices['iPhone 13'] } }] : []),
  ],
});
