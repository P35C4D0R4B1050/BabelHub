import { test, expect, type Page } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import { abrir, archivo, ir, sesionCompleta } from './ayudas';

// La herramienta también debe ser accesible y usable en un móvil: axe (A y AA de WCAG 2.0, 2.1 y 2.2) en cada pantalla y en los dos temas,
// teclado (foco visible, nada tapado por la barra inferior), sin desplazamiento horizontal a 375 y 320 px, objetivos táctiles y movimiento reducido.

const ETIQUETAS = ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa', 'best-practice'];
const TEMAS = ['light', 'dark'] as const;

test.setTimeout(240_000);

async function axe(page: Page, donde: string) {
  await page.evaluate(() => document.querySelectorAll('details').forEach((d) => { d.open = true; }));
  const resultado = await new AxeBuilder({ page }).withTags(ETIQUETAS).analyze();
  expect(resultado.violations.map((v) => `${v.id}: ${v.nodes.map((n) => n.target.join(' ')).join(' | ')}`), `axe en ${donde}`).toEqual([]);
}

const sinDesplazamiento = (page: Page) => page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth);

// Deja la app con una corrida, una sesión de cada lector y una persona, y un informe de importación a la vista, para revisar todas las pantallas.
async function prepararTodo(page: Page) {
  await abrir(page);
  await importarSesiones(page);
  await ir(page, 'Navegadores');
  await page.getByRole('button', { name: 'Nueva corrida en Chrome · Escritorio' }).click();
  await page.getByRole('radio', { name: 'Falla', exact: true }).first().click();
  await page.getByRole('button', { name: 'Agregar defecto' }).click();
  await ir(page, 'Usabilidad');
  await page.getByRole('button', { name: /Agregar persona a la ronda 1/ }).click();
}
async function importarSesiones(page: Page) {
  await ir(page, 'Datos');
  await page.locator('#archivo-json').setInputFiles({ name: 'ejemplo.json', mimeType: 'application/json', buffer: Buffer.from(JSON.stringify(archivo({ dispositivoId: 'disp-ejemplo1', evaluador: 'EV1', sesiones: [sesionCompleta({ id: 'ses-ejemplo01', lector: 'nvda', tema: 'babelhub', fallas: [{ pantalla: 'pago', item: 'L3', e: 'critico', n: 'Ejemplo' }] })] }))) });
  await expect(page.getByText(/archivo leído/)).toBeVisible();
}

for (const tema of TEMAS) {
  test.describe(`Tema ${tema === 'light' ? 'claro' : 'oscuro'}`, () => {
    test.beforeEach(async ({ page }) => { await page.emulateMedia({ colorScheme: tema }); });

    test('axe: todas las pantallas cumplen A y AA, con el tema que toca', async ({ page }) => {
      await prepararTodo(page);
      await expect(page.locator('html')).toHaveAttribute('data-theme', tema === 'light' ? 'babelhub' : 'babelhub-dark');
      // Persona: los cinco pasos
      for (const paso of ['Antes', 'Tarea', 'Resultado', 'SUS', 'Cierre']) {
        await page.getByRole('button', { name: paso, exact: true }).click();
        await axe(page, `persona · ${paso}`);
      }
      await page.getByRole('link', { name: 'Terminar y volver' }).click();
      await axe(page, 'usabilidad');
      await ir(page, 'Lector');
      await axe(page, 'lector');
      await page.getByRole('link', { name: /NVDA · claro/ }).click();
      await axe(page, 'sesión de lector');
      await ir(page, 'Navegadores');
      await axe(page, 'navegadores');
      await page.getByRole('link', { name: /Chrome/ }).first().click();
      await axe(page, 'corrida');
      await ir(page, 'Inicio');
      await axe(page, 'inicio');
      await ir(page, 'Datos');
      await page.getByRole('button', { name: 'Copiar JSON' }).click();
      await page.getByRole('button', { name: 'Ver el texto' }).first().click();
      await axe(page, 'datos con texto exportado');
    });

    test('axe: los avisos, tablas de resumen y el cuadro de diálogo de borrar', async ({ page }) => {
      await abrir(page);
      await importarSesiones(page);
      await page.getByRole('button', { name: 'Borrar los datos locales…' }).click();
      await expect(page.getByRole('dialog')).toBeVisible();
      await axe(page, 'diálogo de borrar');
      await page.getByRole('button', { name: 'Cancelar' }).click();
      await expect(page.getByRole('dialog')).toHaveCount(0);
      await ir(page, 'Usabilidad');
      await axe(page, 'usabilidad vacía');
    });
  });
}

