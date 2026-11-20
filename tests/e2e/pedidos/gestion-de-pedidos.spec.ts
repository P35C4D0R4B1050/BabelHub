import { test, expect } from '@playwright/test';
import {
  clave,
  comprarUnLibroADomicilio,
  comprarUnLibroConRecojo,
  ingresarComoClienteNuevo,
  ingresarComoPersonal,
  rutaDelPrimerPedido,
  sinViolacionesDeAccesibilidad,
  urlDeLaGestion,
} from './ayudas';

// Las pantallas del personal sobre pedidos reales (ecommerce-pedidos T41; PED-07 a PED-13, PED-26, PED-40, PED-41): el encargado de una sucursal prepara, marca listas y entrega un pedido de recojo sin ver el contacto;
// logística ve el contacto de un pedido a domicilio y lo marca listo (se crea el envío); la administración anula con un motivo de la lista cerrada. Cada recorrido empieza con una compra real de un cliente recién
// sembrado en la tienda y sigue en el host de la gestión (`E2E_TLS=1 E2E_HOST=gestion entorno/arrancar-host.sh ecommerce`, https://localhost:8444), que comparte la base con la tienda.
// Datos ficticios; `entorno/sembrar-datos.sh --limpiar` antes de cada corrida.

test.describe.configure({ mode: 'serial' });

const idDe = (ruta: string) => ruta.split('/').pop()!;

