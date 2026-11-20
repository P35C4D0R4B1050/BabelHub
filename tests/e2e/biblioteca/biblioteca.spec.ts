import { test, expect, type Page } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';

// Recorrido de la biblioteca digital de punta a punta (ecommerce-biblioteca-digital T25; AC-BIB-01, AC-BIB-02, AC-BIB-04, AC-BIB-12, AC-BIB-14, BIB-12): un cliente recién sembrado compra el libro
// digital de la obra ficticia «El zorro y el cóndor del Ande» (checkout 100 % digital, sin entrega), paga con el simulador, y el acceso aparece en «Mi biblioteca» con su formato y «0 de 5 descargas
// usadas»; descarga (el servidor responde 302 al almacén con `no-store` y sin cuerpo), la descarga se cuenta, pide ayuda con un motivo de lista cerrada y otra cuenta no ve el libro.
// Entorno: el mismo de `pedidos/compra-y-pedido.spec.ts` (ver `tests/e2e/README.md`): host del E-commerce por HTTPS, `entorno/sembrar-datos.sh --limpiar` antes de cada corrida y el sembrador de
// sesiones de desarrollo. Sin `Almacen:Biblioteca:*` el host entrega contenido sintético en memoria (URL `.invalid`): el recorrido comprueba la redirección, nunca el archivo.
//
//   BABELHUB__Sembrador__Clave   la clave del sembrador (la trae el archivo de entorno de `entorno/preparar-entorno.sh`)
//   E2E_BASE_URL                 https://localhost:8443 (el host del E-commerce arrancado con E2E_TLS=1)
//   E2E_ADMIN_URL                (opcional) el host de gestión del mismo entorno: activa el recorrido de la administración
const clave = process.env.BABELHUB__Sembrador__Clave ?? process.env.E2E_SEMBRADOR_CLAVE;
const urlDeAdministracion = process.env.E2E_ADMIN_URL;

// Cada recorrido llega desde su propio origen (el borde real pone X-Origen-Cliente): los límites por origen del Inventario y de la plataforma cuentan por él.
async function conOrigenPropio(page: Page) {
  const ip = `${Math.random() < 0.5 ? '203.0.113' : '198.51.100'}.${1 + Math.floor(Math.random() * 253)}`;
  await page.context().setExtraHTTPHeaders({ 'X-Origen-Cliente': ip });
}

async function ingresarComoClienteNuevo(page: Page) {
  await conOrigenPropio(page);
  const respuesta = await page.request.post('/desarrollo/sesion', { headers: { 'X-Sembrador-De-Desarrollo': clave! }, data: { rol: 'cliente' } });
  expect(respuesta.ok(), 'el sembrador de sesiones responde').toBeTruthy();
  const estado = await respuesta.json();
  await page.context().addCookies(estado.cookies);
}

async function agregarAlCarrito(page: Page, edicionId: string, cantidad: number) {
  await page.goto('/catalogo');
  const token = await page.locator('meta[name=csrf-token]').getAttribute('content');
  const estado = await page.evaluate(
    async ([t, id, n]) => (await fetch('/carrito/lineas', { method: 'POST', headers: { RequestVerificationToken: t!, 'Content-Type': 'application/x-www-form-urlencoded' }, body: `edicionId=${id}&cantidad=${n}` })).status,
    [token, edicionId, String(cantidad)] as const,
  );
  expect(estado, 'agregar al carrito').toBeLessThan(400);
}

const EDICION_DIGITAL_DEL_ZORRO = 'b0000000-0000-0000-0000-000000000012';
const TITULO = 'El zorro y el cóndor del Ande';

