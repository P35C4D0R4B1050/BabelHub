// Utilidades comunes de los escenarios de carga (K6-1). Las credenciales NUNCA van en este archivo: llegan por variables de entorno y por el archivo de
// credenciales que emite entorno/servidor-de-claves.mjs (fuera del repositorio). Ver tests/load/README.md.
import http from 'k6/http';
import { check, sleep } from 'k6';

export const BASE = __ENV.BASE_URL;
const CRED = {
  lectura: __ENV.CREDENCIAL_DISPONIBILIDAD,
  usuario: __ENV.CREDENCIAL_OPERACION_DE_USUARIO,
  sistema: __ENV.CREDENCIAL_EXPIRACION_DE_RESERVAS,
  origen: __ENV.CREDENCIAL_ORIGEN,
};
const ARCHIVO = __ENV.ARCHIVO_DE_CREDENCIALES;
if (!BASE || !CRED.lectura || !CRED.usuario || !CRED.sistema || !CRED.origen || !ARCHIVO) {
  throw new Error('Faltan BASE_URL, CREDENCIAL_* o ARCHIVO_DE_CREDENCIALES (los fija tests/load/ejecutar.sh).');
}
const credenciales = JSON.parse(open(ARCHIVO));
export const administrador = credenciales.administrador;
export const API = `${BASE}/api/inventario/v1`;

export function uuid() {
  const h = '0123456789abcdef';
  let s = '';
  for (let i = 0; i < 36; i++) {
    if (i === 8 || i === 13 || i === 18 || i === 23) s += '-';
    else if (i === 14) s += '4';
    else if (i === 19) s += h[8 + Math.floor(Math.random() * 4)];
    else s += h[Math.floor(Math.random() * 16)];
  }
  return s;
}

/** Un cliente final distinto por número (usuario virtual o iteración): cada uno cae en su propio límite por origen (SEG-17, SEG-32). */
// El desplazamiento cambia en cada corrida: el tope de 10 reservas por hora y por origen sigue vigente entre corridas, y repetir los mismos orígenes lo agotaría.
const DESPLAZO = Number(__ENV.DESPLAZO_DE_ORIGEN || 0);
export function origen(vu) {
  const n = (vu + DESPLAZO) & 0xffffff;
  return `10.${(n >> 16) & 255}.${(n >> 8) & 255}.${n & 255}`;
}

export function usuarioDe(vu) {
  return credenciales.usuarios[vu % credenciales.usuarios.length];
}

function base(alcance, vu, extra) {
  return Object.assign({
    'Content-Type': 'application/json',
    'X-Credencial-De-Servicio': CRED[alcance],
    'X-Credencial-De-Origen': CRED.origen,
    'X-Origen-Reenviado': origen(vu || 1),
  }, extra || {});
}

export const cabeceras = {
  lectura: (vu) => base('lectura', vu),
  sistema: (vu) => base('sistema', vu, { 'Idempotency-Key': uuid() }),
  usuario: (token, vu, conClave) => base('usuario', vu, Object.assign({ Authorization: `Bearer ${token}` }, conClave ? { 'Idempotency-Key': uuid() } : {})),
};

export function post(ruta, cuerpo, cabecera, etiqueta) {
  return http.post(`${API}${ruta}`, cuerpo === null ? null : JSON.stringify(cuerpo), { headers: cabecera, tags: { operacion: etiqueta || ruta } });
}

/** Siembra una fuente de recepción y `ediciones` ediciones impresas con `unidades` unidades cada una (ids nuevos en cada corrida: el stock inicial es exacto). */
export function sembrar(ediciones, unidades) {
  const admin = (conClave) => cabeceras.usuario(administrador.token, 1, conClave);
  const codigo = `carga-${uuid().slice(0, 8)}`;
  const f = http.post(`${API}/fuentes`, JSON.stringify({ codigo, nombre: `Fuente de carga ${codigo}`, region: 'lima', contactoDeNotificacion: null }), { headers: admin(false) });
  if (f.status !== 201) throw new Error(`No se pudo registrar la fuente: ${f.status} ${f.body}`);
  const r = http.put(`${API}/fuentes-de-recepcion`, JSON.stringify({ codigo }), { headers: admin(false) });
  if (r.status !== 204) throw new Error(`No se pudo designar la recepción: ${r.status} ${r.body}`);
  const ids = [];
  for (let i = 0; i < ediciones; i++) {
    const id = uuid();
    const e = http.put(`${API}/ediciones/${id}`, JSON.stringify({ formato: 'impreso' }), { headers: admin(false) });
    if (e.status !== 204) throw new Error(`No se pudo registrar la edición: ${e.status} ${e.body}`);
    let restante = unidades;
    while (restante > 0) {
      const cantidad = Math.min(restante, 5000);
      const g = http.post(`${API}/ingresos-de-mercaderia`, JSON.stringify({ edicionId: id, codigoDeFuente: null, cantidad, referencia: 'carga', nota: null }), { headers: admin(true) });
      if (g.status !== 201) throw new Error(`No se pudo ingresar mercadería: ${g.status} ${g.body}`);
      restante -= cantidad;
    }
    ids.push(id);
  }
  return { codigo, ediciones: ids };
}

/** Pausa entre solicitudes de un usuario (por omisión 0,5 s: una persona no consulta sin respirar); PENSAR_S=0 mide el peor caso sin pausa. */
export function pensar() {
  const s = Number(__ENV.PENSAR_S === undefined ? 0.5 : __ENV.PENSAR_S);
  if (s > 0) sleep(s);
}

export function disponibilidad(edicion, vu) {
  return contar(http.get(`${API}/disponibilidad/${edicion}`, { headers: cabeceras.lectura(vu), tags: { operacion: 'inventario.disponibilidad.consultar' } }));
}

/** Reserva una unidad de `edicion` a nombre del usuario virtual y devuelve la respuesta. */
export function reservar(edicion, sucursal, vu, pedidoId) {
  const u = usuarioDe(vu);
  const cuerpo = { pedidoId, tipoDeEntrega: 'recojo', codigoDeSucursalDeRecojo: sucursal, regionDeEntrega: null, lineas: [{ lineaId: uuid(), edicionId: edicion, cantidad: 1 }] };
  return contar(post('/reservas', cuerpo, cabeceras.usuario(u.token, vu, true), 'inventario.reservas.crear'));
}

export function confirmar(pedidoId, vu) {
  return contar(http.post(`${API}/reservas/${pedidoId}/confirmar`, null, { headers: cabeceras.sistema(vu), tags: { operacion: 'inventario.reservas.confirmar' } }));
}

export { check, http };

import { Counter } from 'k6/metrics';
const estados = new Counter('estados');
/** Cuenta cada respuesta por su código HTTP (0 es un fallo de red o un plazo vencido): el resumen muestra la distribución. */
export function contar(respuesta) {
  estados.add(1, { estado: String(respuesta.status) });
  if ((respuesta.status === 429 || respuesta.status === 503) && !globalThis.__avisado) {
    globalThis.__avisado = true;
    console.warn(`${respuesta.status} en ${respuesta.url}: ${String(respuesta.body).slice(0, 300)}`);
  }
  return respuesta;
}
export const UMBRALES_DE_ESTADOS = Object.fromEntries(['0', '200', '201', '409', '429', '500', '503'].map((e) => [`estados{estado:${e}}`, ['count>=0']]));
