import { expect, type Page } from '@playwright/test';

// Ayudas de las pruebas de `tools/pruebas-humanas/index.html`. Los datos de ejemplo son SOLO de las pruebas (la app nunca los trae precargados).

export const CLAVE_LS = 'babelhub-pruebas-humanas:datos:v1';

export async function abrir(page: Page) {
  await page.goto('/');
  await expect(page.getByRole('heading', { level: 1, name: 'Pruebas humanas de BabelHub' })).toBeVisible();
}

export async function ir(page: Page, seccion: 'Inicio' | 'Navegadores' | 'Lector' | 'Usabilidad' | 'Datos') {
  await page.getByRole('navigation', { name: 'Secciones' }).getByRole('link', { name: new RegExp(seccion) }).click();
}

export const puntaje = (page: Page, sus: Array<number | null>) =>
  page.evaluate((r) => (window as any).__babelhubPruebasHumanas.puntajeSUS(r) as number | null, sus);

type PersonaDeEjemplo = {
  id: string; ronda: 1 | 2; codigo: string; resultado: 'sin-ayuda' | 'con-ayuda' | 'no-completa'; errores?: number; ayudas?: number; sus?: number[] | null;
  excluida?: boolean; tecApoyo?: 'si' | 'no'; tiempoSeg?: number; gravedad?: number; problema?: string; act?: string;
};

// Un archivo exportado por la herramienta (esquema v1) con personas de ejemplo.
export function archivo(opciones: { dispositivoId: string; evaluador: string; personas?: PersonaDeEjemplo[]; corridas?: unknown[]; sesiones?: unknown[]; rondas?: unknown[]; exportadoEn?: string }) {
  const act = '2026-11-23T10:00:00.000Z';
  return {
    esquema: 'babelhub-pruebas-humanas', version: 1, exportadoEn: opciones.exportadoEn ?? '2026-11-23T12:00:00.000Z',
    dispositivo: { id: opciones.dispositivoId, evaluador: opciones.evaluador, etiqueta: 'Dispositivo de ejemplo' },
    datos: {
      a: { corridas: opciones.corridas ?? [] },
      b: { sesiones: opciones.sesiones ?? [] },
      c: {
        rondas: opciones.rondas ?? [],
        personas: (opciones.personas ?? []).map((p) => ({
          id: p.id, act: p.act ?? act, creada: act, ronda: p.ronda, codigo: p.codigo, evaluador: opciones.evaluador, excluida: p.excluida ?? false, motivoExclusion: p.excluida ? 'no cumplía el perfil' : '',
          perfil: 'estudiante', dispositivo: 'celular', tecApoyo: p.tecApoyo ?? 'no', noConoce: true,
          consentimiento: { leido: true, acepta: true, pantalla: false, retiro: true, fecha: act },
          tarea: { resultado: p.resultado, tiempoSeg: p.tiempoSeg ?? 240, ayudas: Array.from({ length: p.ayudas ?? 0 }, () => ({ t: 130, tipo: 'ayuda', nota: '' })), errores: Array.from({ length: p.errores ?? 0 }, () => ({ t: 60, tipo: 'clic', nota: '' })) },
          seq: 6, post: { dificil: '', facil: '', cambiaria: '' }, sus: p.sus === null ? Array(10).fill(null) : (p.sus ?? [4, 2, 4, 2, 4, 2, 4, 2, 4, 2]), problema: p.problema ?? '', pantallaProblema: '', gravedad: p.gravedad ?? null, observaciones: '',
        })),
      },
    },
  };
}

