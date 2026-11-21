import { test, expect, type Page } from '@playwright/test';
import { clave, conOrigenPropio, ingresarComoClienteNuevo, ingresarComoPersonal, urlDeLaGestion } from '../pedidos/ayudas';

// La sesión tras el ingreso real vista desde el navegador (web-base T35; AC-WEB-03, AC-WEB-22, AC-WEB-28, AC-WEB-34): el JWT y la renovación nunca llegan al navegador (ni en cookies legibles, ni en el almacenamiento, ni en el cuerpo de
// ninguna respuesta), `localStorage` y `sessionStorage` quedan vacíos y «atrás» tras cerrar la sesión no muestra páginas de la persona anterior. Las sesiones salen del sembrador de desarrollo, que ingresa por el camino real.
// Lo que depende del reloj (el cliente actúa cada 20 minutos hasta el tope y el encargado vuelve al ingreso a las 8 horas) y los textos pendientes de aceptar no se esperan en un navegador: los prueban con el reloj manual y la
// política real `SesionesWebPostgresTests`, `AccesoDeLaInterfazTests`, `CookieYCierreDeSesionTests` y `AceptacionDeTextosTests` de `tests/integration/BabelHub.IntegrationTests/Web`.

test.describe.configure({ mode: 'serial' });
// Cada prueba llega desde su propio origen: el tope previo por origen y hora (SEG) cuenta las solicitudes de toda la corrida desde esta máquina.
test.beforeEach(async ({ page }) => conOrigenPropio(page));
test.setTimeout(90_000);

const JWT = /eyJ[A-Za-z0-9_-]{8,}\.eyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}/;

// Recoge el texto de toda respuesta de documento o de petición de la página para buscar en él lo que no debe llegar al navegador.
function recolectar(page: Page) {
  const cuerpos: { url: string; texto: string; cabeceras: string }[] = [];
  page.on('response', async (r) => {
    const tipo = r.headers()['content-type'] ?? '';
    if (!/text|json|javascript/i.test(tipo)) return;
    try {
      cuerpos.push({ url: r.url(), texto: await r.text(), cabeceras: JSON.stringify(r.headers()) });
    } catch {
      /* una redirección o una respuesta cancelada no tiene cuerpo */
    }
  });
  return cuerpos;
}

async function afirmarQueNadaDeLaSesionLlegaAlNavegador(page: Page, cuerpos: { url: string; texto: string; cabeceras: string }[], nombreDeLaCookie: string) {
  const cookies = (await page.context().cookies()).filter((c) => c.name.startsWith('__Host-bh-'));
  const sesion = cookies.find((c) => c.name === nombreDeLaCookie);
  expect(sesion, `la cookie ${nombreDeLaCookie} existe`).toBeTruthy();
  // Toda cookie de la aplicación es de solo HTTP, segura y de sitio propio: ningún script la lee.
  for (const c of cookies) {
    expect(c.httpOnly, `${c.name} es HttpOnly`).toBe(true);
    expect(c.secure, `${c.name} es Secure`).toBe(true);
  }
  expect(await page.evaluate(() => document.cookie), 'document.cookie no trae nada de la sesión').not.toContain('__Host-bh-tienda');
  expect(await page.evaluate(() => document.cookie)).not.toContain('__Host-bh-gestion');
  // El almacenamiento del navegador no guarda credenciales: `localStorage` vacío y, en `sessionStorage`, solo la ruta de historial que la biblioteca htmx deja por su cuenta (nunca un valor que parezca un JWT ni la cookie de sesión).
  expect(await page.evaluate(() => Object.keys(localStorage)), 'localStorage vacío').toEqual([]);
  const sesionStorage = await page.evaluate(() => Object.fromEntries(Object.keys(sessionStorage).map((k) => [k, sessionStorage.getItem(k) ?? ''])));
  expect(Object.keys(sesionStorage).filter((k) => k !== 'htmx-current-path-for-history'), 'sessionStorage sin claves propias').toEqual([]);
  for (const [k, v] of Object.entries(sesionStorage)) {
    expect(JWT.test(v) || v.includes(sesion!.value), `sessionStorage[${k}] sin credenciales`).toBe(false);
  }
  // Ningún cuerpo ni cabecera de respuesta trae un JWT ni el valor de la cookie de sesión (la renovación vive solo en el servidor).
  for (const { url, texto, cabeceras } of cuerpos) {
    expect(JWT.test(texto), `un JWT en el cuerpo de ${url}`).toBe(false);
    expect(texto.includes(sesion!.value), `el valor de la cookie de sesión en el cuerpo de ${url}`).toBe(false);
    expect(JWT.test(cabeceras), `un JWT en las cabeceras de ${url}`).toBe(false);
  }
}

test.describe('La sesión vista desde el navegador', () => {
  test.skip(!clave, 'Falta BABELHUB__Sembrador__Clave (entorno E2E local con el sembrador de sesiones)');

  test('el JWT y la renovación no llegan al navegador en la tienda: cookies de solo HTTP, almacenamiento vacío y ningún cuerpo con credenciales (AC-WEB-03, AC-WEB-28)', async ({ page }) => {
    const cuerpos = recolectar(page);
    await ingresarComoClienteNuevo(page);
    for (const ruta of ['/', '/catalogo', '/carrito', '/cuenta', '/cuenta/datos', '/cuenta/pedidos', '/biblioteca']) {
      await page.goto(ruta);
      await page.waitForLoadState('networkidle');
    }
    expect(cuerpos.length).toBeGreaterThan(5);
    await afirmarQueNadaDeLaSesionLlegaAlNavegador(page, cuerpos, '__Host-bh-tienda');
  });

  test('lo mismo en la gestión para el personal (AC-WEB-03, AC-WEB-28)', async ({ page }) => {
    const cuerpos = recolectar(page);
    await ingresarComoPersonal(page, 'administrador');
    for (const ruta of ['/administracion/obras', '/administracion/usuarios', '/administracion/auditoria']) {
      await page.goto(`${urlDeLaGestion}${ruta}`);
      await page.waitForLoadState('networkidle');
    }
    expect(cuerpos.length).toBeGreaterThan(2);
    await afirmarQueNadaDeLaSesionLlegaAlNavegador(page, cuerpos, '__Host-bh-gestion');
  });

  test('«atrás» tras cerrar la sesión no muestra páginas de la persona anterior y la cookie de sesión desaparece (AC-WEB-22)', async ({ page }) => {
    await ingresarComoClienteNuevo(page);
    await page.goto('/cuenta');
    await expect(page.getByRole('heading', { level: 1, name: 'Mi cuenta' })).toBeVisible();
    await page.goto('/cuenta/pedidos');
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible();

    await page.getByRole('button', { name: 'Cerrar sesión' }).click();
    await page.waitForLoadState('networkidle');
    await expect.poll(async () => (await page.context().cookies()).some((c) => c.name === '__Host-bh-tienda'), { message: 'la cookie de sesión se borra', timeout: 10_000 }).toBe(false);

    // Cada «atrás» vuelve a pedir la página al servidor (sin guardarla en el navegador): la persona anterior no se ve.
    for (let i = 0; i < 2; i++) {
      await page.goBack();
      await page.waitForLoadState('networkidle');
      await expect(page.getByRole('heading', { level: 1, name: 'Mi cuenta' })).toHaveCount(0);
      await expect(page.locator('body')).not.toContainText('Mis pedidos de');
    }
    await page.goto('/cuenta');
    await expect(page).toHaveURL(/\/ingreso$/);
  });
});
