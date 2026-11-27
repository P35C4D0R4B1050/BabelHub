// BabelHub: ayudas de los campos de contraseña (spec web-acceso). Mejora progresiva, sin dependencias, sin eval y sin estilos en línea (CSP, WEB-18).
// Todo ocurre en el navegador: este archivo no hace solicitudes, no usa almacenamiento, cookies ni consola y no copia la contraseña fuera de su campo (ACC-05).
// La barra es solo una guía: nunca detiene el envío y la política (de 10 a 128 caracteres y lista de contraseñas comunes) la aplica el servidor (ACC-02).
(function (entorno) {
  'use strict';

  var NIVELES = ['Débil', 'Aceptable', 'Buena', 'Fuerte'];
  var PAUSA_DEL_ANUNCIO = 1000;
  var PALABRAS = ['password', 'contraseña', 'contrasena', 'qwerty', 'admin', 'bienvenido', 'welcome', 'iloveyou', 'usuario', 'letmein', 'babelhub'];
  var SUSTITUCIONES = { '0': 'o', '1': 'i', '3': 'e', '4': 'a', '@': 'a', '5': 's', '$': 's', '7': 't' };
  var FILAS = ['1234567890', 'qwertyuiop', 'asdfghjklñ', 'zxcvbnm'];

  // ---------- El nivel (spec web-acceso §5) ----------

  function largoDePalabra(letras, desde) {
    var mayor = 0;
    PALABRAS.forEach(function (palabra) {
      var buscada = Array.from(palabra);
      var coincide = buscada.every(function (letra, i) {
        var escrita = letras[desde + i];
        return escrita !== undefined && (escrita === letra || SUSTITUCIONES[escrita] === letra);
      });
      if (coincide) {
        mayor = Math.max(mayor, buscada.length);
      }
    });
    return mayor;
  }

  function largoDeRepeticion(letras, desde) {
    var fin = desde + 1;
    while (fin < letras.length && letras[fin] === letras[desde]) {
      fin++;
    }

    return fin - desde >= 3 ? fin - desde : 0;
  }

  function clase(letra) {
    if (/^[a-z]$/.test(letra)) {
      return 'letra';
    }

    return /^[0-9]$/.test(letra) ? 'digito' : null;
  }

  function largoDeSecuencia(letras, desde) {
    var mayor = 0;
    [1, -1].forEach(function (paso) {
      var fin = desde + 1;
      while (fin < letras.length && clase(letras[fin]) !== null && clase(letras[fin]) === clase(letras[fin - 1])
        && letras[fin].charCodeAt(0) - letras[fin - 1].charCodeAt(0) === paso) {
        fin++;
      }

      mayor = Math.max(mayor, fin - desde);
    });
    return mayor >= 4 ? mayor : 0;
  }

  function largoDeTeclado(letras, desde) {
    var mayor = 0;
    FILAS.forEach(function (fila) {
      var teclas = Array.from(fila);
      var posicion = teclas.indexOf(letras[desde]);
      if (posicion < 0) {
        return;
      }

      [1, -1].forEach(function (paso) {
        var largo = 1;
        while (desde + largo < letras.length && teclas[posicion + paso * largo] !== undefined && letras[desde + largo] === teclas[posicion + paso * largo]) {
          largo++;
        }

        mayor = Math.max(mayor, largo);
      });
    });
    return mayor >= 4 ? mayor : 0;
  }

  function largoDeGrupoRepetido(letras, desde) {
    var mayor = 0;
    for (var tamano = 2; tamano <= 8 && desde + tamano <= letras.length; tamano++) {
      var veces = 1;
      var igual = true;
      while (igual && desde + (veces + 1) * tamano <= letras.length) {
        for (var i = 0; i < tamano; i++) {
          if (letras[desde + veces * tamano + i] !== letras[desde + i]) {
            igual = false;
            break;
          }
        }

        if (igual) {
          veces++;
        }
      }

      if (veces >= 3 || (tamano >= 4 && veces >= 2)) {
        mayor = Math.max(mayor, veces * tamano);
      }
    }

    return mayor;
  }

  // La longitud en la que cada patrón previsible cuenta como un solo carácter: de izquierda a derecha, el patrón más largo en cada posición.
  function longitudUtil(letras) {
    var util = 0;
    var i = 0;
    while (i < letras.length) {
      var largo = Math.max(largoDePalabra(letras, i), largoDeRepeticion(letras, i), largoDeSecuencia(letras, i), largoDeTeclado(letras, i), largoDeGrupoRepetido(letras, i));
      util++;
      i += largo > 0 ? largo : 1;
    }

    return util;
  }

  // Devuelve nulo con el campo vacío y, si no, 0 (Débil), 1 (Aceptable), 2 (Buena) o 3 (Fuerte).
  function evaluar(texto, minimo) {
    var piso = minimo > 0 ? minimo : 10;
    if (!texto) {
      return null;
    }

    var normalizado = texto.normalize('NFKC');
    // Un carácter por punto de código, como cuenta el servidor (no por unidad UTF-16).
    var escritas = Array.from(normalizado);
    if (escritas.length < piso) {
      return 0;
    }

    var util = longitudUtil(escritas.map(function (letra) { return letra.toLowerCase(); }));
    if (util < piso) {
      return 0;
    }

    var clases = [/\p{Ll}/u, /\p{Lu}/u, /\p{Nd}/u, /[^\p{Ll}\p{Lu}\p{Nd}]/u].filter(function (expresion) { return expresion.test(normalizado); }).length;
    return (util >= 16 ? 2 : 1) + (clases >= 3 ? 1 : 0);
  }

  // ---------- La pantalla ----------

  function crear(documento, etiqueta, clases, texto) {
    var nodo = documento.createElement(etiqueta);
    if (clases) {
      nodo.setAttribute('class', clases);
    }

    if (texto) {
      nodo.textContent = texto;
    }

    return nodo;
  }

  // Inserta los nodos, en orden, después de la etiqueta del campo: el botón, la barra y los avisos quedan fuera del <label> (no cambian el nombre del campo).
  function despuesDelCampo(campo, nodos) {
    var etiqueta = campo.closest('label') || campo;
    var siguiente = etiqueta.nextSibling;
    nodos.forEach(function (nodo) {
      etiqueta.parentNode.insertBefore(nodo, siguiente);
    });
  }

  // Anuncia a un lector de pantalla solo los cambios, y cuando la persona deja de escribir (ACC-09).
  function anunciador(documento, ventana) {
    // Región cortés sin rol «status»: las pantallas ya usan ese rol para sus propios mensajes.
    var region = crear(documento, 'p', 'sr-only');
    region.setAttribute('data-bh-anuncio', '');
    region.setAttribute('aria-live', 'polite');
    region.setAttribute('aria-atomic', 'true');
    var espera = null;
    return {
      nodo: region,
      decir: function (texto) {
        ventana.clearTimeout(espera);
        espera = ventana.setTimeout(function () {
          if (region.textContent !== texto) {
            region.textContent = texto;
          }
        }, PAUSA_DEL_ANUNCIO);
      }
    };
  }

  function ponerVisible(campo, visible) {
    campo.setAttribute('type', visible ? 'text' : 'password');
    var boton = campo.botonDeVer;
    if (boton) {
      var nombre = campo.getAttribute('data-bh-nombre') || 'la contraseña';
      boton.textoVisible.textContent = visible ? 'Ocultar' : 'Mostrar';
      boton.complemento.textContent = ' ' + nombre + (visible ? ', ahora visible' : '');
    }
  }

  function botonDeVer(documento, campo) {
    var boton = crear(documento, 'button', 'btn btn-ghost btn-sm self-end');
    boton.setAttribute('type', 'button');
    boton.setAttribute('data-bh-ver', '');
    boton.textoVisible = crear(documento, 'span', null);
    boton.complemento = crear(documento, 'span', 'sr-only');
    boton.appendChild(boton.textoVisible);
    boton.appendChild(boton.complemento);
    campo.botonDeVer = boton;
    boton.addEventListener('click', function () {
      ponerVisible(campo, campo.getAttribute('type') === 'password');
    });
    ponerVisible(campo, false);
    return boton;
  }

  function medidor(documento, ventana, campo) {
    var caja = crear(documento, 'div', 'bh-medidor');
    caja.setAttribute('data-bh-medidor', '');
    caja.hidden = true;
    var barra = crear(documento, 'div', 'bh-medidor-barra');
    barra.setAttribute('aria-hidden', 'true');
    for (var i = 0; i < NIVELES.length; i++) {
      barra.appendChild(crear(documento, 'span', null));
    }

    var texto = crear(documento, 'p', 'text-sm');
    var nombre = crear(documento, 'strong', null);
    texto.appendChild(crear(documento, 'span', null, 'Nivel de seguridad: '));
    texto.appendChild(nombre);
    caja.appendChild(barra);
    caja.appendChild(texto);
    caja.appendChild(crear(documento, 'p', 'text-sm', 'La barra es solo una guía: no garantiza que la contraseña se acepte.'));
    var anuncio = anunciador(documento, ventana);
    var actualizar = function () {
      var nivel = evaluar(campo.value, Number(campo.getAttribute('data-bh-nueva')));
      caja.hidden = nivel === null;
      if (nivel === null) {
        caja.removeAttribute('data-nivel');
        nombre.textContent = '';
        anuncio.decir('');
        return;
      }

      caja.setAttribute('data-nivel', String(nivel + 1));
      nombre.textContent = NIVELES[nivel];
      anuncio.decir('Nivel de seguridad: ' + NIVELES[nivel]);
    };

    campo.addEventListener('input', actualizar);
    campo.addEventListener('change', actualizar);
    return { nodos: [caja, anuncio.nodo], actualizar: actualizar };
  }

  function coincidencia(documento, ventana, campo, original) {
    var aviso = crear(documento, 'p', 'bh-coincidencia text-sm');
    aviso.setAttribute('data-bh-coincidencia', '');
    aviso.hidden = true;
    var icono = crear(documento, 'span', null);
    var texto = crear(documento, 'span', null);
    aviso.appendChild(icono);
    aviso.appendChild(texto);
    // Los iconos los dibuja el servidor en dos <template> junto al campo; aquí solo se copian.
    var envoltura = campo.closest('[data-bh-campo-de-contrasena]');
    var dibujo = function (cual) {
      var plantilla = envoltura && envoltura.querySelector('template[data-bh-icono="' + cual + '"]');
      return plantilla ? plantilla.innerHTML : '';
    };
    var anuncio = anunciador(documento, ventana);
    var actualizar = function () {
      var vacio = !campo.value;
      aviso.hidden = vacio;
      if (vacio) {
        aviso.removeAttribute('data-coincide');
        texto.textContent = '';
        anuncio.decir('');
        return;
      }

      // El texto exacto, como lo compara el servidor.
      var iguales = campo.value === original.value;
      var frase = iguales ? 'Las contraseñas coinciden' : 'Las contraseñas todavía no coinciden';
      if (aviso.getAttribute('data-coincide') !== (iguales ? 'si' : 'no')) {
        aviso.setAttribute('data-coincide', iguales ? 'si' : 'no');
        icono.innerHTML = dibujo(iguales ? 'exito' : 'error');
      }

      texto.textContent = frase;
      anuncio.decir(frase);
    };

    [campo, original].forEach(function (c) {
      c.addEventListener('input', actualizar);
      c.addEventListener('change', actualizar);
    });
    return { nodos: [aviso, anuncio.nodo], actualizar: actualizar };
  }

  // Los formularios que ya ocultan sus contraseñas al enviarse.
  function ocultarAlEnviar(formulario) {
    if (!formulario || formulario.bhOculta) {
      return;
    }

    formulario.bhOculta = true;
    formulario.addEventListener('submit', function () {
      Array.prototype.forEach.call(formulario.querySelectorAll('input[data-bh-nombre]'), function (campo) {
        ponerVisible(campo, false);
      });
    });
  }

  function mejorar(raiz, documento, ventana) {
    // Primero los campos nuevos y sus botones; el aviso de repetición necesita que el primer campo ya exista, y existe en el mismo formulario.
    Array.prototype.forEach.call(raiz.querySelectorAll('input[data-bh-nombre]'), function (campo) {
      if (campo.bhListo) {
        return;
      }

      campo.bhListo = true;
      var nodos = [botonDeVer(documento, campo)];
      var partes = [];
      if (campo.getAttribute('data-bh-nueva') !== null) {
        partes.push(medidor(documento, ventana, campo));
      }

      var repetido = campo.getAttribute('data-bh-repite');
      var original = repetido && campo.form ? campo.form.querySelector('input[name="' + repetido + '"]') : null;
      if (original) {
        partes.push(coincidencia(documento, ventana, campo, original));
      }

      partes.forEach(function (parte) {
        nodos = nodos.concat(parte.nodos);
      });
      despuesDelCampo(campo, nodos);
      partes.forEach(function (parte) {
        parte.actualizar();
      });
      ocultarAlEnviar(campo.form);
    });
  }

  function ocultarTodo(documento) {
    Array.prototype.forEach.call(documento.querySelectorAll('input[data-bh-nombre]'), function (campo) {
      ponerVisible(campo, false);
    });
  }

  if (typeof module === 'object' && module.exports) {
    module.exports = { NIVELES: NIVELES, evaluar: evaluar, mejorar: mejorar, ocultarTodo: ocultarTodo };
  }

  if (entorno.document) {
    var documento = entorno.document;
    mejorar(documento, documento, entorno);
    // Una pantalla que llega por htmx se mejora igual; al volver desde el historial todo queda oculto (ACC-07).
    documento.addEventListener('htmx:load', function (evento) {
      mejorar(evento.detail.elt.parentNode || documento, documento, entorno);
    });
    entorno.addEventListener('pageshow', function () {
      ocultarTodo(documento);
    });
  }
})(typeof window !== 'undefined' ? window : globalThis);
