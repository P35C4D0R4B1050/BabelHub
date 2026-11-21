// El índice infinito (web-indice-infinito D08): «Copiar enlace» es una mejora progresiva. Sin JavaScript la dirección se muestra en un campo de solo lectura y se copia a mano; con él, el botón arma la URL
// absoluta con la ruta canónica de la página mostrada (no location.href: en la página del mensaje amable sería la dirección no válida), la copia y avisa en una región role="status". Sin eval ni scripts en línea (CSP).
(function () {
  'use strict';

  var boton = document.getElementById('copiar-enlace');
  var aviso = document.getElementById('aviso-de-copia');
  var campo = document.getElementById('direccion-de-la-pagina');
  if (!boton || !aviso) {
    return;
  }

  boton.hidden = false;

  function avisar(texto) {
    aviso.textContent = texto;
  }

  boton.addEventListener('click', function () {
    var enlace = window.location.origin + boton.getAttribute('data-ruta');
    var porRespaldo = function () {
      avisar('Copia el enlace desde el campo de dirección.');
      if (campo) {
        campo.focus();
        campo.select();
      }
    };

    if (!navigator.clipboard || !navigator.clipboard.writeText) {
      porRespaldo();
      return;
    }

    navigator.clipboard.writeText(enlace).then(function () {
      avisar('Enlace copiado.');
    }, porRespaldo);
  });
})();
