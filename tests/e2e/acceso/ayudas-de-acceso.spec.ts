import { test, expect, type Page } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';

// Las ayudas de contraseña y el código QR del segundo factor en un navegador real (spec web-acceso; AC-ACC-01, AC-ACC-03, AC-ACC-05, AC-ACC-07, AC-ACC-08, AC-ACC-14, AC-ACC-16).
// Se ejecutan con los proyectos obligatorios contra el E-commerce local (README). Las pantallas del ingreso, el registro y la recuperación no necesitan datos.
//
//   E2E_RUTA_SEGUNDO_FACTOR   ruta que deja a la vista la configuración del segundo factor (necesita un ingreso del personal pendiente); sin ella, esa prueba se omite
const rutaDelSegundoFactor = process.env.E2E_RUTA_SEGUNDO_FACTOR;

const FRASE = 'mi gato come pescado fresco';

async function sinIncumplimientosGraves(page: Page, donde: string) {
  const resultado = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa']).analyze();
  const graves = resultado.violations.filter((v) => v.impact === 'critical' || v.impact === 'serious');
  expect(graves.map((v) => `${v.id}: ${v.nodes.map((n) => n.target.join(' ')).join(' | ')}`), `axe en ${donde}`).toEqual([]);
}

async function sinDesplazamientoHorizontal(page: Page) {
  expect(await page.evaluate(() => document.documentElement.scrollWidth > document.documentElement.clientWidth), 'la página no se desplaza en horizontal').toBe(false);
}

async function elegirTema(page: Page, tema: 'babelhub' | 'babelhub-dark') {
  await page.context().addCookies([{ name: 'bh-tema', value: tema, url: page.url() }]);
}

// La pantalla que define una contraseña nueva y la repite: la confirmación de la recuperación (responde igual exista o no la cuenta).
async function abrirRecuperacion(page: Page) {
  await page.goto('/recuperacion');
  await page.getByLabel('Correo electrónico').fill('nadie-existe@correo.example');
  await page.getByRole('button', { name: 'Enviarme el código' }).click();
  await expect(page.locator('input[name="contrasenaNueva"]')).toBeVisible();
}

