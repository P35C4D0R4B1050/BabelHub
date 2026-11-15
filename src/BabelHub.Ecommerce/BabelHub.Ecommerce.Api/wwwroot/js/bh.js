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
