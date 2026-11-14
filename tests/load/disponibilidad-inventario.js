// Prueba de carga de AC-FUE-08 / FUE-10: 50 usuarios concurrentes consultan la disponibilidad de una edición impresa y el percentil 95 debe ser menor a 500 ms.
//
// La llamada lleva la credencial de servicio del alcance «lectura de disponibilidad» y el origen reenviado con la credencial de origen: sin ellos todo el sitio compartiría el tope de 120
// solicitudes por minuto de su dirección directa (SEG-32) y se mediría el 429 y no la consulta. Cada usuario virtual simula un cliente final con su propia dirección.
//
// Uso (contra un entorno con los datos de ejemplo de docs/07-proceso-de-desarrollo/02-convenciones-tecnicas.md; representativo solo en el despliegue real, FIN-2):
//   k6 run -e BASE_URL=https://inventario.interno \
//          -e CREDENCIAL_DISPONIBILIDAD=... -e CREDENCIAL_ORIGEN=... \
//          -e EDICIONES=<edicionId>,<edicionId>,... tests/load/disponibilidad-inventario.js
// Las credenciales y los identificadores vienen del entorno: nunca se escriben en este archivo.
import http from 'k6/http';
import { check } from 'k6';

const BASE_URL = __ENV.BASE_URL;
const CREDENCIAL = __ENV.CREDENCIAL_DISPONIBILIDAD;
const CREDENCIAL_DE_ORIGEN = __ENV.CREDENCIAL_ORIGEN;
const EDICIONES = (__ENV.EDICIONES || '').split(',').filter((e) => e.length > 0);

if (!BASE_URL || !CREDENCIAL || !CREDENCIAL_DE_ORIGEN || EDICIONES.length === 0) {
  throw new Error('Faltan BASE_URL, CREDENCIAL_DISPONIBILIDAD, CREDENCIAL_ORIGEN o EDICIONES.');
}

export const options = {
  scenarios: {
    disponibilidad: {
      executor: 'constant-vus',
      vus: 50,
      duration: '60s',
    },
  },
  thresholds: {
    // AC-FUE-08: p95 < 500 ms con 50 usuarios concurrentes; y sin errores ni 429 (el origen reenviado evita medir el tope de la dirección directa).
    http_req_duration: ['p(95)<500'],
    http_req_failed: ['rate<0.001'],
    checks: ['rate>0.999'],
  },
};

export default function () {
  const edicion = EDICIONES[Math.floor(Math.random() * EDICIONES.length)];
  const cliente = `203.0.113.${(__VU % 250) + 1}`;
  const respuesta = http.get(`${BASE_URL}/api/inventario/v1/disponibilidad/${edicion}`, {
    headers: {
      'X-Credencial-De-Servicio': CREDENCIAL,
      'X-Credencial-De-Origen': CREDENCIAL_DE_ORIGEN,
      'X-Origen-Reenviado': cliente,
    },
    tags: { operacion: 'inventario.disponibilidad.consultar' },
  });

  check(respuesta, {
    'responde 200': (r) => r.status === 200,
    'trae el vendible de la red': (r) => r.json('vendibleEnLaRed') !== undefined,
    'no revela físico ni reservado': (r) => !r.body.includes('fisico') && !r.body.includes('reservado'),
  });
}
