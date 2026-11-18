import { test, expect } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';

// Recorrido de la administración del catálogo (ecommerce-catalogo). Las pantallas de acceso las construye
// `web-identidad`, así que la sesión de la administración central se aporta ya iniciada:
//   E2E_ADMIN_STORAGE_STATE  ruta a un `storageState` de Playwright con la sesión de un administrador central
// Sin ese archivo, los recorridos se omiten.
const sesionDeAdministracion = process.env.E2E_ADMIN_STORAGE_STATE;

test.describe('Administración del catálogo', () => {
  test.skip(!sesionDeAdministracion, 'Falta E2E_ADMIN_STORAGE_STATE');
  test.use({ storageState: sesionDeAdministracion });

  test('la lista de obras sale en la ruta del catálogo nacional con sus estados', async ({ page }) => {
    await page.goto('/administracion/obras');
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
    await expect(page.getByRole('link', { name: 'Catálogo nacional' }).first()).toBeVisible();
  });

  test('alta de obra: los errores se dicen en un aviso con foco y cada campo tiene etiqueta', async ({ page }) => {
    await page.goto('/administracion/obras/nueva');
    await expect(page.getByLabel('Título *')).toBeVisible();
    await expect(page.getByLabel(/Portada/)).toBeVisible();

    // Sin completar nada: el formulario no envía y avisa en un `role="alert"` que recibe el foco.
    await page.getByRole('button', { name: 'Guardar obra' }).click();
    const aviso = page.getByRole('alert').first();
    await expect(aviso).toBeVisible();
    await expect(aviso).toBeFocused();
  });

  test('alta de obra: las secciones de impresa y digital se abren con su casilla y por teclado', async ({ page }) => {
    await page.goto('/administracion/obras/nueva');
    const impresa = page.getByLabel('Tiene edición impresa');
    await impresa.focus();
    await page.keyboard.press('Space');
    await expect(page.getByLabel('Stock inicial (opcional, de 1 a 5 000)')).toBeVisible();

    const digital = page.getByLabel('Tiene edición digital');
    await digital.focus();
    await page.keyboard.press('Space');
    await expect(page.getByLabel(/Libro \(EPUB o PDF/)).toBeVisible();
  });

  test('la subida con un tipo no permitido se rechaza sin guardar nada', async ({ page }) => {
    await page.goto('/administracion/obras/nueva');
    await page.getByLabel(/Portada/).setInputFiles({ name: 'falsa.png', mimeType: 'image/png', buffer: Buffer.from('no es una imagen') });
    await page.getByLabel('Título *').fill('Obra de prueba E2E');
    await page.getByLabel('Autor *').fill('Autora');
    await page.getByLabel('Editorial *').fill('Editorial');
    await page.getByLabel('Categoría *').fill('Prueba');
    await page.getByLabel('Tiene edición impresa').check();
    await page.getByLabel('ISBN *').first().fill('9780306406157');
    await page.getByLabel('Precio (S/) *').fill('10.00');
    await page.getByRole('button', { name: 'Guardar obra' }).click();
    await expect(page.getByRole('alert').first()).toContainText(/archivo|imagen|tipo/i);
    await expect(page).toHaveURL(/\/administracion\/obras\/nueva$/);
  });

  for (const tema of ['babelhub', 'babelhub-dark'] as const) {
    test(`accesibilidad (${tema}): lista y alta de obras sin violaciones de contraste ni de etiquetas`, async ({ page }) => {
      for (const ruta of ['/administracion/obras', '/administracion/obras/nueva']) {
        await page.goto(ruta);
        await page.evaluate((t) => document.documentElement.setAttribute('data-theme', t), tema);
        const resultado = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa']).analyze();
        expect(resultado.violations.map((v) => `${ruta} ${v.id}: ${v.nodes.length}`)).toEqual([]);
      }
    });
  }

  test('un Encargado de sucursal no entra a la administración del catálogo', async ({ browser }) => {
    const sesionDeEncargado = process.env.E2E_ENCARGADO_STORAGE_STATE;
    test.skip(!sesionDeEncargado, 'Falta E2E_ENCARGADO_STORAGE_STATE');
    const contexto = await browser.newContext({ storageState: sesionDeEncargado });
    const pagina = await contexto.newPage();
    const respuesta = await pagina.goto('/administracion/obras');
    expect(respuesta?.status()).toBe(403);
    await contexto.close();
  });
});
