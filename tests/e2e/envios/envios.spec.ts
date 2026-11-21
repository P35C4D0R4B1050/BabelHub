import { test, expect } from '@playwright/test';
import {
  clave,
  comprarUnLibroADomicilio,
  ingresarComoClienteNuevo,
  ingresarComoPersonal,
  rutaDelPrimerPedido,
  sinViolacionesDeAccesibilidad,
  urlDeLaGestion,
} from '../pedidos/ayudas';

// Envíos de punta a punta (ecommerce-envios T24; AC-ENV-02, AC-ENV-06, AC-ENV-13): un cliente nuevo compra a domicilio, logística marca el pedido listo (se crea el envío), genera la guía con el operador simulado, lo pasa a
// reparto y lo marca entregado; el cliente ve el estado de su envío en cada paso, sin que nadie más ni la lista de logística muestre su contacto. Mismas condiciones que `pedidos/` (entorno local con el operador simulado,
// `entorno/sembrar-datos.sh --limpiar`, host de la tienda en :8443 y de la gestión en :8444). Datos ficticios.

test.describe.configure({ mode: 'serial' });

const idDe = (ruta: string) => ruta.split('/').pop()!;

test.describe('Envíos', () => {
  test.skip(!clave, 'Falta BABELHUB__Sembrador__Clave (entorno E2E local con el sembrador de sesiones)');

  test('AC-ENV-02, AC-ENV-06: logística genera la guía, pasa a reparto y marca entregado, y el cliente ve cada estado', async ({ page }, info) => {
    await ingresarComoClienteNuevo(page);
    await comprarUnLibroADomicilio(page);
    const id = idDe(await rutaDelPrimerPedido(page));

    await ingresarComoPersonal(page, 'logistica');
    await page.goto(`${urlDeLaGestion}/logistica/pedidos/${id}`);
    await page.getByRole('checkbox', { name: /^Elegir / }).first().check();
    await page.getByRole('button', { name: 'Preparar' }).click();
    await expect(page.getByText('Las líneas pasaron a preparación.')).toBeVisible();
    await page.getByRole('checkbox', { name: /^Elegir / }).first().check();
    await page.getByRole('button', { name: 'Marcar listas' }).click();
    await expect(page.getByText('Las líneas se marcaron como listas.')).toBeVisible();

    await page.goto(`${urlDeLaGestion}/logistica/envios`);
    await expect(page.getByRole('heading', { level: 1, name: 'Envíos' })).toBeVisible();
    // La lista nunca lleva el contacto del destinatario.
    await expect(page.getByText('Rosa Quispe Ficticia')).toHaveCount(0);
    await expect(page.getByText('987654321')).toHaveCount(0);
    await expect(page.getByText('Sin guía').first()).toBeVisible();
    await sinViolacionesDeAccesibilidad(page);
    await page.screenshot({ path: info.outputPath('logistica-envios-sin-guia.png'), fullPage: true });

    await page.goto(`/cuenta/pedidos/${id}`);
    await expect(page.getByText('Pendiente de guía')).toBeVisible();

    await page.goto(`${urlDeLaGestion}/logistica/envios`);
    await page.getByRole('button', { name: /^Generar guía del envío/ }).first().click();
    await expect(page.getByText('Guía generada.')).toBeVisible();
    await expect(page.getByText('Con guía').first()).toBeVisible();

    await page.goto(`/cuenta/pedidos/${id}`);
    await expect(page.getByText(/^Guía /).first()).toBeVisible();
    await page.screenshot({ path: info.outputPath('cliente-envio-con-guia.png'), fullPage: true });

    await page.goto(`${urlDeLaGestion}/logistica/envios`);
    await page.getByRole('button', { name: /^Pasar a en reparto del envío/ }).first().click();
    await expect(page.getByText('Envío pasado a reparto.')).toBeVisible();
    await expect(page.getByText('En reparto').first()).toBeVisible();

    await page.getByRole('button', { name: /^Marcar entregado del envío/ }).first().click();
    await expect(page.getByText('Envío marcado como entregado.')).toBeVisible();
    await sinViolacionesDeAccesibilidad(page);

    await page.goto(`/cuenta/pedidos/${id}`);
    await expect(page.getByText(/^Entregado el /).first()).toBeVisible();
    await page.screenshot({ path: info.outputPath('cliente-envio-entregado.png'), fullPage: true });
  });

  test('AC-ENV-13: sin permiso, la lista de logística responde como cualquier recurso ajeno y el cliente de otra cuenta no ve el envío', async ({ page, browser }, info) => {
    await ingresarComoClienteNuevo(page);
    await comprarUnLibroADomicilio(page);
    const ruta = await rutaDelPrimerPedido(page);

    // Un encargado no gestiona envíos.
    await ingresarComoPersonal(page, 'encargado', 'ayacucho');
    const respuesta = await page.goto(`${urlDeLaGestion}/logistica/envios`);
    expect([403, 404]).toContain(respuesta?.status());

    const otro = await browser.newContext({ ...info.project.use });
    const otraPagina = await otro.newPage();
    await ingresarComoClienteNuevo(otraPagina);
    const ajeno = await otraPagina.goto(ruta);
    expect(ajeno?.status()).toBe(404);
    await otro.close();
  });
});
