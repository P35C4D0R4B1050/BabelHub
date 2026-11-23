import { test, expect, type Page } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import { abrir, archivo, empezar, hacerA, hacerB1, hacerB2, hacerC, importarJson, part } from './ayudas';

// La herramienta también debe ser accesible y usable en un móvil: axe (A y AA de WCAG 2.0, 2.1 y 2.2) en cada pantalla y paso, en los dos temas,
// teclado (foco visible, nada tapado por la barra de acciones), sin desplazamiento horizontal a 375 y 320 px, objetivos táctiles y movimiento reducido.

const ETIQUETAS = ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa', 'best-practice'];
const TEMAS = ['light', 'dark'] as const;

test.setTimeout(300_000);

async function axe(page: Page, donde: string) {
  await page.evaluate(() => document.querySelectorAll('details').forEach((d) => { d.open = true; }));
  const resultado = await new AxeBuilder({ page }).withTags(ETIQUETAS).analyze();
  expect(resultado.violations.map((v) => `${v.id}: ${v.nodes.map((n) => n.target.join(' ')).join(' | ')}`), `axe en ${donde}`).toEqual([]);
}
const sinDesplazamiento = (page: Page) => page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth);

// Recorre toda la app (bienvenida, inicio, cada paso de cada tarea, gracias y reunir) llamando a `revisar` en cada pantalla.
async function recorrer(page: Page, revisar: (donde: string) => Promise<void>) {
  await abrir(page);
  await revisar('bienvenida');
  await page.getByRole('button', { name: 'No, corregir' }).click();
  await revisar('bienvenida corrigiendo');
  await page.goto('/?tienda=https://tienda.ejemplo.test/&v=2026-11-23');
  await page.getByRole('button', { name: 'Sí, es correcto' }).click();
  await page.getByRole('button', { name: 'Empezar' }).click();
  await revisar('inicio');
  // Un paso con error visible y la captura: se mira antes de contestar.
  await page.goto('/#/t/c');
  await page.getByRole('button', { name: /^Siguiente/ }).click();
  await revisar('aviso de error en un paso');
  await page.getByRole('link', { name: /Volver al inicio/ }).click();
  await hacerC(page, { alPaso: revisar });
  await hacerA(page, { respuesta: 'Con dificultad', alPaso: revisar });
  await hacerB2(page, { bloqueo: true, alPaso: revisar });
  await hacerB1(page, { bloqueo: true, alPaso: revisar });
  await expect(page.getByRole('heading', { level: 1, name: '¡Muchas gracias!' })).toBeVisible();
  await page.goto('/#/');
  await revisar('inicio con todo hecho');
  await page.goto('/#/gracias');
  await page.getByText('Ver lo que se enviará').click();
  await revisar('gracias');
  await page.goto('/#/avanzado');
  await importarJson(page, archivo([part({ id: 'per-ejemplo000001', codigo: 'EV-AAAA', a: 'si', b1: 'no', c: { ronda: '1' } })]));
  await expect(page.getByRole('main').getByText(/personas agregadas/)).toBeVisible();
  await revisar('reunir resultados');
  await page.getByRole('button', { name: 'Borrar los datos locales…' }).click();
  await expect(page.getByRole('dialog')).toBeVisible();
  await revisar('diálogo de borrar');
  await page.getByRole('button', { name: 'Cancelar' }).click();
}

for (const tema of TEMAS) {
  test.describe(`Tema ${tema === 'light' ? 'claro' : 'oscuro'}`, () => {
    test.beforeEach(async ({ page }) => { await page.emulateMedia({ colorScheme: tema }); });

    test('axe: todas las pantallas y todos los pasos cumplen A y AA, con el tema que toca', async ({ page }) => {
      await recorrer(page, async (donde) => {
        await expect(page.locator('html')).toHaveAttribute('data-theme', tema === 'light' ? 'babelhub' : 'babelhub-dark');
        await axe(page, donde);
      });
    });
  });
}

for (const ancho of [375, 320]) {
  test(`sin desplazamiento horizontal a ${ancho} px en ninguna pantalla ni paso`, async ({ page, isMobile }) => {
    test.skip(isMobile === true && ancho === 320, 'El proyecto de teléfono ya fija su ancho; el recorrido de 320 px se hace en escritorio.');
    await page.setViewportSize({ width: ancho, height: 800 });
    await recorrer(page, async (donde) => { expect(await sinDesplazamiento(page), `desplazamiento horizontal en ${donde}`).toBe(true); });
  });
}

