# Principios de UX y heurísticas

Define cómo se diseña la experiencia, no solo cómo se ve. Sigue el proceso de diseño centrado en las personas de **ISO 9241-210** y usa las **heurísticas de usabilidad de Nielsen** como lista de revisión. Complementa la [identidad y el sistema de diseño](01-identidad-y-sistema-de-diseno.md) y respalda NN16, OB08, AC08, AC10 y RNF27.

## 1. Proceso centrado en las personas (ISO 9241-210)

| Actividad | Cómo se aplica en BabelHub | Producto |
| :--- | :--- | :--- |
| **Comprender y especificar el contexto de uso** | Quién usa el sistema, dónde (celular con conexión variable, mostrador de sucursal), con qué dispositivo. | [Personas y tareas](05-personas-y-tareas.md) |
| **Especificar los requisitos de usuario** | Metas medibles de éxito de tarea, errores y satisfacción. | RNF25, RNF26, RNF27; [pruebas de usabilidad](09-pruebas-de-usabilidad.md) |
| **Producir soluciones de diseño** | Prototipo navegable y luego pantallas reales por spec. | `prototype/ui-v2/`; skill `ui-screen` |
| **Evaluar frente a los requisitos** | Revisión heurística, lista de accesibilidad y prueba con personas. | [Accesibilidad](06-accesibilidad-wcag.md), [pruebas de usabilidad](09-pruebas-de-usabilidad.md); skill `ui-review` |

El ciclo se repite por iteración: no se espera al final del proyecto para evaluar (AD29).

## 2. Principios de interacción de BabelHub

1. **Mostrar la verdad.** Disponibilidad, plazos de traslado y costos se ven antes de pagar; nunca se simula stock (OB01).
2. **Un solo recorrido principal.** Buscar → ver disponibilidad → carrito → entrega → pago → seguimiento, con a lo más cinco pasos (RNF13).
3. **Reducir la carga de memoria.** Datos ya conocidos (nombre, sucursal preferida) se recuerdan; no se pide dos veces lo mismo (WCAG 3.3.7).
4. **Prevenir antes de corregir.** Formularios que validan al salir del campo, con ejemplos de formato; botones deshabilitados con explicación y no solo en gris.
5. **Errores humanos y recuperables.** Todo error dice qué pasó, por qué y qué hacer; la persona nunca pierde lo escrito.
6. **Seguridad sin fricción innecesaria.** La seguridad se explica con mensajes claros (por ejemplo, «espera 12 minutos para intentarlo de nuevo»), sin revelar si una cuenta existe.
7. **Consistencia.** Mismos patrones y palabras en las tres áreas (Tienda, Sucursal, Administración).
8. **Respeto por la atención.** Sin ventanas emergentes de marketing, sin temporizadores falsos; los únicos contadores reales son la reserva de 15 minutos y las esperas por límites.

## 3. Heurísticas de Nielsen aplicadas

| Heurística | Aplicación en BabelHub | Cómo se revisa |
| :--- | :--- | :--- |
| **Visibilidad del estado del sistema** | Estados cargando, éxito, error; estado del pedido y del traslado; tiempo restante de reserva. | Revisión de los seis estados estándar. |
| **Coincidencia con el mundo real** | Lenguaje del [glosario](../glossary.md) en español peruano; «recojo en sucursal», «biblioteca». | Revisión de textos. |
| **Control y libertad del usuario** | Cancelar el checkout libera la reserva; quitar líneas; volver sin perder el carrito. | Recorridos de cancelación. |
| **Consistencia y estándares** | Mismos componentes del sistema de diseño; íconos con texto. | Revisión visual contra el prototipo. |
| **Prevención de errores** | Máximos de cantidad por stock; confirmación antes de acciones irreversibles (cancelar cuenta, anular pedido). | Pruebas de formularios. |
| **Reconocer antes que recordar** | Sugerencias del buscador, filtros visibles, recientes. | Revisión del buscador. |
| **Flexibilidad y eficiencia** | Atajos de teclado en las áreas de trabajo (búsqueda de inventario), filtros guardados. | Recorridos de Sucursal. |
| **Diseño estético y minimalista** | Portadas protagonistas; una acción principal por pantalla. | Revisión visual. |
| **Ayudar a reconocer y recuperarse de errores** | Mensajes con causa y acción (véase [contenido y mensajes](08-contenido-y-mensajes.md)). | Lista de mensajes. |
| **Ayuda y documentación** | Ayuda breve contextual y enlace constante al Libro de Reclamaciones y a ayuda en la cuenta. | Revisión del pie y de la cuenta. |

## 4. Revisión heurística

Antes de dar por terminada la pantalla de una spec, se revisa con esta lista y se registran los hallazgos con gravedad (0 cosmético a 4 catastrófico). Las gravedades 3 y 4 bloquean el paso de la spec a **Done**. La skill `ui-review` guía esta revisión.
