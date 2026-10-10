---
id: ecommerce-libro-reclamaciones
titulo: Libro de Reclamaciones virtual
estado: Draft
servicio: E-commerce
prefijo: REC
origen: [RF25, NN15]
calidad: [RNF07, RNF08]
depende: [transversal-auditoria, transversal-notificaciones]
---

# Spec — Libro de Reclamaciones virtual

## 1. Propósito

Que cualquier consumidor pueda registrar un reclamo o una queja desde la tienda, reciba una constancia y obtenga respuesta, y que la librería conserve las hojas (RF25, confirmado por el equipo). Se aplica con rigurosidad laxa por ser un proyecto académico (`docs/02`, sección 5).

## 2. Alcance

| Dentro | Fuera |
| :--- | :--- |
| Enlace permanente, registro de la hoja, constancia imprimible y respuesta. | La forma exacta del formulario oficial (se aproxima y se revisaría con un abogado). |
| Atención por la administración central. | Atención de reclamos con indemnizaciones o procesos ante la autoridad. |
| Conservación de las hojas. | Gestión de plazos legales ante Indecopi. |

## 3. Requisitos (EARS)

| ID | Patrón | Requisito | Origen |
| :--- | :--- | :--- | :--- |
| REC-01 | Ubicuo | El sistema deberá mostrar en todas las páginas de la tienda un enlace visible y permanente al Libro de Reclamaciones. | RF25 |
| REC-02 | Ubicuo | El sistema deberá permitir registrar una hoja sin que el consumidor haya iniciado sesión. | RF25 |
| REC-03 | Evento | Cuando el consumidor envía una hoja, el sistema deberá registrar su identificación, el bien contratado, el tipo reclamo o queja, el detalle y su pedido. | RF25 |
| REC-04 | No deseado | Si faltan datos obligatorios de la hoja, entonces el sistema deberá rechazarla indicando qué campos faltan. | RF25 |
| REC-05 | Evento | Cuando se registra una hoja, el sistema deberá asignarle un número único y una fecha, y mostrar una constancia que el consumidor pueda imprimir sin costo. | RF25 |
| REC-06 | Ubicuo | El sistema deberá conservar cada hoja al menos dos años y no permitir su eliminación. | RF25, RNF08 |
| REC-07 | Evento | Cuando la administración responde una hoja, el sistema deberá cambiar su estado a respondido, guardar la respuesta y avisar al consumidor por el medio de contacto indicado. | RF25 |
| REC-08 | Evento | Cuando la administración responde una hoja, el sistema deberá registrarlo en la auditoría. | RNF08 |
| REC-09 | Ubicuo | El sistema deberá permitir a la administración central listar y filtrar las hojas por estado. | RF25 |
| REC-10 | Ubicuo | El sistema deberá proteger los datos personales de las hojas y mostrarlos solo a la administración central y al propio consumidor. | RNF07 |

## 4. Criterios de aceptación

| ID | Cubre | Escenario |
| :--- | :--- | :--- |
| AC-REC-01 | REC-01 | **Dado** cualquier página de la tienda, **cuando** se muestra, **entonces** el pie incluye el enlace al Libro de Reclamaciones. |
| AC-REC-02 | REC-02, REC-03 | **Dado** un visitante sin sesión, **cuando** completa la hoja con su identificación, el bien y el detalle, **entonces** la hoja queda registrada. |
| AC-REC-03 | REC-04 | **Dado** una hoja sin detalle, **cuando** se envía, **entonces** se rechaza indicando que falta el detalle. |
| AC-REC-04 | REC-05 | **Dado** una hoja válida, **cuando** se registra, **entonces** recibe un número único y una fecha, y se muestra una constancia imprimible. |
| AC-REC-05 | REC-06 | **Dado** una hoja registrada, **cuando** alguien intenta eliminarla, **entonces** se rechaza y la hoja sigue disponible. |
| AC-REC-06 | REC-07, REC-08 | **Dado** una hoja pendiente, **cuando** la administración la responde, **entonces** pasa a respondido, el consumidor recibe un aviso y queda la auditoría. |
| AC-REC-07 | REC-09 | **Dado** hojas pendientes y respondidas, **cuando** la administración filtra por pendientes, **entonces** ve solo esas. |
| AC-REC-08 | REC-10 | **Dado** una hoja con datos personales, **cuando** otro cliente intenta consultarla, **entonces** se rechaza. |

## 5. Reglas e invariantes

- Una hoja es inmutable una vez registrada; la respuesta se agrega sin alterar el contenido original.
- El número de hoja es único y secuencial por año.
- Se conserva al menos dos años.
- El plazo de respuesta que se informa es de 15 días calendario (propuesta en el prototipo).

## 6. Casos límite y errores

- Una hoja con un número de pedido que no existe se acepta; el pedido es un dato opcional.
- Dos envíos idénticos seguidos generan hojas separadas, pues no se puede distinguir una repetición de un nuevo reclamo.
- Una hoja de un consumidor sin correo electrónico exige otro medio de contacto.

## 7. Preguntas abiertas

- [ ] ¿Cuál es el plazo de respuesta que se informa al consumidor? Propuesta: 15 días calendario. Responde: responsable del proyecto.
- [ ] ¿Cuál es el medio de contacto obligatorio: correo electrónico o teléfono? Propuesta: correo. Responde: responsable del proyecto.
