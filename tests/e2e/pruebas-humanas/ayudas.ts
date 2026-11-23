import { expect, type Page } from '@playwright/test';

// Ayudas de las pruebas de `tools/pruebas-humanas/index.html` (v2). Los datos de ejemplo son SOLO de las pruebas (la app nunca los trae precargados).

export const CLAVE_LS = 'babelhub-pruebas-humanas:datos:v2';
export const ENLACE = '/?tienda=https://tienda.ejemplo.test/&v=2026-11-23';

export async function abrir(page: Page, enlace = ENLACE) {
  await page.goto(enlace);
  await expect(page.getByRole('heading', { level: 1, name: 'Ayúdanos a probar BabelHub' })).toBeVisible();
}

// Abre el enlace, confirma el navegador detectado y llega al inicio (la persona «promedio» no corrige nada).
export async function empezar(page: Page, enlace = ENLACE) {
  await abrir(page, enlace);
  await page.getByRole('button', { name: 'Sí, es correcto' }).click();
  await page.getByRole('button', { name: 'Empezar' }).click();
  await expect(page.getByRole('heading', { level: 1, name: 'Gracias por ayudarnos' })).toBeVisible();
}

export const siguiente = (page: Page) => page.getByRole('button', { name: /^(Siguiente|Terminar tarea)/ }).click();
export const irTarea = (page: Page, nombre: RegExp) => page.getByRole('link', { name: nombre }).first().click();

export async function elegir(page: Page, grupo: RegExp | string, opcion: RegExp | string) {
  await page.getByRole('group', { name: grupo }).getByRole('radio', { name: opcion, exact: typeof opcion === 'string' }).check();
}

type AlPaso = (nombre: string) => Promise<void>;
const paso = async (f: AlPaso | undefined, n: string) => { if (f) await f(n); };

export async function responderLikert(page: Page, valor: number | ((i: number) => number)) {
  const n = await page.locator('fieldset.card').count();
  for (let i = 0; i < n; i++) {
    const v = typeof valor === 'number' ? valor : valor(i);
    await page.locator('fieldset.card').nth(i).getByRole('radio', { name: new RegExp(`^${v}:`) }).check();
  }
}

export const SUS_75 = [4, 2, 4, 2, 4, 2, 4, 2, 4, 2];

export async function hacerC(page: Page, o: { lograste?: 'Sí' | 'No'; ayuda?: boolean; errores?: string; sus?: number[]; conocia?: 'Sí' | 'No'; dificil?: string; espera?: number; alPaso?: AlPaso } = {}) {
  await irTarea(page, /(Empezar|Continuar|Revisar).*tarea Compra un libro/);
  await paso(o.alPaso, 'C · consentimiento');
  for (const t of [/Leí y entendí/, /Sé que puedo parar/, /Acepto participar/]) await page.getByRole('checkbox', { name: t }).check();
  await siguiente(page);
  await paso(o.alPaso, 'C · perfil');
  await page.getByRole('radio', { name: '25 a 34 años' }).check();
  await page.getByRole('radio', { name: 'Pocas veces al año' }).check();
  await elegir(page, /ayuda para ver/, 'No');
  await elegir(page, /Ya habías visto/, o.conocia ?? 'No');
  await siguiente(page);
  await paso(o.alPaso, 'C · tarea');
  await page.getByRole('button', { name: 'Empezar tarea' }).click();
  await paso(o.alPaso, 'C · tarea en curso');
  if (o.espera) await page.waitForTimeout(o.espera);
  await page.getByRole('button', { name: 'Terminé' }).click();
  await paso(o.alPaso, 'C · tarea terminada');
  await siguiente(page);
  await paso(o.alPaso, 'C · cómo te fue');
  await elegir(page, /Lograste comprar/, o.lograste ?? 'Sí');
  await elegir(page, /Pediste ayuda/, o.ayuda ? /Sí, pedí ayuda/ : /No, lo hice/);
  await elegir(page, /Cuántas veces/, o.errores ?? 'Ninguna');
  await siguiente(page);
  await paso(o.alPaso, 'C · SUS');
  const sus = o.sus ?? SUS_75;
  for (let i = 1; i <= 10; i++) await page.getByRole('group', { name: new RegExp(`^${i}\\. `) }).getByRole('radio', { name: new RegExp(`^${sus[i - 1]}:`) }).check();
  await siguiente(page);
  await paso(o.alPaso, 'C · última pregunta');
  await page.getByLabel(/lo más difícil/).fill(o.dificil ?? 'Encontrar dónde pagar');
  await siguiente(page);
}