test.describe('Ayudas de contraseña', () => {
  test('la barra y la coincidencia responden al escribir sin que la contraseña salga del dispositivo (AC-ACC-03, AC-ACC-05, AC-ACC-06)', async ({ page }) => {
    const consola: string[] = [];
    page.on('console', (m) => consola.push(m.text()));
    await abrirRecuperacion(page);
    await page.waitForLoadState('networkidle');
    const solicitudes: string[] = [];
    page.on('request', (s) => solicitudes.push(s.url() + ' ' + (s.postData() ?? '')));

    const nueva = page.locator('input[name="contrasenaNueva"]');
    const medidor = page.locator('[data-bh-medidor]');
    await expect(medidor).toBeHidden();
    await nueva.pressSequentially('Password1!');
    await expect(medidor).toContainText('Nivel de seguridad: Débil');
    await nueva.fill(FRASE);
    await expect(medidor).toContainText('Nivel de seguridad: Buena');
    await expect(medidor).toContainText('solo una guía');

    const repeticion = page.locator('input[name="confirmacion"]');
    const aviso = page.locator('[data-bh-coincidencia]');
    await expect(aviso).toBeHidden();
    await repeticion.pressSequentially('mi gato');
    await expect(aviso).toContainText('Las contraseñas todavía no coinciden');
    await repeticion.fill(FRASE);
    await expect(aviso).toContainText('Las contraseñas coinciden');
    await expect(aviso.locator('svg')).toHaveCount(1);
    // El anuncio llega a la región cortés después de la pausa.
    await expect(page.locator('[data-bh-anuncio][aria-live="polite"]').filter({ hasText: 'Las contraseñas coinciden' })).toHaveCount(1);

    expect(solicitudes, 'escribir no origina solicitudes').toEqual([]);
    const guardado = await page.evaluate(() => JSON.stringify([Object.entries(localStorage), Object.entries(sessionStorage), document.cookie, location.href, document.body.innerHTML]));
    expect(guardado).not.toContain(FRASE);
    expect(consola.join('\n')).not.toContain(FRASE);
  });

  test('ver y ocultar con el teclado, sin enviar ni borrar, y sin ayudas de escritura (AC-ACC-07, AC-ACC-16)', async ({ page }) => {
    await page.goto('/ingreso');
    const campo = page.locator('input[name="contrasena"]');
    await campo.fill(FRASE);
    for (const [atributo, valor] of [['spellcheck', 'false'], ['autocapitalize', 'none'], ['autocorrect', 'off'], ['writingsuggestions', 'false'], ['autocomplete', 'current-password']]) {
      await expect(campo).toHaveAttribute(atributo, valor);
    }
    await campo.focus();
    await page.keyboard.press('Tab');
    const boton = page.getByRole('button', { name: 'Mostrar la contraseña' });
    await expect(boton).toBeFocused();
    await expect(boton).toHaveCSS('outline-style', 'solid');
    await page.keyboard.press('Enter');
    await expect(campo).toHaveAttribute('type', 'text');
    await expect(campo).toHaveValue(FRASE);
    await expect(campo).toHaveAttribute('spellcheck', 'false');
    await expect(page.getByRole('button', { name: 'Ocultar la contraseña, ahora visible' })).toBeFocused();
    await page.keyboard.press('Space');
    await expect(campo).toHaveAttribute('type', 'password');
    expect(page.url()).toContain('/ingreso');
  });

  for (const tema of ['babelhub', 'babelhub-dark'] as const) {
    test(`accesible, sin desplazamiento a 320 px y sin transiciones con movimiento reducido en el tema ${tema} (AC-ACC-08)`, async ({ page }) => {
      await page.emulateMedia({ reducedMotion: 'reduce' });
      await page.goto('/registro');
      await elegirTema(page, tema);
      await abrirRecuperacion(page);
      await page.locator('input[name="contrasenaNueva"]').fill('Rio7-nube!');
      await page.locator('input[name="confirmacion"]').fill('Rio7');
      await expect(page.locator('[data-bh-medidor]')).toContainText('Buena');
      await sinIncumplimientosGraves(page, `recuperación (${tema})`);
      expect(await page.locator('.bh-medidor-barra > span').first().evaluate((e) => getComputedStyle(e).transitionDuration)).toBe('0s');
      await page.setViewportSize({ width: 320, height: 700 });
      await sinDesplazamientoHorizontal(page);
      await page.goto('/ingreso');
      await sinIncumplimientosGraves(page, `ingreso (${tema})`);
      await sinDesplazamientoHorizontal(page);
    });
  }

  test('sin guiones queda la ayuda y no hay nada inerte (AC-ACC-01)', async ({ browser, baseURL }) => {
    const contexto = await browser.newContext({ javaScriptEnabled: false, baseURL, ignoreHTTPSErrors: true });
    const page = await contexto.newPage();
    await page.goto('/registro');
    await expect(page.getByText('Obligatorio:')).toBeVisible();
    await expect(page.getByText('Al guardar comprobamos que no sea una contraseña común o filtrada.')).toBeVisible();
    await expect(page.locator('[data-bh-ver], [data-bh-medidor], [data-bh-coincidencia]')).toHaveCount(0);
    await expect(page.locator('input[name="contrasena"]')).not.toHaveAttribute('minlength', /.*/);
    await contexto.close();
  });
});

test.describe('Código QR del segundo factor', () => {
  test.skip(!rutaDelSegundoFactor, 'Falta E2E_RUTA_SEGUNDO_FACTOR (un ingreso pendiente de configurar el segundo factor).');

  for (const tema of ['babelhub', 'babelhub-dark'] as const) {
    test(`se ve en la página, oscuro sobre claro y sin desbordar a 320 px en el tema ${tema}, también sin guiones (AC-ACC-14)`, async ({ browser, baseURL }) => {
      const contexto = await browser.newContext({ javaScriptEnabled: false, baseURL, ignoreHTTPSErrors: true, viewport: { width: 320, height: 700 } });
      await contexto.addCookies([{ name: 'bh-tema', value: tema, url: baseURL! }]);
      const page = await contexto.newPage();
      const ajenas: string[] = [];
      page.on('request', (s) => {
        if (new URL(s.url()).origin !== new URL(baseURL!).origin) ajenas.push(s.url());
      });
      await page.goto(rutaDelSegundoFactor!);
      const qr = page.getByRole('img', { name: /Código QR/ });
      await expect(qr).toBeVisible();
      const caja = (await qr.boundingBox())!;
      expect(caja.width).toBeGreaterThanOrEqual(176);
      expect(Math.round(caja.width)).toBe(Math.round(caja.height));
      await expect(qr.locator('rect')).toHaveCSS('fill', 'rgb(255, 255, 255)');
      await expect(qr.locator('path')).toHaveCSS('fill', 'rgb(0, 0, 0)');
      await expect(page.locator('img')).toHaveCount(0);
      await sinDesplazamientoHorizontal(page);
      expect(ajenas.filter((u) => !u.includes('challenges.cloudflare.com'))).toEqual([]);
      await contexto.close();
    });
  }

  test('la pantalla con el código es accesible (AC-ACC-08, AC-ACC-14)', async ({ page }) => {
    await page.goto(rutaDelSegundoFactor!);
    await sinIncumplimientosGraves(page, 'configuración del segundo factor');
  });
});
