import { test, expect, type Page } from '@playwright/test';
import { clave, conOrigenPropio, ingresarComoClienteNuevo, ingresarComoPersonal, sinViolacionesDeAccesibilidad, urlDeLaGestion } from '../pedidos/ayudas';

// La gestión de las hojas de reclamación por la administración (ecommerce-libro-reclamaciones T22 y T18; AC-REC-06, AC-REC-07, AC-REC-10, AC-REC-13): una hoja registrada por un consumidor en la tienda llega
// al listado de la administración (sin datos personales en la lista), su detalle avisa de que la consulta queda auditada, se responde o se descarta por spam, y quien no es de la administración no entra.
// Corre sobre el host de la gestión (`E2E_TLS=1 E2E_HOST=gestion entorno/arrancar-host.sh ecommerce`, https://localhost:8444) y el de la tienda, que comparten la base. Chromium de escritorio y de teléfono y WebKit;
// Firefox es informativo. Datos ficticios; `entorno/sembrar-datos.sh --limpiar` antes de cada corrida (también vacía los contadores de límites: 3 hojas por hora por origen).

test.describe.configure({ mode: 'serial' });
test.setTimeout(90_000);

// Registra una hoja como un consumidor sin sesión y devuelve su número («AAAA-NNNNNN»).
async function registrarUnaHoja(page: Page, detalle: string): Promise<string> {
  await conOrigenPropio(page);
  await page.goto('/reclamaciones/nueva');
  await page.getByRole('radio', { name: /^Reclamo/ }).check();
  await page.getByLabel('Nombre completo').fill('Ana Pérez Quispe');
  await page.getByLabel('Documento de identidad').fill('12345678');
  await page.getByLabel('Domicilio').fill('Jr. Lima 123, Ayacucho');
  await page.getByLabel('Correo electrónico').fill('ana.perez@correo.example');
  await page.getByLabel('Bien contratado').fill('Libro «La ciudad y los perros»');
  await page.getByLabel('Monto reclamado (S/)').fill('59.90');
  await page.getByLabel('Detalle').fill(detalle);
  await page.getByRole('button', { name: 'Registrar hoja' }).click();
  const titulo = await page.getByRole('heading', { level: 1, name: /^Hoja de reclamación \d{4}-\d{6}$/ }).textContent();
  return titulo!.match(/\d{4}-\d{6}/)![0];
}

