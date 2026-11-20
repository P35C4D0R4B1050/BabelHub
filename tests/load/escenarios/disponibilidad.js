// RNF02 y AC-FUE-08: 50 usuarios concurrentes consultan la disponibilidad de una edición impresa; el p95 debe ser menor a 500 ms. Sin errores ni 429.
import { check } from 'k6';
import { sembrar, disponibilidad, pensar } from '../lib/comun.js';

import { resumen } from '../lib/resumen.js';
export const options = {
  scenarios: { disponibilidad: { executor: 'constant-vus', vus: 50, duration: __ENV.DURACION || '60s' } },
  thresholds: {
    http_req_duration: ['p(95)<500'],
    http_req_failed: ['rate<0.001'],
    checks: ['rate>0.999'],
  },
  insecureSkipTLSVerify: true,
};

export function setup() { return sembrar(20, 100); }

export default function (datos) {
  const edicion = datos.ediciones[Math.floor(Math.random() * datos.ediciones.length)];
  const r = disponibilidad(edicion, __VU);
  check(r, {
    'responde 200': (x) => x.status === 200,
    'trae el vendible de la red': (x) => x.json('vendibleEnLaRed') !== undefined,
    'no revela físico ni reservado': (x) => !x.body.includes('fisico') && !x.body.includes('reservado'),
  });
  pensar();
}

export const handleSummary = resumen('disponibilidad', 'RNF02 y AC-FUE-08: 50 usuarios concurrentes consultan la disponibilidad; p95 < 500 ms.');
