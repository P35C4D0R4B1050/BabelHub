// Ejecuta el guion real wwwroot/js/contrasenas.js con Node y un doble mínimo del documento (sin red ni dependencias).
// Entrada (stdin, JSON): { guion, evaluar: [texto...], formularios: [ [ {atributos, plantillas} ... ] ], pasos: [...], tardios: [...] }
// Salida (stdout, JSON): { niveles, estados, efectos, fugas }
'use strict';

const entrada = JSON.parse(require('fs').readFileSync(0, 'utf8'));

function coincide(nodo, selector) {
  const partes = /^([a-z]+)?(?:\[([\w:-]+)(?:="([^"]*)")?\])?$/.exec(selector);
  if (!partes) {
    throw new Error('Selector que el doble no entiende: ' + selector);
  }

  const [, etiqueta, atributo, valor] = partes;
  if (etiqueta && nodo.tagName !== etiqueta) {
    return false;
  }

  return !atributo || (nodo.attrs.has(atributo) && (valor === undefined || nodo.attrs.get(atributo) === valor));
}

class Nodo {
  constructor(etiqueta) {
    this.tagName = etiqueta;
    this.attrs = new Map();
    this.hijos = [];
    this.parentNode = null;
    this.propio = '';
    this.oyentes = {};
    this.hidden = false;
    this.innerHTML = '';
    this.value = '';
  }

  setAttribute(nombre, valor) { this.attrs.set(nombre, String(valor)); }

  getAttribute(nombre) { return this.attrs.has(nombre) ? this.attrs.get(nombre) : null; }

  removeAttribute(nombre) { this.attrs.delete(nombre); }

  appendChild(hijo) { this.insertBefore(hijo, null); return hijo; }

  insertBefore(nodo, referencia) {
    nodo.parentNode = this;
    const posicion = referencia ? this.hijos.indexOf(referencia) : -1;
    if (posicion < 0) {
      this.hijos.push(nodo);
    } else {
      this.hijos.splice(posicion, 0, nodo);
    }

    return nodo;
  }

  get nextSibling() {
    return this.parentNode ? this.parentNode.hijos[this.parentNode.hijos.indexOf(this) + 1] || null : null;
  }

  get textContent() { return this.propio + this.hijos.map((h) => h.textContent).join(''); }

  set textContent(valor) { this.hijos = []; this.propio = String(valor); }

  get form() { return this.closest('form'); }

  closest(selector) {
    for (let nodo = this; nodo; nodo = nodo.parentNode) {
      if (coincide(nodo, selector)) {
        return nodo;
      }
    }

    return null;
  }

  todos() { return this.hijos.flatMap((h) => [h, ...h.todos()]); }

  querySelectorAll(selector) { return this.todos().filter((n) => coincide(n, selector)); }

  querySelector(selector) { return this.querySelectorAll(selector)[0] || null; }

  addEventListener(tipo, manejador) { (this.oyentes[tipo] = this.oyentes[tipo] || []).push(manejador); }

  emitir(tipo, extra) {
    const evento = Object.assign({ type: tipo, defaultPrevented: false, preventDefault() { this.defaultPrevented = true; } }, extra);
    (this.oyentes[tipo] || []).forEach((m) => m(evento));
    return evento;
  }
}

const documento = new Nodo('body');
documento.createElement = (etiqueta) => new Nodo(etiqueta);
const campos = {};

function formulario(definicion) {
  const nodo = documento.appendChild(new Nodo('form'));
  for (const campo of definicion) {
    const envoltura = nodo.appendChild(new Nodo('div'));
    envoltura.setAttribute('data-bh-campo-de-contrasena', '');
    const etiqueta = envoltura.appendChild(new Nodo('label'));
    const input = etiqueta.appendChild(new Nodo('input'));
    Object.entries(campo.atributos).forEach(([n, v]) => input.setAttribute(n, v));
    Object.entries(campo.plantillas || {}).forEach(([cual, dibujo]) => {
      const plantilla = envoltura.appendChild(new Nodo('template'));
      plantilla.setAttribute('data-bh-icono', cual);
      plantilla.innerHTML = dibujo;
    });
    campos[input.getAttribute('name')] = input;
  }

  return nodo;
}

(entrada.formularios || []).forEach(formulario);

// El reloj es de la prueba: los anuncios esperan una pausa.
let ahora = 0;
let siguiente = 1;
const esperas = new Map();
globalThis.setTimeout = (funcion, plazo) => { esperas.set(siguiente, { funcion, cuando: ahora + plazo }); return siguiente++; };
globalThis.clearTimeout = (id) => { esperas.delete(id); };
function avanzar(ms) {
  ahora += ms;
  for (const [id, espera] of [...esperas]) {
    if (espera.cuando <= ahora) {
      esperas.delete(id);
      espera.funcion();
    }
  }
}

// Todo lo que sacaría la contraseña del campo: cada uso se anota (ACC-05).
const efectos = [];
const anotar = (que) => () => { efectos.push(que); };
globalThis.fetch = anotar('fetch');
globalThis.XMLHttpRequest = function () { efectos.push('XMLHttpRequest'); };
Object.defineProperty(globalThis, 'navigator', { configurable: true, get() { efectos.push('navigator'); return { sendBeacon: anotar('sendBeacon') }; } });
for (const almacen of ['localStorage', 'sessionStorage', 'history', 'indexedDB']) {
  Object.defineProperty(globalThis, almacen, { configurable: true, get() { efectos.push(almacen); return {}; } });
}

Object.defineProperty(documento, 'cookie', { get() { efectos.push('cookie'); return ''; }, set() { efectos.push('cookie'); } });
const escribirSalida = process.stdout.write.bind(process.stdout);
for (const metodo of ['log', 'info', 'warn', 'error', 'debug', 'trace']) {
  console[metodo] = anotar('console.' + metodo);
}

const oyentesDeLaVentana = {};
globalThis.addEventListener = (tipo, manejador) => { (oyentesDeLaVentana[tipo] = oyentesDeLaVentana[tipo] || []).push(manejador); };
globalThis.document = documento;
const guion = require(entrada.guion);

const secretos = new Set();
const fugas = [];
function buscarFugas() {
  for (const nodo of documento.todos()) {
    for (const texto of [nodo.propio, nodo.innerHTML, ...nodo.attrs.values()]) {
      for (const secreto of secretos) {
        if (texto.includes(secreto)) {
          fugas.push(secreto);
        }
      }
    }
  }
}

function estado(envios) {
  const resultado = { envios, campos: {} };
  for (const [nombre, input] of Object.entries(campos)) {
    const envoltura = input.closest('[data-bh-campo-de-contrasena]');
    const botones = envoltura.querySelectorAll('button[data-bh-ver]');
    const medidor = envoltura.querySelector('[data-bh-medidor]');
    const aviso = envoltura.querySelector('[data-bh-coincidencia]');
    resultado.campos[nombre] = {
      tipo: input.getAttribute('type'),
      valor: input.value,
      botones: botones.length,
      boton: botones[0] ? { tipo: botones[0].getAttribute('type'), visible: botones[0].hijos[0].textContent, nombre: botones[0].textContent, conName: botones[0].attrs.has('name') } : null,
      medidor: medidor ? { oculto: medidor.hidden, nivel: medidor.getAttribute('data-nivel'), texto: medidor.textContent } : null,
      aviso: aviso ? { oculto: aviso.hidden, coincide: aviso.getAttribute('data-coincide'), texto: aviso.textContent, icono: aviso.hijos[0].innerHTML } : null,
      anuncios: envoltura.querySelectorAll('[data-bh-anuncio]').map((r) => ({ cortesia: r.getAttribute('aria-live'), texto: r.textContent })),
    };
  }

  return resultado;
}

const estados = [estado([])];
for (const paso of entrada.pasos || []) {
  const envios = [];
  if (paso.tipo === 'escribir') {
    campos[paso.campo].value = paso.valor;
    if (paso.valor.length >= 6) {
      secretos.add(paso.valor);
    }

    campos[paso.campo].emitir(paso.evento || 'input');
  } else if (paso.tipo === 'pulsar') {
    campos[paso.campo].closest('[data-bh-campo-de-contrasena]').querySelector('button[data-bh-ver]').emitir('click');
  } else if (paso.tipo === 'enviar') {
    envios.push(campos[paso.campo].form.emitir('submit').defaultPrevented);
  } else if (paso.tipo === 'volver') {
    (oyentesDeLaVentana.pageshow || []).forEach((m) => m({ persisted: true }));
  } else if (paso.tipo === 'salir') {
    (oyentesDeLaVentana.pagehide || []).forEach((m) => m({ persisted: true }));
  } else if (paso.tipo === 'avanzar') {
    avanzar(paso.ms);
  } else if (paso.tipo === 'llegaPorHtmx') {
    const nuevo = formulario(entrada.tardios);
    documento.emitir('htmx:load', { detail: { elt: nuevo } });
    documento.emitir('htmx:load', { detail: { elt: nuevo } });
  }

  buscarFugas();
  estados.push(estado(envios));
}

escribirSalida(JSON.stringify({
  niveles: (entrada.evaluar || []).map((texto) => { const n = guion.evaluar(texto, entrada.minimo); return n === null ? null : guion.NIVELES[n]; }),
  estados,
  efectos,
  fugas,
}));
