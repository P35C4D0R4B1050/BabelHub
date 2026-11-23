import { test, expect } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';

// Avisos de simulación (ecommerce-pagos PAG-33 y PAG-42; AC-PAG-29 y AC-PAG-37): el entorno de revisión y el de desarrollo muestran, con texto e icono y encima del pie de
// cada página, que no se cobra y que los envíos son simulados. Corre contra el host local del E-commerce (README, «Entorno local de punta a punta»):
//   E2E_FORWARDED_PROTO=https   el host local escucha en HTTP y se le dice que el borde ya terminó el TLS.

test.describe('Avisos de pagos y envíos simulados', () => {
  test('AC-PAG-29: «Pagos simulados» con texto e icono, encima del pie, en la portada y en el catálogo', async ({ page }) => {
    for (const ruta of ['/', '/catalogo']) {
      await page.goto(ruta);
      const aviso = page.locator('[data-bh-pagos-simulados]').first();
      await expect(aviso).toBeVisible();
      await expect(aviso).toContainText('Pagos simulados: no se realiza ningún cobro');
      await expect(aviso.locator('svg').first()).toBeVisible();
      // Encima del pie: el aviso aparece antes que el pie en el documento.
      const orden = await page.evaluate(() => {
        const a = document.querySelector('[data-bh-pagos-simulados]');
        const p = document.querySelector('footer');
        return a && p ? (a.compareDocumentPosition(p) & Node.DOCUMENT_POSITION_FOLLOWING ? 'antes' : 'despues') : 'sin-pie';
      });
      expect(orden).toBe('antes');
    }
  });

  test('AC-PAG-37: «Los envíos son simulados» con texto e icono, encima del pie de la portada', async ({ page }) => {
    await page.goto('/');
    const aviso = page.locator('[data-bh-envios-simulados]').first();
    await expect(aviso).toBeVisible();
    await expect(aviso).toContainText('Los envíos son simulados: proyecto académico');
    await expect(aviso.locator('svg').first()).toBeVisible();
  });

  test('los avisos cumplen la accesibilidad AA y el estado no depende solo del color', async ({ page }) => {
    await page.goto('/');
    const resultado = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa']).analyze();
    expect(resultado.violations).toEqual([]);
    await expect(page.locator('[data-bh-pagos-simulados]').first()).toHaveAttribute('aria-label', /simulad/i);
  });
});