// Diez personas de ejemplo (5 por ronda) que cumplen o no la meta según los parámetros.
export function diezPersonas(opciones: { sinAyuda: number; sus?: number[]; erroresPorPersona?: number }): PersonaDeEjemplo[] {
  return Array.from({ length: 10 }, (_, i) => ({
    id: `per-ejemplo${String(i + 1).padStart(2, '0')}`, ronda: (i < 5 ? 1 : 2) as 1 | 2, codigo: `P${i + 1}`,
    resultado: (i < opciones.sinAyuda ? 'sin-ayuda' : 'con-ayuda') as 'sin-ayuda' | 'con-ayuda', ayudas: i < opciones.sinAyuda ? 0 : 1, errores: opciones.erroresPorPersona ?? 0,
    sus: opciones.sus ?? [4, 2, 4, 2, 4, 2, 4, 2, 4, 2], tecApoyo: (i === 0 || i === 5 ? 'si' : 'no') as 'si' | 'no',
  }));
}

export const PANTALLAS = ['inicio', 'catalogo', 'ficha', 'carrito', 'checkout', 'pago', 'estado', 'ingreso', 'registro', 'cuenta', 'pedidos', 'biblioteca', 'reclamaciones'];
const ITEMS = ['K1', 'K2', 'K3', 'K4', 'K5', 'K6', 'K7', 'L1', 'L2', 'L3', 'L4', 'L5', 'L6', 'L7', 'L8'];

// Una sesión de lector de pantalla con las 13 pantallas respondidas «cumple» (K1 solo aplica a NVDA); `fallas` pone un estado distinto en pantalla e ítem.
export function sesionCompleta(opciones: { id: string; lector: 'nvda' | 'voiceover'; tema: 'babelhub' | 'babelhub-dark'; fallas?: Array<{ pantalla: string; item: string; e: 'critico' | 'menor'; n?: string }>; act?: string }) {
  const aplicables = ITEMS.filter((i) => !(i === 'K1' && opciones.lector === 'voiceover'));
  return {
    id: opciones.id, act: opciones.act ?? '2026-11-23T10:00:00.000Z', creada: '2026-11-23T09:00:00.000Z', lector: opciones.lector, tema: opciones.tema, evaluador: 'EV1', navegador: 'Firefox 133', versionLector: '2024.4', sistema: 'Windows', fecha: '2026-11-23',
    pantallas: Object.fromEntries(PANTALLAS.map((p) => [p, { items: Object.fromEntries(aplicables.map((i) => {
      const f = opciones.fallas?.find((x) => x.pantalla === p && x.item === i);
      return [i, { e: f ? f.e : 'ok', n: f ? (f.n ?? '') : '' }];
    })), nota: '' }])),
  };
}

const PASOS = ['inicio', 'catalogo', 'ficha', 'carrito', 'ingreso', 'checkout', 'pago', 'estado'];
const GENERALES = ['consola', 'h375', 'temas', 'atras'];

// Una corrida de navegadores con los 12 puntos respondidos «cumple».
export function corridaCompleta(opciones: { id: string; combo: string; navegador: string; version: string; completo?: 'si' | 'no'; defectos?: unknown[]; act?: string }) {
  return {
    id: opciones.id, act: opciones.act ?? '2026-11-23T10:00:00.000Z', creada: '2026-11-23T09:00:00.000Z', combo: opciones.combo, evaluador: 'EV1', navegador: opciones.navegador, version: opciones.version, sistema: 'Sistema de ejemplo', fecha: '2026-11-23', viewport: '375×812',
    pasos: Object.fromEntries(PASOS.map((p) => [p, { r: 'ok', n: '' }])), generales: Object.fromEntries(GENERALES.map((g) => [g, { r: 'ok', n: '' }])),
    completo: opciones.completo ?? 'si', defectos: opciones.defectos ?? [], notas: '',
  };
}

export async function leerDescarga(descarga: import('@playwright/test').Download): Promise<string> {
  const { readFileSync } = await import('node:fs');
  return readFileSync((await descarga.path())!, 'utf8');
}

export async function importarJson(page: Page, contenido: unknown, nombre = 'ejemplo.json') {
  await ir(page, 'Datos');
  await page.locator('#archivo-json').setInputFiles({ name: nombre, mimeType: 'application/json', buffer: Buffer.from(typeof contenido === 'string' ? contenido : JSON.stringify(contenido)) });
}
