import { test, expect, type Page } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';

// Pantallas de acceso y cuenta de `web-base` (WEB-2, T28 a T29; AC-WEB-19, AC-WEB-20, AC-WEB-24, AC-WEB-29 a AC-WEB-33): el ingreso, el registro y la recuperación de un visitante, y la cuenta de un cliente recién sembrado.
// Se ejecutan con los proyectos obligatorios (Chromium de escritorio y de teléfono) y WebKit si está disponible; Firefox es informativo.
//
//   BABELHUB__Sembrador__Clave   la clave del sembrador (la trae el archivo de entorno de `entorno/preparar-entorno.sh`)
//   E2E_FORWARDED_PROTO=https    el host local escucha en HTTP: se le dice que el borde ya terminó el TLS
const clave = process.env.BABELHUB__Sembrador__Clave ?? process.env.E2E_SEMBRADOR_CLAVE;

// Cada recorrido llega desde su propio origen (el Inventario y los límites de Identidad cuentan por origen): direcciones del rango de documentación (RFC 5737).
async function conOrigenPropio(page: Page) {
  const ip = `${Math.random() < 0.5 ? '203.0.113' : '198.51.100'}.${1 + Math.floor(Math.random() * 253)}`;
  await page.context().setExtraHTTPHeaders({ 'X-Origen-Cliente': ip });
}

// Un cliente nuevo, creado por el camino real de ingreso; su cookie de sesión se pone en el contexto del navegador.
async function ingresarComoClienteNuevo(page: Page) {
  await conOrigenPropio(page);
  const respuesta = await page.request.post('/desarrollo/sesion', { headers: { 'X-Sembrador-De-Desarrollo': clave! }, data: { rol: 'cliente' } });
  expect(respuesta.ok(), 'el sembrador de sesiones responde').toBeTruthy();
  const estado = await respuesta.json();
  await page.context().addCookies(estado.cookies);
}

async function sinDesplazamientoHorizontal(page: Page) {
  const desborda = await page.evaluate(() => document.documentElement.scrollWidth > document.documentElement.clientWidth);
  expect(desborda, 'la página no se desplaza en horizontal').toBe(false);
}

async function sinViolacionesAA(page: Page) {
  const resultado = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa']).analyze();
  expect(resultado.violations).toEqual([]);
}

test.describe('Visitante: ingreso, registro y recuperación', () => {
  test.beforeEach(async ({ page }) => conOrigenPropio(page));

  test('el ingreso deja pegar y autocompletar, es accesible (AA), no se desplaza y respeta el tema oscuro', async ({ page }) => {
    await page.goto('/ingreso');
    await expect(page.getByRole('heading', { level: 1, name: 'Ingresar' })).toBeVisible();
    await expect(page.getByLabel('Correo electrónico')).toHaveAttribute('autocomplete', 'username');
    await expect(page.getByLabel('Contraseña')).toHaveAttribute('autocomplete', 'current-password');
    await expect(page.getByLabel('Contraseña')).toHaveAttribute('type', 'password');
    await expect(page.getByRole('link', { name: 'Olvidé mi contraseña' })).toBeVisible();
    await sinDesplazamientoHorizontal(page);
    await sinViolacionesAA(page);

    await page.context().addCookies([{ name: 'bh-tema', value: 'babelhub-dark', url: page.url() }]);
    await page.reload();
    await expect(page.locator('html')).toHaveAttribute('data-theme', 'babelhub-dark');
    await sinViolacionesAA(page);
  });

  test('un dato incorrecto dice siempre lo mismo, sin repetir lo escrito y con el foco en el aviso (AC-WEB-19)', async ({ page }) => {
    await page.goto('/ingreso');
    await page.getByLabel('Correo electrónico').fill('nadie-existe@correo.example');
    await page.getByLabel('Contraseña').fill('una frase larga que no es de nadie');
    await page.getByRole('button', { name: 'Ingresar' }).click();

    const aviso = page.getByRole('alert');
    await expect(aviso).toContainText('El correo o la contraseña no son correctos');
    await expect(aviso).toBeFocused();
    await expect(page.locator('body')).not.toContainText('una frase larga que no es de nadie');
    await expect(page.getByLabel('Contraseña')).toHaveValue('');
  });

  test('el teclado recorre el formulario de ingreso en orden y siempre con el foco visible', async ({ page }) => {
    await page.goto('/ingreso');
    const visibles: string[] = [];
    for (let i = 0; i < 12; i++) {
      await page.keyboard.press('Tab');
      const nombre = await page.evaluate(() => {
        const e = document.activeElement as HTMLElement | null;
        if (!e || e === document.body) return '';
        const sombra = getComputedStyle(e);
        const conAnillo = sombra.outlineStyle !== 'none' || sombra.boxShadow !== 'none';
        return `${e.tagName}:${(e.getAttribute('name') ?? e.textContent ?? '').trim().slice(0, 30)}:${conAnillo}`;
      });
      if (nombre) visibles.push(nombre);
    }
    expect(visibles.some((v) => v.startsWith('INPUT:correo'))).toBe(true);
    expect(visibles.some((v) => v.startsWith('INPUT:contrasena'))).toBe(true);
    expect(visibles.filter((v) => v.endsWith(':false')), 'todo elemento enfocado muestra un indicador de foco').toEqual([]);
  });

  test('el registro ofrece la aceptación de los textos, la declaración de edad y es accesible (AC-WEB-20)', async ({ page }) => {
    await page.goto('/registro');
    await expect(page.getByRole('heading', { level: 1, name: 'Crear mi cuenta' })).toBeVisible();
    await expect(page.getByLabel('Contraseña (mínimo 12 caracteres)')).toHaveAttribute('autocomplete', 'new-password');
    await expect(page.getByRole('link', { name: 'términos y condiciones' })).toHaveAttribute('href', '/legal/terminos');
    await expect(page.getByRole('link', { name: 'política de privacidad' })).toHaveAttribute('href', '/legal/privacidad');
    await expect(page.getByLabel('Declaro que soy mayor de edad.')).toBeVisible();
    await sinDesplazamientoHorizontal(page);
    await sinViolacionesAA(page);
  });

  test('la recuperación responde igual exista o no la cuenta (AC-WEB-20)', async ({ page }) => {
    await page.goto('/recuperacion');
    await expect(page.getByRole('heading', { level: 1, name: 'Recupera tu acceso' })).toBeVisible();
    await page.getByLabel('Correo electrónico').fill('nadie-existe@correo.example');
    await page.getByRole('button', { name: 'Enviarme el código' }).click();

    await expect(page.getByRole('status')).toContainText('Si el correo corresponde a una cuenta');
    await expect(page.getByLabel('Código')).toBeVisible();
    await sinViolacionesAA(page);
  });

  test('una cuenta, sin sesión, lleva al ingreso', async ({ page }) => {
    await page.goto('/cuenta');
    await expect(page).toHaveURL(/\/ingreso$/);
    await expect(page.getByRole('heading', { level: 1, name: 'Ingresar' })).toBeVisible();
  });
});

