import { test, expect, type Page } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';

// El pie con el contacto del comercio, la página de contacto y «Cambios y devoluciones» (web-informacion-del-comercio; AC-COM-01, AC-COM-05, AC-COM-07, AC-COM-08, AC-COM-09 y AC-COM-14).
// Entorno E2E local en `Development` (host HTTP con `E2E_FORWARDED_PROTO=https`, sin `conOrigenPropio`, que reemplaza las cabeceras extra): el contacto es el ficticio (999 000 000, contacto@ejemplo.test), que sale de la configuración del entorno y nunca del repositorio.

const ETIQUETAS = ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'];

async function axe(page: Page, donde: string) {
  const resultado = await new AxeBuilder({ page }).withTags(ETIQUETAS).analyze();
  expect(resultado.violations.map((v) => `${v.id}: ${v.nodes.map((n) => n.target.join(' ')).join(' | ')}`), `axe en ${donde}`).toEqual([]);
}

async function usarTema(page: Page, tema: string) {
  const base = new URL('/', test.info().project.use.baseURL ?? process.env.E2E_BASE_URL ?? 'http://localhost:8080').toString();
  await page.context().addCookies([{ name: 'bh-tema', value: tema, url: base }]);
}

test.describe('Pie y contacto del comercio', () => {
  for (const ruta of ['/', '/catalogo', '/legal/terminos', '/legal/cambios-y-devoluciones', '/contacto', '/ingreso']) {
    test(`el pie de ${ruta} muestra el contacto y los enlaces del comercio (AC-COM-01)`, async ({ page }) => {
      await page.goto(ruta);

      const pie = page.locator('footer');
      await expect(pie.getByRole('link', { name: /Llamar al/ })).toHaveAttribute('href', 'tel:+51999000000');
      await expect(pie.getByRole('link', { name: /Escribir a/ })).toHaveAttribute('href', 'mailto:contacto@ejemplo.test');
      await expect(pie).toContainText('dato de ejemplo');
      await expect(pie.getByRole('link', { name: 'Contacto', exact: true })).toHaveAttribute('href', '/contacto');
      await expect(pie.getByRole('link', { name: /Libro de Reclamaciones/ })).toHaveAttribute('href', '/reclamaciones');
      await expect(pie.getByRole('link', { name: 'Cambios y devoluciones' })).toHaveAttribute('href', '/legal/cambios-y-devoluciones');
      await expect(pie).toContainText('libros impresos y libros digitales');
    });
  }

  test('la página de contacto muestra los tres datos sin formulario ni terceros (AC-COM-05)', async ({ page }) => {
    const externos: string[] = [];
    page.on('request', (r) => {
      if (!r.url().startsWith(new URL(page.url() === 'about:blank' ? (process.env.E2E_BASE_URL ?? 'http://localhost:8080') : page.url()).origin)) externos.push(r.url());
    });
    await page.goto('/contacto');
    await page.waitForLoadState('networkidle');

    const principal = page.locator('main');
    await expect(principal.getByRole('heading', { level: 1, name: 'Contacto' })).toBeVisible();
    await expect(principal).toContainText('999 000 000');
    await expect(principal).toContainText('contacto@ejemplo.test');
    await expect(principal).toContainText('Jr. Ejemplo 123, Ayacucho (dato de ejemplo)');
    await expect(principal.locator('form')).toHaveCount(0);
    expect(externos, 'recursos de terceros').toEqual([]);
  });

  test('«Cambios y devoluciones» muestra su versión y la ruta anterior redirige (AC-COM-08, AC-COM-09)', async ({ page }) => {
    await page.goto('/legal/reembolso');

    await expect(page).toHaveURL(/\/legal\/cambios-y-devoluciones$/);
    await expect(page.getByRole('heading', { level: 1, name: 'Cambios y devoluciones' })).toBeVisible();
    await expect(page.locator('article')).toContainText('Versión');
    await expect(page.locator('article')).toContainText('15 días calendario');
  });

  for (const tema of ['babelhub', 'babelhub-dark']) {
    test(`el pie y la página de contacto cumplen AA en el tema ${tema} y el teclado llega a los enlaces (AC-COM-14)`, async ({ page }) => {
      await usarTema(page, tema);
      for (const ruta of ['/', '/contacto']) {
        await page.goto(ruta);
        await page.waitForLoadState('networkidle');
        await expect(page.locator('html')).toHaveAttribute('data-theme', tema);
        await axe(page, `${ruta} (${tema})`);
      }

      await page.goto('/contacto');
      const telefono = page.locator('main').getByRole('link', { name: /Llamar al/ });
      await telefono.focus();
      await expect(telefono).toBeFocused();
      const foco = await telefono.evaluate((e) => {
        const estilo = getComputedStyle(e);
        return estilo.outlineStyle !== 'none' || estilo.boxShadow !== 'none';
      });
      expect(foco, 'el enlace del teléfono muestra el foco').toBe(true);
    });
  }

  test('en 375 px el pie y la página de contacto no se desplazan en horizontal (AC-COM-01)', async ({ page }) => {
    await page.setViewportSize({ width: 375, height: 800 });
    for (const ruta of ['/', '/contacto']) {
      await page.goto(ruta);
      const desborda = await page.evaluate(() => document.documentElement.scrollWidth > document.documentElement.clientWidth);
      expect(desborda, `${ruta} desborda en horizontal`).toBe(false);
    }
  });
});
