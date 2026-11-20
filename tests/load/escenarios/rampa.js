// Rampa progresiva de 50 hasta 2000 usuarios virtuales sobre la consulta de disponibilidad (RNF02), por niveles consecutivos; cada nivel lleva su etiqueta y su umbral.
// El nivel de 50 usuarios es el criterio de la rúbrica (p95 < 500 ms); los demás documentan el techo del entorno acotado (CPU y memoria de VM-INVENTARIO): el primer nivel
// que rompe p95 < 500 ms o 1 % de errores es el techo medido (ver DOC-1 y progress/evidencias/).
import { check } from 'k6';
import { sembrar, disponibilidad, pensar } from '../lib/comun.js';
import { resumen } from '../lib/resumen.js';

const NIVELES = (__ENV.NIVELES || '50,100,250,500,1000,2000').split(',').map(Number);
const DURACION = Number(__ENV.SEGUNDOS_POR_NIVEL || 40);
const HOLGURA = 10;

const scenarios = {};
const thresholds = { checks: ['rate>0.5'] };
NIVELES.forEach((n, i) => {
  scenarios[`nivel_${n}`] = { executor: 'ramping-vus', startVUs: 0, stages: [{ duration: `${HOLGURA}s`, target: n }, { duration: `${DURACION}s`, target: n }], startTime: `${i * (DURACION + HOLGURA + 5)}s`, tags: { nivel: String(n) }, gracefulRampDown: '5s' };
  thresholds[`http_req_duration{nivel:${n}}`] = ['p(95)<500'];
  thresholds[`http_req_failed{nivel:${n}}`] = ['rate<0.01'];
});

export const options = { scenarios, thresholds, insecureSkipTLSVerify: true, discardResponseBodies: false };

export function setup() { return sembrar(20, 1000); }

export default function (datos) {
  const r = disponibilidad(datos.ediciones[Math.floor(Math.random() * datos.ediciones.length)], __VU);
  check(r, { 'responde 200': (x) => x.status === 200 });
  pensar();
}

export const handleSummary = resumen('rampa-de-usuarios', 'Consulta de disponibilidad por niveles de 50 a 2000 usuarios virtuales; cada nivel con su p95 y su tasa de errores.');
