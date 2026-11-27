import { test, expect, type Page } from '@playwright/test';
import {
  aceptarLosTextosYPagar,
  agregarAlCarrito,
  AYACUCHO,
  clave,
  EDICION_IMPRESA_DEL_RIO,
  EDICION_IMPRESA_DEL_ZORRO,
  ingresarComoClienteNuevo,
  ingresarComoPersonal,
  LIMA,
  rutaDelPrimerPedido,
  sinViolacionesDeAccesibilidad,
  urlDeLaGestion,
} from './ayudas';

// Una compra de dos sucursales de punta a punta (ecommerce-carrito-checkout T49; CAR-23; AC-CAR-21, AC-PED-40): a domicilio cada sucursal cobra su envío y el pedido dice quién atiende cada línea; con una sucursal ya en
// reparto, la anulación alcanza a la que no envió (la devolución de su envío se comprueba en el servidor: `CompraDeDosSucursalesTests`). Mismo entorno que `pedidos/` (`entorno/sembrar-datos.sh --limpiar`, tienda en :8443 y
// gestión en :8444). Datos ficticios.

const ZORRO = 'El zorro y el cóndor del Ande';
const RIO = 'Cuentos del río Pampas';

async function prepararSolo(page: Page, titulo: string) {
  await page.getByRole('checkbox', { name: `Elegir ${titulo}` }).check();
  await page.getByRole('button', { name: 'Preparar' }).click();
  await expect(page.getByText('Las líneas pasaron a preparación.')).toBeVisible();
  await page.getByRole('checkbox', { name: `Elegir ${titulo}` }).check();
  await page.getByRole('button', { name: 'Marcar listas' }).click();
  await expect(page.getByText('Las líneas se marcaron como listas.')).toBeVisible();
}

test.describe('Compra de dos sucursales', () => {
  test.skip(!clave, 'Falta BABELHUB__Sembrador__Clave (entorno E2E local con el sembrador de sesiones)');
  test.setTimeout(150_000);

  test('AC-CAR-21: a domicilio cobra el envío de cada sucursal y, con una en reparto, la anulación alcanza a la que no envió', async ({ page }, info) => {
    await ingresarComoClienteNuevo(page);
    await agregarAlCarrito(page, EDICION_IMPRESA_DEL_ZORRO, 1, AYACUCHO);
    await agregarAlCarrito(page, EDICION_IMPRESA_DEL_RIO, 1, LIMA);
    await page.getByRole('button', { name: 'Continuar con la compra' }).click();
    await expect(page).toHaveURL(/\/checkout$/);
    await page.getByLabel('Nombre completo').fill('Rosa Quispe Ficticia');
    await page.getByLabel('Celular de contacto').fill('987654321');
    await page.getByLabel('Dirección de entrega').fill('Jr. Ficticio 123, Ayacucho');
    await page.getByLabel('Región').fill('ayacucho');
    await page.getByRole('button', { name: 'Elegir domicilio' }).click();
    await expect(page.getByRole('heading', { name: 'Tus datos y aceptaciones' })).toBeVisible();
    await aceptarLosTextosYPagar(page);

    const ruta = await rutaDelPrimerPedido(page);
    const id = ruta.split('/').pop()!;
    await page.goto(ruta);
    await expect(page.getByText(`Atiende: ${AYACUCHO.nombre}`)).toBeVisible();
    await expect(page.getByText(`Atiende: ${LIMA.nombre}`)).toBeVisible();
    const resumen = page.getByRole('complementary', { name: 'Resumen del pedido' });
    await expect(resumen).toContainText(/60[.,]00/);
    await expect(resumen).toContainText(/143[.,]50/);

    // Logística prepara solo la línea de Lima, genera su guía y la pasa a reparto.
    await ingresarComoPersonal(page, 'logistica');
    await page.goto(`${urlDeLaGestion}/logistica/pedidos/${id}`);
    await prepararSolo(page, RIO);
    await page.goto(`${urlDeLaGestion}/logistica/envios`);
    await page.getByRole('button', { name: /^Generar guía del envío/ }).first().click();
    await expect(page.getByText('Guía generada.')).toBeVisible();
    await page.getByRole('button', { name: /^Pasar a en reparto del envío/ }).first().click();
    await expect(page.getByText('Envío pasado a reparto.')).toBeVisible();

    // La administración anula: lo de Lima ya salió; se anula lo de Ayacucho.
    await ingresarComoPersonal(page, 'administrador');
    await page.goto(`${urlDeLaGestion}/administracion/pedidos/${id}`);
    await page.locator('summary', { hasText: 'Anular el pedido' }).click();
    await page.getByLabel('Motivo').selectOption('otro');
    await page.getByRole('button', { name: 'Anular el pedido' }).click();
    // El pedido no queda «anulado» (lo de Lima sigue su camino): lo que se comprueba es el estado de cada línea.
    await expect(page.getByRole('row', { name: new RegExp(ZORRO) })).toContainText('Anulado');
    await expect(page.getByRole('row', { name: new RegExp(RIO) })).toContainText('Despachado');
    await sinViolacionesDeAccesibilidad(page);
    await page.screenshot({ path: info.outputPath('administracion-dos-sucursales.png'), fullPage: true });

    await page.goto(ruta);
    await expect(page.getByRole('row', { name: new RegExp(ZORRO) })).toContainText('Anulado');
    await expect(page.getByRole('row', { name: new RegExp(RIO) })).not.toContainText('Anulado');
  });
});
