// PERFIL REALISTA de carga (K6-1): sesiones de lectores de una librería independiente con varias sucursales, hasta 2 000 sesiones concurrentes, contra el Inventario
// con el dimensionamiento FIJO de producción (VM de 1 OCPU compartida, DD-17). No es una prueba de estrés: el perfil de estrés (rampa.js, pausa de 0,5 s) se conserva aparte como
// evidencia del límite y de la degradación controlada.
//
// Cada usuario virtual es UNA sesión de una persona: hace una acción, piensa de 5 a 30 s (media 15 s; 5 + 25·u^1,5 con u uniforme) y repite. Mezcla de acciones (justificada en DOC-1 §6d):
//   75 %  catálogo y búsqueda     — la atiende el E-commerce (su propio catálogo); NO llega al Inventario y no se mide aquí (el E-commerce no corre en este entorno).
//   18 %  detalle con disponibilidad — una consulta de disponibilidad al Inventario (RNF02).
//    5 %  carrito                 — se modela como una consulta de disponibilidad de la línea (cota superior: AC02, «consultan la disponibilidad y agregan libros al carrito»).
//    2 %  checkout con reserva    — consulta de disponibilidad, reserva y, tras el pago (simulado: la pasarela no entra), confirmación: 3 llamadas; el tramo reservar + confirmar es el de RNF02 (p95 < 3 s).
// Con 2 000 sesiones: 2 000 / 15 s ≈ 133 acciones/s y (0,18 + 0,05 + 0,02·3) = 0,29 llamadas al Inventario por acción ≈ 39 solicitudes/s.
//
// Niveles de sesiones concurrentes (NIVELES, por omisión 100,250,500,1000,1500,2000): cada uno sube en 30 s, se sostiene (SEGUNDOS_POR_NIVEL, 100 s; el último, 180 s) y el último baja en 30 s.
// Umbrales (los de RNF02, sin cambiarlos), por nivel: p95 < 500 ms en la consulta de disponibilidad, p95 < 3 s en reservar + confirmar y menos de 1 % de solicitudes con error.
// La curva «usuarios concurrentes sostenibles» es el mayor nivel que cumple los tres.
import { check, sleep } from 'k6';
import exec from 'k6/execution';
import { Counter, Trend } from 'k6/metrics';
import { sembrar, disponibilidad, reservar, confirmar, uuid } from '../lib/comun.js';
import { resumen } from '../lib/resumen.js';

const NIVELES = (__ENV.NIVELES || '100,250,500,1000,1500,2000').split(',').map(Number);
const SUBIDA = 30;
const SOSTENIDO = Number(__ENV.SEGUNDOS_POR_NIVEL || 100);
const SOSTENIDO_FINAL = Number(__ENV.SEGUNDOS_FINAL || 180);
const BAJADA = 30;
const HOLGURA = 35; // un usuario que sale termina su pausa (hasta 30 s) antes de que empiece el nivel siguiente

const acciones = new Counter('acciones');
const checkoutTramo = new Trend('checkout_tramo', true);
const OP_DISPONIBILIDAD = 'inventario.disponibilidad.consultar';

const scenarios = {};
const thresholds = {
  'acciones{tipo:catalogo}': ['count>=0'], 'acciones{tipo:detalle}': ['count>=0'], 'acciones{tipo:carrito}': ['count>=0'], 'acciones{tipo:checkout}': ['count>=0'],
};
let inicio = 0;
NIVELES.forEach((n, i) => {
  const ultimo = i === NIVELES.length - 1;
  const sostenido = ultimo ? SOSTENIDO_FINAL : SOSTENIDO;
  const stages = [{ duration: `${SUBIDA}s`, target: n }, { duration: `${sostenido}s`, target: n }];
  if (ultimo) stages.push({ duration: `${BAJADA}s`, target: 0 });
  scenarios[`nivel_${n}`] = { executor: 'ramping-vus', startVUs: 0, stages, startTime: `${inicio}s`, tags: { nivel: String(n) }, gracefulRampDown: '5s', gracefulStop: '35s' };
  inicio += SUBIDA + sostenido + HOLGURA;
  thresholds[`http_req_duration{nivel:${n},operacion:${OP_DISPONIBILIDAD}}`] = ['p(95)<500'];
  thresholds[`checkout_tramo{nivel:${n}}`] = ['p(95)<3000'];
  thresholds[`http_req_failed{nivel:${n}}`] = ['rate<0.01'];
  thresholds[`http_reqs{nivel:${n}}`] = ['count>=0'];
  thresholds[`acciones{nivel:${n}}`] = ['count>=0'];
});

export const options = { scenarios, thresholds, insecureSkipTLSVerify: true };

// Una sola entrada de mercadería por edición: el límite de ajustes del personal (por hora) no admite más.
export function setup() { return sembrar(20, 5000); }

/** Pausa de persona: de 5 a 30 s con media 15 s. */
function pensarComoPersona() { sleep(5 + 25 * Math.pow(Math.random(), 1.5)); }

export default function (datos) {
  const vu = exec.vu.idInTest;
  // Las sesiones no arrancan a la vez: la primera acción cae en un instante al azar dentro de una pausa.
  if (exec.vu.iterationInScenario === 0) sleep(Math.random() * 15);
  const edicion = datos.ediciones[Math.floor(Math.random() * datos.ediciones.length)];
  const x = Math.random();
  if (x < 0.75) {
    acciones.add(1, { tipo: 'catalogo' });
  } else if (x < 0.93) {
    acciones.add(1, { tipo: 'detalle' });
    check(disponibilidad(edicion, vu), { 'disponibilidad 200': (r) => r.status === 200 });
  } else if (x < 0.98) {
    acciones.add(1, { tipo: 'carrito' });
    check(disponibilidad(edicion, vu), { 'disponibilidad 200': (r) => r.status === 200 });
  } else {
    acciones.add(1, { tipo: 'checkout' });
    check(disponibilidad(edicion, vu), { 'disponibilidad 200': (r) => r.status === 200 });
    const pedido = uuid();
    const r = reservar(edicion, datos.codigo, vu, pedido);
    let tramo = r.timings.duration;
    if (check(r, { 'reserva creada (201)': (y) => y.status === 201 })) {
      sleep(1 + Math.random() * 2); // el pago en la pasarela (no medido)
      const c = confirmar(pedido, vu);
      tramo += c.timings.duration;
      check(c, { 'reserva confirmada (200)': (y) => y.status === 200 });
    }
    checkoutTramo.add(tramo);
  }
  pensarComoPersona();
}

export const handleSummary = resumen('perfil-realista', 'Perfil realista de sesiones de lectores (pausa de 5 a 30 s, media 15 s; 75 % catálogo, 18 % detalle, 5 % carrito, 2 % checkout) hasta 2 000 sesiones concurrentes, con el dimensionamiento fijo de producción (DD-17); umbrales de RNF02 por nivel.');
