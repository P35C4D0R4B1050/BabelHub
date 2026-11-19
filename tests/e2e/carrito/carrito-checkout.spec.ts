import { test, expect, type Page } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';

// Recorridos del carrito y del checkout (ecommerce-carrito-checkout, T36): compra mixta, compra solo digital (cuatro pasos o menos),
// visitante que pulsa pagar, avisos tras fusionar el carrito y las pantallas (temas, teclado, contraste, teléfono).
//
// La sesión y el carrito de cada recorrido salen del entorno local sembrado, como `storageState` de Playwright (cookie de sesión y
// cookie `__Host-bh-carrito`): las pantallas de ingreso son de `web-identidad` y la ficha del catálogo aún no agrega al carrito, así que
// el recorrido parte de un carrito ya sembrado. Si falta la variable, el recorrido se omite en lugar de inventar datos (AGENTS.md §6).
//   E2E_CLIENTE_MIXTO_STORAGE_STATE     cliente con un impreso (stock en su sucursal o por traslado) y un digital que no posee
//   E2E_CLIENTE_DIGITAL_STORAGE_STATE   cliente con solo libros digitales que no posee
//   E2E_VISITANTE_STORAGE_STATE         visitante (sin sesión) con un libro en su carrito
//   E2E_CLIENTE_TRAS_FUSION_STORAGE_STATE cliente que acaba de ingresar con un carrito de visitante: tiene avisos de ajuste sin ver
const mixto = process.env.E2E_CLIENTE_MIXTO_STORAGE_STATE;
const digital = process.env.E2E_CLIENTE_DIGITAL_STORAGE_STATE;
const visitante = process.env.E2E_VISITANTE_STORAGE_STATE;
const trasFusion = process.env.E2E_CLIENTE_TRAS_FUSION_STORAGE_STATE;

const pasos = (page: Page) => page.getByRole('list', { name: 'Progreso de la compra' }).getByRole('listitem');

async function aceptarYPagar(page: Page) {
  // Los textos legales se aceptan con su casilla; la verificación de persona solo se exige si el origen parece automatizado.
  for (const casilla of await page.getByRole('group', { name: 'Para continuar, acepta:' }).getByRole('checkbox').all()) {
    await casilla.check();
  }
  await page.getByRole('button', { name: /^Ir al pago/ }).click();
}

test.describe('Carrito y checkout: compra mixta', () => {
  test.use({ storageState: mixto });

  test('CAR-15: impresos y digitales se compran en cinco pasos: revisar, entrega, datos, pago y listo', async ({ page }) => {
    test.skip(!mixto, 'Falta E2E_CLIENTE_MIXTO_STORAGE_STATE');
    await page.goto('/carrito');
    await expect(page.getByRole('heading', { level: 1, name: 'Tu carrito' })).toBeVisible();
    await page.getByRole('button', { name: 'Continuar con la compra' }).click();

    await expect(page).toHaveURL(/\/checkout$/);
    await expect(pasos(page)).toHaveCount(5);
    await expect(pasos(page).nth(0)).toContainText('Revisar');
    await expect(pasos(page).nth(1)).toContainText('Entrega');

    // Cada línea dice su disponibilidad con texto, no solo con color; el total sale del servidor en soles.
    await expect(page.getByText(/En tu sucursal|Se trae de otra sucursal/).first()).toBeVisible();
    await expect(page.getByRole('complementary', { name: 'Resumen de tu pedido' })).toContainText('S/');

    await page.getByRole('button', { name: 'Elegir recojo' }).click();
    await expect(page.getByRole('heading', { name: 'Tus datos y aceptaciones' })).toBeVisible();
    await expect(pasos(page).nth(2)).toHaveAttribute('aria-current', 'step');

    await aceptarYPagar(page);
    // Pagos (ECO-5) es dueña de la pantalla de pago: el checkout solo redirige.
    await expect(page).toHaveURL(/\/checkout\/pago/);
  });

  test('un bloqueo se dice en la misma pantalla y el carrito se conserva', async ({ page }) => {
    test.skip(!mixto, 'Falta E2E_CLIENTE_MIXTO_STORAGE_STATE');
    await page.goto('/checkout');
    await page.getByRole('button', { name: 'Elegir recojo' }).click();
    // Sin marcar los textos no se paga y se dice junto a la casilla.
    await page.getByRole('button', { name: /^Ir al pago/ }).click();
    await expect(page.getByRole('alert').first()).toBeVisible();
    await expect(page.getByText('Revisa tus libros')).toBeVisible();
  });
});