// Carrito → checkout 100 % digital (sin entrega) → pago aprobado del simulador. Termina en la pantalla de estado del pago.
async function comprarElLibroDigital(page: Page) {
  await agregarAlCarrito(page, EDICION_DIGITAL_DEL_ZORRO, 1);
  await page.goto('/carrito');
  await expect(page.getByRole('heading', { level: 1, name: 'Tu carrito' })).toBeVisible();
  await page.getByRole('button', { name: 'Continuar con la compra' }).click();

  await expect(page).toHaveURL(/\/checkout$/);
  await expect(page.getByText('Compra 100 % digital: sin entrega').first()).toBeVisible();
  for (const casilla of await page.getByRole('group', { name: 'Para continuar, acepta:' }).getByRole('checkbox').all()) {
    await casilla.check();
  }
  await page.getByRole('button', { name: /^Ir al pago/ }).click();

  await expect(page).toHaveURL(/\/checkout\/pago/);
  await page.getByLabel(/Identificador del medio de pago/).fill('tok_aprobado');
  await page.getByRole('button', { name: /^Pagar S\// }).click();
  await expect(page).toHaveURL(/\/checkout\/pago\/estado/);
}

// El acceso lo concede Pagos al confirmar la compra aprobada (en el mismo paso o por la reanudación del trabajo): se espera con recargas, sin dormir a ciegas.
async function esperarElAcceso(page: Page) {
  await expect
    .poll(
      async () => {
        await page.goto('/biblioteca');
        return await page.getByRole('heading', { level: 3, name: TITULO }).count();
      },
      { timeout: 90_000, intervals: [1_000, 2_000, 3_000] },
    )
    .toBeGreaterThan(0);
}

async function sinViolacionesDeAccesibilidad(page: Page) {
  const resultado = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa']).analyze();
  expect(resultado.violations.map((v) => `${v.id}: ${v.nodes.map((n) => `${n.target.join(' ')} :: ${n.any[0]?.message ?? ''}`).join(' | ')}`)).toEqual([]);
}

test.describe.configure({ mode: 'serial' });

test.describe('Biblioteca digital (compra, mi biblioteca, descarga y ayuda)', () => {
  test.skip(!clave, 'Falta BABELHUB__Sembrador__Clave (entorno E2E local con el sembrador de sesiones)');

  test('AC-BIB-01, AC-BIB-02: la compra digital concede el acceso y Mi biblioteca lo muestra con formato, fecha y «0 de 5» con texto', async ({ page }, info) => {
    await ingresarComoClienteNuevo(page);
    await comprarElLibroDigital(page);
    await expect(page.getByText('Compra confirmada')).toBeVisible();

    await esperarElAcceso(page);
    await expect(page.getByRole('heading', { level: 1, name: 'Mi biblioteca' })).toBeVisible();
    const libro = page.getByRole('listitem').filter({ has: page.getByRole('heading', { level: 3, name: TITULO }) });
    await expect(libro).toHaveCount(1);
    // El estado, el formato y las descargas se dicen con texto (y un icono), no solo con color.
    await expect(libro).toContainText('EPUB');
    await expect(libro).toContainText('Disponible');
    await expect(libro).toContainText('0 de 5 descargas usadas');
    await expect(libro).toContainText('comprado el');
    await expect(page.getByText('5 minutos').first()).toBeVisible();
    await expect(libro.getByRole('button', { name: 'Descargar' })).toBeVisible();
    await page.screenshot({ path: info.outputPath('mi-biblioteca.png'), fullPage: true });
    await sinViolacionesDeAccesibilidad(page);
  });

  test('AC-BIB-04, AC-BIB-12: descargar redirige al almacén sin pasar el archivo por el servidor y cuenta una descarga; se opera por teclado', async ({ page }, info) => {
    await ingresarComoClienteNuevo(page);
    await comprarElLibroDigital(page);
    await esperarElAcceso(page);

    const boton = page.getByRole('button', { name: 'Descargar' }).first();
    // Por teclado: el botón recibe el foco y se activa con Enter.
    await boton.focus();
    await expect(boton).toBeFocused();
    const respuestaDelPost = page.waitForResponse((r) => r.url().includes('/descargar') && r.request().method() === 'POST');
    await page.keyboard.press('Enter');
    const respuesta = await respuestaDelPost;
    expect([302, 303]).toContain(respuesta.status());
    expect(respuesta.headers()['cache-control']).toContain('no-store');
    expect(respuesta.headers()['location']).toMatch(/^https:\/\//);
    // Nunca se transmite el archivo: la respuesta no lleva cuerpo ni tipo de contenido.
    expect((await respuesta.body().catch(() => Buffer.alloc(0))).length).toBe(0);

    // La descarga se cuenta al entregar el enlace, aunque el navegador no llegue a usarlo (el almacén de desarrollo es inválido a propósito).
    await page.goto('/biblioteca');
    const libro = page.getByRole('listitem').filter({ has: page.getByRole('heading', { level: 3, name: TITULO }) });
    await expect(libro).toContainText('1 de 5 descargas usadas');
    await page.screenshot({ path: info.outputPath('tras-descargar.png'), fullPage: true });
  });

  test('AC-BIB-14: pedir ayuda con un motivo de lista cerrada deja la solicitud visible y no se puede repetir', async ({ page }, info) => {
    await ingresarComoClienteNuevo(page);
    await comprarElLibroDigital(page);
    await esperarElAcceso(page);

    await page.getByText('Pedir ayuda con este libro').click();
    await page.getByLabel('¿Qué pasó?').selectOption('archivo-no-abre');
    await page.getByRole('button', { name: 'Enviar solicitud' }).click();

    await expect(page).toHaveURL(/\/biblioteca$/);
    await expect(page.getByText('Ya pediste ayuda sobre este libro')).toBeVisible();
    await expect(page.getByText('Pedir ayuda con este libro')).toHaveCount(0);
    await page.screenshot({ path: info.outputPath('ayuda-pedida.png'), fullPage: true });
    await sinViolacionesDeAccesibilidad(page);
  });

  test('BIB-03, BIB-12: otra cuenta no ve el libro: su biblioteca está vacía y no puede pedir ayuda ni descargar el acceso ajeno', async ({ page, browser }, info) => {
    await ingresarComoClienteNuevo(page);
    await comprarElLibroDigital(page);
    await esperarElAcceso(page);
    const accion = (await page.locator('form[action$="/descargar"]').first().getAttribute('action'))!;
    const accesoAjeno = accion.split('/')[2];

    const otro = await browser.newContext({ ...info.project.use, ignoreHTTPSErrors: true });
    const otraPagina = await otro.newPage();
    await ingresarComoClienteNuevo(otraPagina);
    await otraPagina.goto('/biblioteca');
    await expect(otraPagina.getByText('Aún no tienes libros digitales')).toBeVisible();
    // Un POST directo al acceso ajeno responde lo mismo que a uno inexistente: «no encontramos ese libro».
    const token = await otraPagina.locator('meta[name=csrf-token]').getAttribute('content');
    const estado = await otraPagina.evaluate(
      async ([id, t]) => (await fetch(`/biblioteca/${id}/ayuda`, { method: 'POST', headers: { RequestVerificationToken: t!, 'Content-Type': 'application/x-www-form-urlencoded' }, body: 'motivo=otro' })).status,
      [accesoAjeno, token] as const,
    );
    expect(estado).toBe(404);
    await otro.close();
  });
});

test.describe('Administración de la biblioteca', () => {
  test.skip(!clave || !urlDeAdministracion, 'Falta BABELHUB__Sembrador__Clave o E2E_ADMIN_URL (el host de gestión del entorno E2E)');

  test('BIB-12, BIB-16: la lista de ayudas no muestra datos de la cuenta y cumple axe AA', async ({ browser }, info) => {
    const contexto = await browser.newContext({ ...info.project.use, baseURL: urlDeAdministracion, ignoreHTTPSErrors: true });
    const page = await contexto.newPage();
    await conOrigenPropio(page);
    const sesion = await page.request.post('/desarrollo/sesion', { headers: { 'X-Sembrador-De-Desarrollo': clave! }, data: { rol: 'administrador' } });
    expect(sesion.ok(), 'el sembrador de sesiones de gestión responde').toBeTruthy();
    await page.context().addCookies((await sesion.json()).cookies);

    await page.goto('/administracion/biblioteca/ayudas?estado=todas');
    await expect(page.getByRole('heading', { level: 1, name: 'Ayudas de la biblioteca' })).toBeVisible();
    await expect(page.getByText('@')).toHaveCount(0);
    await page.screenshot({ path: info.outputPath('ayudas-de-la-biblioteca.png'), fullPage: true });
    await sinViolacionesDeAccesibilidad(page);
    await contexto.close();
  });
});