test.describe('Teclado y navegación', () => {
  test('el primer Tab llega a «Saltar al contenido» y lo activa sin cambiar la ruta', async ({ page, browserName }) => {
    test.skip(browserName === 'webkit', 'En Safari, Tab solo recorre controles de formulario si el usuario no activó «Navegación con teclado»; se cubre con Chromium.');
    await page.goto('/#/datos');
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
    await page.keyboard.press('Tab');
    await expect(page.getByRole('link', { name: 'Saltar al contenido' })).toBeFocused();
    await page.keyboard.press('Enter');
    await expect(page.locator('main')).toBeFocused();
    await expect(page).toHaveURL(/#\/datos$/);
  });

  test('al cambiar de sección el foco pasa al título y el título de la pestaña dice dónde estás', async ({ page }) => {
    await abrir(page);
    for (const [seccion, titulo] of [['Navegadores', /Navegadores y móviles reales/], ['Lector', /Teclado y lector de pantalla/], ['Usabilidad', /Modo moderador/], ['Datos', /Datos: exportar/]] as const) {
      await ir(page, seccion);
      await expect(page.getByRole('heading', { level: 1 })).toHaveText(titulo);
      await expect(page.getByRole('heading', { level: 1 })).toBeFocused();
      await expect(page).toHaveTitle(/ · Pruebas humanas de BabelHub$/);
      await expect(page.getByRole('navigation', { name: 'Secciones' }).locator('a[aria-current="page"]')).toBeVisible();
    }
  });

  test('el foco es siempre visible y nunca queda tapado por la barra inferior (WCAG 2.4.7 y 2.4.11)', async ({ page, browserName }) => {
    test.skip(browserName === 'webkit', 'Tab en Safari depende de una preferencia del sistema; se cubre con Chromium (escritorio y teléfono).');
    await abrir(page);
    await ir(page, 'Usabilidad');
    await page.getByRole('button', { name: /Agregar persona a la ronda 1/ }).click();
    await page.getByRole('button', { name: 'SUS', exact: true }).click();
    await page.evaluate(() => (document.activeElement as HTMLElement | null)?.blur());
    const problemas: string[] = [];
    for (let i = 0; i < 70; i++) {
      await page.keyboard.press('Tab');
      const m = await page.evaluate(() => {
        const el = document.activeElement as HTMLElement | null;
        if (!el || el === document.body) return null;
        const radio = el instanceof HTMLInputElement && el.type === 'radio';
        const visible = radio ? (el.nextElementSibling as HTMLElement) : el; // el aro de los grupos de opción va en su texto
        const e = getComputedStyle(visible);
        const caja = (radio ? (el.closest('label') as HTMLElement) : el).getBoundingClientRect();
        const nav = document.querySelector('nav.tabs')!.getBoundingClientRect();
        if (el.closest('nav.tabs')) return null; // la propia barra de navegación
        return { quien: `${el.tagName}:${(el.getAttribute('name') ?? el.textContent ?? '').trim().slice(0, 28)}`, aro: e.outlineStyle !== 'none' && parseFloat(e.outlineWidth) >= 2, tapado: caja.bottom > nav.top + 1 && caja.top < nav.bottom };
      });
      if (m && !m.aro) problemas.push(`sin aro: ${m.quien}`);
      if (m && m.tapado) problemas.push(`tapado por la barra: ${m.quien}`);
    }
    expect(problemas).toEqual([]);
  });

  test('con el teclado se puede responder el SUS completo (las flechas mueven entre opciones y Espacio elige)', async ({ page, browserName }) => {
    test.skip(browserName === 'webkit', 'Tab en Safari depende de una preferencia del sistema.');
    await abrir(page);
    await ir(page, 'Usabilidad');
    await page.getByRole('button', { name: /Agregar persona a la ronda 1/ }).click();
    await page.getByRole('button', { name: 'SUS', exact: true }).click();
    const primero = page.getByRole('group', { name: /^1\. / }).getByRole('radio').first();
    await primero.focus();
    await page.keyboard.press('ArrowRight');
    await page.keyboard.press('ArrowRight');
    await expect(page.getByRole('group', { name: /^1\. / }).getByRole('radio', { name: '3', exact: true })).toBeChecked();
  });
});

test.describe('Responsivo y movimiento', () => {
  for (const ancho of [375, 320]) {
    test(`sin desplazamiento horizontal a ${ancho} px en ninguna pantalla`, async ({ page }) => {
      await page.setViewportSize({ width: ancho, height: 740 });
      await prepararTodo(page);
      const pantallas: Array<[string, () => Promise<void>]> = [
        ['persona', async () => { await page.getByRole('button', { name: 'Antes', exact: true }).click(); }],
        ['persona · tarea', async () => { await page.getByRole('button', { name: 'Tarea', exact: true }).click(); }],
        ['persona · SUS', async () => { await page.getByRole('button', { name: 'SUS', exact: true }).click(); }],
        ['usabilidad', async () => { await ir(page, 'Usabilidad'); }],
        ['lector', async () => { await ir(page, 'Lector'); }],
        ['sesión', async () => { await page.getByRole('link', { name: /NVDA · claro/ }).click(); await page.evaluate(() => document.querySelectorAll('details').forEach((d) => { d.open = true; })); }],
        ['navegadores', async () => { await ir(page, 'Navegadores'); }],
        ['corrida', async () => { await page.getByRole('link', { name: /Chrome/ }).first().click(); }],
        ['datos', async () => { await ir(page, 'Datos'); await page.getByRole('button', { name: 'Ver el texto' }).first().click(); }],
        ['inicio', async () => { await ir(page, 'Inicio'); }],
      ];
      for (const [nombre, ir_] of pantallas) { await ir_(); expect(await sinDesplazamiento(page), `${nombre} a ${ancho} px`).toBe(true); }
    });
  }

  test('los botones, enlaces y opciones miden al menos 44 px de alto para el pulgar (mínimo exigido: 24 px)', async ({ page }) => {
    await prepararTodo(page);
    await page.getByRole('button', { name: 'SUS', exact: true }).click();
    const chicos = await page.evaluate(() => {
      const malos: string[] = [];
      document.querySelectorAll('main button, main .seg span, nav.tabs a, header .btn, main input:not([type=radio]):not([type=checkbox]), main select').forEach((e) => {
        const r = (e as HTMLElement).getBoundingClientRect();
        if (r.width && r.height && (r.height < 40 || r.width < 24)) malos.push(`${e.tagName}:${(e.textContent ?? '').trim().slice(0, 20)} ${Math.round(r.width)}×${Math.round(r.height)}`);
      });
      return malos;
    });
    expect(chicos).toEqual([]);
  });

  test('con «reducir movimiento» no hay animaciones ni transiciones', async ({ page }) => {
    await page.emulateMedia({ reducedMotion: 'reduce' });
    await prepararTodo(page);
    expect(await page.evaluate(() => document.getAnimations().length)).toBe(0);
  });

  test('las regiones de la página: banner, navegación con nombre y contenido principal', async ({ page }) => {
    await abrir(page);
    await expect(page.getByRole('banner')).toBeVisible();
    await expect(page.getByRole('navigation', { name: 'Secciones' })).toBeVisible();
    await expect(page.getByRole('main')).toBeVisible();
    await expect(page.locator('html')).toHaveAttribute('lang', 'es-PE');
  });
});

test.describe('Tema', () => {
  test('el botón alterna sistema → claro → oscuro, se recuerda al recargar y respeta el tema del sistema', async ({ page }) => {
    await page.emulateMedia({ colorScheme: 'dark' });
    await abrir(page);
    await expect(page.locator('html')).toHaveAttribute('data-theme', 'babelhub-dark');
    await expect(page.getByRole('button', { name: /Cambiar tema/ })).toContainText('sistema');
    await page.getByRole('button', { name: /Cambiar tema/ }).click();
    await expect(page.locator('html')).toHaveAttribute('data-theme', 'babelhub');
    await expect(page.getByRole('button', { name: /Cambiar tema/ })).toContainText('claro');
    await page.reload();
    await expect(page.locator('html')).toHaveAttribute('data-theme', 'babelhub');
    await page.getByRole('button', { name: /Cambiar tema/ }).click();
    await expect(page.locator('html')).toHaveAttribute('data-theme', 'babelhub-dark');
    await expect(page.getByRole('button', { name: /Cambiar tema/ })).toContainText('oscuro');
  });
});
