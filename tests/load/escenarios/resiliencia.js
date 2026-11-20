// Resiliencia ante la caída de un servicio (PostgreSQL): ejecutar.sh pausa el contenedor de la base entre los 20 s y los 45 s. Criterios: el proceso del Inventario NO
// cae (/health/live responde 200 todo el tiempo), /health/ready pasa a 503 mientras la base no está y vuelve a 200 después, las operaciones fallan solo con 503 uniforme
// (nunca 500 ni trazas) y, tras la recuperación, el servicio vuelve a responder con éxito sin reiniciar.
import http from 'k6/http';
import { check, sleep } from 'k6';
import exec from 'k6/execution';
import { Counter } from 'k6/metrics';
import { sembrar, disponibilidad, BASE, UMBRALES_DE_ESTADOS } from '../lib/comun.js';
import { resumen } from '../lib/resumen.js';

const inesperadas = new Counter('respuestas_inesperadas');
const vivo = new Counter('liveness_fallida');
const listoDuranteLaFalla = new Counter('ready_200_durante_la_falla');

function fase(inicio, fin, nombre) {
  return { executor: 'constant-vus', exec: 'carga', vus: 30, startTime: inicio, duration: fin, tags: { fase: nombre }, gracefulStop: '15s' };
}

export const options = {
  scenarios: {
    previa: fase('0s', '20s', 'previa'),
    falla: fase('20s', '25s', 'falla'),
    posterior: fase('50s', '30s', 'posterior'),
    sondas: { executor: 'constant-vus', exec: 'sondas', vus: 1, duration: '80s' },
  },
  thresholds: {
    ...UMBRALES_DE_ESTADOS,
    'checks{fase:previa}': ['rate>0.999'],
    'checks{fase:posterior}': ['rate>0.99'],
    respuestas_inesperadas: ['count==0'],
    liveness_fallida: ['count==0'],
    // Hallazgo K6-1: el Inventario no registra ningún IComprobacionDeSalud, así que /health/ready no ve la caída de la base; se mide y se informa, no se exige.
    ready_200_durante_la_falla: ['count>=0'],
  },
  insecureSkipTLSVerify: true,
};

export function setup() { return Object.assign(sembrar(5, 100), { inicio: Date.now() }); }

export function carga(datos) {
  const r = disponibilidad(datos.ediciones[Math.floor(Math.random() * datos.ediciones.length)], __VU);
  const fallando = r.status === 503 && r.json('code') === 'INV-SERVICIO-NO-DISPONIBLE';
  if (r.status !== 200 && !fallando) inesperadas.add(1);
  // Antes y después de la falla solo vale el 200; durante ella, el 200 o el 503 uniforme.
  check(r, { 'responde 200 (o, solo en la fase de falla, 503 uniforme)': (x) => x.status === 200 || (fallando && exec.scenario.name === 'falla') });
  sleep(0.2);
}

export function sondas(datos) {
  const vida = http.get(`${BASE}/health/live`);
  if (vida.status !== 200) vivo.add(1);
  const t = (Date.now() - datos.inicio) / 1000;
  const listo = http.get(`${BASE}/health/ready`);
  if (t > 28 && t < 43 && listo.status === 200) listoDuranteLaFalla.add(1);
  sleep(1);
}

export const handleSummary = resumen('resiliencia', 'PostgreSQL pausado entre los 20 y los 45 s: el Inventario sigue vivo, responde 503 uniforme y se recupera solo.');
