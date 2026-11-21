import { test, expect, type Page } from '@playwright/test';

// El presupuesto de rendimiento de `web-base` (T34; AC-WEB-16; WEB-21; docs/06-experiencia-de-usuario/07-rendimiento-y-movil.md): inicio, catálogo y detalle de una obra con una conexión 4G simulada (1,6 Mb/s de bajada, 750 kb/s de subida y
// 150 ms de latencia) y el procesador 4 veces más lento, midiendo LCP ≤ 2,5 s, CLS ≤ 0,1 e INP ≤ 200 ms (el peor evento de interacción de la visita). La medición es de laboratorio (Chromium, una máquina de desarrollo con el servidor local):
// el percentil 75 con usuarios reales y la prueba de carga en el servidor de 1 OCPU se miden aparte (K6). La meta no se relaja para que pase: un incumplimiento se corrige o se registra como riesgo.
// Solo Chromium (usa CDP). Mismo entorno que los demás recorridos (host de la tienda con datos sembrados).

test.describe.configure({ mode: 'serial' });
test.setTimeout(120_000);

const PRESUPUESTO = { lcpMs: 2_500, cls: 0.1, inpMs: 200 };

async function simular4gYProcesadorLento(page: Page) {
  const cdp = await page.context().newCDPSession(page);
  await cdp.send('Network.enable');
  await cdp.send('Network.emulateNetworkConditions', { offline: false, latency: 150, downloadThroughput: (1.6 * 1024 * 1024) / 8, uploadThroughput: (750 * 1024) / 8 });
  await cdp.send('Emulation.setCPUThrottlingRate', { rate: 4 });
}

async function instrumentar(page: Page) {
  await page.addInitScript(() => {
    const w = window as unknown as { __vitales: { lcp: number; cls: number; inp: number } };
    w.__vitales = { lcp: 0, cls: 0, inp: 0 };
    new PerformanceObserver((lista) => {
      for (const e of lista.getEntries()) w.__vitales.lcp = e.startTime;
    }).observe({ type: 'largest-contentful-paint', buffered: true });
    new PerformanceObserver((lista) => {
      for (const e of lista.getEntries() as unknown as { value: number; hadRecentInput: boolean }[]) if (!e.hadRecentInput) w.__vitales.cls += e.value;
    }).observe({ type: 'layout-shift', buffered: true });
    new PerformanceObserver((lista) => {
      for (const e of lista.getEntries() as unknown as { duration: number; interactionId?: number }[]) if (e.interactionId) w.__vitales.inp = Math.max(w.__vitales.inp, e.duration);
    }).observe({ type: 'event', durationThreshold: 16, buffered: true } as PerformanceObserverInit);
  });
}

async function medir(page: Page, ruta: string) {
  await page.goto(ruta, { waitUntil: 'load' });
  await page.waitForLoadState('networkidle');
  // Una interacción real: un clic en la primera zona interactiva visible (el evento se mide con su duración hasta pintar).
  const interactivo = page.locator('main a, main button').first();
  if (await interactivo.isVisible().catch(() => false)) {
    await page.mouse.move(5, 5);
    await interactivo.hover();
    await page.keyboard.press('Tab');
  }
  await page.waitForTimeout(500);
  return page.evaluate(() => (window as unknown as { __vitales: { lcp: number; cls: number; inp: number } }).__vitales);
}

test.describe('Presupuesto de rendimiento en 4G simulada', () => {
  test.skip(({ browserName }) => browserName !== 'chromium', 'La simulación de red y procesador usa el protocolo de Chromium.');

  for (const [nombre, ruta] of [['inicio', '/'], ['catálogo', '/catalogo']] as const) {
    test(`${nombre} cumple LCP, CLS e INP (AC-WEB-16)`, async ({ page }) => {
      await instrumentar(page);
      await simular4gYProcesadorLento(page);
      const v = await medir(page, ruta);
      test.info().annotations.push({ type: 'vitales', description: JSON.stringify(v) });
      expect(v.lcp, `LCP de ${nombre} (ms)`).toBeGreaterThan(0);
      expect(v.lcp, `LCP de ${nombre} (ms)`).toBeLessThanOrEqual(PRESUPUESTO.lcpMs);
      expect(v.cls, `CLS de ${nombre}`).toBeLessThanOrEqual(PRESUPUESTO.cls);
      expect(v.inp, `INP de ${nombre} (ms)`).toBeLessThanOrEqual(PRESUPUESTO.inpMs);
    });
  }

  test('el detalle de una obra cumple LCP, CLS e INP (AC-WEB-16)', async ({ page }) => {
    await page.goto('/catalogo');
    const ruta = await page.locator('a[href^="/catalogo/obras/"]').first().getAttribute('href');
    expect(ruta, 'el catálogo sembrado tiene al menos una obra').toBeTruthy();
    await instrumentar(page);
    await simular4gYProcesadorLento(page);
    const v = await medir(page, ruta!);
    test.info().annotations.push({ type: 'vitales', description: JSON.stringify(v) });
    expect(v.lcp, 'LCP del detalle (ms)').toBeGreaterThan(0);
    expect(v.lcp, 'LCP del detalle (ms)').toBeLessThanOrEqual(PRESUPUESTO.lcpMs);
    expect(v.cls, 'CLS del detalle').toBeLessThanOrEqual(PRESUPUESTO.cls);
    expect(v.inp, 'INP del detalle (ms)').toBeLessThanOrEqual(PRESUPUESTO.inpMs);
  });
});
