import { test, expect } from '@playwright/test';
import { readFileSync } from 'node:fs';
import { createHash } from 'node:crypto';
import { join } from 'node:path';
import { abrir, empezar, hacerA, hacerB1, hacerB2, hacerC } from './ayudas';

// La herramienta no envía datos a ningún sitio, no carga nada externo, no usa eval ni manejadores en línea y funciona sin conexión.
// (security-review de web-base T32b a T34b: sin telemetría, sin envío de datos, compatible con una política de contenido estricta.)

const html = readFileSync(join(__dirname, '../../../tools/pruebas-humanas/index.html'), 'utf8');

test.describe('Código fuente y arranque', () => {
  test('es un solo archivo sin recursos externos, sin llamadas de red, sin eval y sin manejadores en línea', () => {
    expect(html, 'scripts externos').not.toMatch(/<script[^>]*\ssrc=/i);
    expect(html, 'hojas de estilo externas').not.toMatch(/<link[^>]*rel=["']?stylesheet/i);
    expect(html, 'importaciones CSS').not.toMatch(/@import/i);
    expect(html, 'recursos por URL absoluta (src o url())').not.toMatch(/(?:src|srcset)=["']https?:/i);
    expect(html).not.toMatch(/url\(\s*["']?https?:/i);
    for (const prohibido of ['fetch(', 'XMLHttpRequest', 'WebSocket', 'sendBeacon', 'EventSource', 'importScripts', 'serviceWorker', 'eval(', 'new Function', 'document.write', 'window.open', 'postMessage', 'location.href', 'location.assign', 'location.replace', 'setTimeout("', "setTimeout('", 'insertAdjacentHTML']) {
      expect(html.includes(prohibido), `no debe usar ${prohibido}`).toBe(false);
    }
    expect(html, 'manejadores en línea (onclick=, onerror=…)').not.toMatch(/\son[a-z]+\s*=\s*["']/i);
    expect(html, 'formularios').not.toMatch(/<form[\s>]/i);
  });

  test('declara una política de contenido que impide cualquier conexión', () => {
    const csp = html.match(/http-equiv="Content-Security-Policy" content="([^"]+)"/)?.[1] ?? '';
    expect(csp).toContain("default-src 'none'");
    expect(csp).toContain("connect-src 'none'");
    expect(csp).toContain("form-action 'none'");
    expect(csp).toContain("base-uri 'none'");
    expect(csp).not.toContain("'unsafe-eval'");
    // Solo corre el script exacto de la página: un script o manejador inyectado no se ejecuta (script-src con hash, sin 'unsafe-inline').
    const scripts = [...html.matchAll(/<script>([\s\S]*?)<\/script>/g)];
    expect(scripts, 'un único <script> sin atributos').toHaveLength(1);
    const hash = createHash('sha256').update(scripts[0][1], 'utf8').digest('base64');
    expect(csp, 'el hash está al día: ejecuta «node tools/pruebas-humanas/fijar-csp.mjs»').toContain(`script-src 'sha256-${hash}'`);
    expect(csp.match(/script-src[^;]*/)![0]).not.toContain('unsafe-inline');
    expect(csp, 'sin orígenes externos').not.toMatch(/https?:\/\//);
  });

  test('la app arranca vacía: sin respuestas, sin personas y sin resultados de ejemplo', async ({ page }) => {
    await abrir(page);
    const ex = await page.evaluate(() => (window as any).__babelhubPruebasHumanas.construirExport());
    expect(ex.participantes).toEqual([]);
    const guardado = JSON.parse(await page.evaluate(() => localStorage.getItem('babelhub-pruebas-humanas:datos:v2')!));
    expect(guardado.otros).toEqual([]);
    for (const k of ['a', 'b1', 'b2', 'c']) expect(guardado.yo[k].estado).toBe('pendiente');
    expect(guardado.yo.c.sus.every((v: unknown) => v === null)).toBe(true);
    await page.goto('/#/avanzado');
    await expect(page.getByText('Todavía no hay resultados.')).toBeVisible();
  });

  test('no trae datos precargados de personas en el código (ni JSON, ni correos, ni teléfonos)', () => {
    expect(html).not.toMatch(/[\w.+-]+@(?!prueba\.invalid)[\w-]+\.[a-z]{2,}/i);
    expect(html).not.toMatch(/"participantes"\s*:\s*\[\s*\{/);
  });
});

test.describe('En ejecución', () => {
  test('solo se pide el documento: ninguna otra solicitud de red al recorrer y usar toda la app', async ({ page }) => {
    test.setTimeout(240_000);
    const solicitudes: string[] = [];
    const errores: string[] = [];
    page.on('request', (r) => solicitudes.push(r.url()));
    page.on('console', (m) => { if (m.type() === 'error' || m.type() === 'warning') errores.push(m.text()); });
    page.on('pageerror', (e) => errores.push(e.message));
    await empezar(page);
    await hacerC(page);
    await hacerA(page);
    await hacerB2(page);
    await hacerB1(page);
    await page.getByRole('button', { name: /Cambiar tema/ }).click();
    const d = page.waitForEvent('download');
    await page.getByRole('button', { name: 'Descargar mi resultado' }).click();
    await d;
    await page.goto('/#/avanzado');
    await page.getByRole('button', { name: 'Borrar los datos locales…' }).click();
    await page.getByRole('button', { name: 'Cancelar' }).click();
    const externas = solicitudes.filter((u) => !u.startsWith('blob:') && !u.startsWith('data:'));
    expect(new Set(externas.map((u) => u.split('#')[0].split('?')[0]))).toEqual(new Set([new URL('/', page.url()).toString()]));
    expect(errores, 'sin errores ni avisos en la consola').toEqual([]);
  });

  test('la política de contenido bloquea fetch, XHR y WebSocket aunque algo lo intentara', async ({ page }) => {
    await abrir(page);
    const r = await page.evaluate(async () => {
      const intentar = async (f: () => unknown) => { try { await f(); return 'permitido'; } catch (e) { return (e as Error).name; } };
      return {
        fetchPropio: await intentar(() => fetch('/')),
        fetchAjeno: await intentar(() => fetch('https://example.com/')),
        ws: await intentar(async () => { const w = new WebSocket('ws://localhost:1/'); await new Promise<void>((res) => { w.onclose = () => res(); w.onerror = () => res(); }); if (w.readyState === WebSocket.OPEN) throw new Error('abierto'); throw new DOMException('cerrado sin abrir', 'SecurityError'); }),
        xhr: await new Promise<string>((res) => { const x = new XMLHttpRequest(); x.onerror = () => res('bloqueado'); x.onload = () => res('permitido'); try { x.open('GET', '/'); x.send(); } catch { res('bloqueado'); } }),
      };
    });
    expect(r.fetchPropio).toBe('TypeError');
    expect(r.fetchAjeno).toBe('TypeError');
    expect(r.ws).toBe('SecurityError');
    expect(r.xhr).toBe('bloqueado');
  });

  test('un script o manejador inyectado no se ejecuta: la política solo admite el script exacto de la página', async ({ page }) => {
    await abrir(page);
    await expect(page.addScriptTag({ content: 'window.__inyectado = 1' })).rejects.toThrow(/Content Security Policy/);
    await page.evaluate(() => { const i = document.createElement('div'); i.innerHTML = '<img src="data:image/gif;base64,AAAA" onerror="window.__inyectado = 2">'; document.body.appendChild(i); });
    await page.waitForTimeout(300);
    expect(await page.evaluate(() => (window as any).__inyectado)).toBeUndefined();
  });

  test('funciona sin conexión una vez cargada: se puede anotar, descargar y guardar', async ({ page, context }) => {
    await empezar(page);
    await context.setOffline(true);
    await hacerB1(page);
    const [d] = await Promise.all([page.waitForEvent('download'), (async () => { await page.evaluate(() => { location.hash = '#/gracias'; }); await page.getByRole('button', { name: 'Descargar mi resultado' }).click(); })()]);
    expect(JSON.parse(readFileSync((await d.path())!, 'utf8')).participantes[0].b1.estado).toBe('hecha');
  });

  test('no escribe cookies ni guarda nada fuera de sus dos claves de almacenamiento local', async ({ page, context }) => {
    await empezar(page);
    await page.getByRole('button', { name: /Cambiar tema/ }).click();
    expect(await context.cookies()).toEqual([]);
    await expect.poll(() => page.evaluate(() => Object.keys(localStorage).length)).toBe(2);
    const claves = await page.evaluate(() => [Object.keys(localStorage).sort(), Object.keys(sessionStorage), document.cookie]);
    expect(claves[0]).toEqual(['babelhub-pruebas-humanas:datos:v2', 'babelhub-pruebas-humanas:tema:v1']);
    expect(claves[1]).toEqual([]);
    expect(claves[2]).toBe('');
  });

  test('el enlace a la tienda se abre sin pasar el origen ni la ventana de la herramienta y solo si es http(s)', async ({ page }) => {
    await empezar(page);
    const enlace = page.getByRole('link', { name: /Abrir la tienda de prueba/ });
    await expect(enlace).toHaveAttribute('rel', /noopener/);
    await expect(enlace).toHaveAttribute('rel', /noreferrer/);
    await expect(enlace).toHaveAttribute('target', '_blank');
    await expect(enlace).toHaveAttribute('href', 'https://tienda.ejemplo.test/');
    // Un JSON importado con una dirección que no es http(s) nunca se vuelve un enlace.
    await page.evaluate(() => {
      const k = 'babelhub-pruebas-humanas:datos:v2'; const o = JSON.parse(localStorage.getItem(k)!); o.yo.tienda.url = 'javascript:alert(1)'; localStorage.setItem(k, JSON.stringify(o));
    });
    await page.goto('/');
    await expect(page.getByRole('link', { name: /Abrir la tienda de prueba/ })).toHaveCount(0);
    await expect(page.locator('a[href^="javascript:"]')).toHaveCount(0);
  });

  test('el aviso de privacidad está a la vista y dice que nada sale del dispositivo', async ({ page }) => {
    await abrir(page);
    await expect(page.getByText(/no se conecta a internet ni envía nada/)).toBeVisible();
    await expect(page.getByText(/No te pedimos tu nombre/)).toBeVisible();
  });
});
