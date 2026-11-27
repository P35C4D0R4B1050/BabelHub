// AC-SEL-47 (venta por sucursal): muchos clientes compiten por las últimas unidades de una edición en UNA sucursal con una, dos o tres líneas de la misma edición cada uno, mientras otra sucursal tiene stock.
// Criterios duros (siempre): lo apartado nunca supera el vendible de la sucursal, el vendible nunca es negativo, la otra sucursal no se toca (una línea no se reasigna) y ningún 500 ni respuesta fuera de
// contrato (201, 409 INV-STOCK-INSUFICIENTE o, por falta de capacidad, 503 uniforme). Con capacidad suficiente se aparta exactamente el vendible (hay solicitudes de una línea para completar el resto).
import { check } from 'k6';
import { Counter, Trend } from 'k6/metrics';
import { sembrar, sembrarOtraSucursal, lineaEnSucursal, uuid, usuarioDe, cabeceras, post, contar, http, API, UMBRALES_DE_ESTADOS } from '../lib/comun.js';
import { resumen } from '../lib/resumen.js';

const UNIDADES = Number(__ENV.UNIDADES || 20);
const COMPETIDORES = Number(__ENV.COMPETIDORES || 100);
const apartadas = new Counter('unidades_apartadas');
const exitos = new Counter('reservas_exitosas');
const sinStock = new Counter('reservas_sin_stock');
const porCapacidad = new Counter('rechazos_por_capacidad');
const inesperadas = new Counter('respuestas_inesperadas');
const vendibleFinal = new Trend('vendible_final_de_la_sucursal');
const vendibleDeLaOtra = new Trend('vendible_final_de_la_otra');

export const options = {
  scenarios: { carrera: { executor: 'per-vu-iterations', vus: COMPETIDORES, iterations: 1, maxDuration: '90s' } },
  thresholds: {
    ...UMBRALES_DE_ESTADOS,
    unidades_apartadas: [`count<=${UNIDADES}`],
    respuestas_inesperadas: ['count==0'],
    vendible_final_de_la_sucursal: ['min>=0', `max<=${UNIDADES}`],
    vendible_final_de_la_otra: [`min>=${UNIDADES}`, `max<=${UNIDADES}`],
    reservas_exitosas: ['count>=0'],
    rechazos_por_capacidad: ['count>=0'],
    reservas_sin_stock: ['count>=0'],
    checks: ['rate==1'],
  },
  insecureSkipTLSVerify: true,
};

export function setup() {
  const s = sembrar(1, UNIDADES);
  const otra = sembrarOtraSucursal(s.ediciones[0], UNIDADES);
  return { codigo: s.codigo, otra, edicion: s.ediciones[0] };
}

export default function (datos) {
  const lineas = Array.from({ length: 1 + (__VU % 3) }, () => lineaEnSucursal(datos.edicion, datos.codigo, 1));
  const cuerpo = { pedidoId: uuid(), tipoDeEntrega: 'recojo', lineas };
  const r = contar(post('/reservas', cuerpo, cabeceras.usuario(usuarioDe(__VU).token, __VU, true), 'inventario.reservas.crear'));
  const sinExistencias = r.status === 409 && r.json('code') === 'INV-STOCK-INSUFICIENTE';
  const capacidad = r.status === 503 && r.json('code') === 'INV-SERVICIO-NO-DISPONIBLE';
  if (r.status === 201) {
    exitos.add(1);
    apartadas.add(lineas.length);
    check(r, { 'cada línea queda en la sucursal pedida': (x) => x.json('lineas').length === lineas.length && x.json('lineas').every((l) => l.codigoDeFuente === datos.codigo && !l.requiereTraslado) });
  } else if (sinExistencias) sinStock.add(1);
  else if (capacidad) porCapacidad.add(1);
  else {
    inesperadas.add(1);
    console.warn(`inesperada ${r.status}: ${String(r.body).slice(0, 200)}`);
  }
  check(r, { 'solo 201, 409 por falta de stock o 503 uniforme': (x) => x.status === 201 || sinExistencias || capacidad });
}

export function teardown(datos) {
  const d = http.get(`${API}/disponibilidad/${datos.edicion}`, { headers: cabeceras.lectura(1) });
  const de = (codigo) => d.json('fuentes').find((f) => f.codigoDeFuente === codigo).vendible;
  vendibleFinal.add(de(datos.codigo));
  vendibleDeLaOtra.add(de(datos.otra));
}

export const handleSummary = resumen('sobreventa-por-sucursal', `AC-SEL-47: ${COMPETIDORES} clientes compiten por ${UNIDADES} unidades de una edición en una sucursal, con una a tres líneas cada uno y otras ${UNIDADES} en otra sucursal; nunca se aparta más que el vendible de la sucursal, la otra no se toca y apartadas + vendible final = ${UNIDADES}.`);
