// BabelHub: JavaScript propio mínimo (docs/06 §3). Sin dependencias, sin eval y sin scripts en línea (CSP, WEB-18).
// Solo mejora lo que ya funciona sin JavaScript: los formularios de tema y de sucursal se envían igual con el botón de <noscript>.
(function () {
  'use strict';

  var UN_ANIO = 60 * 60 * 24 * 365;
  var TEMAS = { 'babelhub': true, 'babelhub-dark': true };

  function guardarCookie(nombre, valor) {
    document.cookie = nombre + '=' + encodeURIComponent(valor) + '; path=/; max-age=' + UN_ANIO + '; samesite=lax; secure';
  }

  function borrarCookie(nombre) {
    document.cookie = nombre + '=; path=/; max-age=0; samesite=lax; secure';
  }

  // Antiforgery (WEB-05): htmx envía el token del <meta> en la cabecera de cada solicitud que cambia algo; los formularios normales llevan su campo oculto.
  document.addEventListener('htmx:configRequest', function (evento) {
    var meta = document.querySelector('meta[name="csrf-token"]');
    if (meta && meta.content) {
      evento.detail.headers['RequestVerificationToken'] = meta.content;
    }
  });

  // Los avisos de la búsqueda del catálogo (filtro inválido, búsqueda muy amplia, límite, servicio no disponible) vienen como 400, 422, 429 y 503 con el parcial de resultados:
  // htmx no cambia el contenido con esos códigos por defecto, así que se pide solo para esa región.
  document.addEventListener('htmx:beforeSwap', function (evento) {
    var estado = evento.detail.xhr ? evento.detail.xhr.status : 0;
    var destino = evento.detail.target;
    if (destino && destino.id === 'resultados' && (estado === 400 || estado === 422 || estado === 429 || estado === 503)) {
      evento.detail.shouldSwap = true;
      evento.detail.isError = false;
    }
  });

  // Imprimir (la constancia de una hoja de reclamación): un manejador por atributo, porque la política de contenido no admite scripts en línea.
  document.addEventListener('click', function (evento) {
    var boton = evento.target instanceof Element ? evento.target.closest('[data-bh-imprimir]') : null;
    if (boton) {
      window.print();
    }
  });

  // El foco pasa al mensaje de error de una pantalla repintada por el servidor (docs/06 §8): el aviso marcado con data-bh-foco-inicial.
  var avisoInicial = document.querySelector('[data-bh-foco-inicial]');
  if (avisoInicial) {
    avisoInicial.focus();
  }

  // Cuenta regresiva del pago (PAG-01): el servidor da el instante de vencimiento; sin script la página muestra la hora fija. Al llegar a cero se avisa y se deshabilita el envío (el servidor igual lo rechazaría).
  Array.prototype.forEach.call(document.querySelectorAll('[data-bh-cuenta-regresiva]'), function (nodo) {
    var vence = Date.parse(nodo.getAttribute('data-bh-cuenta-regresiva'));
    var restante = nodo.querySelector('[data-bh-restante]');
    if (isNaN(vence)) {
      return;
    }

    var reloj = null;
    var pintar = function () {
      var segundos = Math.max(0, Math.round((vence - Date.now()) / 1000));
      if (segundos === 0) {
        window.clearInterval(reloj);
        if (restante) {
          restante.textContent = '';
        }

        var aviso = document.querySelector('[data-bh-vencido]');
        if (aviso) {
          aviso.classList.remove('hidden');
        }

        Array.prototype.forEach.call(document.querySelectorAll('[data-bh-deshabilitar-al-vencer]'), function (boton) {
          boton.disabled = true;
        });
        return;
      }

      if (restante) {
        var minutos = Math.floor(segundos / 60);
        var resto = segundos % 60;
        restante.textContent = ' (te quedan ' + (minutos < 10 ? '0' : '') + minutos + ':' + (resto < 10 ? '0' : '') + resto + ')';
      }
    };

    pintar();
    reloj = window.setInterval(pintar, 1000);
  });

  // Tema (WEB-09): se aplica al instante y se recuerda; sin elección manda la preferencia del dispositivo.
  var selectorDeTema = document.querySelector('[data-bh-tema]');
  if (selectorDeTema) {
    selectorDeTema.addEventListener('change', function () {
      var valor = selectorDeTema.value;
      if (TEMAS[valor] === true) {
        document.documentElement.setAttribute('data-theme', valor);
        guardarCookie('bh-tema', valor);
      } else {
        document.documentElement.removeAttribute('data-theme');
        borrarCookie('bh-tema');
      }
    });
  }

  // Sucursal preferida (WEB-16): al elegir se envía el formulario, que la guarda en el servidor.
  var selectorDeSucursal = document.querySelector('[data-bh-sucursal]');
  if (selectorDeSucursal && selectorDeSucursal.form) {
    selectorDeSucursal.addEventListener('change', function () {
      selectorDeSucursal.form.requestSubmit();
    });
  }
})();
