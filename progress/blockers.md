# Bloqueos y decisiones pendientes

Se actualiza con la skill `progress-log`. Cada ítem tiene dueño y condición de cierre.

## Abiertos

| ID | Tema | Detalle | Bloquea | Dueño |
| :--- | :--- | :--- | :--- | :--- |
| B02 | Almacén de archivos | Elegir Cloudflare R2 u Oracle Object Storage, confirmar cuotas gratuitas vigentes y elegir un emulador S3 para desarrollo (no se confirmó una imagen oficial vigente de MinIO en Docker Hub). | `ecommerce-biblioteca-digital` (plan) | Responsable del proyecto |
| B03 | Cuenta de Oracle Cloud | La región no se puede cambiar después y la capacidad ARM puede fallar al crear la VM. Pasar la cuenta a PAYG (sin superar el cupo) con presupuesto y alerta de 1 USD. El cupo es de 2 OCPU y 12 GB ARM y 2 VMs micro **por cuenta**. | Despliegue (no bloquea el desarrollo local) | Responsable del proyecto |
| B04 | Dominio y Cloudflare | Registrar un dominio para el borde de red. | Despliegue | Responsable del proyecto |
| B05 | Culqi (modo de pruebas) | Crear la cuenta de pruebas y confirmar idempotencia, Yape y reembolsos. | `ecommerce-pagos` (plan) | Responsable del proyecto |
| B06 | Proveedor de LLM | Elegir proveedor y obtener clave para el asistente. | `asistente-compra-ia` (plan) | Responsable del proyecto |
| B07 | Número de WhatsApp | Un número dedicado para Evolution API (no se necesita en desarrollo: hay notificador simulado). | Despliegue | Responsable del proyecto |
| B08 | Proveedor de correo | Elegir un proveedor gratuito de envío de correo para los códigos de verificación y recuperación, las respuestas a reclamos, las alertas a la administración y los avisos de pago pendiente, y configurar el dominio remitente (SPF, DKIM y DMARC). | `identidad-seguridad` (plan), `ecommerce-libro-reclamaciones` y despliegue | Responsable del proyecto |
| B09 | Verificación legal | Confirmar con una persona experta lo marcado «a verificar» en `docs/01-analisis-de-sistema/03-marco-legal.md`: Ley 29571, campos y plazo de la hoja de reclamación, plazos ARCO, plazo de notificación de incidentes, menores y cookies. | No bloquea: riesgo aceptado el 2026-10-16 por ser un proyecto académico; las specs se aprobaron con los valores propuestos. Imprescindible antes de operar con clientes reales | Responsable del proyecto |
| B10 | Segunda cuenta de Oracle | El perfil objetivo de cuatro VMs ARM usa una segunda cuenta (con otra tarjeta). El FAQ de Oracle permite una cuenta gratuita por persona y prohíbe crear varias: la cuenta puede ser suspendida o cancelada. Mitigación: la cuenta secundaria solo aloja VM-BORDE y VM-VENTAS (sin estado) y el perfil mínimo de dos VMs queda listo y probado en la principal. Alternativa: pagar las dos VMs ARM adicionales en la misma cuenta (~14 USD/mes cada una, a verificar). Decisión del responsable antes de abrir la segunda cuenta. | Despliegue del perfil objetivo (no bloquea el desarrollo ni el perfil mínimo) | Responsable del proyecto |
| B11 | Red entre VMs y cuentas | Prueba de concepto de la malla WireGuard entre las cuatro VMs frente al peering de redes virtuales entre cuentas: latencia en la misma región, consumo de CPU y comportamiento ante reinicios. Si la latencia incumple RNF02 en k6, agrupar Inventario y E-commerce (`docs/02-arquitectura-de-software/07-presupuesto-de-recursos.md` §7). | Despliegue del perfil objetivo | Responsable del proyecto |

## Cerrados

| ID | Tema | Cierre |
| :--- | :--- | :--- |
| C01 | Validación de AD08 (reserva con expiración) | Validada con el docente. |
| C02 | Comprobantes electrónicos | Descartados por ser un proyecto académico. |
| C03 | Libro de Reclamaciones | Confirmado (RF25). |
| C04 | Aprobación de specs | Las 19 specs fueron aprobadas (`Approved`) por el responsable el 2026-10-16. Quedan pendientes los planes y las tareas de cada una. |
| C05 | Pasarela de pago | Culqi confirmada el 2026-11-06 (AD17). Queda B05 para las llaves de pruebas. |
