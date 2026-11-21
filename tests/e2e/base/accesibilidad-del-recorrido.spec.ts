import { test, expect, type Page } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import { agregarAlCarrito, clave, EDICION_IMPRESA_DEL_ZORRO, ingresarComoClienteNuevo } from '../pedidos/ayudas';

// La revisión automática de accesibilidad con axe en el recorrido de compra y en las pantallas base (web-base T33; AC-WEB-07, AC-WEB-15): 0 incumplimientos de nivel A y AA (WCAG 2.0, 2.1 y 2.2 AA) en cada paso,
// en los dos temas, y el foco por teclado visible en el recorrido. La lista manual de teclado y lector de pantalla está en `docs/06-experiencia-de-usuario/10-lista-manual-de-accesibilidad.md` (la ejecuta una persona).
// Mismo entorno que `pedidos/compra-y-pedido.spec.ts` (host de la tienda por HTTPS y `entorno/sembrar-datos.sh --limpiar` antes de cada corrida).

test.describe.configure({ mode: 'serial' });
test.setTimeout(120_000);

const ETIQUETAS = ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'];

async function axe(page: Page, donde: string) {
  const resultado = await new AxeBuilder({ page }).withTags(ETIQUETAS).analyze();
  expect(resultado.violations.map((v) => `${v.id}: ${v.nodes.map((n) => n.target.join(' ')).join(' | ')}`), `axe en ${donde}`).toEqual([]);
}

// Cada elemento al que llega el teclado muestra un indicador de foco (contorno o sombra), salvo el cuerpo y los contenedores vacíos de terceros.
async function recorrerConTeclado(page: Page, pasos: number) {
  const sinIndicador: string[] = [];
  for (let i = 0; i < pasos; i++) {
    await page.keyboard.press('Tab');
    const medida = await page.evaluate(() => {
      const e = document.activeElement as HTMLElement | null;
      if (!e || e === document.body || (e.tagName === 'DIV' && !e.children.length && !e.textContent?.trim() && !e.className)) return null;
      const estilo = getComputedStyle(e);
      return { e: `${e.tagName}:${(e.getAttribute('name') ?? e.textContent ?? '').trim().slice(0, 24)}`, anillo: estilo.outlineStyle !== 'none' || estilo.boxShadow !== 'none' };
    });
    if (medida && !medida.anillo) sinIndicador.push(medida.e);
  }
  expect(sinIndicador, 'elementos enfocados sin indicador de foco').toEqual([]);
}

test.describe('Accesibilidad del recorrido de compra y de las pantallas base', () => {
  test('las pantallas base cumplen AA en los dos temas y el teclado muestra el foco (AC-WEB-07)', async ({ page }) => {
    const base = new URL('/', test.info().project.use.baseURL ?? process.env.E2E_BASE_URL ?? 'https://localhost:8443').toString();
    for (const tema of ['babelhub', 'babelhub-dark']) {
      await page.context().addCookies([{ name: 'bh-tema', value: tema, url: base }]);
      for (const ruta of ['/', '/catalogo', '/carrito', '/ingreso', '/registro', '/recuperacion', '/legal/terminos', '/reclamaciones/nueva']) {
        await page.goto(ruta);
        await page.waitForLoadState('networkidle');
        await expect(page.locator('html')).toHaveAttribute('data-theme', tema);
        await axe(page, `${ruta} (${tema})`);
      }
    }
    await page.goto('/catalogo');
    await recorrerConTeclado(page, 18);
  });

  test.describe('Recorrido de compra con recojo', () => {
    test.skip(!clave, 'Falta BABELHUB__Sembrador__Clave (entorno E2E local con el sembrador de sesiones)');

    test('catálogo, ficha, carrito, checkout, pago y estado cumplen AA y el teclado muestra el foco (AC-WEB-15)', async ({ page }) => {
      await ingresarComoClienteNuevo(page);

      await page.goto('/catalogo');
      await axe(page, 'catálogo');
      const enlaceDeObra = page.locator('a[href^="/catalogo/obras/"]').first();
      await expect(enlaceDeObra).toBeVisible();
      await enlaceDeObra.click();
      await expect(page).toHaveURL(/\/catalogo\/obras\//);
      await axe(page, 'ficha de la obra');

      await agregarAlCarrito(page, EDICION_IMPRESA_DEL_ZORRO, 1);
      await page.goto('/carrito');
      await expect(page.getByRole('heading', { level: 1, name: 'Tu carrito' })).toBeVisible();
      await axe(page, 'carrito');
      await recorrerConTeclado(page, 14);

      await page.getByRole('button', { name: 'Continuar con la compra' }).click();
      await expect(page).toHaveURL(/\/checkout$/);
      await axe(page, 'checkout: entrega');
      await page.getByLabel('Sucursal de recojo').selectOption({ label: 'Sucursal Ayacucho (prueba)' });
      await page.getByRole('button', { name: 'Elegir recojo' }).click();
      await expect(page.getByRole('heading', { name: 'Tus datos y aceptaciones' })).toBeVisible();
      await axe(page, 'checkout: aceptaciones');
      for (const casilla of await page.getByRole('group', { name: 'Para continuar, acepta:' }).getByRole('checkbox').all()) {
        await casilla.check();
      }
      await page.getByRole('button', { name: /^Ir al pago/ }).click();
      await expect(page.getByRole('heading', { level: 1, name: 'Pagar tu compra' })).toBeVisible();
      await axe(page, 'pago');
      await page.getByRole('radiogroup', { name: 'Medio de pago' }).locator('label', { hasText: /Tarjeta/i }).click();
      await page.getByLabel(/Identificador del medio de pago/).fill('tok_aprobado');
      await page.getByRole('button', { name: /^Pagar S\// }).click();
      await expect(page).toHaveURL(/\/checkout\/pago\/estado/);
      await axe(page, 'estado del pago');
    });
  });
});
