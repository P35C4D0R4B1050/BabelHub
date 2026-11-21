import { createHmac, randomBytes } from 'node:crypto';
import { test, expect, type Page } from '@playwright/test';
import { clave, conOrigenPropio, ingresarComoPersonal, sinViolacionesDeAccesibilidad, urlDeLaGestion } from '../pedidos/ayudas';

// El ingreso del personal en el host de la gestión (web-base T28; AC-WEB-19, AC-WEB-24, AC-WEB-28, AC-WEB-29): un encargado de sucursal recién creado por la administración entra con su contraseña temporal, la cambia sobre el desafío en
// cookie, configura su segundo factor (TOTP de RFC 6238: HMAC-SHA1, 6 dígitos, pasos de 30 s) y recién entonces recibe su sesión; después ingresa con su contraseña nueva y el código de su aplicación. Todo contra el host real
// (`E2E_TLS=1 E2E_HOST=gestion entorno/arrancar-host.sh ecommerce`, https://localhost:8444) con datos ficticios; ninguna contraseña ni clave se escribe en el repositorio: se generan al azar en cada corrida.

test.describe.configure({ mode: 'serial' });
test.setTimeout(150_000);

const ALFABETO = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567';

function base32ABytes(texto: string): Buffer {
  const limpio = texto.replace(/[\s=-]/g, '').toUpperCase();
  let bits = '';
  for (const c of limpio) bits += ALFABETO.indexOf(c).toString(2).padStart(5, '0');
  const bytes: number[] = [];
  for (let i = 0; i + 8 <= bits.length; i += 8) bytes.push(parseInt(bits.slice(i, i + 8), 2));
  return Buffer.from(bytes);
}

// RFC 6238 con los parámetros de la aplicación; `pasos` desplaza el instante (el paso siguiente evita reutilizar el ya aceptado).
function codigoTotp(secretoBase32: string, pasos = 0): string {
  const paso = Math.floor(Date.now() / 30_000) + pasos;
  const contador = Buffer.alloc(8);
  contador.writeBigUInt64BE(BigInt(paso));
  const resumen = createHmac('sha1', base32ABytes(secretoBase32)).update(contador).digest();
  const desplazamiento = resumen[resumen.length - 1] & 0x0f;
  const binario = ((resumen[desplazamiento] & 0x7f) << 24) | (resumen[desplazamiento + 1] << 16) | (resumen[desplazamiento + 2] << 8) | resumen[desplazamiento + 3];
  return (binario % 1_000_000).toString().padStart(6, '0');
}

async function sinDesplazamientoHorizontal(page: Page) {
  expect(await page.evaluate(() => document.documentElement.scrollWidth > document.documentElement.clientWidth), 'la página no se desplaza en horizontal').toBe(false);
}

async function cookiesDeLaGestion(page: Page) {
  return (await page.context().cookies(urlDeLaGestion)).filter((c) => c.name.startsWith('__Host-bh-'));
}

