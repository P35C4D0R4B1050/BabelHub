// BabelHub: formularios de la administración del catálogo (T25, T26). JavaScript mínimo, sin eval ni scripts en línea (CSP, WEB-18).
// Solo traduce los formularios a las acciones JSON de /administracion/…: sube primero los archivos (referencias de un solo uso), arma el cuerpo y muestra los errores
// de campo. Ninguna regla de negocio vive aquí: la autoridad es siempre la Aplicación del Catálogo (el rol, las versiones, la licencia, el ISBN, el stock).
(function () {
  'use strict';

  var metaToken = document.querySelector('meta[name="csrf-token"]');
  var TOKEN = metaToken ? metaToken.content : '';

  // Los motivos estables de los errores de campo, en español (docs/06/08). Lo no listado se dice de forma genérica, sin mostrar lo recibido.
  var CAMPOS = {
    titulo: 'El título', autor: 'El autor', editorial: 'La editorial', categoria: 'La categoría', portadaRef: 'La portada', ediciones: 'Las ediciones', version: 'La versión',
    isbn: 'El ISBN', precio: 'El precio', formato: 'El formato', 'licencia.titular': 'El titular', 'licencia.territorios': 'Los territorios', 'licencia.vigenteHasta': 'La vigencia',
    archivoRef: 'El archivo', stockInicial: 'El stock inicial', cantidad: 'La cantidad', referencia: 'La referencia', nota: 'La nota', token: 'El formulario', motivo: 'El motivo', cuerpo: 'Los datos'
  };
  var MOTIVOS = {
    obligatorio: 'es obligatorio.', 'formato-invalido': 'no tiene un formato válido.', 'fuera-de-rango': 'está fuera del rango permitido.', 'demasiado-largo': 'es demasiado largo.',
    'demasiado-corto': 'es demasiado corto.', 'ya-vencida': 'ya venció: usa una fecha futura.', 'solo-edicion-digital': 'solo aplica a una edición digital.',
    'solo-edicion-impresa-activa': 'solo aplica a una edición impresa activa.', 'tipo-no-valido': 'no es de un tipo admitido.', 'dimensiones-invalidas': 'supera las dimensiones permitidas.',
    'estructura-invalida': 'está dañado o incompleto.', 'demasiado-grande': 'supera el tamaño permitido.', 'pendiente-vencido': 'lleva más de 24 horas pendiente: concílialo.',
    'sin-coincidencia-unica': 'no tiene una única coincidencia en el Inventario.', 'consta-como-aplicado': 'consta como aplicado en el Inventario: no puede marcarse como fallido.',
    'intento-reciente': 'tuvo un intento hace menos de 2 minutos: espera antes de marcarlo como fallido.', obligatorioConfirmar: 'necesita tu confirmación.'
  };
  var CODIGOS = {
    'ECO-ISBN-DUPLICADO': 'Ese ISBN ya lo usa otra edición.',
    'ECO-EDICION-DUPLICADA-POR-TIPO': 'La obra ya tiene una edición de ese formato.',
    'ECO-LICENCIA-INVALIDA': 'Revisa la licencia: titular, territorios y vigencia.',
    'ECO-REFERENCIA-DE-ARCHIVO-INVALIDA': 'El archivo subido venció o ya se usó. Súbelo de nuevo.',
    'ECO-ARCHIVO-INVALIDO': 'El archivo no es válido.',
    'ECO-ARCHIVO-DEMASIADO-GRANDE': 'El archivo supera el tamaño permitido (libros hasta 50 MB, portadas hasta 2 MB).',
    'ECO-CONFLICTO': 'Otra persona cambió esta obra. Recarga la página para ver la versión actual.',
    'ECO-OBRA-SIN-EDICIONES': 'No se puede dar de baja la última edición: da de baja la obra.',
    'ECO-CLAVE-INVALIDA': 'El formulario venció o ya se usó. Recarga la página para registrar otro ingreso.',
    'ECO-INGRESO-PENDIENTE': 'Esta edición ya tiene un ingreso pendiente: reinténtalo o concílialo antes de registrar otro.',
    'ECO-PROHIBIDO': 'No tienes permiso para hacer esto.',
    'ECO-NO-AUTENTICADO': 'Tu sesión venció. Vuelve a ingresar.',
    'ECO-NO-ENCONTRADO': 'Ya no existe lo que intentas cambiar.',
    'ECO-LIMITE-EXCEDIDO': 'Hiciste demasiadas subidas seguidas. Espera un momento e intenta de nuevo.',
    'ECO-SERVICIO-NO-DISPONIBLE': 'Una parte del sistema no responde ahora. Intenta de nuevo en unos minutos.',
    'ECO-REGLA-DE-NEGOCIO-INCUMPLIDA': 'No se puede hacer en este estado.',
    'ECO-VALIDACION-INVALIDA': 'Revisa los datos marcados.'
  };

  function texto(valor) { return (valor == null ? '' : String(valor)).trim(); }
  function numero(valor) { var t = texto(valor).replace(',', '.'); return t === '' ? null : Number(t); }
  function campo(form, nombre) { return form.elements[nombre]; }
  function valor(form, nombre) { var c = campo(form, nombre); return c ? texto(c.value) : ''; }
  function marcado(form, nombre) { var c = campo(form, nombre); return !!(c && c.checked); }

  async function enviar(metodo, url, cuerpo) {
    var cabeceras = { 'RequestVerificationToken': TOKEN };
    var opciones = { method: metodo, headers: cabeceras, credentials: 'same-origin' };
    if (cuerpo !== undefined) { cabeceras['Content-Type'] = 'application/json'; opciones.body = JSON.stringify(cuerpo); }
    var respuesta;
    try { respuesta = await fetch(url, opciones); } catch (e) { return { ok: false, estado: 0, problema: null }; }
    // La sesión vencida llega como una redirección al ingreso (HTML), no como JSON.
    if (respuesta.redirected) { return { ok: false, estado: 401, datos: null, problema: { code: 'ECO-NO-AUTENTICADO' } }; }
    var datos = null;
    try { datos = await respuesta.json(); } catch (e) { datos = null; }
    return { ok: respuesta.ok, estado: respuesta.status, datos: datos, problema: respuesta.ok ? null : datos };
  }

  async function subir(archivo, finalidad) {
    var respuesta;
    try {
      respuesta = await fetch('/administracion/archivos?finalidad=' + finalidad, { method: 'POST', headers: { 'RequestVerificationToken': TOKEN, 'Content-Type': 'application/octet-stream' }, credentials: 'same-origin', body: archivo });
    } catch (e) { return { ok: false, problema: null }; }
    if (respuesta.redirected) { return { ok: false, problema: { code: 'ECO-NO-AUTENTICADO' } }; }
    var datos = null;
    try { datos = await respuesta.json(); } catch (e) { datos = null; }
    return respuesta.ok && datos && datos.referenciaId ? { ok: true, ref: datos.referenciaId } : { ok: false, problema: datos };
  }

  function mensajeDe(problema) {
    var lineas = [];
    if (!problema) { return ['No pudimos conectar con el servidor. Revisa tu conexión e intenta de nuevo.']; }
    var general = CODIGOS[problema.code];
    if (general) { lineas.push(general); }
    (problema.errores || []).forEach(function (e) {
      if (e.campo === 'tituloExistente') { lineas.push('El ISBN ya lo usa «' + e.clave + '».'); return; }
      var nombre = CAMPOS[e.campo] || 'Un dato';
      lineas.push(nombre + ' ' + (MOTIVOS[e.clave] || 'no es válido.'));
    });
    if (lineas.length === 0) { lineas.push('No pudimos completar la acción. Intenta de nuevo.'); }
    return lineas;
  }

  function mostrarErrores(form, lineas) {
    var region = form.querySelector('[data-bh-errores]');
    if (!region) { return; }
    region.textContent = '';
    if (lineas.length === 0) { region.hidden = true; return; }
    var lista = document.createElement('ul');
    lineas.forEach(function (l) { var li = document.createElement('li'); li.textContent = l; lista.appendChild(li); });
    region.appendChild(lista);
    region.hidden = false;
    region.focus();
  }

  function ocupado(form, si) {
    form.querySelectorAll('button[type="submit"], button[data-bh-enviar]').forEach(function (b) { b.disabled = si; });
    form.setAttribute('aria-busy', si ? 'true' : 'false');
  }

  // ---------- Cuerpos ----------

  async function archivoDe(form, nombre, finalidad) {
    var entrada = campo(form, nombre);
    if (!entrada || !entrada.files || entrada.files.length === 0) { return { ok: true, ref: null }; }
    return subir(entrada.files[0], finalidad);
  }

  function licenciaDe(form, prefijo) {
    var territorios = valor(form, prefijo + 'territorios').split(/[\s,;]+/).map(function (t) { return t.toUpperCase(); }).filter(Boolean);
    var tipo = valor(form, prefijo + 'tipo');
    var licencia = { tipo: tipo, titular: valor(form, prefijo + 'titular'), territorios: territorios, formato: valor(form, prefijo + 'formato') };
    if (tipo === 'comercial') { licencia.vigenteHasta = valor(form, prefijo + 'vigencia') || null; }
    return licencia;
  }

  async function edicionNueva(form, tipo) {
    var base = { formato: tipo, isbn: valor(form, tipo + '-isbn'), precio: numero(valor(form, tipo + '-precio')) };
    if (tipo === 'impresa') {
      var stock = numero(valor(form, 'impresa-stock'));
      if (stock !== null) { base.stockInicial = stock; }
      return { ok: true, edicion: base };
    }
    var libro = await archivoDe(form, 'digital-archivo', 'libro');
    if (!libro.ok) { return { ok: false, problema: libro.problema }; }
    base.licencia = licenciaDe(form, 'digital-');
    if (libro.ref) { base.archivoRef = libro.ref; }
    return { ok: true, edicion: base };
  }

  var ACCIONES = {
    'alta-obra': async function (form) {
      var portada = await archivoDe(form, 'portada', 'portada');
      if (!portada.ok) { return { ok: false, problema: portada.problema }; }
      var cuerpo = { titulo: valor(form, 'titulo'), autor: valor(form, 'autor'), editorial: valor(form, 'editorial'), categoria: valor(form, 'categoria'), ediciones: [] };
      if (portada.ref) { cuerpo.portadaRef = portada.ref; }
      var tipos = [];
      if (marcado(form, 'impresa-on')) { tipos.push('impresa'); }
      if (marcado(form, 'digital-on')) { tipos.push('digital'); }
      for (var i = 0; i < tipos.length; i++) {
        var e = await edicionNueva(form, tipos[i]);
        if (!e.ok) { return e; }
        cuerpo.ediciones.push(e.edicion);
      }
      var r = await enviar('POST', '/administracion/obras', cuerpo);
      return r.ok ? { ok: true, destino: '/administracion/obras/' + r.datos.obraId } : r;
    },
    'editar-obra': async function (form) {
      var portada = await archivoDe(form, 'portada', 'portada');
      if (!portada.ok) { return { ok: false, problema: portada.problema }; }
      var cuerpo = { version: Number(form.dataset.version), titulo: valor(form, 'titulo'), autor: valor(form, 'autor'), editorial: valor(form, 'editorial'), categoria: valor(form, 'categoria') };
      if (portada.ref) { cuerpo.portadaRef = portada.ref; }
      var r = await enviar('PUT', form.dataset.url, cuerpo);
      return r.ok ? { ok: true, recargar: true } : r;
    },
    'agregar-edicion': async function (form) {
      var tipo = form.dataset.formato;
      var e = await edicionNueva(form, tipo);
      if (!e.ok) { return e; }
      var r = await enviar('POST', form.dataset.url, { version: Number(form.dataset.version), edicion: e.edicion });
      return r.ok ? { ok: true, recargar: true } : r;
    },
    'editar-edicion': async function (form) {
      var cuerpo = { version: Number(form.dataset.version) };
      var precio = numero(valor(form, 'precio'));
      if (precio !== null) { cuerpo.precio = precio; }
      if (marcado(form, 'cambiar-licencia')) { cuerpo.licencia = licenciaDe(form, 'licencia-'); }
      var libro = await archivoDe(form, 'archivo', 'libro');
      if (!libro.ok) { return { ok: false, problema: libro.problema }; }
      if (libro.ref) { cuerpo.archivoRef = libro.ref; }
      var r = await enviar('PUT', form.dataset.url, cuerpo);
      return r.ok ? { ok: true, recargar: true } : r;
    },
    'accion-simple': async function (form) {
      var cuerpo = {};
      if (form.dataset.version) { cuerpo.version = Number(form.dataset.version); }
      if (campo(form, 'motivo')) { cuerpo.motivo = valor(form, 'motivo'); }
      var r = await enviar(form.dataset.metodo || 'POST', form.dataset.url, cuerpo);
      return r.ok ? { ok: true, destino: form.dataset.destino || null, recargar: !form.dataset.destino } : r;
    },
    'registrar-ingreso': async function (form) {
      var cuerpo = { cantidad: numero(valor(form, 'cantidad')), token: form.dataset.token };
      if (valor(form, 'referencia')) { cuerpo.referencia = valor(form, 'referencia'); }
      if (valor(form, 'nota')) { cuerpo.nota = valor(form, 'nota'); }
      if (cuerpo.cantidad === null) { delete cuerpo.cantidad; }
      var r = await enviar('POST', form.dataset.url, cuerpo);
      if (!r.ok) { return r; }
      var estado = r.datos && r.datos.estado;
      return { ok: true, recargar: true, aviso: estado === 'pendiente' ? 'pendiente' : 'registrado' };
    },
    'reintentar-ingreso': async function (form) {
      var r = await enviar('POST', form.dataset.url);
      return r.ok ? { ok: true, recargar: true, aviso: r.datos && r.datos.estado === 'pendiente' ? 'pendiente' : 'registrado' } : r;
    },
    'conciliar-ingreso': async function (form) {
      var resultado = form.querySelector('input[name="resultado"]:checked');
      if (!resultado) { return { ok: false, problema: { code: 'ECO-VALIDACION-INVALIDA', errores: [{ campo: 'resultado', clave: 'obligatorio' }] }, extra: ['Elige si el ingreso se aplicó o no.'] }; }
      var cuerpo = { resultado: resultado.value, confirmaFallido: marcado(form, 'confirma') };
      var r = await enviar('POST', form.dataset.url, cuerpo);
      return r.ok ? { ok: true, recargar: true } : r;
    }
  };

  document.addEventListener('submit', async function (evento) {
    var form = evento.target;
    if (!(form instanceof HTMLFormElement) || !form.dataset.bhFormulario) { return; }
    var accion = ACCIONES[form.dataset.bhFormulario];
    if (!accion) { return; }
    evento.preventDefault();
    if (form.dataset.confirmar && !window.confirm(form.dataset.confirmar)) { return; }
    mostrarErrores(form, []);
    ocupado(form, true);
    var resultado;
    try { resultado = await accion(form); } catch (e) { resultado = { ok: false, problema: null }; }
    if (resultado.ok) {
      var destino = resultado.destino || (resultado.recargar ? window.location.pathname + (resultado.aviso ? '?aviso=' + resultado.aviso : '') : null);
      if (destino) { window.location.assign(destino); return; }
    }
    ocupado(form, false);
    mostrarErrores(form, resultado.extra || mensajeDe(resultado.problema));
  });

  // Las secciones opcionales del alta (impresa y digital) y la licencia de dominio público (sin vigencia).
  function alternar(form) {
    ['impresa', 'digital'].forEach(function (t) {
      var caja = form.querySelector('[data-bh-seccion="' + t + '"]');
      var marca = campo(form, t + '-on');
      if (caja && marca) { caja.hidden = !marca.checked; }
    });
    form.querySelectorAll('[data-bh-vigencia]').forEach(function (envoltorio) {
      var prefijo = envoltorio.dataset.bhVigencia;
      var tipo = campo(form, prefijo + 'tipo');
      envoltorio.hidden = !!tipo && tipo.value === 'dominioPublico';
    });
    form.querySelectorAll('[data-bh-licencia-campos]').forEach(function (envoltorio) {
      var marca = campo(form, 'cambiar-licencia');
      envoltorio.hidden = !!marca && !marca.checked;
    });
  }
  document.addEventListener('change', function (evento) {
    var form = evento.target && evento.target.form;
    if (form && form.dataset && form.dataset.bhFormulario) { alternar(form); }
  });
  document.querySelectorAll('form[data-bh-formulario]').forEach(alternar);
})();
