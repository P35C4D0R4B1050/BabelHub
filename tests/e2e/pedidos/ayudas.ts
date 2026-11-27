import { expect, type Page } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';

// Las ayudas comunes de los recorridos de compra y de gestión de pedidos (carrito, checkout, pago y pantallas del personal), contra el entorno local de `entorno/` con datos ficticios.
//   BABELHUB__Sembrador__Clave   la clave del sembrador de sesiones (el archivo de entorno de `entorno/preparar-entorno.sh`)
//   E2E_BASE_URL                 https://localhost:8443 (host de la tienda, E2E_TLS=1)
//   E2E_GESTION_URL              https://localhost:8444 (host de la gestión: `E2E_TLS=1 E2E_HOST=gestion arrancar-host.sh ecommerce`)
export const clave = process.env.BABELHUB__Sembrador__Clave ?? process.env.E2E_SEMBRADOR_CLAVE;

export const urlDeLaGestion = process.env.E2E_GESTION_URL ?? 'https://localhost:8444';

export const EDICION_IMPRESA_DEL_ZORRO = 'b0000000-0000-0000-0000-000000000011';

export const EDICION_DIGITAL_DEL_ZORRO = 'b0000000-0000-0000-0000-000000000012';

// Cada recorrido llega desde su propio origen (el borde real pone X-Origen-Cliente): el Inventario limita las reservas a 10 por hora por origen (SEL-19) y todos los recorridos salen de esta máquina.
// El host local confía en 127.0.0.1 como proxy (BABELHUB__Ecommerce__ProxiesConocidos del archivo de entorno); las direcciones son del rango de documentación (RFC 5737), públicas para el borde.
export async function conOrigenPropio(page: Page) {
  const ip = `${Math.random() < 0.5 ? '203.0.113' : '198.51.100'}.${1 + Math.floor(Math.random() * 253)}`;
  await page.context().setExtraHTTPHeaders({ 'X-Origen-Cliente': ip });
}

// Un cliente nuevo, creado por el camino real de ingreso; su cookie de sesión se pone en el contexto del navegador.
export async function ingresarComoClienteNuevo(page: Page) {
  await conOrigenPropio(page);
  const respuesta = await page.request.post('/desarrollo/sesion', { headers: { 'X-Sembrador-De-Desarrollo': clave! }, data: { rol: 'cliente' } });
  expect(respuesta.ok(), 'el sembrador de sesiones de la tienda responde').toBeTruthy();
  const estado = await respuesta.json();
  await page.context().addCookies(estado.cookies);
}

// Una cuenta del personal en el host de la gestión (el encargado con el código de su sucursal): su cookie `__Host-bh-gestion` convive con la de la tienda en el mismo contexto.
export async function ingresarComoPersonal(page: Page, rol: 'encargado' | 'logistica' | 'administrador', sucursal?: string) {
  await conOrigenPropio(page);
  const respuesta = await page.request.post(`${urlDeLaGestion}/desarrollo/sesion`, {
    headers: { 'X-Sembrador-De-Desarrollo': clave! },
    data: rol === 'encargado' ? { rol, sucursal } : { rol },
  });
  expect(respuesta.ok(), `el sembrador de sesiones de la gestión responde para ${rol}`).toBeTruthy();
  await page.context().addCookies((await respuesta.json()).cookies);
}

// La obra sembrada de cada edición y su formato (`entorno/sembrar-datos.sh`).
const FICHAS: Record<string, { obra: string; digital: boolean }> = {
  [EDICION_IMPRESA_DEL_ZORRO]: { obra: 'a0000000-0000-0000-0000-000000000001', digital: false },
  [EDICION_DIGITAL_DEL_ZORRO]: { obra: 'a0000000-0000-0000-0000-000000000001', digital: true },
};

// Agrega al carrito como lo hace una persona: abre la ficha y envía su formulario (nunca un POST armado a mano: si la ficha deja de ofrecer comprar, el recorrido falla). Termina en el carrito.
export async function agregarAlCarrito(page: Page, edicionId: string, cantidad: number) {
  const ficha = FICHAS[edicionId];
  await page.goto(`/catalogo/obras/${ficha.obra}?edicion=${ficha.digital ? 'digital' : 'impresa'}`);
  if (!ficha.digital) {
    await page.getByLabel('Cantidad').fill(String(cantidad));
  }
  await page.getByRole('button', { name: ficha.digital ? 'Comprar digital' : 'Agregar impreso al carrito' }).click();
  await expect(page, 'agregar al carrito lleva al carrito').toHaveURL(/\/carrito$/);
  await expect(page.getByRole('heading', { level: 1, name: 'Tu carrito' })).toBeVisible();
}

