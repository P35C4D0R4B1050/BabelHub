---
spec: web-informacion-del-comercio
---

# Decisiones locales — Información del comercio, contacto y política de cambios y devoluciones

Decisiones de esta feature que no merecen un AD global. Las de negocio (DD-75 a DD-79) están en `progress/decisiones-delegadas.md`.

| ID | Decisión | Motivo | Alternativa descartada | Enlace |
| :--- | :--- | :--- | :--- | :--- |
| D01 | **«Cambios y devoluciones» es `TipoDeTextoLegal.Reembolso` con otra clave de ruta y otro título**: el tipo, el identificador de Identidad, la columna `reembolso` y la aceptación del pago no cambian; la clave de ruta pasa a `cambios-y-devoluciones` y `reembolso` queda como alias que redirige. | Un tipo nuevo tocaría `identidad.versiones_de_textos`, IDE-57, CAR-22 y las aceptaciones guardadas. | Renombrar el tipo y la columna. | DD-75, COM-10, COM-11 |
| D02 | **Las opciones de contacto se leen en `OpcionesDelAnfitrionDelEcommerce.Leer`, no con `IOptions` ni en el controlador**, y llegan a la vista como un registro inmutable ya validado (`DatosDeContacto`) con el enlace `tel:` armado al validar. El pie las recibe por `ModeloBaseDePagina.Contacto` (servicio opcional en `ContextoDePagina`, como `IMenuDeNavegacion`). | Es el patrón de `Carrito:CostoDeEnvio`: una sola falla de arranque que junta todos los problemas y nombra la variable. | `IOptions` con validación diferida; leer la configuración en la vista. | COM-02 a COM-05 |
| D03 | **La versión de los textos se vigila con una versión de referencia y la huella SHA-256 del cuerpo, guardadas en una prueba de integración**; el despliegue fija esa misma versión en `identidad.versiones_de_textos` (paso documentado en `infra/README.md`). La prueba es de repositorio, no de ejecución. | La versión vive en la base de Identidad: el sistema no puede impedir en ejecución un cuerpo nuevo con la versión vieja. | Calcular la versión desde la huella en tiempo de ejecución (rompe IDE-57); un hook de commit. | COM-15 |
| D04 | **`/contacto` no lleva atributo de la política de textos pendientes** (se niega por defecto, WEB-46) y **no usa `[PermitidaConPasoPendiente]`**. | DD-79: declararla permitida obliga a modificar WEB-34, WEB-41 y WEB-46. | Declararla como `TextosLegales`. | COM-07 |
| D05 | **Entorno de ejemplo = `IsDevelopment()` o `EnvironmentName == "Test"`**; cualquier otro nombre exige las tres variables. `OpcionesDeContacto.Leer(configuracion, permitirEjemplo)` recibe el booleano; `Leer` del anfitrión lo toma por parámetro opcional (por omisión `!esProduccion`, para no romper los llamadores existentes). | Spec §5 «Entornos». | Basarlo solo en `esProduccion` (dejaría Staging sin exigencia). | COM-03, COM-05 |