test.describe('Cliente con sesión: la cuenta', () => {
  test.beforeEach(async ({ page }) => ingresarComoClienteNuevo(page));

  test('cada sección de la cuenta existe, es accesible (AA) y no se desplaza en 375 px', async ({ page }) => {
    for (const [ruta, vinculo] of [
      ['/cuenta', 'Resumen'],
      ['/cuenta/datos', 'Mis datos'],
      ['/cuenta/segundo-factor', 'Segundo factor'],
      ['/cuenta/correo', 'Correo'],
      ['/cuenta/avisos', 'Avisos'],
      ['/cuenta/solicitudes', 'Mis datos personales'],
      ['/cuenta/cancelacion', 'Cancelar mi cuenta'],
    ] as const) {
      await page.goto(ruta);
      await expect(page.getByRole('heading', { level: 1, name: 'Mi cuenta' })).toBeVisible();
      await expect(page.getByRole('navigation', { name: 'Secciones de tu cuenta' }).getByRole('link', { name: vinculo, exact: true })).toHaveAttribute('aria-current', 'page');
      await sinDesplazamientoHorizontal(page);
      await sinViolacionesAA(page);
    }
  });

  test('guardar el nombre vuelve con un aviso y la respuesta no se guarda en el navegador (AC-WEB-29)', async ({ page }) => {
    const respuesta = await page.goto('/cuenta/datos');
    expect(respuesta!.headers()['cache-control']).toContain('no-store');
    await page.getByLabel('Nombre completo').fill('Ana Pérez Quispe');
    await page.getByRole('button', { name: 'Guardar mi nombre' }).click();

    await expect(page).toHaveURL(/\/cuenta\/datos\?aviso=nombre-guardado$/);
    await expect(page.getByRole('status')).toContainText('Guardamos tu nombre.');
    await expect(page.getByLabel('Nombre completo')).toHaveValue('Ana Pérez Quispe');
  });

  test('un aviso inventado en la dirección no se refleja en la pantalla', async ({ page }) => {
    await page.goto('/cuenta/datos?aviso=%3Cimg%20src%3Dx%20onerror%3Dwindow.__xss%3D1%3E');
    await expect(page.locator('body')).not.toContainText('onerror');
    expect(await page.evaluate(() => (window as unknown as { __xss?: number }).__xss)).toBeUndefined();
  });

  test('el segundo factor se ofrece con la contraseña actual y la solicitud de datos tiene su fecha límite (AC-WEB-30, AC-WEB-36)', async ({ page }) => {
    await page.goto('/cuenta/segundo-factor');
    await expect(page.getByRole('button', { name: 'Activar el segundo factor' })).toBeVisible();
    await page.goto('/cuenta/solicitudes');
    await page.getByLabel('Tipo de solicitud').selectOption('Acceso');
    await page.getByRole('button', { name: 'Presentar la solicitud' }).click();

    await expect(page.getByRole('status')).toContainText('Presentamos tu solicitud');
    await expect(page.getByRole('columnheader', { name: 'Fecha límite' })).toBeVisible();
    await expect(page.getByRole('row').nth(1)).toContainText(/\d{2}\/\d{2}\/\d{4}/);
  });

  test('cerrar todas las sesiones limpia el navegador y la cuenta vuelve a pedir ingreso (AC-WEB-32)', async ({ page }) => {
    await page.goto('/cuenta');
    await page.getByRole('button', { name: 'Cerrar todas mis sesiones' }).click();

    await expect(page).toHaveURL(/\/ingreso$/);
    await page.goto('/cuenta');
    await expect(page).toHaveURL(/\/ingreso$/);
  });
});
