import { test, expect, type Page } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';

// Recorrido de compra de punta a punta (ecommerce-carrito-checkout T36 y ecommerce-pedidos T41): un cliente recién sembrado agrega un libro impreso al carrito, hace el checkout con recojo
// en sucursal, paga con el simulador de la pasarela, y el pedido aparece en «Mis pedidos» con su detalle; también cancela su pedido y otra cuenta no lo ve. Todo con datos ficticios del
// entorno local (`entorno/sembrar-datos.sh --limpiar` antes de cada corrida) y la sesión del sembrador de desarrollo (`POST /desarrollo/sesion`, solo Debug + Development en la propia máquina).
//
//   BABELHUB__Sembrador__Clave   la clave del sembrador (la trae el archivo de entorno de `entorno/preparar-entorno.sh`)
//   E2E_BASE_URL                 https://localhost:8443 (el host del E-commerce arrancado con E2E_TLS=1)
const clave = process.env.BABELHUB__Sembrador__Clave ?? process.env.E2E_SEMBRADOR_CLAVE;

// Cada recorrido llega desde su propio origen (el borde real pone X-Origen-Cliente): el Inventario limita las reservas a 10 por hora por origen (SEL-19) y todos los recorridos salen de esta máquina.
// El host local confía en 127.0.0.1 como proxy (BABELHUB__Ecommerce__ProxiesConocidos del archivo de entorno); las direcciones son del rango de documentación (RFC 5737), públicas para el borde.
async function conOrigenPropio(page: Page) {
  const ip = `${Math.random() < 0.5 ? '203.0.113' : '198.51.100'}.${1 + Math.floor(Math.random() * 253)}`;
  await page.context().setExtraHTTPHeaders({ 'X-Origen-Cliente': ip });
}

// Un cliente nuevo, creado por el camino real de ingreso; su cookie de sesión se pone en el contexto del navegador.
async function ingresarComoClienteNuevo(page: Page) {
  await conOrigenPropio(page);
  const respuesta = await page.request.post('/desarrollo/sesion', { headers: { 'X-Sembrador-De-Desarrollo': clave! }, data: { rol: 'cliente' } });
  expect(respuesta.ok(), 'el sembrador de sesiones responde').toBeTruthy();
  const estado = await respuesta.json();
  await page.context().addCookies(estado.cookies);
}

// La ficha del catálogo aún no agrega al carrito (pantalla de web-catalogo): el libro se agrega con la misma petición que enviará su botón, desde el navegador (con su Origin y el token).
async function agregarAlCarrito(page: Page, edicionId: string, cantidad: number) {
  await page.goto('/catalogo');
  const token = await page.locator('meta[name=csrf-token]').getAttribute('content');
  const estado = await page.evaluate(
    async ([t, id, n]) => (await fetch('/carrito/lineas', { method: 'POST', headers: { RequestVerificationToken: t!, 'Content-Type': 'application/x-www-form-urlencoded' }, body: `edicionId=${id}&cantidad=${n}` })).status,
    [token, edicionId, String(cantidad)] as const,
  );
  expect(estado, 'agregar al carrito').toBeLessThan(400);
}

const EDICION_IMPRESA_DEL_ZORRO = 'b0000000-0000-0000-0000-000000000011';