export async function hacerA(page: Page, o: { respuesta?: 'Sí' | 'Con dificultad' | 'No'; completo?: 'Sí' | 'No'; alPaso?: AlPaso } = {}) {
  await irTarea(page, /(Empezar|Continuar|Revisar).*tarea Recorre la compra/);
  await paso(o.alPaso, 'A · introducción');
  await siguiente(page);
  for (let i = 1; i <= 8; i++) {
    await paso(o.alPaso, `A · paso ${i}`);
    await page.getByRole('radio', { name: o.respuesta ?? 'Sí', exact: true }).check();
    if (o.respuesta && o.respuesta !== 'Sí') await page.getByLabel('Cuéntanos qué pasó').fill('No encontré el botón');
    await siguiente(page);
  }
  await paso(o.alPaso, 'A · para terminar');
  await elegir(page, /completar la compra/, o.completo ?? 'Sí');
  await siguiente(page);
}

export async function hacerB1(page: Page, o: { bloqueo?: boolean; valor?: number; alPaso?: AlPaso } = {}) {
  await irTarea(page, /(Empezar|Continuar|Revisar).*tarea Usa la tienda solo con el teclado/);
  await paso(o.alPaso, 'B1 · teclado');
  await elegir(page, /teclado a mano/, /Sí, tengo teclado/);
  await siguiente(page);
  await paso(o.alPaso, 'B1 · teclas');
  await siguiente(page);
  await paso(o.alPaso, 'B1 · recorrido');
  await elegir(page, /Con qué tema/, 'Claro');
  await siguiente(page);
  await paso(o.alPaso, 'B1 · preguntas');
  await responderLikert(page, o.valor ?? 4);
  await siguiente(page);
  await paso(o.alPaso, 'B1 · bloqueo');
  await elegir(page, /no pudiste continuar usando solo el teclado/, o.bloqueo ? 'Sí' : 'No');
  if (o.bloqueo) await page.getByLabel(/Dónde pasó/).fill('En el carrito el foco se quedó atrapado');
  await siguiente(page);
}

export async function hacerB2(page: Page, o: { lector?: RegExp; bloqueo?: boolean; alPaso?: AlPaso } = {}) {
  await irTarea(page, /(Empezar|Continuar|Revisar).*tarea Lector de pantalla/);
  await paso(o.alPaso, 'B2 · lector');
  await elegir(page, /Con qué lector/, o.lector ?? /NVDA/);
  await siguiente(page);
  await paso(o.alPaso, 'B2 · guía');
  await siguiente(page);
  await paso(o.alPaso, 'B2 · recorrido');
  await elegir(page, /Con qué tema/, 'Oscuro');
  await siguiente(page);
  await paso(o.alPaso, 'B2 · preguntas');
  await responderLikert(page, 4);
  await siguiente(page);
  await paso(o.alPaso, 'B2 · bloqueo');
  await elegir(page, /no te dijo algo importante/, o.bloqueo ? 'Sí' : 'No');
  if (o.bloqueo) await page.getByLabel(/Dónde pasó/).fill('El botón de pagar no tenía nombre');
  await siguiente(page);
}

export const exportacion = (page: Page) => page.evaluate(() => (window as any).__babelhubPruebasHumanas.construirExport());
export const calculado = (page: Page) => page.evaluate(() => (window as any).__babelhubPruebasHumanas.calcular());
export const puntaje = (page: Page, sus: Array<number | null>) => page.evaluate((r) => (window as any).__babelhubPruebasHumanas.puntajeSUS(r) as number | null, sus);

const PASOS = ['inicio', 'catalogo', 'ficha', 'carrito', 'ingreso', 'checkout', 'pago', 'estado'];
const ITEMS_B1 = ['T1', 'T2', 'T3', 'T4', 'T5', 'T6'];
const ITEMS_B2 = ['L1', 'L2', 'L3', 'L4', 'L5', 'L6', 'L7', 'L8', 'K6', 'K7'];

export type PartDeEjemplo = {
  id: string; codigo?: string; nav?: string; ver?: string; sis?: string; disp?: 'computadora' | 'celular' | 'tableta'; act?: string;
  a?: 'si' | 'no'; b1?: 'si' | 'no'; b2?: { lector: 'nvda' | 'voiceover' | 'otro'; bloqueo?: 'si' | 'no' };
  c?: { ronda?: '1' | '2'; lograste?: 'si' | 'no'; ayuda?: 'si' | 'no'; errores?: string; sus?: number[]; conocia?: 'si' | 'no'; despuesDeA?: boolean; consentimiento?: boolean; apoyo?: 'si' | 'no' };
};

