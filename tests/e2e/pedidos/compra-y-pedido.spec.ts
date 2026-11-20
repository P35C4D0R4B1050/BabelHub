import { test, expect } from '@playwright/test';
import { clave, comprarUnLibroConRecojo, esperarElPedido, ingresarComoClienteNuevo, sinViolacionesDeAccesibilidad } from './ayudas';

// Recorrido de compra de punta a punta (ecommerce-carrito-checkout T36 y ecommerce-pedidos T41): un cliente recién sembrado agrega un libro impreso al carrito, hace el checkout con recojo
// en sucursal, paga con el simulador de la pasarela, y el pedido aparece en «Mis pedidos» con su detalle; también cancela su pedido y otra cuenta no lo ve. Todo con datos ficticios del
// entorno local (`entorno/sembrar-datos.sh --limpiar` antes de cada corrida) y la sesión del sembrador de desarrollo (`POST /desarrollo/sesion`, solo Debug + Development en la propia máquina).
// Las ayudas (sesión, carrito, checkout, pago) están en `ayudas.ts` y las usan también los recorridos del personal.

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
