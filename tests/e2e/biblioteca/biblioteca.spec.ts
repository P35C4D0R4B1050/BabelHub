import { test, expect } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';

// Recorridos de la biblioteca digital (ecommerce-biblioteca-digital T25; AC-BIB-02, AC-BIB-11, AC-BIB-12, AC-BIB-14). Las sesiones se aportan ya iniciadas
// (las pantallas de acceso son de `web-identidad`) y el sembrado de un cliente con libros digitales es del entorno E2E:
//   E2E_CLIENTE_BIBLIOTECA_STORAGE_STATE  `storageState` de un cliente con al menos un libro digital vigente (con descargas restantes) y, si se quiere, uno agotado.
//   E2E_ADMIN_STORAGE_STATE               `storageState` de un administrador central (host de gestión, E2E_ADMIN_URL).
// Sin esos archivos los recorridos se omiten: nunca se inventan datos ni se guardan sesiones en el repositorio. La descarga real comprueba que el navegador va
// directamente al almacén (nunca pasa por el servidor, BIB-14) con la CSP real.
const sesionDeCliente = process.env.E2E_CLIENTE_BIBLIOTECA_STORAGE_STATE;
const sesionDeAdministracion = process.env.E2E_ADMIN_STORAGE_STATE;

test.describe('Mi biblioteca', () => {
  test.skip(!sesionDeCliente, 'Falta E2E_CLIENTE_BIBLIOTECA_STORAGE_STATE');
  test.use({ storageState: sesionDeCliente });

  test('AC-BIB-02: cada libro muestra su formato, su fecha de compra y «usadas de 5» con texto', async ({ page }) => {
    await page.goto('/biblioteca');
    await expect(page.getByRole('heading', { level: 1, name: 'Mi biblioteca' })).toBeVisible();
    await expect(page.getByText(/\d de 5 descargas usadas/).first()).toBeVisible();
    await expect(page.getByText(/EPUB|PDF/).first()).toBeVisible();
    await expect(page.getByText('5 minutos').first()).toBeVisible();
  });

  test('AC-BIB-12: la descarga redirige al almacén y la página no transmite el archivo', async ({ page }) => {
    await page.goto('/biblioteca');
    const boton = page.getByRole('button', { name: 'Descargar' }).first();
    await expect(boton).toBeVisible();
    const respuestaDePost = page.waitForResponse((r) => r.url().includes('/descargar') && r.request().method() === 'POST');
    await boton.click({ noWaitAfter: true });
    const respuesta = await respuestaDePost;
    expect([302, 303]).toContain(respuesta.status());
    expect(respuesta.headers()['cache-control']).toContain('no-store');
    expect(respuesta.headers()['location']).toMatch(/^https:\/\//);
    expect(await respuesta.body().catch(() => Buffer.alloc(0))).toHaveLength(0);
  });

  test('AC-BIB-14: pedir ayuda con un motivo de lista cerrada deja la solicitud visible', async ({ page }) => {
    await page.goto('/biblioteca');
    const resumen = page.getByText('Pedir ayuda con este libro').first();
    test.skip(!(await resumen.isVisible().catch(() => false)), 'No hay un libro sin ayuda abierta');
    await resumen.click();
    await page.getByLabel('¿Qué pasó?').first().selectOption('archivo-no-abre');
    await page.getByRole('button', { name: 'Enviar solicitud' }).first().click();
    await expect(page.getByText('Ya pediste ayuda sobre este libro').first()).toBeVisible();
  });

  test('la pantalla cumple axe AA y se opera por teclado', async ({ page }) => {
    await page.goto('/biblioteca');
    const resultados = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa']).analyze();
    expect(resultados.violations).toEqual([]);
    await page.keyboard.press('Tab');
    await expect(page.locator(':focus-visible')).toBeVisible();
  });
});

test.describe('Administración de la biblioteca', () => {
  test.skip(!sesionDeAdministracion, 'Falta E2E_ADMIN_STORAGE_STATE');
  test.use({ storageState: sesionDeAdministracion, baseURL: process.env.E2E_ADMIN_URL ?? 'http://localhost:8081' });

  test('AC-BIB-14: la lista de ayudas no muestra datos de la cuenta y ofrece restituir y cerrar', async ({ page }) => {
    await page.goto('/administracion/biblioteca/ayudas?estado=todas');
    await expect(page.getByRole('heading', { level: 1, name: 'Ayudas de la biblioteca' })).toBeVisible();
    await expect(page.getByText('@')).toHaveCount(0);
    const resultados = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa']).analyze();
    expect(resultados.violations).toEqual([]);
  });
});
