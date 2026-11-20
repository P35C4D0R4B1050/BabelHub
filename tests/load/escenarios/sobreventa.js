// AC01 (concurrencia sin sobreventa): sobre las últimas unidades de una edición compiten muchos usuarios a la vez. Debe haber exactamente tantas reservas
// como unidades (el resto, 409 INV-STOCK-INSUFICIENTE) y el vendible final es 0: jamás negativo ni más reservas que stock.
import { check } from 'k6';
import { Counter } from 'k6/metrics';
import { sembrar, reservar, disponibilidad, uuid } from '../lib/comun.js';

const UNIDADES = Number(__ENV.UNIDADES || 10);
const COMPETIDORES = Number(__ENV.COMPETIDORES || 100);
const exitos = new Counter('reservas_exitosas');
const sinStock = new Counter('reservas_sin_stock');
const inesperadas = new Counter('respuestas_inesperadas');

export const options = {
  scenarios: { carrera: { executor: 'per-vu-iterations', vus: COMPETIDORES, iterations: 1, maxDuration: '60s' } },
  thresholds: {
    reservas_exitosas: [`count==${UNIDADES}`],
    reservas_sin_stock: [`count==${COMPETIDORES - UNIDADES}`],
    respuestas_inesperadas: ['count==0'],
    checks: ['rate==1'],
  },
  insecureSkipTLSVerify: true,
};

export function setup() {
  const s = sembrar(1, UNIDADES);
  return { codigo: s.codigo, edicion: s.ediciones[0] };
}

export default function (datos) {
  const r = reservar(datos.edicion, datos.codigo, __VU * 1000, uuid());
  if (r.status === 201) exitos.add(1);
  else if (r.status === 409 && r.json('code') === 'INV-STOCK-INSUFICIENTE') sinStock.add(1);
  else inesperadas.add(1);
  check(r, { 'solo 201 o 409 por falta de stock': (x) => x.status === 201 || (x.status === 409 && x.json('code') === 'INV-STOCK-INSUFICIENTE') });
}

export function teardown(datos) {
  const d = disponibilidad(datos.edicion, 1);
  check(d, { 'el vendible final es 0 (ni sobreventa ni stock perdido)': (x) => x.json('vendibleEnLaRed') === 0 });
}

import { resumen } from '../lib/resumen.js';
export const handleSummary = resumen('sobreventa', 'AC01: 100 usuarios compiten por 10 unidades; exactamente 10 reservas, 90 rechazos por falta de stock y vendible final 0.');
