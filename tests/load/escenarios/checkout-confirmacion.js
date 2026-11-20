// Confirmación del checkout, tramo del Inventario (sin pasarela): reservar las líneas del pedido y confirmar la reserva al aprobarse el pago. El p95 del
// tramo completo debe ser menor a 3 s con hasta 50 usuarios concurrentes y un ritmo de 5 compras por segundo (RITMO). (El tramo del E-commerce —carrito, pedido— se mide cuando su Web esté completa.)
import { check } from 'k6';
import exec from 'k6/execution';
import { Trend } from 'k6/metrics';
import { sembrar, reservar, confirmar, uuid, UMBRALES_DE_ESTADOS } from '../lib/comun.js';

const tramo = new Trend('checkout_reservar_y_confirmar', true);
export const options = {
  scenarios: { checkout: { executor: 'constant-arrival-rate', rate: Number(__ENV.RITMO || 5), timeUnit: '1s', duration: __ENV.DURACION || '60s', preAllocatedVUs: 50, maxVUs: 50 } },
  thresholds: {
    ...UMBRALES_DE_ESTADOS,
    checkout_reservar_y_confirmar: ['p(95)<3000'],
    http_req_failed: ['rate<0.001'],
    checks: ['rate>0.999'],
  },
  insecureSkipTLSVerify: true,
};

export function setup() { return sembrar(10, 5000); }

export default function (datos) {
  const edicion = datos.ediciones[Math.floor(Math.random() * datos.ediciones.length)];
  const pedido = uuid();
  const t0 = Date.now();
  // Cada compra es de un cliente distinto (origen y usuario propios): el límite de reservas por origen es de 10 por hora y no se mide aquí, sino en limites.js.
  const cliente = exec.scenario.iterationInTest;
  const r = reservar(edicion, datos.codigo, cliente, pedido);
  const ok = check(r, { 'reserva creada (201)': (x) => x.status === 201 });
  if (ok) {
    const c = confirmar(pedido, cliente);
    check(c, { 'reserva confirmada (200)': (x) => x.status === 200 });
  }
  tramo.add(Date.now() - t0);
}

import { resumen } from '../lib/resumen.js';
export const handleSummary = resumen('checkout-confirmacion', 'Tramo del Inventario de la confirmación del checkout (reservar y confirmar), 50 usuarios concurrentes; p95 < 3 s.');