// Carrito → checkout con recojo en Ayacucho → pago aprobado del simulador. Termina en la pantalla de estado del pago.
async function comprarUnLibroConRecojo(page: Page) {
  await agregarAlCarrito(page, EDICION_IMPRESA_DEL_ZORRO, 1);
  await page.goto('/carrito');
  await expect(page.getByRole('heading', { level: 1, name: 'Tu carrito' })).toBeVisible();
  await expect(page.getByText('El zorro y el cóndor del Ande', { exact: true }).first()).toBeVisible();
  await expect(page.getByText('Hay stock en la red')).toBeVisible();
  await page.getByRole('button', { name: 'Continuar con la compra' }).click();

  await expect(page).toHaveURL(/\/checkout$/);
  await page.getByLabel('Sucursal de recojo').selectOption({ label: 'Sucursal Ayacucho (prueba)' });
  await page.getByRole('button', { name: 'Elegir recojo' }).click();
  await expect(page.getByRole('heading', { name: 'Tus datos y aceptaciones' })).toBeVisible();
  for (const casilla of await page.getByRole('group', { name: 'Para continuar, acepta:' }).getByRole('checkbox').all()) {
    await casilla.check();
  }
  await page.getByRole('button', { name: /^Ir al pago/ }).click();

  await expect(page).toHaveURL(/\/checkout\/pago/);
  await expect(page.getByRole('heading', { name: 'Pagar tu compra' })).toBeVisible();
  await page.getByLabel(/Identificador del medio de pago/).fill('tok_aprobado');
  await page.getByRole('button', { name: /^Pagar S\// }).click();
  await expect(page).toHaveURL(/\/checkout\/pago\/estado/);
}

// El pedido lo crea Pagos al confirmar la compra aprobada (en el mismo paso o por la reanudación del trabajo): se espera con recargas, sin dormir a ciegas.
async function esperarElPedido(page: Page) {
  await expect
    .poll(
      async () => {
        await page.goto('/cuenta/pedidos');
        return await page.getByRole('link', { name: /^Ver el pedido/ }).count();
      },
      { timeout: 90_000, intervals: [1_000, 2_000, 3_000] },
    )
    .toBeGreaterThan(0);
}

async function sinViolacionesDeAccesibilidad(page: Page) {
  const resultado = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa']).analyze();
  expect(resultado.violations.map((v) => `${v.id}: ${v.nodes.map((n) => `${n.target.join(" ")} :: ${n.any[0]?.message ?? ""}`).join(" | ")}`)).toEqual([]);
}

test.describe.configure({ mode: 'serial' });

test.describe('Compra y pedido (carrito, checkout, pago y Mis pedidos)', () => {
  test.skip(!clave, 'Falta BABELHUB__Sembrador__Clave (entorno E2E local con el sembrador de sesiones)');

  test('CAR-15, PED-04, PED-07: la compra con recojo deja su pedido confirmado en Mis pedidos, con su detalle y sin contacto', async ({ page }, info) => {
    await ingresarComoClienteNuevo(page);
    await comprarUnLibroConRecojo(page);
    await expect(page.getByText('Compra confirmada')).toBeVisible();

    await esperarElPedido(page);
    await expect(page.getByRole('heading', { level: 1, name: 'Mis pedidos' })).toBeVisible();
    const fila = page.getByRole('row').filter({ hasText: 'Recojo en sucursal' });
    await expect(fila).toHaveCount(1);
    // El estado se dice con texto (y un icono), no solo con color; el total sale en soles.
    await expect(fila).toContainText('Confirmado');
    await expect(fila).toContainText('S/');
    await page.screenshot({ path: info.outputPath('mis-pedidos.png'), fullPage: true });
    await sinViolacionesDeAccesibilidad(page);

    await fila.getByRole('link', { name: /^Ver el pedido/ }).click();
    await expect(page).toHaveURL(/\/cuenta\/pedidos\/[0-9a-f-]{36}$/);
    await expect(page.getByText('El zorro y el cóndor del Ande', { exact: true }).first()).toBeVisible();
    await expect(page.getByText('Libro impreso')).toBeVisible();
    await expect(page.getByRole('complementary', { name: 'Resumen del pedido' })).toContainText('Recojo en sucursal');
    // Un pedido de recojo no tiene contacto de entrega ni seguimiento de envío; sí se puede cancelar mientras todo está confirmado.
    await expect(page.getByText('Entrega a domicilio')).toHaveCount(0);
    await expect(page.getByText('Cancelar el pedido', { exact: true })).toBeVisible();
    await page.screenshot({ path: info.outputPath('detalle-del-pedido.png'), fullPage: true });
    await sinViolacionesDeAccesibilidad(page);
  });

  test('PED-17, PED-24: el titular cancela su pedido confirmado y queda anulado con su reembolso', async ({ page }, info) => {
    await ingresarComoClienteNuevo(page);
    await comprarUnLibroConRecojo(page);
    await esperarElPedido(page);
    await page.getByRole('link', { name: /^Ver el pedido/ }).first().click();

    await page.getByText('Cancelar el pedido', { exact: true }).click();
    await page.getByRole('button', { name: 'Sí, cancelar el pedido' }).click();

    await expect(page.getByText('El pedido se canceló y el reembolso quedó registrado.')).toBeVisible();
    await expect(page.getByText('Anulado').first()).toBeVisible();
    // Ya no se ofrece cancelar de nuevo.
    await expect(page.getByText('Cancelar el pedido', { exact: true })).toHaveCount(0);
    await page.screenshot({ path: info.outputPath('pedido-cancelado.png'), fullPage: true });
  });

  test('PED-41: otra cuenta no ve el pedido: su lista está vacía y el detalle responde «no encontrado» sin pistas', async ({ page, browser }, info) => {
    await ingresarComoClienteNuevo(page);
    await comprarUnLibroConRecojo(page);
    await esperarElPedido(page);
    const rutaDelPedido = (await page.getByRole('link', { name: /^Ver el pedido/ }).first().getAttribute('href'))!;

    const otro = await browser.newContext({ ...info.project.use, ignoreHTTPSErrors: true });
    const otraPagina = await otro.newPage();
    await ingresarComoClienteNuevo(otraPagina);
    await otraPagina.goto('/cuenta/pedidos');
    await expect(otraPagina.getByText('Aún no hay pedidos')).toBeVisible();
    const respuesta = await otraPagina.goto(rutaDelPedido);
    expect(respuesta?.status()).toBe(404);
    await expect(otraPagina.getByRole('heading', { level: 2, name: 'No encontramos ese pedido' })).toBeVisible();
    await otro.close();
  });
});
