import { test, expect, type Page } from '@playwright/test';
import { clave, conOrigenPropio, ingresarComoClienteNuevo, ingresarComoPersonal, sinViolacionesDeAccesibilidad, urlDeLaGestion } from '../pedidos/ayudas';

// Las pantallas de administración de `web-base` (WEB-2, T30 y T31; AC-WEB-25, AC-WEB-27, AC-WEB-29) sobre el host de la gestión
// (`E2E_TLS=1 E2E_HOST=gestion entorno/arrancar-host.sh ecommerce`, https://localhost:8444, que comparte la base con la tienda): usuarios y roles, solicitudes sobre
// datos personales, auditoría unificada y alertas de seguridad. Las sesiones salen del sembrador de ese host (`administrador`, `encargado`).
// Datos ficticios; `entorno/sembrar-datos.sh --limpiar` antes de cada corrida. Chromium de escritorio y de teléfono y WebKit; Firefox es informativo.

test.describe.configure({ mode: 'serial' });
test.setTimeout(90_000);

async function sinDesplazamientoHorizontal(page: Page) {
  expect(await page.evaluate(() => document.documentElement.scrollWidth > document.documentElement.clientWidth), 'la página no se desplaza en horizontal').toBe(false);
}

test.describe('Administración central: usuarios, solicitudes, auditoría y alertas', () => {
  test.skip(!clave, 'Falta BABELHUB__Sembrador__Clave (entorno E2E local con el sembrador de sesiones)');

  test('cada pantalla existe, es accesible (AA), no se desplaza y respeta el tema oscuro (AC-WEB-25, AC-WEB-29)', async ({ page }) => {
    await ingresarComoPersonal(page, 'administrador');
    for (const [ruta, titulo] of [
      ['/administracion/usuarios', 'Usuarios y roles'],
      ['/administracion/solicitudes', 'Solicitudes sobre datos personales'],
      ['/administracion/auditoria', 'Auditoría'],
      ['/administracion/alertas', 'Alertas de seguridad'],
    ] as const) {
      const respuesta = await page.goto(`${urlDeLaGestion}${ruta}`);
      expect(respuesta!.status(), `${ruta} responde`).toBe(200);
      await expect(page.getByRole('heading', { level: 1, name: titulo })).toBeVisible();
      await sinDesplazamientoHorizontal(page);
      await sinViolacionesDeAccesibilidad(page);
    }

    await page.context().addCookies([{ name: 'bh-tema', value: 'babelhub-dark', url: urlDeLaGestion }]);
    for (const ruta of ['/administracion/usuarios', '/administracion/auditoria']) {
      await page.goto(`${urlDeLaGestion}${ruta}`);
      await expect(page.locator('html')).toHaveAttribute('data-theme', 'babelhub-dark');
      await sinViolacionesDeAccesibilidad(page);
    }
  });

  test('crear un usuario muestra la contraseña temporal una sola vez y la auditoría lo registra sin ella (AC-WEB-29, AC-WEB-25)', async ({ page }) => {
    await ingresarComoPersonal(page, 'administrador');
    await page.goto(`${urlDeLaGestion}/administracion/usuarios`);

    const correo = `e2e-logistica-${Date.now()}-${Math.floor(Math.random() * 1e6)}@example.test`;
    await page.getByLabel('Nombre completo').fill('Luis Ficticio Logística');
    await page.getByLabel('Correo electrónico').fill(correo);
    await page.locator('form[action$="/administracion/usuarios/crear"]').getByLabel('Rol').selectOption('Logistica');
    const respuesta = page.waitForResponse((r) => r.url().endsWith('/administracion/usuarios/crear') && r.request().method() === 'POST');
    await page.getByRole('button', { name: 'Crear y mostrar la contraseña temporal' }).click();

    // La respuesta no se guarda en el navegador y trae la contraseña temporal una sola vez.
    expect((await respuesta).headers()['cache-control']).toContain('no-store');
    await expect(page.getByRole('status')).toContainText('Creamos el usuario.');
    await expect(page.getByRole('alert').filter({ hasText: 'Contraseña temporal' })).toBeVisible();
    const resultado = page.getByRole('region', { name: 'Resultado' });
    await expect(resultado).toContainText(correo);
    const usuarioId = (await resultado.locator('dt:has-text("Identificador") + dd').textContent())!.trim();
    expect(usuarioId).toMatch(/^[0-9a-f-]{36}$/);
    const temporal = (await page.getByRole('alert').filter({ hasText: 'Contraseña temporal' }).locator('strong').textContent())!.trim();
    expect(temporal.length).toBeGreaterThanOrEqual(12);
    await sinViolacionesDeAccesibilidad(page);

    // Al volver a la pantalla la contraseña ya no está en ninguna parte.
    await page.goto(`${urlDeLaGestion}/administracion/usuarios`);
    await expect(page.locator('body')).not.toContainText(temporal);
    await expect(page.locator('body')).not.toContainText('Contraseña temporal (solo se muestra ahora');

    // La auditoría de ese usuario tiene su registro y no guarda la contraseña ni el correo.
    await page.goto(`${urlDeLaGestion}/administracion/auditoria`);
    await page.getByLabel('Usuario (identificador)').fill(usuarioId);
    await page.getByRole('button', { name: 'Filtrar' }).click();
    await expect(page.getByRole('table', { name: 'Registros de auditoría' }).getByRole('row')).not.toHaveCount(1);
    await expect(page.locator('body')).not.toContainText(temporal);
    await expect(page.locator('body')).not.toContainText(correo);
  });

  test('el texto de un tercero se muestra siempre como texto en la auditoría (XSS almacenado, AC-WEB-25)', async ({ page }) => {
    const errores: string[] = [];
    page.on('pageerror', (e) => errores.push(e.message));
    await ingresarComoPersonal(page, 'administrador');
    await page.goto(`${urlDeLaGestion}/administracion/auditoria`);
    await page.getByLabel('Texto').fill('<script>window.__xss = 1</script>');
    await page.getByRole('button', { name: 'Filtrar' }).click();
    expect(await page.evaluate(() => (window as unknown as { __xss?: number }).__xss)).toBeUndefined();
    expect(errores).toEqual([]);
  });

  test('una solicitud del cliente llega a la administración con su fecha límite y se responde (AC-WEB-33)', async ({ page, browser }) => {
    // El cliente presenta una solicitud de acceso en la tienda (con su identificador, para reconocerla en la lista de todos).
    await conOrigenPropio(page);
    const sembrado = await page.request.post('/desarrollo/sesion', { headers: { 'X-Sembrador-De-Desarrollo': clave! }, data: { rol: 'cliente' } });
    expect(sembrado.ok(), 'el sembrador de sesiones responde').toBeTruthy();
    const titular = sembrado.headers()['x-usuario-id'];
    expect(titular).toMatch(/^[0-9a-f-]{36}$/);
    await page.context().addCookies((await sembrado.json()).cookies);
    await page.goto('/cuenta/solicitudes');
    await page.getByLabel('Tipo de solicitud').selectOption('Acceso');
    await page.getByRole('button', { name: 'Presentar la solicitud' }).click();
    await expect(page.getByRole('status')).toContainText('Presentamos tu solicitud');

    // La administración la ve en su lista, ordenada por fecha límite, la reconoce por su titular (otras corridas dejan solicitudes de otras cuentas) y la responde.
    const contexto = await browser.newContext({ ignoreHTTPSErrors: true });
    const admin = await contexto.newPage();
    await ingresarComoPersonal(admin, 'administrador');
    await admin.goto(`${urlDeLaGestion}/administracion/solicitudes`);
    await expect(admin.getByRole('columnheader', { name: 'Fecha límite' })).toBeVisible();
    await sinViolacionesDeAccesibilidad(admin);
    const rutas = await admin.getByRole('link', { name: 'Abrir' }).evaluateAll((enlaces) => enlaces.map((e) => (e as HTMLAnchorElement).getAttribute('href')!));
    let encontrada = false;
    for (const ruta of rutas) {
      await admin.goto(`${urlDeLaGestion}${ruta}`);
      if (((await admin.locator('dt:has-text("Titular") + dd').textContent()) ?? '').trim() === titular) {
        encontrada = true;
        break;
      }
    }
    expect(encontrada, 'la solicitud del cliente está en la lista de la administración').toBe(true);
    await expect(admin.getByRole('heading', { level: 2, name: 'Responder' })).toBeVisible();

    await admin.getByLabel('Resolución').selectOption('Atendida');
    await admin.getByLabel('Respuesta al titular').fill('Te enviamos tus datos por el canal acordado.');
    await admin.getByRole('button', { name: 'Registrar la respuesta' }).click();
    await expect(admin.getByRole('status')).toContainText('Registramos la respuesta.');
    await contexto.close();
  });

  test('las alertas de seguridad se listan con su estado y marcar una como revisada pide una acción explícita (AC-WEB-27)', async ({ page }) => {
    await ingresarComoPersonal(page, 'administrador');
    await page.goto(`${urlDeLaGestion}/administracion/alertas`);
    await expect(page.getByRole('heading', { level: 1, name: 'Alertas de seguridad' })).toBeVisible();
    const marcar = page.getByRole('button', { name: 'Marcar como revisada' });
    if ((await marcar.count()) === 0) {
      // Sin alertas del umbral, la pantalla lo dice con un estado accesible en lugar de una tabla vacía.
      await expect(page.locator('body')).toContainText(/No hay alertas|Sin alertas|todavía no/i);
    } else {
      await marcar.first().click();
      await expect(page.getByRole('status')).toContainText('Marcamos la alerta como revisada.');
    }
  });
});

