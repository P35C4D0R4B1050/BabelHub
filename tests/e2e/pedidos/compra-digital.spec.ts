import { test, expect } from '@playwright/test';
import { clave, comprarSoloDigital, comprarUnLibroConRecojo, ingresarComoClienteNuevo, ingresarComoPersonal, rutaDelPrimerPedido, urlDeLaGestion } from './ayudas';

// La compra solo digital con las cadenas reales (Carrito, Pagos con el simulador, Pedidos y Biblioteca) y el filtro por estado de la administración
// (ecommerce-pedidos PED-12, PED-14; AC-PED-10, AC-PED-12; ecommerce-carrito-checkout CAR-07, AC-CAR-06). Datos ficticios; `entorno/sembrar-datos.sh --limpiar` antes de cada corrida.

test.describe.configure({ mode: 'serial' });

test.describe('Compra solo digital y filtro de la administración', () => {
  test.skip(!clave, 'Falta BABELHUB__Sembrador__Clave (entorno E2E local con el sembrador de sesiones)');

  test('AC-PED-10, AC-CAR-06, PED-12: una compra solo digital deja el pedido entregado y el libro en la biblioteca', async ({ page }) => {
    await ingresarComoClienteNuevo(page);
    await comprarSoloDigital(page);
    const id = (await rutaDelPrimerPedido(page)).split('/').pop()!;

    // El acceso se concede con el pago aprobado y el pedido solo digital queda entregado sin intervención del personal.
    await expect
      .poll(async () => {
        await page.goto(`/cuenta/pedidos/${id}`);
        return await page.getByText('Entregado').count();
      }, { timeout: 60_000, intervals: [1_000, 2_000, 3_000] })
      .toBeGreaterThan(0);
    await expect(page.getByText('Entrega a domicilio')).toHaveCount(0);

    await page.goto('/biblioteca');
    await expect(page.getByText('El zorro y el cóndor del Ande').first()).toBeVisible();
  });

  test('AC-PED-12, PED-14: la administración filtra los pedidos por estado y solo ve los de ese estado', async ({ page }) => {
    await ingresarComoClienteNuevo(page);
    await comprarUnLibroConRecojo(page);
    const id = (await rutaDelPrimerPedido(page)).split('/').pop()!;

    await ingresarComoPersonal(page, 'administrador');
    await page.goto(`${urlDeLaGestion}/administracion/pedidos?estado=confirmado`);
    await expect(page.locator(`a[href$="/administracion/pedidos/${id}"]`)).toHaveCount(1);

    await page.goto(`${urlDeLaGestion}/administracion/pedidos?estado=en-transito`);
    await expect(page.locator(`a[href$="/administracion/pedidos/${id}"]`)).toHaveCount(0);

    await page.goto(`${urlDeLaGestion}/administracion/pedidos?estado=anulado`);
    await expect(page.locator(`a[href$="/administracion/pedidos/${id}"]`)).toHaveCount(0);
  });
});
