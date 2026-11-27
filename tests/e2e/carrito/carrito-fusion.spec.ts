import { randomBytes } from 'node:crypto';
import { test, expect, type Browser, type Locator, type Page } from '@playwright/test';
import { agregarAlCarrito, clave, comprarSoloDigital, conOrigenPropio, EDICION_DIGITAL_DEL_ZORRO, EDICION_IMPRESA_DEL_ZORRO, LIMA } from '../pedidos/ayudas';

// La fusión de carritos al ingresar (ecommerce-carrito-checkout T36; CAR-24, CAR-38; AC-CAR-22, AC-CAR-33) con el ingreso REAL de web-identidad: un visitante con un carrito ingresa por la pantalla `/ingreso` con el correo y
// la contraseña de una cuenta que ya tiene su propio carrito y un libro digital comprado. La cuenta se siembra con el sembrador de desarrollo (que acepta una contraseña elegida por la prueba, generada al azar en cada
// corrida y nunca escrita en el repositorio ni devuelta por el sembrador), pero la sesión del visitante nace del formulario de ingreso, no de una cookie sembrada: es ese camino el que dispara la fusión.
//   Datos: `entorno/sembrar-datos.sh --limpiar`; BABELHUB__Sembrador__Clave; E2E_BASE_URL=https://localhost:8443.

test.describe.configure({ mode: 'serial' });

const UNIDADES_EN_LA_CUENTA = 8;
const UNIDADES_DEL_VISITANTE = 10;
// Lima tiene 12 vendibles sembrados: la fusión recorta al vendible de la sucursal de la línea.
const VENDIBLE_EN_LIMA = 12;

// Una contraseña larga y aleatoria que cumple la política (frase de 20 o más caracteres, sin patrones comunes).
const contrasena = `e2e-${randomBytes(12).toString('hex')}-Fusion`;

async function sembrarClienteConContrasena(browser: Browser) {
  const contexto = await browser.newContext({ ignoreHTTPSErrors: true });
  const page = await contexto.newPage();
  await conOrigenPropio(page);
  const respuesta = await page.request.post('/desarrollo/sesion', { headers: { 'X-Sembrador-De-Desarrollo': clave! }, data: { rol: 'cliente', contrasena } });
  expect(respuesta.ok(), 'el sembrador de sesiones de la tienda responde').toBeTruthy();
  const estado = await respuesta.json();
  await page.context().addCookies(estado.cookies);
  const correo = respuesta.headers()['x-correo-sembrado'];
  expect(correo, 'el sembrador devuelve el correo de la cuenta').toBeTruthy();
  return { contexto, page, correo };
}

// «Entendido» envía un formulario: se espera la respuesta del POST antes de recargar (si no, la recarga cancela el envío en WebKit).
async function verAviso(page: Page, boton: Locator) {
  await Promise.all([page.waitForResponse((r) => r.request().method() === 'POST' && r.url().includes('/visto')), boton.click()]);
}

test.describe('Fusión del carrito al ingresar (ingreso real)', () => {
  test.skip(!clave, 'Falta BABELHUB__Sembrador__Clave (entorno E2E local con el sembrador de sesiones)');
  test.setTimeout(180_000);

  test('AC-CAR-22, AC-CAR-33: al ingresar se fusionan los carritos, se descarta el digital poseído, se recorta al vendible de la sucursal y cada aviso se descarta al verlo', async ({ browser }) => {
    // 1. La cuenta: compra el digital (queda en su biblioteca) y deja 8 unidades del impreso de Lima en su carrito.
    const cuenta = await sembrarClienteConContrasena(browser);
    await comprarSoloDigital(cuenta.page);
    await expect
      .poll(async () => {
        await cuenta.page.goto('/biblioteca');
        return await cuenta.page.getByText('El zorro y el cóndor del Ande').count();
      }, { timeout: 60_000, intervals: [1_000, 2_000, 3_000] })
      .toBeGreaterThan(0);
    await agregarAlCarrito(cuenta.page, EDICION_IMPRESA_DEL_ZORRO, UNIDADES_EN_LA_CUENTA, LIMA);
    await cuenta.contexto.close();

    // 2. El visitante (sin sesión): el mismo digital y 10 unidades del impreso en la misma sucursal; sumadas pasarían de lo vendible allí.
    const contexto = await browser.newContext({ ignoreHTTPSErrors: true });
    const page = await contexto.newPage();
    await conOrigenPropio(page);
    await agregarAlCarrito(page, EDICION_IMPRESA_DEL_ZORRO, UNIDADES_DEL_VISITANTE, LIMA);
    await agregarAlCarrito(page, EDICION_DIGITAL_DEL_ZORRO, 1);
    await page.goto('/carrito');
    await expect(page.getByText('Para pagar necesitas una cuenta.')).toBeVisible();

    // 3. Ingresa por la pantalla real: ahí se fusiona el carrito del visitante con el de la cuenta.
    await page.goto('/ingreso');
    await page.getByLabel('Correo electrónico').fill(cuenta.correo);
    await page.getByLabel('Contraseña').fill(contrasena);
    await page.getByRole('button', { name: 'Ingresar' }).click();
    await expect(page).not.toHaveURL(/\/ingreso/);

    // 4. El impreso queda con lo vendible de su sucursal (nunca la suma), el digital poseído se descarta y se avisa de ambos ajustes.
    await page.goto('/carrito');
    const cantidad = page.getByRole('spinbutton', { name: /^Cantidad de / });
    await expect(cantidad).toHaveCount(1);
    const final = Number(await cantidad.inputValue());
    expect(final).toBe(VENDIBLE_EN_LIMA);
    await expect(page.getByText('Una licencia por cuenta.')).toHaveCount(0);

    const avisos = page.getByRole('region', { name: 'Avisos sobre tu carrito' });
    await expect(avisos).toBeVisible();
    await expect(avisos.getByRole('button', { name: 'Entendido' })).toHaveCount(2);

    // 5. Ver un aviso lo descarta; el otro sigue hasta que también se vea. Ingresar de nuevo no duplica nada.
    await verAviso(page, avisos.getByRole('button', { name: 'Entendido' }).first());
    await page.goto('/carrito');
    await expect(page.getByRole('region', { name: 'Avisos sobre tu carrito' }).getByRole('button', { name: 'Entendido' })).toHaveCount(1);
    await verAviso(page, page.getByRole('region', { name: 'Avisos sobre tu carrito' }).getByRole('button', { name: 'Entendido' }));
    await page.goto('/carrito');
    await expect(page.getByRole('region', { name: 'Avisos sobre tu carrito' })).toHaveCount(0);
    await expect(page.getByRole('spinbutton', { name: /^Cantidad de / })).toHaveCount(1);
    await contexto.close();
  });
});