test.describe('Quien no es de la administración central no entra a estas pantallas (AC-WEB-25, AC-WEB-29)', () => {
  test.skip(!clave, 'Falta BABELHUB__Sembrador__Clave (entorno E2E local con el sembrador de sesiones)');

  test('un encargado de sucursal recibe 403 en las cuatro pantallas', async ({ page }) => {
    await ingresarComoPersonal(page, 'encargado', 'ayacucho');
    for (const ruta of ['/administracion/usuarios', '/administracion/solicitudes', '/administracion/auditoria', '/administracion/alertas']) {
      const respuesta = await page.goto(`${urlDeLaGestion}${ruta}`);
      expect(respuesta!.status(), `${ruta} para un encargado`).toBe(403);
      await expect(page.locator('body')).not.toContainText('Crear un usuario del personal');
    }
  });

  test('un visitante sin sesión es llevado al ingreso del personal', async ({ page }) => {
    await page.goto(`${urlDeLaGestion}/administracion/usuarios`);
    await expect(page).toHaveURL(/\/ingreso/);
  });

  test('la administración no existe en el host de la tienda (404, también con la sesión de un cliente)', async ({ page }) => {
    await ingresarComoClienteNuevo(page);
    for (const ruta of ['/administracion/usuarios', '/administracion/auditoria']) {
      const respuesta = await page.goto(ruta);
      expect(respuesta!.status(), `${ruta} en la tienda`).toBe(404);
    }
  });
});
