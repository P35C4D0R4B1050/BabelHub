import { test, expect, type Page } from '@playwright/test';
import { agregarAlCarrito, clave, EDICION_IMPRESA_DEL_ZORRO, ingresarComoClienteNuevo, sinViolacionesDeAccesibilidad } from '../pedidos/ayudas';

// La pantalla de pago y su estado (ecommerce-pagos T36; AC-PAG-04, AC-PAG-05, AC-PAG-12): los dos medios (Yape y tarjeta), el rechazo con su mensaje de lista cerrada y el reintento con otro medio, el pago pendiente de
// confirmación y la confirmación del pago aprobado, con teclado, contraste AA en los dos temas y 375 px. El cobro es el simulador de la pasarela (tokens `tok_*`); ningún dato de tarjeta se escribe: el campo es el identificador
// del medio que entrega la pasarela. Mismo entorno que `pedidos/compra-y-pedido.spec.ts` (host de la tienda por HTTPS y datos sembrados con `entorno/sembrar-datos.sh --limpiar` antes de cada corrida).
// Chromium de escritorio y de teléfono y WebKit; Firefox es informativo.

test.describe.configure({ mode: 'serial' });
test.setTimeout(90_000);

// Carrito → checkout con recojo en Ayacucho → aceptaciones → pantalla de pago (sin pagar).
async function llegarAlPago(page: Page) {
  await agregarAlCarrito(page, EDICION_IMPRESA_DEL_ZORRO, 1);
  await page.goto('/carrito');
  await page.getByRole('button', { name: 'Continuar con la compra' }).click();
  await expect(page).toHaveURL(/\/checkout$/);
  await page.getByRole('button', { name: 'Elegir recojo' }).click();
  await expect(page.getByRole('heading', { name: 'Tus datos y aceptaciones' })).toBeVisible();
  for (const casilla of await page.getByRole('group', { name: 'Para continuar, acepta:' }).getByRole('checkbox').all()) {
    await casilla.check();
  }
  await page.getByRole('button', { name: /^Ir al pago/ }).click();
  await expect(page).toHaveURL(/\/checkout\/pago/);
  await expect(page.getByRole('heading', { level: 1, name: 'Pagar tu compra' })).toBeVisible();
}

