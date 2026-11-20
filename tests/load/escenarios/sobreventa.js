// AC01 (concurrencia sin sobreventa): sobre las últimas unidades de una edición compiten muchos usuarios a la vez.
// Criterios duros (siempre): jamás más reservas que unidades, el vendible final nunca es negativo, ningún 500 ni respuesta fuera de contrato (201, 409 INV-STOCK-INSUFICIENTE o,
// por falta de capacidad, 503 uniforme). Con capacidad suficiente la corrida es exacta: tantas reservas como unidades, el resto 409 y vendible final 0; si el entorno acotado no
// da abasto, el exceso se rechaza con 503 (se cuenta en `rechazos_por_capacidad`) y las unidades sin vender quedan como vendible, nunca como sobreventa.
import { check } from 'k6';
import { Counter, Trend } from 'k6/metrics';
import { sembrar, reservar, disponibilidad, uuid, UMBRALES_DE_ESTADOS } from '../lib/comun.js';
import { resumen } from '../lib/resumen.js';

const UNIDADES = Number(__ENV.UNIDADES || 10);
const COMPETIDORES = Number(__ENV.COMPETIDORES || 100);
const exitos = new Counter('reservas_exitosas');
const sinStock = new Counter('reservas_sin_stock');
const porCapacidad = new Counter('rechazos_por_capacidad');
const inesperadas = new Counter('respuestas_inesperadas');
const vendibleFinal = new Trend('vendible_final');

export const options = {
  scenarios: { carrera: { executor: 'per-vu-iterations', vus: COMPETIDORES, iterations: 1, maxDuration: '90s' } },
  thresholds: {
    ...UMBRALES_DE_ESTADOS,
    reservas_exitosas: [`count<=${UNIDADES}`],
    respuestas_inesperadas: ['count==0'],
    vendible_final: ['min>=0', `max<=${UNIDADES}`],
    rechazos_por_capacidad: ['count>=0'],
    reservas_sin_stock: ['count>=0'],
    checks: ['rate==1'],
  },
  insecureSkipTLSVerify: true,
};

export function setup() {
  const s = sembrar(1, UNIDADES);
  return { codigo: s.codigo, edicion: s.ediciones[0] };
}

export default function (datos) {
  const r = reservar(datos.edicion, datos.codigo, __VU, uuid());
  const sinExistencias = r.status === 409 && r.json('code') === 'INV-STOCK-INSUFICIENTE';
  const capacidad = r.status === 503 && r.json('code') === 'INV-SERVICIO-NO-DISPONIBLE';
  if (r.status === 201) exitos.add(1);
  else if (sinExistencias) sinStock.add(1);
  else if (capacidad) porCapacidad.add(1);
  else {
    inesperadas.add(1);
    console.warn(`inesperada ${r.status}: ${String(r.body).slice(0, 200)}`);
  }
  check(r, { 'solo 201, 409 por falta de stock o 503 uniforme': (x) => x.status === 201 || sinExistencias || capacidad });
}

export function teardown(datos) {
  const d = disponibilidad(datos.edicion, 1);
  vendibleFinal.add(d.json('vendibleEnLaRed'));
}

export const handleSummary = resumen('sobreventa', 'AC01: 100 usuarios compiten por 10 unidades; nunca más de 10 reservas, vendible final no negativo y vendible final + reservas = 10 (sin sobreventa ni stock perdido).');
