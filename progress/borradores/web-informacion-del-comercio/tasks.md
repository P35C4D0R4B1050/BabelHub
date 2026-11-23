---
spec: web-informacion-del-comercio
---

# Tareas — Información del comercio, contacto y política de cambios y devoluciones

> Tareas atómicas ordenadas **de adentro hacia afuera**: Dominio → Aplicación → Infraestructura → Api → Web → pruebas de integración y E2E. Cada tarea cabe en un commit, empieza con una prueba que falla y deja el proyecto compilando. Cada criterio de aceptación de la spec aparece en al menos una tarea. **Nivel de rigor** tras la capa: `C` crítico (arranque con configuración, política de acceso, redirección), `E` estándar, `B` bajo. **Bloques propuestos:** `COM-1` = T01 a T05 (C: configuración y contenido legal), `COM-2` = T06 a T10 (E: pie, contacto y redirección), `COM-3` = T11 a T13 (E: pruebas y cierre).

Formato de cada línea: `- [ ] T01 [Capa · Nivel] Descripción (cubre: AC-XXX-01, XXX-01)`.

- [ ] T01 [Docs · B] Actualizar los documentos de la spec §5 (`docs/06/02`, `03` §4 y `08`, `docs/01/03` y `06`, `docs/05/04` y `07`, `docs/glossary.md`), las filas de `infra/secretos-requeridos.txt` (las tres variables en `ecommerce.env` y `ecommerce-gestion.env`, no secretas) y el paso de versión de `infra/README.md` (cubre: AC-COM-02, AC-COM-13, COM-02, COM-15)
- [ ] T02 [Api · C] `OpcionesDeContacto` y `DatosDeContacto` con las reglas de formato, los ficticios, el enlace `tel:` y la lectura en `OpcionesDelAnfitrionDelEcommerce.Leer` y `AgregarEcommerce` (falla nombrando la variable, nunca el valor; entorno de ejemplo solo `Development` y `Test`) (cubre: AC-COM-03, AC-COM-04, COM-02, COM-03, COM-04, COM-05)
- [ ] T03 [Aplicación · C] `TiposDeTextoLegal`: clave `cambios-y-devoluciones` con el alias `reembolso` y el recurso embebido renombrado (cubre: AC-COM-08, COM-10)
- [ ] T04 [Infra · C] Reescribir los tres cuerpos legales (datos del comercio por referencia, ley peruana, Indecopi, reglas de Devoluciones, ARCO desde la cuenta) y la versión de referencia con su huella (cubre: AC-COM-10, AC-COM-11, AC-COM-12, AC-COM-13, COM-09, COM-12, COM-13, COM-14, COM-15, COM-17)
- [ ] T05 [Api · C] Redirección permanente de `/legal/reembolso` en `LegalController` y títulos de la etiqueta de aceptación del pago, de `Aceptar.cshtml` y de los mensajes que citan la política (cubre: AC-COM-08, AC-COM-09, COM-10, COM-11)
- [ ] T06 [Api · E] `ModeloBaseDePagina.Contacto` y `ContextoDePagina` (servicio opcional) (cubre: AC-COM-01, COM-01)
- [ ] T07 [Web · E] Pie con teléfono, correo, dirección, enlace a `/contacto`, enlace «Cambios y devoluciones» y la frase del comercio; inicio con la frase (cubre: AC-COM-01, AC-COM-07, COM-01, COM-08, COM-09)
- [ ] T08 [Api · E] `ContactoController` y su vista con el estado de servicio no disponible y sin atributo de textos pendientes (cubre: AC-COM-05, AC-COM-06, COM-06, COM-07)
- [ ] T09 [Infra · B] Propagar las variables de contacto a `tests/e2e/entorno/preparar-entorno.sh`, al sembrado de versiones E2E y a los archivos de despliegue y restauración que las necesiten (cubre: AC-COM-03, COM-03)
- [ ] T10 [Pruebas · C] Integración: opciones y arranque, barrido de páginas con el pie, `/contacto`, política de textos pendientes (cliente, personal con segundo factor, Identidad caída), redirección y enumeración de rutas (cubre: AC-COM-01, AC-COM-03, AC-COM-04, AC-COM-05, AC-COM-06, AC-COM-09)
- [ ] T11 [Pruebas · C] Integración: contenido legal (plazos frente a `PoliticaDeDevolucion`, estados, leyes), versión de referencia y huella, y escaneo de contacto fuera del repositorio (cubre: AC-COM-02, AC-COM-10, AC-COM-11, AC-COM-12, AC-COM-13)
- [ ] T12 [Pruebas · E] Actualizar las pruebas existentes que fijan el título «Política de reembolso», la ruta `/legal/reembolso` y la etiqueta del pago (AC-WEB-05, AC-WEB-24, AC-CAR-11) sin debilitarlas (cubre: AC-COM-08)
- [ ] T13 [Pruebas · E] E2E de Playwright del pie y de `/contacto` en escritorio y teléfono, ambos temas, con axe y teclado, y verificación visual contra el prototipo (`ui-review`) (cubre: AC-COM-01, AC-COM-05, AC-COM-14, COM-16)