// Una persona de ejemplo con el formato v2 (lo que exporta la app). Sirve para probar la importación, la cobertura y los criterios.
export function part(o: PartDeEjemplo) {
  const act = o.act ?? '2026-11-23T10:00:00.000Z';
  const items = (ids: string[]) => Object.fromEntries(ids.map((i) => [i, { v: 4, na: false }]));
  const c = o.c;
  return {
    id: o.id, act, creada: act, codigo: o.codigo ?? o.id.slice(-4).toUpperCase(), apodo: '', bienvenida: true,
    entorno: { navegador: o.nav ?? 'Chrome', version: o.ver ?? '131.0.0.0', sistema: o.sis ?? 'Windows', sistemaVersion: '', dispositivo: o.disp ?? 'computadora', ua: '', ventana: '1280×800', pantalla: { ancho: 1920, alto: 1080, ratio: 1 }, detectado: {}, corregido: false, confirmado: true },
    tienda: { url: 'https://tienda.ejemplo.test/', version: '2026-11-23' },
    a: o.a ? { estado: 'hecha', paso: 0, iniciadoEn: act, terminadoEn: act, pasos: Object.fromEntries(PASOS.map((p) => [p, { r: 'ok', n: '', captura: '' }])), completo: o.a, raro: { tipos: [], nota: '' } } : { estado: 'pendiente' },
    b1: o.b1 ? { estado: 'hecha', iniciadoEn: act, terminadoEn: act, teclado: 'si', tema: 'claro', items: items(ITEMS_B1), bloqueo: o.b1, nota: o.b1 === 'si' ? 'Se quedó atrapado en el carrito' : '' } : { estado: 'pendiente' },
    b2: o.b2 ? { estado: 'hecha', iniciadoEn: act, terminadoEn: act, lector: o.b2.lector, tema: 'oscuro', items: items(ITEMS_B2), bloqueo: o.b2.bloqueo ?? 'no', nota: o.b2.bloqueo === 'si' ? 'Botón sin nombre' : '' } : { estado: 'pendiente' },
    c: c ? {
      estado: 'hecha', iniciadoEn: act, terminadoEn: act, ronda: c.ronda ?? '1', despuesDeA: c.despuesDeA ?? false,
      consentimiento: { leido: c.consentimiento !== false, retiro: c.consentimiento !== false, acepta: c.consentimiento !== false, fecha: act },
      perfil: { edad: '25-34', experiencia: 'poco', apoyo: c.apoyo ?? 'no', conocia: c.conocia ?? 'no' },
      tarea: { tiempoSeg: 240, abandono: false, lograste: c.lograste ?? 'si', ayuda: c.ayuda ?? 'no', errores: c.errores ?? '0' },
      sus: c.sus ?? SUS_75, dificil: '',
    } : { estado: 'pendiente' },
  };
}

export function archivo(participantes: unknown[], exportadoEn = '2026-11-23T12:00:00.000Z') {
  return { esquema: 'babelhub-pruebas-humanas', version: 2, exportadoEn, participantes };
}

export async function importarJson(page: Page, contenido: unknown, nombre = 'ejemplo.json') {
  await page.goto('/#/avanzado');
  await expect(page.getByRole('heading', { level: 1, name: /Reunir y revisar/ })).toBeVisible();
  const leer = () => page.evaluate(() => Number(document.querySelector('[data-importacion]')?.getAttribute('data-importacion') ?? 0));
  const antes = await leer();
  await page.locator('#archivo-json').setInputFiles({ name: nombre, mimeType: 'application/json', buffer: Buffer.from(typeof contenido === 'string' ? contenido : JSON.stringify(contenido)) });
  await expect.poll(leer, { message: 'la importación termina y se muestra su informe' }).toBeGreaterThan(antes);
}

// Las seis combinaciones de AC-WEB-18, con dos versiones en las de escritorio.
export function seisCombinaciones(opciones: { sinSegundaVersion?: string; sin?: string[]; a?: 'si' | 'no' } = {}) {
  const base: Array<[string, PartDeEjemplo]> = [
    ['chrome-escritorio-1', { id: 'per-chrome0001', nav: 'Chrome', ver: '130.0.0.0' }], ['chrome-escritorio-2', { id: 'per-chrome0002', nav: 'Chrome', ver: '131.0.0.0' }],
    ['edge-escritorio-1', { id: 'per-edge000001', nav: 'Edge', ver: '130.0.0.0' }], ['edge-escritorio-2', { id: 'per-edge000002', nav: 'Edge', ver: '131.0.0.0' }],
    ['firefox-escritorio-1', { id: 'per-firefox001', nav: 'Firefox', ver: '132.0' }], ['firefox-escritorio-2', { id: 'per-firefox002', nav: 'Firefox', ver: '133.0' }],
    ['safari-macos-1', { id: 'per-safari00001', nav: 'Safari', ver: '17.4', sis: 'macOS' }], ['safari-macos-2', { id: 'per-safari00002', nav: 'Safari', ver: '17.5', sis: 'macOS' }],
    ['chrome-android', { id: 'per-android0001', nav: 'Chrome', ver: '131.0.0.0', sis: 'Android', disp: 'celular' }],
    ['safari-iphone', { id: 'per-iphone00001', nav: 'Safari', ver: '17.5', sis: 'iOS', disp: 'celular' }],
  ];
  return base
    .filter(([k]) => k !== opciones.sinSegundaVersion && !(opciones.sin ?? []).some((s) => k.startsWith(s)))
    .map(([, p]) => part({ ...p, a: opciones.a ?? 'si' }));
}

export async function leerDescarga(descarga: import('@playwright/test').Download): Promise<string> {
  const { readFileSync } = await import('node:fs');
  return readFileSync((await descarga.path())!, 'utf8');
}