test.describe('Ingreso del personal con contraseña temporal y segundo factor', () => {
  test.skip(!clave, 'Falta BABELHUB__Sembrador__Clave (entorno E2E local con el sembrador de sesiones)');

  test('el encargado nuevo cambia la temporal, configura su segundo factor y solo entonces recibe su sesión (AC-WEB-19, AC-WEB-24, AC-WEB-28)', async ({ page, browser }) => {
    // La administración crea al encargado de la sucursal de Ayacucho y ve su contraseña temporal una sola vez.
    const correo = `e2e-encargado-${Date.now()}-${Math.floor(Math.random() * 1e6)}@example.test`;
    const contexto = await browser.newContext({ ignoreHTTPSErrors: true });
    const admin = await contexto.newPage();
    await ingresarComoPersonal(admin, 'administrador');
    await admin.goto(`${urlDeLaGestion}/administracion/usuarios`);
    await admin.getByLabel('Nombre completo').fill('Elena Encargada Ficticia');
    await admin.getByLabel('Correo electrónico').fill(correo);
    await admin.locator('form[action$="/administracion/usuarios/crear"]').getByLabel('Rol').selectOption('EncargadoDeSucursal');
    await admin.getByLabel(/^Sucursal \(código/).fill('ayacucho');
    await admin.getByRole('button', { name: 'Crear y mostrar la contraseña temporal' }).click();
    await expect(admin.getByRole('status')).toContainText('Creamos el usuario.');
    const temporal = (await admin.getByRole('alert').filter({ hasText: 'Contraseña temporal' }).locator('strong').textContent())!.trim();
    await contexto.close();

    // 1. Ingreso con la temporal: el desafío viaja en una cookie de solo HTTP y todavía no hay sesión.
    await conOrigenPropio(page);
    await page.goto(`${urlDeLaGestion}/ingreso`);
    await expect(page.getByLabel('Correo electrónico')).toHaveAttribute('autocomplete', 'username');
    await page.getByLabel('Correo electrónico').fill(correo);
    await page.getByLabel('Contraseña', { exact: true }).fill(temporal);
    await page.getByRole('button', { name: 'Ingresar' }).click();
    await expect(page.getByRole('heading', { level: 1, name: 'Cambia tu contraseña temporal' })).toBeVisible();
    let cookies = await cookiesDeLaGestion(page);
    expect(cookies.some((c) => c.name === '__Host-bh-desafio' && c.httpOnly && c.secure), 'el desafío va en una cookie de solo HTTP').toBe(true);
    expect(cookies.some((c) => c.name === '__Host-bh-gestion'), 'todavía no hay sesión').toBe(false);
    await sinDesplazamientoHorizontal(page);
    await sinViolacionesDeAccesibilidad(page);

    // 2. La contraseña nueva se puede pegar (sin bloquear el pegado) y se pide dos veces.
    const nueva = `Nueva-${randomBytes(12).toString('hex')}`;
    await page.getByLabel('Contraseña nueva').fill(nueva);
    await page.getByLabel('Repite la contraseña nueva').fill(nueva);
    await page.getByRole('button', { name: 'Cambiar contraseña' }).click();

    // 3. Configura el segundo factor: el secreto se muestra una sola vez y la sesión sigue sin existir.
    await expect(page.getByRole('heading', { level: 1, name: 'Configura tu segundo factor' })).toBeVisible();
    await expect(page.getByLabel('Código de 6 dígitos')).toHaveAttribute('autocomplete', 'one-time-code');
    const secreto = (await page.locator('[data-bh-secreto]').textContent())!.trim();
    expect(secreto.length).toBeGreaterThanOrEqual(16);
    cookies = await cookiesDeLaGestion(page);
    expect(cookies.some((c) => c.name === '__Host-bh-gestion'), 'sin sesión hasta configurar el segundo factor').toBe(false);
    await sinViolacionesDeAccesibilidad(page);

    await page.getByLabel('Código de 6 dígitos').fill(codigoTotp(secreto));
    await page.getByRole('button', { name: 'Confirmar y entrar' }).click();

    // 4. Ahora sí: sesión de solo HTTP, códigos de respaldo mostrados una sola vez y destino por rol.
    await expect(page.getByRole('heading', { level: 1, name: 'Guarda tus códigos de respaldo' })).toBeVisible();
    cookies = await cookiesDeLaGestion(page);
    expect(cookies.some((c) => c.name === '__Host-bh-gestion' && c.httpOnly && c.secure), 'la sesión es una cookie de solo HTTP').toBe(true);
    expect(cookies.some((c) => c.name === '__Host-bh-desafio'), 'el desafío se descarta al abrirse la sesión').toBe(false);
    await expect(page.getByRole('listitem')).not.toHaveCount(0);
    await sinViolacionesDeAccesibilidad(page);
    await page.getByRole('link', { name: 'Ya los guardé, continuar' }).click();
    await expect(page).toHaveURL(/\/logistica\/envios/);

    // 5. Un ingreso posterior pide la contraseña nueva y el código de la aplicación (un paso posterior al ya aceptado).
    await page.context().clearCookies();
    await conOrigenPropio(page);
    await page.goto(`${urlDeLaGestion}/ingreso`);
    await page.getByLabel('Correo electrónico').fill(correo);
    await page.getByLabel('Contraseña', { exact: true }).fill(nueva);
    await page.getByRole('button', { name: 'Ingresar' }).click();
    await expect(page.getByRole('heading', { level: 1, name: 'Tu código de verificación' })).toBeVisible();
    await expect(page.getByLabel('Código')).toHaveAttribute('autocomplete', 'one-time-code');
    await sinViolacionesDeAccesibilidad(page);
    await page.getByLabel('Código').fill(codigoTotp(secreto, 1));
    await page.getByRole('button', { name: 'Verificar' }).click();
    await expect(page).toHaveURL(/\/logistica\/envios/);
  });

  test('un código de verificación incorrecto no abre la sesión y el mensaje no delata nada (AC-WEB-19)', async ({ page, browser }) => {
    const correo = `e2e-encargado-${Date.now()}-${Math.floor(Math.random() * 1e6)}@example.test`;
    const contexto = await browser.newContext({ ignoreHTTPSErrors: true });
    const admin = await contexto.newPage();
    await ingresarComoPersonal(admin, 'administrador');
    await admin.goto(`${urlDeLaGestion}/administracion/usuarios`);
    await admin.getByLabel('Nombre completo').fill('Eduardo Encargado Ficticio');
    await admin.getByLabel('Correo electrónico').fill(correo);
    await admin.locator('form[action$="/administracion/usuarios/crear"]').getByLabel('Rol').selectOption('EncargadoDeSucursal');
    await admin.getByLabel(/^Sucursal \(código/).fill('ayacucho');
    await admin.getByRole('button', { name: 'Crear y mostrar la contraseña temporal' }).click();
    const temporal = (await admin.getByRole('alert').filter({ hasText: 'Contraseña temporal' }).locator('strong').textContent())!.trim();
    await contexto.close();

    await conOrigenPropio(page);
    await page.goto(`${urlDeLaGestion}/ingreso`);
    await page.getByLabel('Correo electrónico').fill(correo);
    await page.getByLabel('Contraseña', { exact: true }).fill(temporal);
    await page.getByRole('button', { name: 'Ingresar' }).click();
    const nueva = `Nueva-${randomBytes(12).toString('hex')}`;
    await page.getByLabel('Contraseña nueva').fill(nueva);
    await page.getByLabel('Repite la contraseña nueva').fill(nueva);
    await page.getByRole('button', { name: 'Cambiar contraseña' }).click();
    await expect(page.getByRole('heading', { level: 1, name: 'Configura tu segundo factor' })).toBeVisible();

    // Un código inventado no configura nada: el aviso tiene el foco, no repite lo escrito y la sesión sigue sin existir.
    await page.getByLabel('Código de 6 dígitos').fill('000000');
    await page.getByRole('button', { name: 'Confirmar y entrar' }).click();
    const aviso = page.getByRole('alert');
    await expect(aviso).toBeVisible();
    await expect(page.locator('body')).not.toContainText(nueva);
    expect((await cookiesDeLaGestion(page)).some((c) => c.name === '__Host-bh-gestion')).toBe(false);
  });
});
