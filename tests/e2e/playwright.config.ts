import { spawnSync } from 'node:child_process';
import { existsSync } from 'node:fs';
import { defineConfig, devices, firefox, webkit, type PlaywrightTestConfig } from '@playwright/test';

// La URL base es la del E-commerce local de `infra/` (host de la tienda). Se cambia con E2E_BASE_URL.
const baseURL = process.env.E2E_BASE_URL ?? 'http://localhost:8080';

type Proyecto = NonNullable<PlaywrightTestConfig['projects']>[number];

// Obligatorios: Chromium de escritorio y de teléfono (375 px: el catálogo debe verse bien en ambos). WebKit y Firefox son opcionales: un navegador
// que no está en la caché de Playwright o que el sistema no deja lanzar se omite con un aviso, nunca falla la corrida. Un fallo solo de Firefox es
// informativo (no reabre bloques cerrados, salvo un defecto real de estándares o de accesibilidad que afecte también a los demás).
function disponible(ejecutable: () => string): { disponible: boolean; motivo?: string } {
  let ruta: string;
  try {
    ruta = ejecutable();
  } catch {
    return { disponible: false, motivo: 'no está en la caché de Playwright' };
  }
  if (!ruta || !existsSync(ruta)) {
    return { disponible: false, motivo: 'no está en la caché de Playwright' };
  }
  // Se prueba que el sistema deje lanzarlo (antivirus, política): «Permission denied» o «spawn UNKNOWN» lo omiten sin tocar nada del sistema.
  const prueba = spawnSync(ruta, ['--version'], { timeout: 8000 });
  return prueba.error ? { disponible: false, motivo: `el sistema no lo deja lanzar (${(prueba.error as NodeJS.ErrnoException).code ?? 'error'})` } : { disponible: true };
}

const opcionales: Proyecto[] = [];
const quiereOpcionales = process.env.E2E_SIN_NAVEGADORES_OPCIONALES !== '1';
for (const [nombre, ejecutable, dispositivo] of [
  ['webkit', () => webkit.executablePath(), devices['Desktop Safari']],
  ['firefox', () => firefox.executablePath(), devices['Desktop Firefox']],
] as const) {
  if (!quiereOpcionales) {
    continue;
  }
  const estado = disponible(ejecutable);
  if (estado.disponible) {
    opcionales.push({ name: nombre, use: { ...dispositivo } });
  } else {
    console.warn(`[e2e] el proyecto ${nombre} se omite: ${estado.motivo}.`);
  }
}

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
    // El host local escucha en HTTP; detrás del borde recibe X-Forwarded-Proto: https (las cookies __Host- y el antiforgery lo exigen).
    extraHTTPHeaders: process.env.E2E_FORWARDED_PROTO ? { 'X-Forwarded-Proto': process.env.E2E_FORWARDED_PROTO } : undefined,
  },
  projects: [
    { name: 'escritorio', use: { ...devices['Desktop Chrome'] } },
    { name: 'telefono', use: { ...devices['Pixel 5'] } },
    ...opcionales,
  ],
});
