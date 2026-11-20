import { test, expect, type Page } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';

// El Libro de Reclamaciones del consumidor de punta a punta (ecommerce-libro-reclamaciones T22; AC-REC-01, AC-REC-04, AC-REC-12): el enlace del pie en toda página, el formulario sin sesión, la constancia imprimible, los campos que faltan sin eco de valores, el texto de un tercero siempre
// codificado y «Mis hojas» de un cliente. La gestión de la administración (AC-REC-06, AC-REC-10) vive en el host de gestión y la cubre `acceso/` cuando exista su sembrador de administración.
// Se ejecutan con los proyectos obligatorios (Chromium de escritorio y de teléfono) y WebKit si está disponible; Firefox es informativo.
//
//   BABELHUB__Sembrador__Clave   la clave del sembrador (la trae el archivo de entorno de `entorno/preparar-entorno.sh`)
//   E2E_FORWARDED_PROTO=https    el host local escucha en HTTP: se le dice que el borde ya terminó el TLS
const clave = process.env.BABELHUB__Sembrador__Clave ?? process.env.E2E_SEMBRADOR_CLAVE;

// Cada recorrido llega desde su propio origen: el cupo de hojas es de 3 por hora por origen (REC-11) y todos los recorridos salen de esta máquina (direcciones del rango de documentación, RFC 5737).
async function conOrigenPropio(page: Page) {
  const ip = `${Math.random() < 0.5 ? '203.0.113' : '198.51.100'}.${1 + Math.floor(Math.random() * 253)}`;
  await page.context().setExtraHTTPHeaders({ 'X-Origen-Cliente': ip });
}

async function sinDesplazamientoHorizontal(page: Page) {
  expect(await page.evaluate(() => document.documentElement.scrollWidth > document.documentElement.clientWidth), 'la página no se desplaza en horizontal').toBe(false);
}

async function sinViolacionesAA(page: Page) {
  const resultado = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa']).analyze();
  expect(resultado.violations).toEqual([]);
}

async function llenarLaHoja(page: Page, detalle: string) {
  await page.getByRole('radio', { name: /^Reclamo/ }).check();
  await page.getByLabel('Nombre completo').fill('Ana Pérez Quispe');
  await page.getByLabel('Documento de identidad').fill('12345678');
  await page.getByLabel('Domicilio').fill('Jr. Lima 123, Ayacucho');
  await page.getByLabel('Correo electrónico').fill('ana.perez@correo.example');
  await page.getByLabel('Bien contratado').fill('Libro «La ciudad y los perros»');
  await page.getByLabel('Monto reclamado (S/)').fill('59.90');
  await page.getByLabel('Detalle').fill(detalle);
}