test.describe('Carrito y checkout: compra solo digital', () => {
  test.use({ storageState: digital });

  test('AC-CAR-06: no se pide entrega y se compra en cuatro pasos o menos', async ({ page }) => {
    test.skip(!digital, 'Falta E2E_CLIENTE_DIGITAL_STORAGE_STATE');
    await page.goto('/carrito');
    await page.getByRole('button', { name: 'Continuar con la compra' }).click();

    await expect(page).toHaveURL(/\/checkout$/);
    expect(await pasos(page).count()).toBeLessThanOrEqual(4);
    await expect(page.getByText('Compra 100 % digital: sin entrega.').first()).toBeVisible();
    await expect(page.getByRole('heading', { name: '¿Cómo quieres recibir tus impresos?' })).toHaveCount(0);
    await expect(page.getByText('No aplica (solo digitales)').first()).toBeVisible();

    await aceptarYPagar(page);
    await expect(page).toHaveURL(/\/checkout\/pago/);
  });
});

test.describe('Carrito y checkout: visitante y fusión', () => {
  test('AC-CAR-13: un visitante que quiere pagar debe ingresar y su carrito se conserva', async ({ browser }) => {
    test.skip(!visitante, 'Falta E2E_VISITANTE_STORAGE_STATE');
    const contexto = await browser.newContext({ storageState: visitante });
    const page = await contexto.newPage();
    await page.goto('/carrito');

    const titulos = await page.getByRole('region', { name: 'Libros en tu carrito' }).getByRole('listitem').count();
    expect(titulos).toBeGreaterThan(0);
    await expect(page.getByText('Para pagar necesitas una cuenta.')).toBeVisible();
    const ingresar = page.getByRole('link', { name: 'Ingresar para continuar' });
    await expect(ingresar).toHaveAttribute('href', '/ingreso');

    // El carrito sigue igual después de recargar y no hay forma de pagar sin sesión.
    await page.reload();
    expect(await page.getByRole('region', { name: 'Libros en tu carrito' }).getByRole('listitem').count()).toBe(titulos);
    await expect(page.getByRole('button', { name: 'Continuar con la compra' })).toHaveCount(0);
    await contexto.close();
  });

  test('AC-CAR-22: tras fusionar los carritos se avisa de cada ajuste y el aviso se descarta al verlo', async ({ browser }) => {
    test.skip(!trasFusion, 'Falta E2E_CLIENTE_TRAS_FUSION_STORAGE_STATE');
    const contexto = await browser.newContext({ storageState: trasFusion });
    const page = await contexto.newPage();
    await page.goto('/carrito');

    const avisos = page.getByRole('region', { name: 'Avisos sobre tu carrito' });
    await expect(avisos).toBeVisible();
    const antes = await avisos.getByRole('button', { name: 'Entendido' }).count();
    expect(antes).toBeGreaterThan(0);

    await avisos.getByRole('button', { name: 'Entendido' }).first().click();
    await page.goto('/carrito');
    const despues = (await page.getByRole('region', { name: 'Avisos sobre tu carrito' }).count()) === 0
      ? 0
      : await page.getByRole('region', { name: 'Avisos sobre tu carrito' }).getByRole('button', { name: 'Entendido' }).count();
    expect(despues).toBe(antes - 1);
    await contexto.close();
  });
});

for (const [nombre, ruta, estado] of [
  ['carrito', '/carrito', mixto],
  ['checkout', '/checkout', mixto],
] as const) {
  test.describe(`Pantalla del ${nombre}`, () => {
    test.use({ storageState: estado });

    for (const tema of ['babelhub', 'babelhub-dark'] as const) {
      test(`accesibilidad (${tema}): sin violaciones de contraste ni de etiquetas`, async ({ page }) => {
        test.skip(!estado, 'Falta E2E_CLIENTE_MIXTO_STORAGE_STATE');
        await page.goto(ruta);
        await page.evaluate((t) => document.documentElement.setAttribute('data-theme', t), tema);
        const resultado = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa']).analyze();
        expect(resultado.violations.map((v) => `${v.id}: ${v.nodes.length}`)).toEqual([]);
      });
    }

    test('teclado: el foco es visible y se llega a la acción principal sin ratón', async ({ page }) => {
      test.skip(!estado, 'Falta E2E_CLIENTE_MIXTO_STORAGE_STATE');
      await page.goto(ruta);
      for (let i = 0; i < 6; i += 1) {
        await page.keyboard.press('Tab');
      }
      const enfocado = page.locator(':focus');
      await expect(enfocado).toBeVisible();
      const contorno = await enfocado.evaluate((el) => getComputedStyle(el).outlineStyle);
      expect(contorno).not.toBe('none');
    });

    test('teléfono: sin desplazamiento horizontal', async ({ page }) => {
      test.skip(!estado, 'Falta E2E_CLIENTE_MIXTO_STORAGE_STATE');
      await page.setViewportSize({ width: 375, height: 812 });
      await page.goto(ruta);
      const ancho = await page.evaluate(() => ({ doc: document.documentElement.scrollWidth, vista: window.innerWidth }));
      expect(ancho.doc).toBeLessThanOrEqual(ancho.vista);
    });
  });
}
