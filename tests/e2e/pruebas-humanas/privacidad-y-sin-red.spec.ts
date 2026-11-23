import { test, expect } from '@playwright/test';
import { readFileSync } from 'node:fs';
import { createHash } from 'node:crypto';
import { join } from 'node:path';
import { abrir, ir } from './ayudas';

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

  test('el JSON de ejemplo de estas pruebas no está en la app: la app arranca vacía', async ({ page }) => {
    await abrir(page);
    const db = await page.evaluate(() => (window as any).__babelhubPruebasHumanas.construirExport().datos);
    expect(db.a.corridas).toEqual([]);
    expect(db.b.sesiones).toEqual([]);
    expect(db.c.personas).toEqual([]);
    expect(db.c.rondas.every((r: any) => !r.resumen && !r.cambios)).toBe(true);
    await ir(page, 'Navegadores');
    await expect(page.getByRole('link', { name: /sin fecha|20dd-/ })).toHaveCount(0);
    await ir(page, 'Lector');
    await expect(page.getByText('Todavía no hay sesiones.')).toBeVisible();
    await ir(page, 'Usabilidad');
    await expect(page.getByText('Todavía no hay personas en esta ronda.')).toBeVisible();
  });
});

test.describe('En ejecución', () => {
  test('solo se pide el documento: ninguna otra solicitud de red al recorrer y usar toda la app', async ({ page }) => {
    const solicitudes: string[] = [];
    const errores: string[] = [];
    page.on('request', (r) => solicitudes.push(r.url()));
    page.on('console', (m) => { if (m.type() === 'error' || m.type() === 'warning') errores.push(m.text()); });
    page.on('pageerror', (e) => errores.push(e.message));
    await abrir(page);
    for (const s of ['Navegadores', 'Lector', 'Usabilidad', 'Datos', 'Inicio'] as const) await ir(page, s);
    await ir(page, 'Navegadores');
    await page.getByRole('button', { name: 'Nueva corrida en Chrome · Escritorio' }).click();
    await ir(page, 'Lector');
    await page.getByRole('button', { name: 'Nueva sesión: VoiceOver · oscuro' }).click();
    await ir(page, 'Usabilidad');
    await page.getByRole('button', { name: /Agregar persona a la ronda 1/ }).click();
    await ir(page, 'Datos');
    await page.getByRole('button', { name: 'Copiar JSON' }).click();
    await page.getByRole('button', { name: 'Ver el texto' }).first().click();
    const descarga = page.waitForEvent('download');
    await page.getByRole('button', { name: 'Descargar JSON' }).click();
    await descarga;
    expect(solicitudes.filter((u) => !u.startsWith('blob:') && !u.startsWith('data:'))).toEqual([new URL('/', page.url()).toString()]);
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

  test('un script o manejador inyectado no se ejecuta: la política solo admite el script exacto de la página (sin unsafe-inline ni unsafe-eval)', async ({ page }) => {
    await abrir(page);
    // Un <script> insertado en la página (lo que lograría un XSS) viola la política y no corre.
    await expect(page.addScriptTag({ content: 'window.__inyectado = 1' })).rejects.toThrow(/Content Security Policy/);
    // Un manejador en línea tampoco: se inserta un elemento con onerror y no debe ejecutarse.
    await page.evaluate(() => { const i = document.createElement('div'); i.innerHTML = '<img src="data:image/gif;base64,AAAA" onerror="window.__inyectado = 2">'; document.body.appendChild(i); });
    await page.waitForTimeout(300);
    expect(await page.evaluate(() => (window as any).__inyectado)).toBeUndefined();
  });


  test('funciona sin conexión una vez cargada: se puede anotar, exportar y guardar', async ({ page, context }) => {
    await abrir(page);
    await context.setOffline(true);
    await ir(page, 'Navegadores');
    await page.getByRole('button', { name: 'Nueva corrida en Safari · iPhone real' }).click();
    await page.getByLabel('Versión del navegador').fill('17.5');
    await ir(page, 'Datos');
    const [d] = await Promise.all([page.waitForEvent('download'), page.getByRole('button', { name: 'Descargar JSON' }).click()]);
    expect(JSON.parse(readFileSync((await d.path())!, 'utf8')).datos.a.corridas[0].version).toBe('17.5');
  });

  test('no escribe cookies ni guarda nada fuera de sus dos claves de almacenamiento local', async ({ page, context }) => {
    await abrir(page);
    await page.getByLabel('Código de quien evalúa').fill('EV1');
    await page.getByRole('button', { name: /Cambiar tema/ }).click();
    await ir(page, 'Datos');
    expect(await context.cookies()).toEqual([]);
    // El guardado de lo escrito espera un instante (agrupa las pulsaciones) y se vacía al cerrar o recargar la página.
    await expect.poll(() => page.evaluate(() => Object.keys(localStorage).length)).toBe(2);
    const claves = await page.evaluate(() => [Object.keys(localStorage).sort(), Object.keys(sessionStorage), document.cookie]);
    expect(claves[0]).toEqual(['babelhub-pruebas-humanas:datos:v1', 'babelhub-pruebas-humanas:tema:v1']);
    expect(claves[1]).toEqual([]);
    expect(claves[2]).toBe('');
  });

  test('el enlace a la tienda de pruebas se abre sin pasar el origen ni la ventana de la herramienta', async ({ page }) => {
    await abrir(page);
    await page.getByLabel('Dirección de la tienda de pruebas (opcional)').fill('https://tienda.invalid/');
    await page.getByLabel('Código de quien evalúa').click();
    const enlace = page.getByRole('link', { name: 'Abrir la tienda de pruebas en otra pestaña' });
    await expect(enlace).toHaveAttribute('rel', /noopener/);
    await expect(enlace).toHaveAttribute('rel', /noreferrer/);
    await expect(enlace).toHaveAttribute('target', '_blank');
    // Una dirección que no es http(s) (por ejemplo javascript:) nunca se vuelve un enlace.
    await page.getByLabel('Dirección de la tienda de pruebas (opcional)').fill('javascript:alert(1)');
    await page.getByLabel('Código de quien evalúa').click();
    await ir(page, 'Navegadores');
    await ir(page, 'Inicio');
    await expect(page.getByRole('link', { name: 'Abrir la tienda de pruebas en otra pestaña' })).toHaveCount(0);
  });
});
