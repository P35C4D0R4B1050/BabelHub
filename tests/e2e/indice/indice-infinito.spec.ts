import { test, expect, type Page } from '@playwright/test';
import { conOrigenPropio, sinViolacionesDeAccesibilidad } from '../pedidos/ayudas';

// El índice infinito (web-indice-infinito T15; AC-IND-02, AC-IND-04): el aviso de texto generado y el enlace al catálogo real, «otra página al azar», «copiar enlace» con y sin JavaScript, el formulario «Ir a una dirección», el mensaje amable de una
// dirección no válida, el teclado y el foco, los temas claro y oscuro, el movimiento reducido, las pantallas pequeñas, sin violaciones de la CSP en la consola y la separación del contenido respecto del carrito y la búsqueda. Es una pantalla pública: solo
// necesita el host de la tienda (mismo entorno que `base/`). La revisión visual contra `prototype/ui-v2/tienda/indice-infinito.html` (`ui-review`) se registra en `specs/web-indice-infinito/verificacion.md`.

test.beforeEach(async ({ page }) => conOrigenPropio(page));

const URL_FIJA = '/indice-infinito/s12-p3-e4-v5-pg100';

function urlBase(): string {
  return new URL('/', test.info().project.use.baseURL ?? process.env.E2E_BASE_URL ?? 'https://localhost:8443').toString();
}

const direccionDe = async (page: Page) => page.getByLabel('Dirección', { exact: true }).inputValue();
const textoDe = async (page: Page) => page.getByRole('region', { name: 'Página generada' }).locator('pre').innerText();

