# Decisiones de implementación — web-informacion-del-comercio

Decisiones menores tomadas al implementar que el plan no fija. No cambian el comportamiento de la spec ni las decisiones D01 a D06 de `decisions.md`.

| Tarea | Decisión | Motivo |
| :--- | :--- | :--- |
| T07 | La propiedad del modelo base se llama **`ContactoDelComercio`** (el plan dice `Contacto`): `DetalleDePedidoVista` ya tiene una propiedad `Contacto` (el contacto de entrega) y la ocultaría. | Evitar el choque de nombres sin tocar la vista de pedidos. |
| T05 | `DatosDeContacto.Leer(LectorDeSecretos, bool)` vive en la fábrica del propio tipo (no hay una clase `OpcionesDeContacto` aparte): el dato validado y el enlace se arman en el mismo lugar y el constructor es privado. El recorte previo solo quita espacios U+0020 de los extremos y el valor se muestra ya recortado. | Menos piezas; plan §1 («`OpcionesDeContacto` y `DatosDeContacto`») se cumple con un solo tipo. |
| T05 | La parte local del correo no admite `%` y el dominio no admite `_`; las etiquetas del dominio se separan por punto y el sufijo tiene al menos dos letras `A-Za-z`. | Spec §5: letras ASCII, dígitos, punto, guion, guion bajo y `+` en la parte local; letras, dígitos, punto y guion en el dominio. |
| T04 | La huella de la versión de referencia se calcula sobre los bytes UTF-8 del recurso embebido sin normalizar (`.gitattributes` fija `eol=lf`). | Plan §1 y D03. |
| T11 | Las pruebas de las opciones y del arranque están en `tests/integration/.../Api/OpcionesDeContactoTests.cs` y en `AnfitrionDelEcommerceTests.cs`; no hay una prueba unitaria aparte porque no necesitan red ni base y el tipo está en la Api (`InternalsVisibleTo` de integración). | Una sola ubicación. |
| T12 | El pie de las páginas de ingreso y de error de un módulo se verifica en el E2E (el host de pruebas en memoria no tiene Identidad y su página de ingreso no es representativa); el barrido de integración usa `/`, los textos legales, `/contacto`, `/sin-permiso`, `/no-disponible`, `/error` y una página de prueba por área. | El host en memoria no compone Identidad. |