async function pagarCon(page: Page, token: string, medio: 'Yape' | 'Tarjeta') {
  // La entrada del medio es visualmente oculta: se elige con un clic en su etiqueta, como lo haría una persona.
  await page.getByRole('radiogroup', { name: 'Medio de pago' }).locator('label', { hasText: new RegExp(medio, 'i') }).click();
  await expect(page.getByRole('radio', { name: new RegExp(medio, 'i') })).toBeChecked();
  await page.getByLabel(/Identificador del medio de pago/).fill(token);
  await page.getByRole('button', { name: /^Pagar S\// }).click();
}

async function sinDesplazamientoHorizontal(page: Page) {
  expect(await page.evaluate(() => document.documentElement.scrollWidth > document.documentElement.clientWidth), 'la página no se desplaza en horizontal').toBe(false);
}

test.describe('Pago de la compra con el simulador de la pasarela', () => {
  test.skip(!clave, 'Falta BABELHUB__Sembrador__Clave (entorno E2E local con el sembrador de sesiones)');

  test('la pantalla de pago ofrece Yape y tarjeta, muestra la reserva y es accesible en los dos temas, por teclado y sin desplazarse (AC-PAG-04, AC-PAG-12)', async ({ page }) => {
    await ingresarComoClienteNuevo(page);
    await llegarAlPago(page);

    await expect(page.getByRole('radiogroup', { name: 'Medio de pago' })).toBeVisible();
    await expect(page.getByRole('radio', { name: /Yape/i })).toBeVisible();
    await expect(page.getByRole('radio', { name: /Tarjeta/i })).toBeVisible();
    await expect(page.getByText(/^Total a pagar:/)).toBeVisible();
    await expect(page.getByText(/Reservamos tus libros impresos hasta las/)).toBeVisible();
    await expect(page.locator('[data-bh-cuenta-regresiva]')).toHaveCount(1);
    await sinDesplazamientoHorizontal(page);
    await sinViolacionesDeAccesibilidad(page);

    // Por teclado: el medio se elige con las flechas y el campo del identificador y el botón son alcanzables.
    await page.getByRole('radio', { name: /Yape/i }).focus();
    await page.keyboard.press('ArrowRight');
    await expect(page.getByRole('radio', { name: /Tarjeta/i })).toBeChecked();
    await page.keyboard.press('Tab');
    await expect(page.getByLabel(/Identificador del medio de pago/)).toBeFocused();

    await page.context().addCookies([{ name: 'bh-tema', value: 'babelhub-dark', url: new URL('/', test.info().project.use.baseURL ?? process.env.E2E_BASE_URL ?? 'https://localhost:8443').toString() }]);
    await page.reload();
    await expect(page.locator('html')).toHaveAttribute('data-theme', 'babelhub-dark');
    await sinViolacionesDeAccesibilidad(page);
  });

  test('un rechazo muestra un mensaje de lista cerrada con el foco en el aviso y se reintenta con otro medio hasta aprobarse (AC-PAG-05)', async ({ page }) => {
    await ingresarComoClienteNuevo(page);
    await llegarAlPago(page);

    await pagarCon(page, 'tok_tarjeta_rechazada', 'Tarjeta');
    const aviso = page.getByRole('alert').filter({ hasText: 'No se pudo completar el pago' });
    await expect(aviso).toBeVisible();
    await expect(aviso).toContainText('No se te cobró nada');
    await expect(aviso).not.toContainText('tok_tarjeta_rechazada');
    await sinViolacionesDeAccesibilidad(page);

    // El mismo formulario deja intentarlo con otro medio: Yape rechazado y después una tarjeta aprobada.
    // Tras el rechazo la pantalla de estado ofrece volver a pagar.
    await page.getByRole('link', { name: 'Pagar', exact: true }).click();
    await pagarCon(page, 'tok_yape_rechazado', 'Yape');
    await expect(page.getByRole('alert').filter({ hasText: 'No se pudo completar el pago' })).toBeVisible();

    await page.getByRole('link', { name: 'Pagar', exact: true }).click();
    await pagarCon(page, 'tok_aprobado', 'Tarjeta');
    await expect(page).toHaveURL(/\/checkout\/pago\/estado/);
    await expect(page.getByRole('heading', { level: 1, name: 'Estado de tu pago' })).toBeVisible();
    await sinViolacionesDeAccesibilidad(page);
  });

  test('un pago que la pasarela no confirma a tiempo queda pendiente y la pantalla dice hasta cuándo se confirmará (AC-PAG-12)', async ({ page }) => {
    await ingresarComoClienteNuevo(page);
    await llegarAlPago(page);

    await pagarCon(page, 'tok_lento', 'Tarjeta');
    // La pasarela tarda más que el plazo del cobro (el simulador responde a los 11 s): la redirección al estado puede tardar.
    await expect(page).toHaveURL(/\/checkout\/pago\/estado/, { timeout: 40_000 });
    await expect(page.getByRole('heading', { level: 1, name: 'Estado de tu pago' })).toBeVisible();
    await expect(page.getByText(/Lo confirmaremos antes de las/)).toBeVisible();
    await sinDesplazamientoHorizontal(page);
    await sinViolacionesDeAccesibilidad(page);
  });

  test('la confirmación de un pago aprobado se ve en el estado y el pedido llega a «Mis pedidos» (AC-PAG-04)', async ({ page }) => {
    await ingresarComoClienteNuevo(page);
    await llegarAlPago(page);

    await pagarCon(page, 'tok_aprobado', 'Tarjeta');
    await expect(page).toHaveURL(/\/checkout\/pago\/estado/);
    await expect(page.getByRole('heading', { level: 1, name: 'Estado de tu pago' })).toBeVisible();
    await sinViolacionesDeAccesibilidad(page);

    await expect
      .poll(
        async () => {
          await page.goto('/cuenta/pedidos');
          return await page.getByRole('link', { name: /^Ver el pedido/ }).count();
        },
        { timeout: 90_000, intervals: [1_000, 2_000, 3_000] },
      )
      .toBeGreaterThan(0);
  });
});
