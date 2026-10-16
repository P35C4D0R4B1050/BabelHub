---
id: ecommerce-libro-reclamaciones
titulo: Libro de Reclamaciones virtual
estado: Draft
servicio: E-commerce
prefijo: REC
origen: [RF25, NN15]
calidad: [RNF07, RNF08, RNF17, AC13]
depende: [transversal-auditoria, transversal-seguridad]
---

# Spec — Libro de Reclamaciones virtual

## 1. Propósito

Que cualquier consumidor pueda registrar un reclamo o una queja desde la tienda, reciba una constancia y obtenga respuesta, y que la librería conserve las hojas (RF25, confirmado por el equipo). Se aplica con rigurosidad laxa por ser un proyecto académico (`docs/01-analisis-de-sistema/03-marco-legal.md`).

## 2. Alcance

| Dentro | Fuera |
| :--- | :--- |
| Enlace permanente, registro de la hoja, constancia imprimible y respuesta. | La forma exacta del formulario oficial (se aproxima y se revisaría con un abogado). |
| Atención por la administración central y aviso de la respuesta por correo. | Atención de reclamos con indemnizaciones o procesos ante la autoridad. |
| Conservación de las hojas y descarte de las que son spam, sin borrarlas. | Gestión de plazos legales ante Indecopi. |
| Límite de hojas por origen (el formato de rechazo es de `transversal-seguridad`). | El Libro de Reclamaciones físico de cada sucursal. |

## 3. Requisitos (EARS)

| ID | Patrón | Requisito | Origen |
| :--- | :--- | :--- | :--- |
| REC-01 | Ubicuo | El sistema deberá mostrar en todas las páginas de la tienda un enlace visible y permanente al Libro de Reclamaciones. | RF25 |
| REC-02 | Ubicuo | El sistema deberá permitir registrar una hoja sin que el consumidor haya iniciado sesión. | RF25 |
| REC-03 | Evento | Cuando el consumidor envía una hoja, el sistema deberá registrar su nombre, documento de identidad, domicilio y correo de contacto, el bien contratado, el monto reclamado, el tipo reclamo o queja, el detalle y, si lo indica, su pedido. | RF25 |
| REC-04 | No deseado | Si faltan datos obligatorios de la hoja, entonces el sistema deberá rechazarla indicando qué campos faltan. | RF25 |
| REC-05 | Evento | Cuando se registra una hoja, el sistema deberá asignarle un número único y una fecha, y mostrar una constancia, con el plazo de respuesta informado, que el consumidor pueda imprimir sin costo. | RF25 |
| REC-06 | Ubicuo | El sistema deberá conservar cada hoja al menos dos años y no permitir su eliminación. | RF25, RNF08 |
| REC-07 | Evento | Cuando la administración responde una hoja, el sistema deberá cambiar su estado a respondido, guardar la respuesta y enviarla por correo electrónico al correo de contacto de la hoja. | RF25 |
| REC-08 | Evento | Cuando la administración responde una hoja, el sistema deberá registrarlo en la auditoría. | RNF08 |
| REC-09 | Ubicuo | El sistema deberá permitir a la administración central listar y filtrar las hojas por estado. | RF25 |
| REC-10 | Ubicuo | El sistema deberá proteger los datos personales de las hojas y mostrarlos solo a la administración central y al propio consumidor. | RNF07 |
| REC-11 | Estado | Mientras un mismo origen supera 3 hojas registradas en una ventana deslizante de una hora, el sistema deberá rechazar las siguientes y responder y registrar el exceso según `transversal-seguridad` (SEG-07 y SEG-08). | RNF17, AC13 |
| REC-12 | Evento | Cuando la administración central descarta una hoja por ser spam, el sistema deberá marcarla como descartada, ocultarla del listado por defecto, conservarla sin eliminarla y registrarlo en la auditoría. | RF25, RNF08 |
| REC-13 | No deseado | Si falla el envío del correo de respuesta, entonces el sistema deberá mantener la hoja como respondida con la respuesta guardada, marcar el aviso como pendiente y permitir a la administración reintentarlo. | RF25 |

## 4. Criterios de aceptación

