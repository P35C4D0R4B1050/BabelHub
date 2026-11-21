import { test, expect, type Page } from '@playwright/test';

// El diseño responsivo, los temas y el movimiento reducido de `web-base` (T32; AC-WEB-01, AC-WEB-18): las pantallas públicas no se desplazan en horizontal en 320, 375, 768 y 1280 px, los dos temas se aplican por la cookie `bh-tema` y
// `prefers-reduced-motion: reduce` apaga las animaciones y transiciones. El recorrido en las dos últimas versiones de Chrome, Edge, Firefox y Safari y en móviles lo cubren los proyectos de Playwright (Chromium de escritorio y de teléfono y WebKit
// como Safari; Firefox, informativo, si el equipo lo lanza); Edge y las versiones concretas de cada navegador las verifica la persona con la lista manual de `docs/06-experiencia-de-usuario/10-lista-manual-de-accesibilidad.md`.
// Mismo entorno que los demás recorridos (host de la tienda con datos sembrados).

const PANTALLAS_PUBLICAS = ['/', '/catalogo', '/carrito', '/ingreso', '/registro', '/recuperacion', '/reclamaciones/nueva', '/legal/terminos', '/legal/privacidad', '/legal/reembolso'];

async function sinDesplazamientoHorizontal(page: Page, ruta: string) {
  expect(await page.evaluate(() => document.documentElement.scrollWidth > document.documentElement.clientWidth), `${ruta} no se desplaza en horizontal`).toBe(false);
}

function urlBase(): string {
  return new URL('/', test.info().project.use.baseURL ?? process.env.E2E_BASE_URL ?? 'https://localhost:8443').toString();
}

test.describe('Responsivo, temas y movimiento reducido', () => {
  for (const ancho of [320, 375, 768, 1280]) {
    test(`ninguna pantalla pública se desplaza en horizontal a ${ancho} px (AC-WEB-01)`, async ({ page }) => {
      await page.setViewportSize({ width: ancho, height: 800 });
      for (const ruta of PANTALLAS_PUBLICAS) {
        await page.goto(ruta);
        await page.waitForLoadState('networkidle');
        await sinDesplazamientoHorizontal(page, ruta);
      }
    });
  }

  test('en 375 px el catálogo muestra sus controles utilizables y ninguno mide menos de 24 px (AC-WEB-01)', async ({ page }) => {
    await page.setViewportSize({ width: 375, height: 812 });
    await page.goto('/catalogo');
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
    await sinDesplazamientoHorizontal(page, '/catalogo');
    // Todo control del contenido mide al menos 24 px de lado (WCAG 2.5.8).
    const pequenos = await page.evaluate(() =>
      [...document.querySelectorAll<HTMLElement>('main button, main input:not([type=hidden]):not([type=checkbox]):not([type=radio]), main select, main textarea')]
        .filter((e) => e.offsetParent !== null)
        .map((e) => ({ e: e.tagName + (e.getAttribute('name') ? `[${e.getAttribute('name')}]` : ''), alto: e.getBoundingClientRect().height, ancho: e.getBoundingClientRect().width }))
        .filter((m) => m.alto < 24 || m.ancho < 24),
    );
    expect(pequenos, 'controles de menos de 24 px').toEqual([]);
  });

  test('los dos temas se aplican por la cookie del tema y se conservan al recargar', async ({ page }) => {
    for (const tema of ['babelhub', 'babelhub-dark']) {
      await page.context().addCookies([{ name: 'bh-tema', value: tema, url: urlBase() }]);
      for (const ruta of ['/', '/catalogo', '/ingreso']) {
        await page.goto(ruta);
        await expect(page.locator('html')).toHaveAttribute('data-theme', tema);
      }
      await page.reload();
      await expect(page.locator('html')).toHaveAttribute('data-theme', tema);
    }
  });

  test('con movimiento reducido no queda ninguna animación ni transición de más de 50 ms (AC-WEB-01)', async ({ page }) => {
    await page.emulateMedia({ reducedMotion: 'reduce' });
    for (const ruta of ['/', '/catalogo', '/ingreso']) {
      await page.goto(ruta);
      const largas = await page.evaluate(() => {
        const aMs = (t: string) => Math.max(...t.split(',').map((x) => (x.trim().endsWith('ms') ? parseFloat(x) : parseFloat(x) * 1000)));
        return [...document.querySelectorAll<HTMLElement>('body, body *')]
          .map((e) => ({ e: e.tagName + '.' + String(e.className).slice(0, 40), animacion: aMs(getComputedStyle(e).animationDuration), transicion: aMs(getComputedStyle(e).transitionDuration) }))
          .filter((m) => m.animacion > 50 || m.transicion > 50);
      });
      expect(largas, `${ruta} con movimiento reducido`).toEqual([]);
    }
  });
});
