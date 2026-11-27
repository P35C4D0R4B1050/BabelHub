---
spec: web-acceso
---

# Tareas — Ayudas de contraseña, acceso del personal y código QR del segundo factor

> Tareas atómicas ordenadas **de adentro hacia afuera**: Aplicación → Infraestructura (incluida la operación) → Api → Web → pruebas de integración y E2E. Cada tarea empieza con una prueba que falla y deja el proyecto compilando. **Nivel de rigor:** `C` crítico (secreto del segundo factor, contraseñas, arranque con configuración), `E` estándar, `B` bajo.

Formato de cada línea: `- [ ] T01 [Capa · Nivel] Descripción (cubre: AC-XXX-01, XXX-01)`.

- [x] T01 [Aplicación · C] `IGeneradorDeCodigoQr` y la clase `CodigoQr` en el módulo Web (lado y módulos, sin el texto codificado, `ToString` neutro) (cubre: AC-ACC-12, ACC-13)
- [x] T02 [Infra · C] Comprobar editor, licencia, dependencias y avisos de `Net.Codecrete.QrCodeGenerator`, fijarlo con los cuatro archivos de bloqueo, `CodecreteGeneradorDeCodigoQr` (nulo ante cualquier falla, aviso sin datos) y su registro en la composición del módulo Web, con las pruebas del adaptador leídas con `ZXing.Net` (solo pruebas) (cubre: AC-ACC-12, AC-ACC-13, AC-ACC-15, ACC-13, ACC-14, ACC-16)
- [ ] T03 [Infra · E] `generar-secretos.sh` escribe la dirección en `ventas/ecommerce.env`, comentario en `secretos-requeridos.txt`, prueba del generador y paso de la guía para las carpetas ya generadas (cubre: AC-ACC-09, AC-ACC-10, ACC-10, ACC-11)
- [ ] T04 [Api · C] `DireccionDelAccesoDelPersonal` (fábrica validada), su lectura en `OpcionesDelAnfitrionDelEcommerce.Leer` con el indicador de desarrollo y prueba, su registro, `ModeloBaseDePagina.AccesoDelPersonal` y su llenado en `ContextoDePagina` solo en el host de la tienda, con las pruebas de arranque (cubre: AC-ACC-09, AC-ACC-10, AC-ACC-11, ACC-10, ACC-11, ACC-12)
- [x] T05 [Api · C] El código QR en el modelo de la pantalla de configuración del ingreso y de la cuenta, con la misma dirección que el enlace y `no-store` (cubre: AC-ACC-12, AC-ACC-13, AC-ACC-15, ACC-13, ACC-14, ACC-16)
- [x] T06 [Web · E] Parcial `_CodigoQr` en las dos pantallas con su texto alternativo, su margen y tamaño, las variables de color en los dos temas y el texto que invita a escanear (cubre: AC-ACC-12, AC-ACC-14, ACC-15)
- [ ] T07 [Web · E] «Acceso del personal» en el pie y en la pantalla de ingreso de la tienda (cubre: AC-ACC-09, AC-ACC-10, ACC-10, ACC-11)
- [x] T08 [Web · C] Atributos de ACC-17 en **todos** los campos de contraseña, parcial de ayuda y atributos `data-bh-…` en los de contraseña nueva de registro, recuperación, cuenta y temporal, sin longitud mínima del navegador (cubre: AC-ACC-01, AC-ACC-16, ACC-01, ACC-02, ACC-08, ACC-17)
- [x] T09 [Web · C] `contrasenas.js`: función del nivel según §5 de la spec, probada con Node contra la tabla de ejemplos y con el mínimo que entrega el servidor (cubre: AC-ACC-04, ACC-04, ACC-05)
- [x] T10 [Web · C] `contrasenas.js`: barra, aviso de coincidencia y botón de ver u ocultar, con el doble del documento en Node y los estilos con tokens en los dos temas y movimiento reducido (cubre: AC-ACC-02, AC-ACC-03, AC-ACC-05, AC-ACC-06, AC-ACC-07, AC-ACC-08, ACC-03, ACC-06, ACC-07, ACC-09)
- [ ] T11 [Pruebas · C] Integración de las vistas, del arranque y del secreto (cabeceras, registros, borradores, recursos externos, política de contenido y lectura del QR) (cubre: AC-ACC-01, AC-ACC-09, AC-ACC-11, AC-ACC-12, AC-ACC-13, AC-ACC-14)
- [ ] T12 [Pruebas · E] E2E de Playwright (escritorio y teléfono, ambos temas, teclado, axe, movimiento reducido, 320 px y red vigilada) y verificación visual (`ui-review`) (cubre: AC-ACC-03, AC-ACC-05, AC-ACC-07, AC-ACC-08, AC-ACC-14, AC-ACC-16)
- [ ] T13 [Docs · B] `docs/06-experiencia-de-usuario` (pantallas y mensajes), el puerto nuevo en `docs/03-diseno-de-software/01-clean-architecture.md`, la amenaza del enlace del personal y la nota del secreto en el QR en `docs/05-seguridad/01-modelo-de-amenazas.md`, la variable en `docs/05-seguridad/07-operacion-segura.md` §3.2 y la bitácora (cubre: AC-ACC-09, ACC-10)
