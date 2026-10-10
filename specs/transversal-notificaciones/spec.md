---
id: transversal-notificaciones
titulo: Avisos por WhatsApp
estado: Draft
servicio: Transversal
prefijo: NOT
origen: [RF07, RF16, NN03]
calidad: [AC03, RNF03, RNF07, RNF08, RNF21]
depende: [identidad-seguridad, transversal-seguridad]
---

# Spec — Avisos por WhatsApp

## 1. Propósito

Avisar al cliente y a las sucursales de cada cambio importante sin que el aviso pueda bloquear una compra o una operación. Define el contrato de aviso que usan los demás servicios; el canal es WhatsApp (AD16).

## 2. Alcance

| Dentro | Fuera |
| :--- | :--- |
| Qué eventos generan un aviso y a quién. | El contenido de cada pantalla de estado (otras specs). |
| Consentimiento, reintentos, deduplicación y estado de cada aviso. | Otros canales como correo o SMS (mejora posterior). |
| Notificador simulado para desarrollo. | Conversaciones bidireccionales con el cliente. |

## 3. Requisitos (EARS)

| ID | Patrón | Requisito | Origen |
| :--- | :--- | :--- | :--- |
| NOT-01 | Evento | Cuando ocurre un evento de negocio notificable, el sistema deberá enviar un aviso por WhatsApp a quien corresponde: el cliente, la sucursal de origen o la de destino. | RF07, RF16 |
| NOT-02 | Ubicuo | El sistema deberá considerar como eventos notificables el pedido confirmado, en preparación, en tránsito, listo para recojo, entregado, la guía de envío, el libro digital disponible, los cambios de estado de un traslado, de una devolución y la respuesta a un reclamo. | RF07, NN03 |
| NOT-03 | No deseado | Si el cliente no dio su consentimiento o lo revocó, entonces el sistema deberá no enviarle avisos. | RNF07 |
| NOT-04 | No deseado | Si el envío de un aviso falla, entonces el sistema deberá dejarlo pendiente, reintentarlo con esperas crecientes y no interrumpir la operación que lo originó. | AC03, RNF03 |
| NOT-05 | Ubicuo | El sistema deberá evitar enviar dos veces el mismo aviso para el mismo evento. | RF07 |
| NOT-06 | Ubicuo | El sistema deberá registrar el estado de cada aviso como pendiente, enviado o fallido, con su fecha. | RNF08 |
| NOT-07 | Ubicuo | El sistema deberá excluir de los mensajes los datos prohibidos en registros y mensajes definidos en `transversal-seguridad`. | RNF07, RNF20 |
| NOT-08 | Evento | Cuando el cliente revoca su consentimiento, el sistema deberá cancelar los avisos pendientes que le correspondían. | RNF07 |
| NOT-09 | Ubicuo | El sistema deberá usar el contacto de notificación registrado de cada sucursal para avisarle. | RF16 |
| NOT-10 | Ubicuo | El sistema deberá permitir cambiar el texto de los mensajes sin modificar el código de los servicios. | RF07 |
| NOT-11 | Estado | Mientras el entorno es de desarrollo, el sistema deberá usar un notificador simulado que registre los avisos sin enviarlos. | RF07 |
| NOT-12 | Ubicuo | El sistema deberá autenticar las llamadas hacia el servicio de envío y las respuestas o estados que este devuelva, y descartar y registrar las que no se puedan verificar. | RNF21 |

## 4. Criterios de aceptación

| ID | Cubre | Escenario |
| :--- | :--- | :--- |
| AC-NOT-01 | NOT-01, NOT-02 | **Dado** un pedido que pasa a listo para recojo, **cuando** cambia el estado, **entonces** el cliente y la sucursal reciben su aviso. |
| AC-NOT-02 | NOT-03 | **Dado** un cliente sin consentimiento, **cuando** su pedido cambia de estado, **entonces** no se le envía ningún aviso. |
| AC-NOT-03 | NOT-04 | **Dado** que el canal de envío está caído, **cuando** un pedido cambia de estado, **entonces** el cambio se aplica, el aviso queda pendiente y se reintenta con esperas crecientes. |
| AC-NOT-04 | NOT-05 | **Dado** el mismo evento de negocio reportado dos veces, **cuando** se procesa, **entonces** se envía un solo aviso. |
| AC-NOT-05 | NOT-06 | **Dado** un aviso enviado y otro fallido, **cuando** se consulta su registro, **entonces** cada uno muestra su estado y fecha. |
| AC-NOT-06 | NOT-07 | **Dado** una compra de un libro digital, **cuando** se envía el aviso, **entonces** el mensaje no contiene el enlace de descarga ni datos de pago. |
| AC-NOT-07 | NOT-08 | **Dado** un aviso pendiente para un cliente, **cuando** revoca su consentimiento, **entonces** el aviso se cancela. |
| AC-NOT-08 | NOT-09 | **Dado** un traslado hacia Ayacucho, **cuando** se aprueba, **entonces** se avisa al contacto registrado de Ayacucho y al de la sucursal de origen. |
| AC-NOT-09 | NOT-10 | **Dado** una plantilla de mensaje, **cuando** se modifica su texto, **entonces** los avisos siguientes usan el nuevo texto sin cambiar el código. |
| AC-NOT-10 | NOT-11 | **Dado** el entorno de desarrollo, **cuando** ocurre un evento notificable, **entonces** el aviso se registra sin enviarse a ningún número real. |
| AC-NOT-11 | NOT-12 | **Dado** una respuesta del servicio de envío sin firma o con firma inválida, **cuando** llega, **entonces** se descarta y se registra, y el aviso no se marca como enviado. |

## 5. Reglas e invariantes

- Un aviso nunca bloquea ni revierte la operación que lo originó.
- Sin consentimiento no hay avisos al cliente; las sucursales siempre se avisan por su contacto registrado.
- Los avisos son transaccionales: sin publicidad.
- El mensaje no incluye datos sensibles; los detalles se consultan dentro de la tienda.

## 6. Casos límite y errores

- Un número inválido marca el aviso como fallido tras agotar los reintentos y lo deja visible a la administración.
- Si el canal cambia de proveedor, solo cambia el adaptador, no los servicios (AD16).
- Un evento anterior al consentimiento no genera avisos retroactivos.

## 7. Preguntas abiertas

- [ ] ¿Cuántos reintentos y con qué espera? Propuesta: 5 reintentos con espera creciente hasta 1 hora. Se decide en el plan.
- [ ] ¿Se avisa también por correo cuando no hay WhatsApp? Propuesta: fuera de esta versión. Responde: responsable del proyecto.