test.describe('El índice infinito', () => {
  test('se llega desde el inicio de la tienda y la página trae el aviso, el texto y el enlace al catálogo real (AC-IND-04)', async ({ page }) => {
    await page.goto('/');
    await page.getByRole('link', { name: 'El índice infinito' }).click();

    await expect(page).toHaveURL(/\/indice-infinito\/[a-z0-9-]+$/);
    await expect(page.getByRole('heading', { level: 1, name: 'El índice infinito' })).toBeVisible();
    await expect(page.getByText('no son libros reales ni están a la venta')).toBeVisible();
    await expect(page.getByRole('region', { name: 'Página generada' }).locator('pre')).toBeVisible();
    const catalogo = page.getByRole('link', { name: 'Vuelve al catálogo de BabelHub' });
    await expect(catalogo).toHaveAttribute('href', '/catalogo');
    await catalogo.click();
    await expect(page).toHaveURL(/\/catalogo/);
  });

  test('la misma dirección da el mismo texto y la dirección viaja en la ruta (AC-IND-01, AC-IND-03)', async ({ page, browser }) => {
    await page.goto(URL_FIJA);
    const primero = await textoDe(page);
    await page.reload();

    expect(await textoDe(page)).toBe(primero);
    expect(await direccionDe(page)).toBe('s12-p3-e4-v5-pg100');
    expect(primero.split('\n')).toHaveLength(20);
    // Otra persona (otro contexto, sin cookies) abre el mismo enlace y ve la misma página.
    const otra = await browser.newContext({ ignoreHTTPSErrors: true, baseURL: test.info().project.use.baseURL });
    try {
      const paginaAjena = await otra.newPage();
      await conOrigenPropio(paginaAjena);
      await paginaAjena.goto(URL_FIJA);
      expect(await textoDe(paginaAjena)).toBe(primero);
    } finally {
      await otra.close();
    }
  });

  test('«Otra página al azar» cambia la dirección y el texto (AC-IND-02)', async ({ page }) => {
    await page.goto(URL_FIJA);
    const antesTexto = await textoDe(page);

    await page.getByRole('link', { name: 'Otra página al azar' }).click();

    await expect(page).not.toHaveURL(new RegExp(`${URL_FIJA}$`));
    expect(await direccionDe(page)).not.toBe('s12-p3-e4-v5-pg100');
    expect(await textoDe(page)).not.toBe(antesTexto);
  });

  test('«Copiar enlace» copia la dirección absoluta de la página mostrada y avisa en una región de estado (AC-IND-03)', async ({ page, browserName }) => {
    if (browserName === 'chromium') {
      await page.context().grantPermissions(['clipboard-read', 'clipboard-write']);
    }
    await page.goto(URL_FIJA);

    await page.getByRole('button', { name: 'Copiar enlace' }).click();

    const aviso = page.getByRole('status').filter({ hasText: /copiado|Copia el enlace/ });
    await expect(aviso).toBeVisible();
    if (browserName === 'chromium') {
      await expect(aviso).toHaveText('Enlace copiado.');
      expect(await page.evaluate(() => navigator.clipboard.readText())).toBe(new URL(URL_FIJA, urlBase()).toString());
    }
  });

  test('sin JavaScript la dirección se muestra en un campo de solo lectura y el botón de copiar no se ofrece (D08)', async ({ browser }) => {
    const contexto = await browser.newContext({ javaScriptEnabled: false, ignoreHTTPSErrors: true, baseURL: test.info().project.use.baseURL });
    try {
      const page = await contexto.newPage();
      await conOrigenPropio(page);
      await page.goto(URL_FIJA);

      const campo = page.getByLabel('Dirección', { exact: true });
      await expect(campo).toHaveValue('s12-p3-e4-v5-pg100');
      await expect(campo).toHaveAttribute('readonly', '');
      await expect(page.getByRole('button', { name: 'Copiar enlace' })).toBeHidden();
      await page.getByRole('link', { name: 'Otra página al azar' }).click();
      await expect(page).toHaveURL(/\/indice-infinito\/[a-z0-9-]+$/);
    } finally {
      await contexto.close();
    }
  });

  test('el formulario «Ir a una dirección» abre la página pedida y no repite lo escrito si no es válido (AC-IND-06)', async ({ page }) => {
    await page.goto(URL_FIJA);

    await page.getByLabel('Ir a una dirección').fill('  s1-p1-e1-v1-pg1  ');
    await page.getByRole('button', { name: 'Ir', exact: true }).click();
    await expect(page).toHaveURL(/\/indice-infinito\/s1-p1-e1-v1-pg1$/);
    expect(await direccionDe(page)).toBe('s1-p1-e1-v1-pg1');

    await page.getByLabel('Ir a una dirección').fill('Sala Ñandú <b>1</b>');
    await page.getByRole('button', { name: 'Ir', exact: true }).click();
    await expect(page.getByRole('heading', { name: 'Esa dirección no existe en este laberinto' })).toBeVisible();
    await expect(page.getByLabel('Ir a una dirección')).toHaveValue('');
    await expect(page.getByText('Sala Ñandú')).toHaveCount(0);
    await expect(page.getByText('no son libros reales ni están a la venta')).toBeVisible();
    await expect(page.getByRole('link', { name: 'Vuelve al catálogo de BabelHub' })).toBeVisible();
    await expect(page.getByRole('region', { name: 'Página generada' }).locator('pre')).toBeVisible();
  });

  test('una dirección no válida en la ruta da el mensaje amable y no un error (AC-IND-06)', async ({ page }) => {
    const respuesta = await page.goto('/indice-infinito/MAYUSCULAS-y-tildes-é');

    expect(respuesta!.status()).toBe(200);
    await expect(page.getByRole('heading', { name: 'Esa dirección no existe en este laberinto' })).toBeVisible();
    await expect(page.getByText('MAYUSCULAS')).toHaveCount(0);
    await sinViolacionesDeAccesibilidad(page);
  });

  test('las respuestas no se indexan ni se guardan, y el robots.txt lo desaconseja (D06)', async ({ page, request }) => {
    const respuesta = await page.goto(URL_FIJA);
    expect(respuesta!.headers()['x-robots-tag']).toBe('noindex, nofollow');
    expect(respuesta!.headers()['cache-control']).toBe('no-store');

    const robots = await request.get('/robots.txt');
    expect(await robots.text()).toMatch(/^Disallow: \/indice-infinito\s*$/m);
  });

  test('el teclado llega a los controles en orden, el texto es enfocable y el foco se ve (spec §5)', async ({ page, browserName }) => {
    await page.goto(URL_FIJA);

    const alcanzados: string[] = [];
    for (let i = 0; i < 40 && alcanzados.length < 8; i++) {
      await page.keyboard.press('Tab');
      const nombre = await page.evaluate(() => {
        const e = document.activeElement as HTMLElement | null;
        if (!e || !e.closest('main')) return '';
        return e.tagName === 'PRE' ? 'PRE' : e.getAttribute('aria-label') || e.textContent?.trim() || e.getAttribute('name') || e.tagName;
      });
      if (nombre && alcanzados.at(-1) !== nombre) alcanzados.push(nombre);
    }

    // Safari no incluye los enlaces en el orden del tabulador por omisión (solo botones y campos): ahí no se exige el enlace.
    expect(alcanzados).toEqual(expect.arrayContaining(['Copiar enlace', 'PRE', 'direccion', 'Ir']));
    expect(alcanzados.indexOf('Copiar enlace')).toBeLessThan(alcanzados.indexOf('PRE'));
    if (browserName !== 'webkit') {
      expect(alcanzados).toContain('Otra página al azar');
      expect(alcanzados.indexOf('Otra página al azar')).toBeLessThan(alcanzados.indexOf('Copiar enlace'));
    }
    // El texto enfocado muestra el foco (contorno visible, no «none»).
    await page.getByRole('region', { name: 'Página generada' }).locator('pre').focus();
    const contorno = await page.evaluate(() => getComputedStyle(document.activeElement!).outlineStyle);
    expect(contorno).not.toBe('none');
  });

  for (const tema of ['babelhub', 'babelhub-dark']) {
    test(`sin violaciones de accesibilidad A ni AA en el tema ${tema} (AC-IND-04)`, async ({ page }) => {
      await page.context().addCookies([{ name: 'bh-tema', value: tema, url: urlBase() }]);
      await page.goto(URL_FIJA);

      await expect(page.locator('html')).toHaveAttribute('data-theme', tema);
      await sinViolacionesDeAccesibilidad(page);
    });
  }

  test('con movimiento reducido no hay animaciones ni transiciones (spec §5)', async ({ page }) => {
    await page.emulateMedia({ reducedMotion: 'reduce' });
    await page.goto(URL_FIJA);

    const conMovimiento = await page.evaluate(
      () =>
        [...document.querySelectorAll<HTMLElement>('main *')].filter((e) => {
          const c = getComputedStyle(e);
          return (c.animationName !== 'none' && parseFloat(c.animationDuration) > 0) || parseFloat(c.transitionDuration) > 0;
        }).length,
    );
    expect(conMovimiento).toBe(0);
    await page.getByRole('link', { name: 'Otra página al azar' }).click();
    await expect(page.getByRole('region', { name: 'Página generada' }).locator('pre')).toBeVisible();
  });

  for (const ancho of [320, 375, 768]) {
    test(`a ${ancho} px no se desplaza en horizontal y el texto se ajusta (AC-IND-04)`, async ({ page }) => {
      await page.setViewportSize({ width: ancho, height: 800 });
      await page.goto(URL_FIJA);

      expect(await page.evaluate(() => document.documentElement.scrollWidth > document.documentElement.clientWidth)).toBe(false);
      await expect(page.getByRole('link', { name: 'Otra página al azar' })).toBeVisible();
    });
  }

  test('no hay violaciones de la CSP ni errores en la consola (D06)', async ({ page }) => {
    const problemas: string[] = [];
    page.on('console', (m) => {
      // WebKit informa dos veces por página «Refused to apply a stylesheet» en toda la tienda (también en / y /catalogo: es de la plataforma, no del índice; hallazgo Menor registrado): se excluye solo ese mensaje.
      if (/Refused to apply a stylesheet/i.test(m.text())) return;
      if (m.type() === 'error' || /Content Security Policy|Refused to/i.test(m.text())) problemas.push(m.text());
    });
    page.on('pageerror', (e) => problemas.push(e.message));

    await page.goto(URL_FIJA);
    await page.getByRole('button', { name: 'Copiar enlace' }).click();
    await page.getByRole('link', { name: 'Otra página al azar' }).click();
    await page.waitForLoadState('networkidle');

    expect(problemas).toEqual([]);
  });

  test('en el host real la solicitud 61 de un origen sin sesión recibe el límite excedido con las cabeceras del índice (AC-IND-07)', async ({ request }) => {
    const origen = { 'X-Origen-Cliente': '203.0.113.' + (200 + Math.floor(Math.random() * 50)) };
    let limite = null as Awaited<ReturnType<typeof request.get>> | null;
    // 60 solicitudes por minuto por origen sin sesión (SEG-18); se cuenta por origen y no por ruta.
    for (let i = 0; i < 70 && limite === null; i++) {
      const r = await request.get(i % 2 === 0 ? URL_FIJA : '/indice-infinito/acciones/al-azar?desde=s1-p1-e1-v1-pg1', { headers: origen, maxRedirects: 0 });
      if (r.status() === 429) limite = r;
      else expect([200, 302]).toContain(r.status());
    }

    expect(limite, 'debió llegar el límite antes de la solicitud 70').not.toBeNull();
    expect(limite!.headers()['x-robots-tag']).toBe('noindex, nofollow');
    expect(limite!.headers()['cache-control']).toBe('no-store');
    expect(limite!.headers()['retry-after']).toBeDefined();
    expect((await limite!.json()).code).toBe('ECO-LIMITE-EXCEDIDO');
  });

  test('el contenido del índice no usa datos del catálogo, del carrito ni de la búsqueda (AC-IND-04)', async ({ page }) => {
    await page.goto(URL_FIJA);

    const principal = page.locator('main');
    await expect(principal.locator('a[href^="/catalogo/obras/"]')).toHaveCount(0);
    await expect(principal.locator('a[href^="/carrito"]')).toHaveCount(0);
    await expect(principal.locator('input[name="q"], [hx-get], [hx-post]')).toHaveCount(0);
    // El carrito de la cabecera sigue siendo de la plataforma, y el índice no lo toca.
    await expect(page.locator('header a[href="/carrito"]').first()).toBeAttached();
  });
});