async function aceptarLosTextosYPagar(page: Page) {
  for (const casilla of await page.getByRole('group', { name: 'Para continuar, acepta:' }).getByRole('checkbox').all()) {
    await casilla.check();
  }
  await page.getByRole('button', { name: /^Ir al pago/ }).click();
  await expect(page).toHaveURL(/\/checkout\/pago/);
  await expect(page.getByRole('heading', { name: 'Pagar tu compra' })).toBeVisible();
  await page.getByLabel(/Identificador del medio de pago/).fill('tok_aprobado');
  await page.getByRole('button', { name: /^Pagar S\// }).click();
  await expect(page).toHaveURL(/\/checkout\/pago\/estado/);
}

// Carrito → checkout con recojo en Ayacucho → pago aprobado del simulador. Termina en la pantalla de estado del pago.
export async function comprarUnLibroConRecojo(page: Page, cantidad = 1) {
  await agregarAlCarrito(page, EDICION_IMPRESA_DEL_ZORRO, cantidad);
  await page.goto('/carrito');
  await expect(page.getByRole('heading', { level: 1, name: 'Tu carrito' })).toBeVisible();
  await expect(page.getByText('El zorro y el cóndor del Ande', { exact: true }).first()).toBeVisible();
  await expect(page.getByText('Hay stock en la red')).toBeVisible();
  await page.getByRole('button', { name: 'Continuar con la compra' }).click();

  await expect(page).toHaveURL(/\/checkout$/);
  await page.getByLabel('Sucursal de recojo').selectOption({ label: 'Sucursal Ayacucho (prueba)' });
  await page.getByRole('button', { name: 'Elegir recojo' }).click();
  await expect(page.getByRole('heading', { name: 'Tus datos y aceptaciones' })).toBeVisible();
  await aceptarLosTextosYPagar(page);
}

// Lo mismo con despacho a domicilio: el contacto se da en el checkout (datos ficticios) y viaja al pedido.
export async function comprarUnLibroADomicilio(page: Page) {
  await agregarAlCarrito(page, EDICION_IMPRESA_DEL_ZORRO, 1);
  await page.goto('/carrito');
  await page.getByRole('button', { name: 'Continuar con la compra' }).click();

  await expect(page).toHaveURL(/\/checkout$/);
  await page.getByLabel('Nombre completo').fill('Rosa Quispe Ficticia');
  await page.getByLabel('Celular de contacto').fill('987654321');
  await page.getByLabel('Dirección de entrega').fill('Jr. Ficticio 123, Ayacucho');
  await page.getByLabel('Región').fill('ayacucho');
  await page.getByRole('button', { name: 'Elegir domicilio' }).click();
  await expect(page.getByRole('heading', { name: 'Tus datos y aceptaciones' })).toBeVisible();
  await aceptarLosTextosYPagar(page);
}

// El pedido lo crea Pagos al confirmar la compra aprobada (en el mismo paso o por la reanudación del trabajo): se espera con recargas, sin dormir a ciegas.
export async function esperarElPedido(page: Page) {
  await expect
    .poll(
      async () => {
        await page.goto('/cuenta/pedidos');
        return await page.getByRole('link', { name: /^Ver el pedido/ }).count();
      },
      { timeout: 90_000, intervals: [1_000, 2_000, 3_000] },
    )
    .toBeGreaterThan(0);
}

// La ruta del primer pedido de la lista del titular (para abrirlo desde el personal con su identificador).
export async function rutaDelPrimerPedido(page: Page) {
  await esperarElPedido(page);
  return (await page.getByRole('link', { name: /^Ver el pedido/ }).first().getAttribute('href'))!;
}

export async function sinViolacionesDeAccesibilidad(page: Page) {
  const resultado = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa']).analyze();
  expect(resultado.violations.map((v) => `${v.id}: ${v.nodes.map((n) => `${n.target.join(' ')} :: ${n.any[0]?.message ?? ''}`).join(' | ')}`)).toEqual([]);
}

// Carrito solo digital → checkout sin entrega → pago aprobado del simulador (CAR-07; AC-CAR-06). Termina en la pantalla de estado del pago.
export async function comprarSoloDigital(page: Page) {
  await agregarAlCarrito(page, EDICION_DIGITAL_DEL_ZORRO, 1);
  await page.goto('/carrito');
  await page.getByRole('button', { name: 'Continuar con la compra' }).click();
  await expect(page).toHaveURL(/\/checkout$/);
  await expect(page.getByText('Compra 100 % digital: sin entrega.').first()).toBeVisible();
  await aceptarLosTextosYPagar(page);
}