test.describe('Gestión de pedidos por el personal', () => {
  test.skip(!clave, 'Falta BABELHUB__Sembrador__Clave (entorno E2E local con el sembrador de sesiones)');

  test('PED-08, PED-09, PED-11, PED-40: el encargado prepara, marca listas y entrega un pedido de recojo sin ver el contacto', async ({ page }, info) => {
    await ingresarComoClienteNuevo(page);
    await comprarUnLibroConRecojo(page);
    const id = idDe(await rutaDelPrimerPedido(page));

    await ingresarComoPersonal(page, 'encargado', 'ayacucho');
    await page.goto(`${urlDeLaGestion}/sucursal/ayacucho/pedidos`);
    await expect(page.getByRole('heading', { level: 1, name: 'Pedidos de recojo' })).toBeVisible();
    await expect(page.getByRole('link', { name: /^Ver el pedido/ }).first()).toBeVisible();
    await sinViolacionesDeAccesibilidad(page);

    await page.goto(`${urlDeLaGestion}/sucursal/ayacucho/pedidos/${id}`);
    await expect(page.getByText('Recojo en sucursal').first()).toBeVisible();
    // El encargado nunca ve datos de contacto ni de la cuenta.
    await expect(page.getByText('Entrega a domicilio')).toHaveCount(0);
    await page.screenshot({ path: info.outputPath('encargado-detalle.png'), fullPage: true });

    await page.getByRole('checkbox', { name: /^Elegir / }).first().check();
    await page.getByRole('button', { name: 'Preparar' }).click();
    await expect(page.getByText('Las líneas pasaron a preparación.')).toBeVisible();
    await expect(page.getByText('En preparación').first()).toBeVisible();

    await page.getByRole('checkbox', { name: /^Elegir / }).first().check();
    await page.getByRole('button', { name: 'Marcar listas' }).click();
    await expect(page.getByText('Las líneas se marcaron como listas.')).toBeVisible();
    await expect(page.getByText('Listo para recojo').first()).toBeVisible();

    await page.getByRole('checkbox', { name: /^Elegir / }).first().check();
    await page.getByRole('button', { name: 'Entregar' }).click();
    await expect(page.getByText('La entrega quedó registrada.')).toBeVisible();
    await expect(page.getByText('Entregado').first()).toBeVisible();
    await sinViolacionesDeAccesibilidad(page);
    await page.screenshot({ path: info.outputPath('encargado-entregado.png'), fullPage: true });

    // El titular ve el pedido entregado y ya no puede cancelarlo.
    await page.goto(`/cuenta/pedidos/${id}`);
    await expect(page.getByText('Entregado').first()).toBeVisible();
    await expect(page.getByText('Cancelar el pedido', { exact: true })).toHaveCount(0);
  });

  test('PED-41: el encargado de otra sucursal recibe el mismo «no encontrado» que un pedido inexistente', async ({ page }) => {
    await ingresarComoClienteNuevo(page);
    await comprarUnLibroConRecojo(page);
    const id = idDe(await rutaDelPrimerPedido(page));

    await ingresarComoPersonal(page, 'encargado', 'cusco');
    const ajeno = await page.goto(`${urlDeLaGestion}/sucursal/cusco/pedidos/${id}`);
    const textoDelAjeno = await page.locator('main').innerText();
    const inexistente = await page.goto(`${urlDeLaGestion}/sucursal/cusco/pedidos/00000000-0000-4000-8000-000000000000`);
    const textoDelInexistente = await page.locator('main').innerText();

    expect(ajeno?.status()).toBe(404);
    expect(inexistente?.status()).toBe(404);
    expect(textoDelAjeno).toBe(textoDelInexistente);
  });

  test('PED-26, PED-28, PED-40: logística ve el contacto de un pedido a domicilio, lo marca listo y se crea el envío', async ({ page }, info) => {
    await ingresarComoClienteNuevo(page);
    await comprarUnLibroADomicilio(page);
    const id = idDe(await rutaDelPrimerPedido(page));

    await ingresarComoPersonal(page, 'logistica');
    await page.goto(`${urlDeLaGestion}/logistica/pedidos`);
    await expect(page.getByRole('heading', { level: 1, name: 'Pedidos a domicilio' })).toBeVisible();
    // La lista no lleva contacto: solo el detalle, y cada lectura queda auditada.
    await expect(page.getByText('Rosa Quispe Ficticia')).toHaveCount(0);
    await sinViolacionesDeAccesibilidad(page);

    await page.goto(`${urlDeLaGestion}/logistica/pedidos/${id}`);
    await expect(page.getByText('Rosa Quispe Ficticia')).toBeVisible();
    await expect(page.getByText('987654321')).toBeVisible();
    await expect(page.getByText('Jr. Ficticio 123, Ayacucho')).toBeVisible();
    await page.screenshot({ path: info.outputPath('logistica-detalle.png'), fullPage: true });

    await page.getByRole('checkbox', { name: /^Elegir / }).first().check();
    await page.getByRole('button', { name: 'Preparar' }).click();
    await expect(page.getByText('Las líneas pasaron a preparación.')).toBeVisible();
    await page.getByRole('checkbox', { name: /^Elegir / }).first().check();
    await page.getByRole('button', { name: 'Marcar listas' }).click();
    await expect(page.getByText('Las líneas se marcaron como listas.')).toBeVisible();
    await sinViolacionesDeAccesibilidad(page);

    // El envío aparece en la lista de logística (sin guía todavía) y el titular ve el seguimiento de su pedido.
    await page.goto(`${urlDeLaGestion}/logistica/envios`);
    await expect(page.getByText('Sin guía').first()).toBeVisible();
    await page.goto(`/cuenta/pedidos/${id}`);
    await expect(page.getByText('Pendiente de guía')).toBeVisible();

    // Un encargado no ve un pedido a domicilio.
    await ingresarComoPersonal(page, 'encargado', 'ayacucho');
    const respuesta = await page.goto(`${urlDeLaGestion}/sucursal/ayacucho/pedidos/${id}`);
    expect(respuesta?.status()).toBe(404);
  });

  test('PED-13, PED-14, PED-24: la administración anula un pedido con un motivo de la lista cerrada y no hay operaciones fallidas', async ({ page }, info) => {
    await ingresarComoClienteNuevo(page);
    await comprarUnLibroConRecojo(page);
    const id = idDe(await rutaDelPrimerPedido(page));

    await ingresarComoPersonal(page, 'administrador');
    await page.goto(`${urlDeLaGestion}/administracion/pedidos`);
    await expect(page.getByRole('heading', { level: 1, name: 'Pedidos' })).toBeVisible();
    await sinViolacionesDeAccesibilidad(page);

    await page.goto(`${urlDeLaGestion}/administracion/pedidos/${id}`);
    await page.getByText('Anular el pedido', { exact: true }).click();
    await expect(page.getByLabel('Motivo').locator('option')).toHaveText(['Pago duplicado', 'No recogido', 'Otro']);
    await page.getByLabel('Motivo').selectOption('pago-duplicado');
    await page.getByRole('button', { name: 'Anular el pedido' }).click();

    await expect(page.getByText('El pedido se anuló y el reembolso quedó registrado.')).toBeVisible();
    await expect(page.getByText('Anulado').first()).toBeVisible();
    await expect(page.getByText('Anular el pedido', { exact: true })).toHaveCount(0);
    await page.screenshot({ path: info.outputPath('administracion-anulado.png'), fullPage: true });
    await sinViolacionesDeAccesibilidad(page);

    await page.goto(`${urlDeLaGestion}/administracion/pedidos/operaciones-fallidas`);
    await expect(page.getByRole('heading', { level: 1, name: 'Operaciones fallidas' })).toBeVisible();
    await expect(page.getByText('No hay operaciones fallidas')).toBeVisible();

    // El cliente ve su pedido anulado.
    await page.goto(`/cuenta/pedidos/${id}`);
    await expect(page.getByText('Anulado').first()).toBeVisible();
  });
});