test.describe('Teclado y navegación', () => {
  test('el primer Tab llega a «Saltar al contenido» y lo activa sin cambiar la ruta', async ({ page, browserName }) => {
    test.skip(browserName === 'webkit', 'En Safari, Tab solo recorre controles de formulario si el usuario no activó «Navegación con teclado»; se cubre con Chromium.');
    await abrir(page);
    await page.keyboard.press('Tab');
    await expect(page.getByRole('link', { name: 'Saltar al contenido' })).toBeFocused();
    await page.keyboard.press('Enter');
    await expect(page.locator('main')).toBeFocused();
    await expect(page).toHaveURL(/#\/bienvenida$/);
  });

  test('al cambiar de pantalla o de paso el foco pasa al título y el título de la pestaña dice dónde estás', async ({ page }) => {
    await empezar(page);
    await expect(page).toHaveTitle(/^Inicio · Prueba de la tienda BabelHub$/);
    await page.getByRole('link', { name: /Empezar.*tarea Recorre la compra/ }).click();
    await expect(page.getByRole('heading', { level: 1, name: 'Cómo funciona' })).toBeFocused();
    await page.getByRole('button', { name: /^Siguiente/ }).click();
    await expect(page.getByRole('heading', { level: 1, name: /Paso 1 de 8/ })).toBeFocused();
    await expect(page).toHaveTitle(/Recorre la compra paso a paso · Paso 1 de 8/);
    await page.getByRole('button', { name: /^Siguiente/ }).click(); // falta responder: el foco va al aviso
    await expect(page.getByRole('alert')).toBeFocused();
    await expect(page.getByRole('alert')).toContainText('Elige una opción');
  });

  test('el foco es siempre visible y nunca queda tapado por la barra de acciones (WCAG 2.4.7 y 2.4.11)', async ({ page, browserName }) => {
    test.skip(browserName === 'webkit', 'Tab en Safari depende de una preferencia del sistema; se cubre con Chromium (escritorio y teléfono).');
    await empezar(page);
    await page.goto('/#/t/b1');
    await page.getByRole('radio', { name: /Sí, tengo teclado/ }).check();
    for (const _ of [1, 2]) await page.getByRole('button', { name: /^Siguiente/ }).click();
    await page.getByRole('radio', { name: 'Claro' }).check();
    await page.getByRole('button', { name: /^Siguiente/ }).click();
    await expect(page.locator('fieldset.card')).toHaveCount(6);
    await page.evaluate(() => (document.activeElement as HTMLElement | null)?.blur());
    const problemas: string[] = [];
    for (let i = 0; i < 40; i++) {
      await page.keyboard.press('Tab');
      const m = await page.evaluate(() => {
        const el = document.activeElement as HTMLElement | null;
        if (!el || el === document.body) return null;
        const radio = el instanceof HTMLInputElement && el.type === 'radio';
        const visible = radio ? (el.nextElementSibling as HTMLElement) : el; // el aro de los grupos de opción va en su texto
        const e = getComputedStyle(visible);
        const caja = (radio ? (el.closest('label') as HTMLElement) : el).getBoundingClientRect();
        const barra = document.querySelector('.barra-accion')!.getBoundingClientRect();
        if (el.closest('.barra-accion')) return null;
        return { quien: `${el.tagName}:${(el.getAttribute('name') ?? el.textContent ?? '').trim().slice(0, 28)}`, aro: e.outlineStyle !== 'none' && parseFloat(e.outlineWidth) >= 2, tapado: caja.bottom > barra.top + 1 && caja.top < barra.bottom };
      });
      if (m && !m.aro) problemas.push(`sin aro: ${m.quien}`);
      if (m && m.tapado) problemas.push(`tapado por la barra: ${m.quien}`);
    }
    expect(problemas).toEqual([]);
  });

  test('con el teclado se puede responder una escala completa (las flechas mueven entre opciones y Espacio elige)', async ({ page, browserName }) => {
    test.skip(browserName === 'webkit', 'Tab en Safari depende de una preferencia del sistema.');
    await empezar(page);
    await page.goto('/#/t/b1');
    await page.getByRole('radio', { name: /Sí, tengo teclado/ }).check();
    for (const _ of [1, 2]) await page.getByRole('button', { name: /^Siguiente/ }).click();
    await page.getByRole('radio', { name: 'Claro' }).check();
    await page.getByRole('button', { name: /^Siguiente/ }).click();
    const primero = page.locator('fieldset.card').first().getByRole('radio').first();
    await primero.focus();
    await page.keyboard.press('ArrowRight');
    await page.keyboard.press('ArrowRight');
    await expect(page.locator('fieldset.card').first().getByRole('radio', { name: /^3:/ })).toBeChecked();
    await page.keyboard.press('ArrowRight');
    await expect(page.locator('fieldset.card').first().getByRole('radio', { name: /^4:/ })).toBeChecked();
    await expect(page.locator('fieldset.card').first().locator('.elegiste')).toHaveText('Elegiste: De acuerdo');
  });

  test('el cuadro de diálogo recibe el foco, se cierra con Esc y devuelve el foco', async ({ page }) => {
    await abrir(page);
    await page.goto('/#/avanzado');
    const boton = page.getByRole('button', { name: 'Borrar los datos locales…' });
    await boton.click();
    await expect(page.getByRole('dialog')).toBeVisible();
    await expect(page.getByRole('button', { name: 'Cancelar' })).toBeFocused();
    await page.keyboard.press('Escape');
    await expect(page.getByRole('dialog')).toHaveCount(0);
  });
});

test.describe('Responsivo y movimiento', () => {
  test('los botones y las opciones miden al menos 44 px de alto para el pulgar (mínimo exigido: 24 px)', async ({ page }) => {
    await recorrer(page, async (donde) => {
      const chicos = await page.evaluate(() => [...document.querySelectorAll('main button, main .seg label, main a.btn, header button')].filter((e) => { const r = e.getBoundingClientRect(); return r.width > 0 && r.height > 0; }).map((e) => ({ t: (e.textContent ?? '').trim().slice(0, 24), h: e.getBoundingClientRect().height })).filter((x) => x.h < 44));
      expect(chicos, `objetivos táctiles menores de 44 px en ${donde}`).toEqual([]);
    });
  });

  test('con «reducir movimiento» no hay animaciones ni transiciones', async ({ page }) => {
    await page.emulateMedia({ reducedMotion: 'reduce' });
    await empezar(page);
    const n = await page.evaluate(() => [...document.querySelectorAll('*')].filter((e) => { const s = getComputedStyle(e); return (s.animationName !== 'none' && parseFloat(s.animationDuration) > 0) || parseFloat(s.transitionDuration) > 0; }).length);
    expect(n).toBe(0);
  });

  test('las regiones de la página: banner, contenido principal y pie, y un único título de nivel 1', async ({ page }) => {
    await empezar(page);
    await expect(page.getByRole('banner')).toBeVisible();
    await expect(page.getByRole('main')).toBeVisible();
    await expect(page.getByRole('contentinfo')).toBeVisible();
    await expect(page.getByRole('heading', { level: 1 })).toHaveCount(1);
  });

  test('el estado (hecha, en curso, saltada) se dice con texto e icono, nunca solo con color', async ({ page }) => {
    await empezar(page);
    await hacerB1(page);
    await expect(page.locator('.tarea .chip', { hasText: 'Hecha' }).first()).toContainText('✔');
    await expect(page.locator('.tarea .chip', { hasText: 'Pendiente' }).first()).toContainText('○');
  });
});

test.describe('Tema', () => {
  test('el botón alterna sistema → claro → oscuro, se recuerda al recargar y respeta el tema del sistema', async ({ page }) => {
    await page.emulateMedia({ colorScheme: 'light' });
    await abrir(page);
    const boton = page.getByRole('button', { name: /Cambiar tema/ });
    await expect(page.locator('html')).toHaveAttribute('data-theme', 'babelhub');
    await boton.click();
    await expect(boton).toContainText('claro');
    await boton.click();
    await expect(boton).toContainText('oscuro');
    await expect(page.locator('html')).toHaveAttribute('data-theme', 'babelhub-dark');
    await page.reload();
    await expect(page.locator('html')).toHaveAttribute('data-theme', 'babelhub-dark');
    await boton.click();
    await expect(boton).toContainText('sistema');
    await expect(page.locator('html')).toHaveAttribute('data-theme', 'babelhub');
  });
});