test.describe('Administración: hojas de reclamación', () => {
  test.skip(!clave, 'Falta BABELHUB__Sembrador__Clave (entorno E2E local con el sembrador de sesiones)');

  test('una hoja llega al listado sin datos personales, se abre con el aviso de auditoría y se responde (AC-REC-06, AC-REC-07)', async ({ page, browser }) => {
    const numero = await registrarUnaHoja(page, 'El libro llegó con las hojas sueltas y pido su reposición.');

    const contexto = await browser.newContext({ ignoreHTTPSErrors: true });
    const admin = await contexto.newPage();
    await ingresarComoPersonal(admin, 'administrador');
    await admin.goto(`${urlDeLaGestion}/administracion/reclamaciones`);
    await expect(admin.getByRole('heading', { level: 1, name: 'Hojas de reclamación' })).toBeVisible();
    await expect(admin.getByRole('navigation', { name: 'Filtrar por estado' }).getByRole('link', { name: 'Pendientes y respondidas' })).toHaveAttribute('aria-current', 'page');
    // El listado no lleva datos personales (ni nombre, ni documento, ni correo).
    await expect(admin.locator('body')).not.toContainText('Ana Pérez Quispe');
    await expect(admin.locator('body')).not.toContainText('12345678');
    await sinViolacionesDeAccesibilidad(admin);

    await admin.getByRole('link', { name: numero, exact: true }).click();
    await expect(admin.getByRole('heading', { level: 1, name: `Hoja ${numero}` })).toBeVisible();
    await expect(admin.getByRole('note')).toContainText('Esta consulta queda registrada en la auditoría');
    await expect(admin.getByText('Ana Pérez Quispe')).toBeVisible();
    await expect(admin.locator('body')).toContainText('Pendiente de respuesta');
    await sinViolacionesDeAccesibilidad(admin);

    await admin.getByLabel('Respuesta al consumidor').fill('Lamentamos lo ocurrido. Repondremos el libro sin costo en la sucursal de Ayacucho.');
    await admin.getByRole('button', { name: 'Responder' }).click();
    await expect(admin).toHaveURL(new RegExp(`/administracion/reclamaciones/[0-9a-f-]{36}$`));
    await expect(admin.locator('body')).toContainText('Respondida');
    await expect(admin.getByText('Repondremos el libro sin costo')).toBeVisible();
    // Una hoja respondida ya no ofrece responder ni descartar.
    await expect(admin.getByRole('button', { name: 'Responder' })).toHaveCount(0);
    await expect(admin.getByRole('button', { name: 'Descartar por spam' })).toHaveCount(0);
    await contexto.close();
  });

  test('descartar por spam saca la hoja de la lista principal y la deja en «Descartadas» (AC-REC-07, AC-REC-13)', async ({ page, browser }) => {
    const numero = await registrarUnaHoja(page, 'Comprar seguidores baratos en un sitio dudoso.');

    const contexto = await browser.newContext({ ignoreHTTPSErrors: true });
    const admin = await contexto.newPage();
    await ingresarComoPersonal(admin, 'administrador');
    await admin.goto(`${urlDeLaGestion}/administracion/reclamaciones`);
    await admin.getByRole('link', { name: numero, exact: true }).click();
    await admin.getByRole('button', { name: 'Descartar por spam' }).click();
    await expect(admin.locator('body')).toContainText('Descartada');

    await admin.goto(`${urlDeLaGestion}/administracion/reclamaciones`);
    await expect(admin.getByRole('link', { name: numero, exact: true })).toHaveCount(0);
    await admin.goto(`${urlDeLaGestion}/administracion/reclamaciones?estado=descartado`);
    await expect(admin.getByRole('link', { name: numero, exact: true })).toBeVisible();
    await expect(admin.getByRole('navigation', { name: 'Filtrar por estado' }).locator('[aria-current="page"]')).toHaveCount(1);
    await contexto.close();
  });

  test('el texto de un tercero se ve como texto en el detalle de la administración (XSS almacenado, AC-REC-04)', async ({ page, browser }) => {
    const numero = await registrarUnaHoja(page, '<script>window.__xss = 1</script><img src=x onerror="window.__xss = 2"> pedido dañado');

    const contexto = await browser.newContext({ ignoreHTTPSErrors: true });
    const admin = await contexto.newPage();
    const errores: string[] = [];
    admin.on('pageerror', (e) => errores.push(e.message));
    await ingresarComoPersonal(admin, 'administrador');
    await admin.goto(`${urlDeLaGestion}/administracion/reclamaciones`);
    await admin.getByRole('link', { name: numero, exact: true }).click();
    await expect(admin.locator('dd', { hasText: '<script>window.__xss = 1</script>' })).toBeVisible();
    expect(await admin.evaluate(() => (window as unknown as { __xss?: number }).__xss)).toBeUndefined();
    expect(errores).toEqual([]);
    await contexto.close();
  });
});

test.describe('Quien no es de la administración no gestiona hojas (AC-REC-10)', () => {
  test.skip(!clave, 'Falta BABELHUB__Sembrador__Clave (entorno E2E local con el sembrador de sesiones)');

  test('un encargado de sucursal recibe 403 en el listado', async ({ page }) => {
    await ingresarComoPersonal(page, 'encargado', 'ayacucho');
    const respuesta = await page.goto(`${urlDeLaGestion}/administracion/reclamaciones`);
    expect(respuesta!.status()).toBe(403);
    await expect(page.getByRole('heading', { level: 1, name: 'Hojas de reclamación' })).toHaveCount(0);
  });

  test('un visitante sin sesión es llevado al ingreso del personal', async ({ page }) => {
    await page.goto(`${urlDeLaGestion}/administracion/reclamaciones`);
    await expect(page).toHaveURL(/\/ingreso/);
  });

  test('en el host de la tienda la gestión no existe, ni con la sesión de un cliente', async ({ page }) => {
    await ingresarComoClienteNuevo(page);
    const respuesta = await page.goto('/administracion/reclamaciones');
    expect(respuesta!.status()).toBe(404);
  });
});
