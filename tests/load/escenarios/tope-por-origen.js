// SEL-19 / AC-SEL-19 (S2 de inventario-seleccion-fuente): un mismo origen no inicia más de 10 reservas temporales en una ventana deslizante de una hora.
// Fase 1 (ráfaga): ORIGENES orígenes distintos, cada uno con INTENTOS cuentas distintas que reservan a la vez; por origen deben aceptarse exactamente 10 (201) y el resto
// rechazarse con 429 INV-LIMITE-EXCEDIDO y Retry-After. Fase 2 (después de la ráfaga): un intento más de cada origen lleno sigue rechazado, y un origen nuevo (cuya hora aún no se
// ha tocado) sigue reservando: el tope de uno no afecta a otro. El arnés reparte orígenes distintos a propósito en los demás escenarios; este fija el origen por grupo.
// La latencia de la ráfaga (todas las solicitudes a la vez, sin pausa) se informa pero no es umbral: aquí se valida la cifra del tope, no el rendimiento (eso lo miden checkout-confirmacion y perfil-realista).
// La latencia de la ráfaga (todas las solicitudes a la vez, sin pausa) se informa pero no es umbral: aquí se valida la cifra del tope, no el rendimiento (eso lo miden checkout-confirmacion y perfil-realista).
import { check } from 'k6';
import exec from 'k6/execution';
import { Counter } from 'k6/metrics';
import { sembrar, uuid, usuarioDe, cabeceras, post, contar, lineaEnSucursal, UMBRALES_DE_ESTADOS } from '../lib/comun.js';
import { resumen } from '../lib/resumen.js';

const ORIGENES = Number(__ENV.ORIGENES || 10);
const INTENTOS = Number(__ENV.INTENTOS || 12);
const TOPE = 10;
const aceptadas = new Counter('reservas_aceptadas');
const limitadas = new Counter('reservas_limitadas');
const tardias = new Counter('fase2_limitadas');
const nuevoOrigen = new Counter('fase2_origen_nuevo_aceptadas');
const inesperadas = new Counter('respuestas_inesperadas');

const umbralesPorOrigen = {};
for (let o = 0; o < ORIGENES; o++) {
  umbralesPorOrigen[`reservas_aceptadas{origen:o${o}}`] = [`count==${TOPE}`];
}

export const options = {
  scenarios: {
    rafaga: { executor: 'per-vu-iterations', exec: 'rafaga', vus: ORIGENES * INTENTOS, iterations: 1, maxDuration: '90s' },
    despues: { executor: 'per-vu-iterations', exec: 'despues', vus: ORIGENES + 1, iterations: 1, startTime: '40s', maxDuration: '60s' },
  },
  thresholds: {
    ...UMBRALES_DE_ESTADOS,
    ...umbralesPorOrigen,
    reservas_aceptadas: [`count==${TOPE * ORIGENES}`],
    reservas_limitadas: [`count==${(INTENTOS - TOPE) * ORIGENES}`],
    fase2_limitadas: [`count==${ORIGENES}`],
    fase2_origen_nuevo_aceptadas: ['count==1'],
    respuestas_inesperadas: ['count==0'],
    'estados{estado:500}': ['count==0'],
    checks: ['rate==1'],
  },
  insecureSkipTLSVerify: true,
};

export function setup() {
  // Una unidad por reserva aceptada de la ráfaga y de la fase 2, con holgura; varias ediciones para no concentrar el bloqueo en una sola fila.
  const s = sembrar(5, Math.ceil((ORIGENES * (TOPE + 2)) / 5) + 20);
  return { codigo: s.codigo, ediciones: s.ediciones };
}

// Los orígenes de este escenario se numeran aparte (de 1 000 en adelante) y cada corrida se desplaza (DESPLAZO_DE_ORIGEN): el tope sigue vigente entre corridas durante una hora.
function intento(datos, numeroDeOrigen, numeroDeCuenta, edicion) {
  const u = usuarioDe(numeroDeCuenta);
  const cuerpo = { pedidoId: uuid(), tipoDeEntrega: 'recojo', lineas: [lineaEnSucursal(edicion, datos.codigo, 1)] };
  return contar(post('/reservas', cuerpo, cabeceras.usuario(u.token, 1000 + numeroDeOrigen, true), 'inventario.reservas.crear'));
}

function clasificar(r, etiqueta) {
  const limite = r.status === 429 && r.json('code') === 'INV-LIMITE-EXCEDIDO';
  if (limite) check(r, { '429 con Retry-After': (x) => Number(x.headers['Retry-After']) > 0 });
  if (r.status !== 201 && !limite) {
    inesperadas.add(1);
    console.warn(`inesperada ${etiqueta} ${r.status}: ${String(r.body).slice(0, 200)}`);
  }
  return { creada: r.status === 201, limite };
}

export function rafaga(datos) {
  const indice = exec.scenario.iterationInTest;
  const origen = Math.floor(indice / INTENTOS);
  const c = clasificar(intento(datos, origen, indice, datos.ediciones[origen % datos.ediciones.length]), 'rafaga');
  if (c.creada) aceptadas.add(1, { origen: `o${origen}` });
  else if (c.limite) limitadas.add(1);
}

export function despues(datos) {
  const indice = exec.scenario.iterationInTest;
  if (indice < ORIGENES) {
    // Un origen ya lleno: otra cuenta del mismo origen sigue rechazada.
    const c = clasificar(intento(datos, indice, ORIGENES * INTENTOS + indice, datos.ediciones[indice % datos.ediciones.length]), 'fase2');
    if (c.limite) tardias.add(1);
  } else {
    // Un origen que no participó en la ráfaga reserva con normalidad.
    const c = clasificar(intento(datos, ORIGENES + 7, ORIGENES * INTENTOS + indice, datos.ediciones[0]), 'fase2-nuevo');
    if (c.creada) nuevoOrigen.add(1);
  }
}

export const handleSummary = resumen('tope-por-origen', `SEL-19, AC-SEL-19: ${ORIGENES} orígenes con ${INTENTOS} cuentas cada uno reservan a la vez; exactamente ${TOPE} se aceptan por origen y el resto recibe 429 INV-LIMITE-EXCEDIDO con Retry-After; un intento posterior del origen lleno sigue rechazado y un origen nuevo reserva con normalidad.`);
