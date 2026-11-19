import { test, expect } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';

// Humo del entorno local (T36 de ecommerce-carrito-checkout): comprueba que el host del E-commerce compuesto (COMP-1), con su base migrada
// (COMP-2), sirve las pantallas del visitante sin ningún dato sembrado. Es el primer recorrido de punta a punta del sistema; los
// recorridos con compra (`carrito-checkout.spec.ts`) necesitan además el sembrado de catálogo, inventario y sesiones (ver README).
//   E2E_FORWARDED_PROTO=https   el host local escucha en HTTP: se le dice que el borde ya terminó el TLS.

test.describe('Entorno local: visitante sin datos sembrados', () => {
  test('el carrito vacío dice su estado y no pide ingresar', async ({ page }) => {
    await page.goto('/carrito');
    await expect(page.getByRole('heading', { level: 1, name: 'Tu carrito' })).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Tu carrito está vacío' })).toBeVisible();
  });

  test('el catálogo responde con su estado (con datos o vacío) y sin errores de consola', async ({ page }) => {
    const errores: string[] = [];
    page.on('pageerror', (e) => errores.push(e.message));
    const respuesta = await page.goto('/catalogo');
    expect(respuesta?.status()).toBe(200);
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
    expect(errores).toEqual([]);
  });

  test('las cabeceras de seguridad y la accesibilidad del carrito vacío (AA) se cumplen', async ({ page }) => {
    const respuesta = await page.goto('/carrito');
    const cabeceras = respuesta!.headers();
    expect(cabeceras['x-content-type-options']).toBe('nosniff');
    expect(cabeceras['content-security-policy']).toContain("default-src 'self'");
    const resultado = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa']).analyze();
    expect(resultado.violations).toEqual([]);
  });
});
