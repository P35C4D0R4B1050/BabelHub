# Bloqueos y decisiones pendientes

Se actualiza con la skill `progress-log`. Cada ítem tiene dueño y condición de cierre.

## Abiertos

| ID | Tema | Detalle | Bloquea | Dueño |
| :--- | :--- | :--- | :--- | :--- |
| B02 | Almacén de archivos | Elegir Cloudflare R2 u Oracle Object Storage y un emulador S3 para desarrollo (no se confirmó una imagen oficial vigente de MinIO en Docker Hub). **Recomendación (2026-11-06, a confirmar en las páginas oficiales):** Cloudflare R2: nivel gratuito de 10 GB-mes, 1 millón de operaciones de escritura y listado y 10 millones de lectura al mes, sin costo de salida de datos, y ya se usa Cloudflare en el borde. Oracle Object Storage Always Free: unos 20 GB y ~50 000 solicitudes al mes según fuentes secundarias (cifras antiguas de 10 GB), sin confirmar en la consola. | `ecommerce-biblioteca-digital` (plan) | Responsable del proyecto |
| B03 | Cuenta de Oracle Cloud | Una sola cuenta con los recursos gratuitos (2 VMs ARM y 2 AMD micro). La región no se puede cambiar después y la capacidad ARM puede fallar al crear la VM. Pasar la cuenta a PAYG (sin superar el cupo) con presupuesto y alerta de 1 USD. | Despliegue (no bloquea el desarrollo local) | Responsable del proyecto |
| B04 | Dominio y Cloudflare | Registrar un dominio para el borde de red. | Despliegue | Responsable del proyecto |
| B05 | Culqi (modo de pruebas) | Crear la cuenta de pruebas y confirmar idempotencia, Yape y reembolsos. | `ecommerce-pagos` (plan) | Responsable del proyecto |
| B06 | Proveedor de LLM | Elegir proveedor y obtener clave para el asistente. Opciones: (a) un modelo económico de pago por uso con un tope de gasto en la cuenta del proveedor; (b) la capa gratuita de un proveedor, con límites de uso que condicionan ASI-09 y ASI-20. El asistente es opcional y degrada con aviso si el proveedor falla (ASI), así que no bloquea el resto. Datos mínimos al proveedor (`docs/05-seguridad/06-seguridad-del-asistente.md`). | `asistente-compra-ia` (plan) | Responsable del proyecto |
| B07 | Número de WhatsApp | Un número dedicado para Evolution API (no se necesita en desarrollo: hay notificador simulado). | Despliegue | Responsable del proyecto |
| B08 | Proveedor de correo | Elegir un proveedor gratuito de envío de correo para los códigos de verificación y recuperación, las respuestas a reclamos, las alertas a la administración y los avisos de pago pendiente, y configurar el dominio remitente (SPF, DKIM y DMARC). **Referencia (2026-11-06, fuentes secundarias, a confirmar):** Resend ofrece 3 000 correos al mes con tope de 100 al día y un dominio; Brevo, 300 al día; Mailjet, 6 000 al mes con tope de 200 al día. El volumen del proyecto (códigos, reclamos, alertas) cabe en cualquiera; Resend es la opción más simple para un dominio propio. Requiere B04 (dominio). | `identidad-seguridad` (plan), `ecommerce-libro-reclamaciones` y despliegue | Responsable del proyecto |
| B09 | Verificación legal | Confirmar con una persona experta lo marcado «a verificar» en `docs/01-analisis-de-sistema/03-marco-legal.md`: Ley 29571, campos y plazo de la hoja de reclamación, plazos ARCO, plazo de notificación de incidentes, menores y cookies. | No bloquea: riesgo aceptado el 2026-10-16 por ser un proyecto académico; las specs se aprobaron con los valores propuestos. Imprescindible antes de operar con clientes reales | Responsable del proyecto |
| B11 | Rendimiento de la entrada y latencia entre VMs | Medir con k6 la VM-BORDE (1/8 de OCPU), la latencia entre VM-VENTAS y VM-INVENTARIO y la CPU de cada VM; si la entrada no rinde, cloudflared y Nginx pasan a VM-VENTAS. Decidir también el archivado de WAL y la réplica opcional de PostgreSQL. | Despliegue | Responsable del proyecto |
| B12 | Aprobación de dos specs | `transversal-auditoria` volvió a `Draft` el 2026-11-06 (auditoría R6, H01: reescrita para dos servicios) y `transversal-plataforma` es nueva (H07, RNF10, 11, 12, 19, 24 y 28). **Un agente no las aprueba**: esperan la revisión y la aprobación de la persona responsable. Mientras tanto, las specs que dependen de la auditoría siguen `Approved`. | Planes de las specs que dependen de la auditoría y de la plataforma | Responsable del proyecto |

## Cerrados

| ID | Tema | Cierre |
| :--- | :--- | :--- |
| C01 | Validación de AD08 (reserva con expiración) | Validada con el docente. |
| C02 | Comprobantes electrónicos | Descartados por ser un proyecto académico. |
| C03 | Libro de Reclamaciones | Confirmado (RF25). |
| C04 | Aprobación de specs | Las 19 specs fueron aprobadas (`Approved`) por el responsable el 2026-10-16. Quedan pendientes los planes y las tareas de cada una. |
| C05 | Pasarela de pago | Culqi confirmada el 2026-11-06 (AD17). Queda B05 para las llaves de pruebas. |
| C06 | Segunda cuenta de Oracle | Descartada el 2026-11-06: se usa una sola cuenta con recursos gratuitos (antes B10). |
| C07 | Red entre cuentas (WireGuard) | Ya no aplica: todas las VMs comparten la red privada de la cuenta (antes B11). |
| C08 | Reformulación a dos servicios | Aprobada por el responsable el 2026-11-06 (E-commerce e Inventario como monolitos modulares). |
| C09 | Reaprobación de las seis specs | Aprobadas por el responsable el 2026-11-06 tras la revisión (antes B10): `inventario-ajustes`, `inventario-fuentes`, `ecommerce-catalogo`, `identidad-seguridad`, `asistente-compra-ia` y `transversal-seguridad`. Quedan los planes y las tareas. |
