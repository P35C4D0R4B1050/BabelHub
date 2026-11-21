import { test, expect } from '@playwright/test';
import {
  clave,
  comprarSoloDigital,
  comprarUnLibroConRecojo,
  esperarElPedido,
  ingresarComoClienteNuevo,
  ingresarComoPersonal,
  rutaDelPrimerPedido,
  sinViolacionesDeAccesibilidad,
  urlDeLaGestion,
} from '../pedidos/ayudas';

// Devoluciones de punta a punta (ecommerce-devoluciones T33; AC-DEV-01, AC-DEV-10, AC-DEV-14): un cliente recién sembrado compra, el encargado entrega el impreso, el cliente pide su devolución, el encargado la aprueba y confirma la
// recepción; el reembolso de un digital y el reporte de un archivo defectuoso, que decide la administración. Mismas condiciones que `pedidos/` (entorno local, `entorno/sembrar-datos.sh --limpiar`, host de la tienda en :8443 y de la
// gestión en :8444). Datos ficticios; el motivo que se escribe es un texto sin datos personales.

test.describe.configure({ mode: 'serial' });

const idDe = (ruta: string) => ruta.split('/').pop()!;

test.describe('Devoluciones', () => {
  test.skip(!clave, 'Falta BABELHUB__Sembrador__Clave (entorno E2E local con el sembrador de sesiones)');

  test('AC-DEV-01, AC-DEV-10: devolver un impreso entregado: se pide, el encargado aprueba y confirma la recepción y el cliente lo ve reembolsado', async ({ page }, info) => {
    await ingresarComoClienteNuevo(page);
    await comprarUnLibroConRecojo(page);
    const id = idDe(await rutaDelPrimerPedido(page));

    // El encargado de la sucursal entrega el pedido (prepara, marca lista y entrega).
    await ingresarComoPersonal(page, 'encargado', 'ayacucho');
    await page.goto(`${urlDeLaGestion}/sucursal/ayacucho/pedidos/${id}`);
    for (const accion of ['Preparar', 'Marcar listas', 'Entregar']) {
      await page.getByRole('checkbox', { name: /^Elegir / }).first().check();
      await page.getByRole('button', { name: accion }).click();
    }
    await expect(page.getByText('La entrega quedó registrada.')).toBeVisible();

    // El cliente pide la devolución desde el detalle del pedido.
    await page.goto(`/cuenta/pedidos/${id}`);
    await page.getByRole('link', { name: 'Devolver libros' }).click();
    await expect(page.getByRole('heading', { level: 1, name: 'Devolver un pedido' })).toBeVisible();
    await page.getByRole('spinbutton').first().fill('1');
    await page.getByLabel('Sucursal donde entregarás los libros').selectOption({ index: 0 });
    await page.getByRole('textbox', { name: /^Motivo/ }).fill('No era el libro que buscaba');
    await sinViolacionesDeAccesibilidad(page);
    await page.screenshot({ path: info.outputPath('devolver-un-pedido.png'), fullPage: true });
    await page.getByRole('button', { name: 'Solicitar la devolución' }).click();
    await expect(page.getByText('Tu solicitud quedó registrada. Te avisaremos cuando cambie de estado.')).toBeVisible();
    await expect(page.getByRole('heading', { level: 1, name: /^Devolución / })).toBeVisible();
    await page.goto('/cuenta/devoluciones');
    await expect(page.getByRole('heading', { level: 1, name: 'Mis devoluciones' })).toBeVisible();
    await expect(page.getByRole('row').filter({ hasText: 'Solicitada' })).toHaveCount(1);
    await sinViolacionesDeAccesibilidad(page);
    await page.screenshot({ path: info.outputPath('mis-devoluciones.png'), fullPage: true });

    // El encargado de la sucursal receptora la ve, la aprueba y confirma la recepción.
    await ingresarComoPersonal(page, 'encargado', 'ayacucho');
    await page.goto(`${urlDeLaGestion}/sucursal/ayacucho/devoluciones`);
    await expect(page.getByRole('heading', { level: 1, name: 'Devoluciones de impresos' })).toBeVisible();
    await sinViolacionesDeAccesibilidad(page);
    await page.locator('tbody a.link').first().click();
    await page.getByRole('button', { name: 'Aprobar', exact: true }).click();
    await expect(page.getByText('La devolución quedó aprobada.')).toBeVisible();
    await page.getByRole('button', { name: 'Confirmar recepción' }).click();
    await expect(page.getByText('La recepción quedó confirmada y el reembolso está en curso.')).toBeVisible();
    await page.screenshot({ path: info.outputPath('sucursal-recibida.png'), fullPage: true });

    // El cliente ve el estado con texto (no solo con color).
    await page.goto('/cuenta/devoluciones');
    await expect(page.getByRole('row').filter({ hasText: /Recibida|Reembolsada/ })).toHaveCount(1);
  });

  test('AC-DEV-04: el reembolso de un digital sin descargas se pide desde el pedido y queda registrado', async ({ page }, info) => {
    await ingresarComoClienteNuevo(page);
    await comprarSoloDigital(page);
    await esperarElPedido(page);
    await page.getByRole('link', { name: /^Ver el pedido/ }).first().click();
    await page.getByRole('link', { name: 'Devolver libros' }).click();

    await page.getByLabel('Motivo').first().fill('Lo compré por error');
    await page.getByRole('button', { name: 'Pedir el reembolso' }).click();

    await expect(page.getByRole('heading', { level: 1, name: /^Devolución / })).toBeVisible();
    await page.goto('/cuenta/devoluciones');
    await expect(page.getByRole('heading', { level: 1, name: 'Mis devoluciones' })).toBeVisible();
    await expect(page.getByRole('row').filter({ hasText: /Reembolsada|Aprobada|Solicitada/ })).toHaveCount(1);
    await sinViolacionesDeAccesibilidad(page);
    await page.screenshot({ path: info.outputPath('reembolso-digital.png'), fullPage: true });
  });

  test('AC-DEV-14: el reporte de un archivo defectuoso lo decide la administración y un encargado no lo ve', async ({ page }, info) => {
    await ingresarComoClienteNuevo(page);
    await comprarSoloDigital(page);
    await esperarElPedido(page);
    await page.getByRole('link', { name: /^Ver el pedido/ }).first().click();
    await page.getByRole('link', { name: 'Devolver libros' }).click();

    const formulario = page.locator('form[action$="/devoluciones/reporte"]');
    await formulario.getByLabel('Motivo').fill('El archivo no abre');
    await formulario.getByLabel(/^Qué le pasa al archivo/).fill('Al abrirlo en el lector aparece una pantalla en blanco desde el capítulo 2.');
    await formulario.getByRole('button', { name: 'Reportar el archivo' }).click();
    await expect(page.getByRole('heading', { level: 1, name: /^Devolución / })).toBeVisible();

    // Un encargado no ve los reportes: responde lo mismo que ante cualquier recurso fuera de su alcance.
    await ingresarComoPersonal(page, 'encargado', 'ayacucho');
    const respuesta = await page.goto(`${urlDeLaGestion}/administracion/devoluciones/reportes`);
    expect([403, 404]).toContain(respuesta?.status());

    // La administración central lo ve y lo aprueba.
    await ingresarComoPersonal(page, 'administrador');
    await page.goto(`${urlDeLaGestion}/administracion/devoluciones/reportes`);
    await sinViolacionesDeAccesibilidad(page);
    await page.screenshot({ path: info.outputPath('admin-reportes.png'), fullPage: true });
    await page.locator('tbody a.link').first().click();
    await page.getByRole('button', { name: 'Aprobar el reporte' }).click();
    await expect(page.getByText('El reporte quedó aprobado.')).toBeVisible();
  });
});
