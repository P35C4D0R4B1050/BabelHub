// Límites de uso (API4; transversal-seguridad): 100 solicitudes por minuto por usuario. Un usuario que excede el tope recibe 429 con Retry-After y el código
// estable INV-LIMITE-EXCEDIDO, y el exceso de uno NO afecta a otro usuario (el vecino sigue sin recibir ningún 429).
import http from 'k6/http';
import { check } from 'k6';
import { Counter } from 'k6/metrics';
import { API, cabeceras, usuarioDe, uuid } from '../lib/comun.js';
import { resumen } from '../lib/resumen.js';

const permitidas = new Counter('abusivo_permitidas');
const limitadas = new Counter('abusivo_limitadas');
const vecinoLimitado = new Counter('vecino_limitadas');

export const options = {
  scenarios: {
    abusivo: { executor: 'shared-iterations', exec: 'abusivo', vus: 1, iterations: 140, maxDuration: '50s' },
    vecino: { executor: 'constant-arrival-rate', exec: 'vecino', rate: 1, timeUnit: '1s', duration: '40s', preAllocatedVUs: 2 },
  },
  thresholds: {
    abusivo_permitidas: ['count<=100'],
    abusivo_limitadas: ['count>=30'],
    vecino_limitadas: ['count==0'],
    'checks{tipo:limite}': ['rate==1'],
  },
  insecureSkipTLSVerify: true,
};

function consultar(numero) {
  const u = usuarioDe(numero);
  return http.get(`${API}/reservas/${uuid()}`, { headers: cabeceras.usuario(u.token, numero, false), tags: { operacion: 'inventario.reservas.consultar' } });
}

export function abusivo() {
  const r = consultar(1);
  if (r.status === 429) {
    limitadas.add(1);
    check(r, {
      '429 con Retry-After': (x) => Number(x.headers['Retry-After']) > 0,
      'código estable INV-LIMITE-EXCEDIDO': (x) => x.json('code') === 'INV-LIMITE-EXCEDIDO',
    }, { tipo: 'limite' });
  } else {
    permitidas.add(1);
  }
}

export function vecino() {
  const r = consultar(2);
  if (r.status === 429) vecinoLimitado.add(1);
}

export const handleSummary = resumen('limites-de-uso', 'Un usuario que excede 100 solicitudes por minuto recibe 429 con Retry-After; otro usuario no se ve afectado.');
