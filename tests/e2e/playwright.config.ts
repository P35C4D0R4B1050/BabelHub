import { defineConfig, devices } from '@playwright/test';

// La URL base es la del E-commerce local de `infra/` (host de la tienda). Se cambia con E2E_BASE_URL.
const baseURL = process.env.E2E_BASE_URL ?? 'http://localhost:8080';

export default defineConfig({
  testDir: '.',
  testMatch: '**/*.spec.ts',
  fullyParallel: true,
  retries: 0,
  reporter: [['list']],
  use: {
    baseURL,
    locale: 'es-PE',
    timezoneId: 'America/Lima',
    trace: 'retain-on-failure',
  },
  projects: [
    // Escritorio y teléfono (375 px): el catálogo debe verse bien en ambos.
    { name: 'escritorio', use: { ...devices['Desktop Chrome'] } },
    { name: 'telefono', use: { ...devices['Pixel 5'] } },
    // Firefox corre en este equipo (el navegador ya está en la caché de Playwright): los recorridos se repiten en él.
    { name: 'firefox', use: { ...devices['Desktop Firefox'] } },
  ],
});