| ID | Cubre | Escenario |
| :--- | :--- | :--- |
| AC-REC-01 | REC-01 | **Dado** cualquier página de la tienda, **cuando** se muestra, **entonces** el pie incluye el enlace al Libro de Reclamaciones. |
| AC-REC-02 | REC-02, REC-03 | **Dado** un visitante sin sesión, **cuando** completa la hoja con su nombre, documento, domicilio, correo, el bien, el monto reclamado y el detalle, **entonces** la hoja queda registrada. |
| AC-REC-03 | REC-04 | **Dado** una hoja sin detalle, **cuando** se envía, **entonces** se rechaza indicando que falta el detalle. |
| AC-REC-04 | REC-05 | **Dado** una hoja válida, **cuando** se registra, **entonces** recibe un número único y una fecha, y se muestra una constancia imprimible que indica el plazo de respuesta de 15 días calendario. |
| AC-REC-05 | REC-06 | **Dado** una hoja registrada, **cuando** alguien intenta eliminarla, **entonces** se rechaza y la hoja sigue disponible. |
| AC-REC-06 | REC-07, REC-08 | **Dado** una hoja pendiente, **cuando** la administración la responde, **entonces** pasa a respondido, el consumidor recibe la respuesta en su correo y queda la auditoría. |
| AC-REC-07 | REC-09 | **Dado** hojas pendientes y respondidas, **cuando** la administración filtra por pendientes, **entonces** ve solo esas. |
| AC-REC-08 | REC-10 | **Dado** una hoja con datos personales, **cuando** otro cliente intenta consultarla, **entonces** se rechaza. |
| AC-REC-09 | REC-11 | **Dado** un origen que registró 3 hojas en la última hora, **cuando** envía una cuarta, **entonces** se rechaza con la respuesta de límite excedido y su espera sugerida, queda un evento de seguridad y las 3 hojas anteriores siguen registradas. |
| AC-REC-10 | REC-12 | **Dado** una hoja de spam, **cuando** la administración la descarta, **entonces** su estado es descartada, no aparece en el listado por defecto pero sí al filtrar por descartadas, no se puede eliminar y queda una entrada de auditoría. |
| AC-REC-11 | REC-13 | **Dado** que el envío de correo falla, **cuando** la administración responde una hoja, **entonces** la hoja queda respondida con su respuesta guardada, el aviso figura pendiente y al reintentar con el envío disponible el consumidor recibe el correo una sola vez. |

## 5. Reglas e invariantes

- Una hoja es inmutable una vez registrada; la respuesta se agrega sin alterar el contenido original.
- El número de hoja es único y secuencial por año.
- Se conserva al menos dos años.
- El plazo de respuesta que se informa es de 15 días calendario, dentro del máximo legal (a verificar en una operación real, ver `docs/01-analisis-de-sistema/03-marco-legal.md`).
- Los datos de identificación de la hoja (documento y domicilio) se piden por obligación legal; son una excepción documentada a la minimización de `docs/05-seguridad/04-privacidad-y-datos-personales.md`.
- Una hoja descartada se conserva como las demás y sus datos personales se anonimizan al cumplirse el plazo de conservación.

## 6. Casos límite y errores

- Una hoja con un número de pedido que no existe se acepta; el pedido es un dato opcional.
- Dos envíos idénticos seguidos generan hojas separadas, pues no se puede distinguir una repetición de un nuevo reclamo.
- El correo de contacto es obligatorio: sin él la hoja se rechaza (REC-04).
- Un correo mal escrito impide recibir la respuesta; la hoja y la respuesta quedan registradas y la administración puede reenviarla (REC-13).

## 7. Preguntas abiertas

- [x] ¿Cuál es el plazo de respuesta que se informa al consumidor? Resuelto el 2026-10-16: 15 días calendario (REC-05).
- [x] ¿Cuál es el medio de contacto obligatorio? Resuelto el 2026-10-16: correo electrónico; la respuesta sale por correo (REC-07, RC18 ampliada).
- [x] ¿Cómo se evita el spam si las hojas no se pueden borrar? Resuelto el 2026-10-16: límite de 3 hojas por hora por origen y descarte sin borrado (REC-11, REC-12).
- Nota de verificación (B09): los campos de la hoja (documento, domicilio y monto reclamado) se contrastan con el formato oficial vigente antes de operar con clientes reales; no cambia el comportamiento de la spec.
