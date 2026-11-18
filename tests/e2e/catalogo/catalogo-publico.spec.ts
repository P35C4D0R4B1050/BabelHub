import { test, expect } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';

// Recorrido del catálogo público (ecommerce-catalogo): búsqueda -> ficha con las dos ediciones -> agotado.
// Los datos salen del entorno local sembrado; si no se indican las obras de prueba, los recorridos que las
// necesitan se omiten en lugar de inventar datos (AGENTS.md §6: no se simula stock).
//   E2E_OBRA_DOS_EDICIONES   título de una obra con edición impresa (con stock en alguna sucursal) y digital
//   E2E_OBRA_IMPRESA_AGOTADA título de una obra con impresa agotada en toda la red y digital disponible
const obraDosEdiciones = process.env.E2E_OBRA_DOS_EDICIONES;
const obraAgotada = process.env.E2E_OBRA_IMPRESA_AGOTADA;

async function abrirFicha(page: import('@playwright/test').Page, titulo: string) {
  await page.goto('/catalogo');
  await page.getByLabel('Buscar por título').fill(titulo);
  await page.getByRole('button', { name: 'Buscar' }).click();
  await page.getByRole('link', { name: titulo, exact: true }).first().click();
  await expect(page.getByRole('heading', { level: 1, name: titulo })).toBeVisible();
}

test.describe('Catálogo público', () => {
  test('AC-CAT-07: la ficha muestra las dos ediciones, la disponibilidad por sucursal y «disponible al instante»', async ({ page }) => {
    test.skip(!obraDosEdiciones, 'Falta E2E_OBRA_DOS_EDICIONES');
    await abrirFicha(page, obraDosEdiciones!);

    const ediciones = page.getByRole('navigation', { name: 'Edición' });
    await expect(ediciones.getByRole('link', { name: 'Impreso' })).toBeVisible();
    await expect(ediciones.getByRole('link', { name: 'Digital' })).toBeVisible();

    // Impresa: tabla con una fila por sucursal; el estado se dice con texto, no solo con color.
    await expect(page.getByRole('table', { name: 'Stock vendible por sucursal' })).toBeVisible();
    await expect(page.getByRole('columnheader', { name: 'Estado' })).toBeVisible();

    // Digital: disponible al instante, sin consultar sucursales.
    await ediciones.getByRole('link', { name: 'Digital' }).click();
    await expect(page.getByText('Disponible al instante en tu biblioteca')).toBeVisible();
    await expect(page.getByRole('table', { name: 'Stock vendible por sucursal' })).toHaveCount(0);
  });

  test('AC-CAT-17: una impresa agotada en la red se ve como agotada y ofrece la digital', async ({ page }) => {
    test.skip(!obraAgotada, 'Falta E2E_OBRA_IMPRESA_AGOTADA');
    await abrirFicha(page, obraAgotada!);

    await expect(page.getByText('Impreso agotado en todas las sucursales.')).toBeVisible();
    await expect(page.getByText('Puedes verlo en digital.')).toBeVisible();
    // La obra sigue a la venta: la ficha no se oculta ni se da por retirada.
    await expect(page.getByRole('link', { name: 'Digital' })).toBeVisible();
  });

  test('AC-CAT-07: el filtro con un valor inválido avisa y no rompe la pantalla', async ({ page }) => {
    const respuesta = await page.goto('/catalogo?precioMin=abc');
    expect(respuesta?.status()).toBe(400);
    await expect(page.getByRole('heading', { level: 1, name: 'Catálogo' })).toBeVisible();
  });

  test('teclado: se puede buscar y llegar a un resultado sin ratón, con el foco visible', async ({ page }) => {
    await page.goto('/catalogo');
    await page.getByLabel('Buscar por título').focus();
    await page.keyboard.type('a');
    await page.keyboard.press('Enter');
    await page.waitForLoadState('networkidle');
    await page.keyboard.press('Tab');
    const enfocado = page.locator(':focus');
    await expect(enfocado).toBeVisible();
    const contorno = await enfocado.evaluate((el) => getComputedStyle(el).outlineStyle);
    expect(contorno).not.toBe('none');
  });

  for (const tema of ['babelhub', 'babelhub-dark'] as const) {
    test(`accesibilidad (${tema}): el catálogo no tiene violaciones de contraste ni de etiquetas`, async ({ page }) => {
      await page.goto('/catalogo');
      await page.evaluate((t) => document.documentElement.setAttribute('data-theme', t), tema);
      const resultado = await new AxeBuilder({ page })
        .withTags(['wcag2a', 'wcag2aa', 'wcag21aa'])
        .analyze();
      expect(resultado.violations.map((v) => `${v.id}: ${v.nodes.length}`)).toEqual([]);
    });
  }

  test('teléfono: la página no tiene desplazamiento horizontal', async ({ page }) => {
    await page.setViewportSize({ width: 375, height: 812 });
    await page.goto('/catalogo');
    const ancho = await page.evaluate(() => ({ doc: document.documentElement.scrollWidth, vista: window.innerWidth }));
    expect(ancho.doc).toBeLessThanOrEqual(ancho.vista);
  });
});
