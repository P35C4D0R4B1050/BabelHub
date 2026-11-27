import { test, expect, type Page } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import { agregarAlCarrito, clave, conOrigenPropio, EDICION_DIGITAL_DEL_ZORRO, EDICION_IMPRESA_DEL_ZORRO, ingresarComoClienteNuevo } from '../pedidos/ayudas';

// Los recorridos del carrito y del checkout con el entorno sembrado (ecommerce-carrito-checkout T36): compra mixta en cinco pasos, compra solo digital en cuatro o menos, visitante que debe ingresar para pagar, el
// bloqueo sin aceptar los textos y la accesibilidad de ambas pantallas en los dos temas. La sesión de cada recorrido sale del sembrador de desarrollo y el libro se agrega con el formulario de
// la ficha. Complementa a `carrito-checkout.spec.ts`, que usa `storageState` ya sembrados; el recorrido de la fusión de carritos (AC-CAR-22) sigue necesitando el ingreso real de web-identidad.
//   Datos: `entorno/sembrar-datos.sh --limpiar`; BABELHUB__Sembrador__Clave; E2E_BASE_URL=https://localhost:8443.

const pasos = (page: Page) => page.getByRole('list', { name: 'Progreso de la compra' }).getByRole('listitem');

async function mezclarImpresoYDigital(page: Page) {
  await ingresarComoClienteNuevo(page);
  await agregarAlCarrito(page, EDICION_IMPRESA_DEL_ZORRO, 1);
  await agregarAlCarrito(page, EDICION_DIGITAL_DEL_ZORRO, 1);
}

// El checkout se abre desde el carrito (POST /checkout/iniciar): pedir `/checkout` sin haberlo iniciado no muestra los pasos.
async function irA(page: Page, ruta: string) {
  if (ruta !== '/checkout') {
    await page.goto(ruta);
    return;
  }

  await page.goto('/carrito');
  await page.getByRole('button', { name: 'Continuar con la compra' }).click();
  await expect(page).toHaveURL(/\/checkout$/);
}

async function marcarLosTextos(page: Page) {
  for (const casilla of await page.getByRole('group', { name: 'Para continuar, acepta:' }).getByRole('checkbox').all()) {
    await casilla.check();
  }
}

test.describe.configure({ mode: 'serial' });