test.describe('Visitante: el Libro de Reclamaciones', () => {
  test.beforeEach(async ({ page }) => conOrigenPropio(page));

  test('el enlace del pie está en toda página y lleva al formulario (AC-REC-01)', async ({ page }) => {
    for (const ruta of ['/', '/catalogo', '/carrito', '/legal/terminos', '/ingreso', '/registro']) {
      await page.goto(ruta);
      const enlace = page.getByRole('navigation', { name: 'Información legal' }).getByRole('link', { name: /Libro de Reclamaciones/ });
      await expect(enlace, `el pie de ${ruta}`).toHaveAttribute('href', '/reclamaciones');
    }
    await page.getByRole('navigation', { name: 'Información legal' }).getByRole('link', { name: /Libro de Reclamaciones/ }).click();
    await expect(page).toHaveURL(/\/reclamaciones\/nueva$/);
    await expect(page.getByRole('heading', { level: 1, name: 'Libro de Reclamaciones' })).toBeVisible();
  });

  test('el formulario es accesible (AA), no se desplaza en 375 px y avisa de no escribir datos personales de más (AC-REC-01)', async ({ page }) => {
    await page.goto('/reclamaciones/nueva');
    await expect(page.getByRole('radio', { name: /^Reclamo/ })).toBeVisible();
    await expect(page.getByRole('radio', { name: /^Queja/ })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Registrar hoja' })).toBeVisible();
    await expect(page.locator('body')).toContainText('No escribas datos personales');
    await sinDesplazamientoHorizontal(page);
    await sinViolacionesAA(page);
  });

  test('registrar una hoja da una constancia con su número, el plazo de 15 días y una impresión sin controles (AC-REC-01, AC-REC-04)', async ({ page }) => {
    await page.goto('/reclamaciones/nueva');
    await llenarLaHoja(page, 'El libro llegó con las hojas sueltas y pido su reposición.');
    const respuesta = page.waitForResponse((r) => r.url().endsWith('/reclamaciones/nueva') && r.request().method() === 'POST');
    await page.getByRole('button', { name: 'Registrar hoja' }).click();

    expect((await respuesta).headers()['cache-control']).toContain('no-store');
    await expect(page.getByRole('heading', { level: 1, name: /^Hoja de reclamación \d{4}-\d{6}$/ })).toBeVisible();
    await expect(page.locator('body')).toContainText('15 días calendario');
    await expect(page.locator('body')).toContainText('Ana Pérez Quispe');
    await sinViolacionesAA(page);

    // La impresión oculta el botón de imprimir y no usa scripts en línea (la política de contenido no los admite).
    await page.emulateMedia({ media: 'print' });
    await expect(page.getByRole('button', { name: 'Imprimir constancia' })).toBeHidden();
    await page.emulateMedia({ media: 'screen' });
    await expect(page.getByRole('button', { name: 'Imprimir constancia' })).toBeVisible();
  });

  test('el texto de un tercero se muestra siempre como texto, nunca como código (AC-REC-04)', async ({ page }) => {
    const errores: string[] = [];
    page.on('pageerror', (e) => errores.push(e.message));
    await page.goto('/reclamaciones/nueva');
    await llenarLaHoja(page, '<script>window.__xss = 1</script><img src=x onerror="window.__xss = 2"> pedido dañado');
    await page.getByRole('button', { name: 'Registrar hoja' }).click();

    await expect(page.getByRole('heading', { level: 1, name: /^Hoja de reclamación/ })).toBeVisible();
    await expect(page.locator('dd', { hasText: '<script>window.__xss = 1</script>' })).toBeVisible();
    expect(await page.evaluate(() => (window as unknown as { __xss?: number }).__xss)).toBeUndefined();
    expect(errores).toEqual([]);
  });

  test('los campos que faltan se listan sin devolver lo escrito (AC-REC-01)', async ({ page }) => {
    await page.goto('/reclamaciones/nueva');
    await page.getByLabel('Nombre completo').fill('Ana Pérez Quispe');
    await page.getByLabel('Detalle').fill('texto reservado que no debe volver');
    // Se quita la validación del navegador para que el servidor demuestre la suya.
    await page.locator('#formulario-de-reclamacion').evaluate((f: HTMLFormElement) => { f.noValidate = true; });
    await page.getByRole('button', { name: 'Registrar hoja' }).click();

    const aviso = page.getByRole('alert');
    await expect(aviso).toContainText('Revisa estos datos');
    await expect(aviso).toBeFocused();
    await expect(page.locator('body')).not.toContainText('texto reservado que no debe volver');
    await expect(page.getByLabel('Nombre completo')).toHaveValue('');
  });

  test('«Mis hojas» pide ingresar a quien no tiene sesión', async ({ page }) => {
    await page.goto('/reclamaciones/mis-hojas');
    await expect(page).toHaveURL(/\/ingreso$/);
  });
});

test.describe('Cliente con sesión: Mis hojas', () => {
  test('un cliente recién sembrado ve su estado vacío accesible y la invitación a verificar su correo (AC-REC-12)', async ({ page }) => {
    await conOrigenPropio(page);
    const respuesta = await page.request.post('/desarrollo/sesion', { headers: { 'X-Sembrador-De-Desarrollo': clave! }, data: { rol: 'cliente' } });
    expect(respuesta.ok(), 'el sembrador de sesiones responde').toBeTruthy();
    await page.context().addCookies((await respuesta.json()).cookies);

    await page.goto('/reclamaciones/mis-hojas');
    await expect(page.getByRole('heading', { level: 1, name: 'Mis hojas de reclamación' })).toBeVisible();
    await expect(page.locator('body')).toContainText('No tienes hojas registradas con tu correo verificado.');
    await sinDesplazamientoHorizontal(page);
    await sinViolacionesAA(page);
  });
});