test.describe('Carrito y checkout con el entorno sembrado', () => {
  test.skip(!clave, 'Falta BABELHUB__Sembrador__Clave (entorno E2E local con el sembrador de sesiones)');

  test('CAR-15: una compra mixta se hace en cinco pasos y llega al pago con los dos libros', async ({ page }) => {
    await mezclarImpresoYDigital(page);
    await page.goto('/carrito');
    await expect(page.getByRole('heading', { level: 1, name: 'Tu carrito' })).toBeVisible();
    await expect(page.getByText('El zorro y el cóndor del Ande', { exact: true })).toHaveCount(2);
    await page.getByRole('button', { name: 'Continuar con la compra' }).click();

    await expect(page).toHaveURL(/\/checkout$/);
    await expect(pasos(page)).toHaveCount(5);
    await expect(pasos(page).nth(0)).toContainText('Revisar');
    await expect(pasos(page).nth(1)).toContainText('Entrega');
    // Cada línea dice su disponibilidad con texto, no solo con color; el total sale del servidor en soles.
    await expect(page.getByText(/En tu sucursal|Hay stock|Se trae de otra sucursal/).first()).toBeVisible();
    await expect(page.getByRole('complementary', { name: 'Resumen de tu pedido' })).toContainText('S/');

    await page.getByLabel('Sucursal de recojo').selectOption({ label: 'Sucursal Ayacucho (prueba)' });
    await page.getByRole('button', { name: 'Elegir recojo' }).click();
    await expect(page.getByRole('heading', { name: 'Tus datos y aceptaciones' })).toBeVisible();
    await expect(pasos(page).nth(2)).toHaveAttribute('aria-current', 'step');
    // Con un libro digital se pide también la política de reembolso digital.
    await expect(page.getByRole('group', { name: 'Para continuar, acepta:' }).getByRole('checkbox')).toHaveCount(3);

    await marcarLosTextos(page);
    await page.getByRole('button', { name: /^Ir al pago/ }).click();
    await expect(page).toHaveURL(/\/checkout\/pago/);
  });

  test('un bloqueo se dice en la misma pantalla y el carrito se conserva', async ({ page }) => {
    await mezclarImpresoYDigital(page);
    await irA(page, '/checkout');
    await page.getByLabel('Sucursal de recojo').selectOption({ label: 'Sucursal Ayacucho (prueba)' });
    await page.getByRole('button', { name: 'Elegir recojo' }).click();

    // Sin marcar los textos no se paga y se dice junto a la casilla.
    await page.getByRole('button', { name: /^Ir al pago/ }).click();
    await expect(page.getByRole('alert').first()).toBeVisible();
    await expect(page.getByText('Revisa tus libros')).toBeVisible();
    await page.goto('/carrito');
    await expect(page.getByText('El zorro y el cóndor del Ande', { exact: true })).toHaveCount(2);
  });

  test('AC-CAR-06: una compra solo digital no pide entrega y se hace en cuatro pasos o menos', async ({ page }) => {
    await ingresarComoClienteNuevo(page);
    await agregarAlCarrito(page, EDICION_DIGITAL_DEL_ZORRO, 1);
    await page.goto('/carrito');
    await page.getByRole('button', { name: 'Continuar con la compra' }).click();

    await expect(page).toHaveURL(/\/checkout$/);
    expect(await pasos(page).count()).toBeLessThanOrEqual(4);
    await expect(page.getByText('Compra 100 % digital: sin entrega.').first()).toBeVisible();
    await expect(page.getByRole('heading', { name: '¿Cómo quieres recibir tus impresos?' })).toHaveCount(0);
    await expect(page.getByText('No aplica (solo digitales)').first()).toBeVisible();

    await marcarLosTextos(page);
    await page.getByRole('button', { name: /^Ir al pago/ }).click();
    await expect(page).toHaveURL(/\/checkout\/pago/);
  });

  test('AC-CAR-13: un visitante que quiere pagar debe ingresar y su carrito se conserva', async ({ page }) => {
    await conOrigenPropio(page);
    await agregarAlCarrito(page, EDICION_IMPRESA_DEL_ZORRO, 1);
    await page.goto('/carrito');

    const titulos = await page.getByRole('region', { name: 'Libros en tu carrito' }).getByRole('listitem').count();
    expect(titulos).toBeGreaterThan(0);
    await expect(page.getByText('Para pagar necesitas una cuenta.')).toBeVisible();
    await expect(page.getByRole('link', { name: 'Ingresar para continuar' })).toHaveAttribute('href', '/ingreso');

    // El carrito sigue igual después de recargar y no hay forma de pagar sin sesión.
    await page.reload();
    expect(await page.getByRole('region', { name: 'Libros en tu carrito' }).getByRole('listitem').count()).toBe(titulos);
    await expect(page.getByRole('button', { name: 'Continuar con la compra' })).toHaveCount(0);
  });

  for (const [nombre, ruta] of [
    ['carrito', '/carrito'],
    ['checkout', '/checkout'],
  ] as const) {
    for (const tema of ['babelhub', 'babelhub-dark'] as const) {
      test(`accesibilidad del ${nombre} (${tema}): sin violaciones de contraste ni de etiquetas`, async ({ page }) => {
        await mezclarImpresoYDigital(page);
        await irA(page, ruta);
        await page.evaluate((t) => document.documentElement.setAttribute('data-theme', t), tema);
        const resultado = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa']).analyze();
        expect(resultado.violations.map((v) => `${v.id}: ${v.nodes.map((n) => n.target.join(' ')).join(' | ')}`)).toEqual([]);
      });
    }

    test(`teclado en el ${nombre}: el foco es visible y se llega a la acción principal sin ratón`, async ({ page }) => {
      await mezclarImpresoYDigital(page);
      await irA(page, ruta);
      const principal = page.getByRole('button', { name: /Continuar con la compra|Elegir recojo|Ir al pago/ }).first();
      let llegado = false;
      for (let i = 0; i < 60 && !llegado; i += 1) {
        await page.keyboard.press('Tab');
        const enfocado = page.locator(':focus');
        await expect(enfocado).toBeVisible();
        expect(await enfocado.evaluate((el) => getComputedStyle(el).outlineStyle)).not.toBe('none');
        llegado = await principal.evaluate((el) => el === document.activeElement);
      }
      expect(llegado).toBe(true);
    });

    test(`teléfono en el ${nombre}: sin desplazamiento horizontal`, async ({ page }) => {
      await mezclarImpresoYDigital(page);
      await page.setViewportSize({ width: 375, height: 812 });
      await irA(page, ruta);
      const ancho = await page.evaluate(() => ({ doc: document.documentElement.scrollWidth, vista: window.innerWidth }));
      expect(ancho.doc).toBeLessThanOrEqual(ancho.vista);
    });
  }
});
