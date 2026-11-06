# Bloqueos y decisiones pendientes

Se actualiza con la skill `progress-log`. Cada ítem tiene dueño y condición de cierre.

## Abiertos

| ID | Tema | Detalle | Bloquea | Dueño |
| :--- | :--- | :--- | :--- | :--- |
| B02 | Almacén de archivos | Elegir Cloudflare R2 u Oracle Object Storage, confirmar cuotas gratuitas vigentes y elegir un emulador S3 para desarrollo (no se confirmó una imagen oficial vigente de MinIO en Docker Hub). | `ecommerce-biblioteca-digital` (plan) | Responsable del proyecto |
| B03 | Cuenta de Oracle Cloud | Una sola cuenta con los recursos gratuitos (2 VMs ARM y 2 AMD micro). La región no se puede cambiar después y la capacidad ARM puede fallar al crear la VM. Pasar la cuenta a PAYG (sin superar el cupo) con presupuesto y alerta de 1 USD. | Despliegue (no bloquea el desarrollo local) | Responsable del proyecto |
| B04 | Dominio y Cloudflare | Registrar un dominio para el borde de red. | Despliegue | Responsable del proyecto |
| B05 | Culqi (modo de pruebas) | Crear la cuenta de pruebas y confirmar idempotencia, Yape y reembolsos. | `ecommerce-pagos` (plan) | Responsable del proyecto |
| B06 | Proveedor de LLM | Elegir proveedor y obtener clave para el asistente. | `asistente-compra-ia` (plan) | Responsable del proyecto |
| B07 | Número de WhatsApp | Un número dedicado para Evolution API (no se necesita en desarrollo: hay notificador simulado). | Despliegue | Responsable del proyecto |
| B08 | Proveedor de correo | Elegir un proveedor gratuito de envío de correo para los códigos de verificación y recuperación, las respuestas a reclamos, las alertas a la administración y los avisos de pago pendiente, y configurar el dominio remitente (SPF, DKIM y DMARC). | `identidad-seguridad` (plan), `ecommerce-libro-reclamaciones` y despliegue | Responsable del proyecto |
| B09 | Verificación legal | Confirmar con una persona experta lo marcado «a verificar» en `docs/01-analisis-de-sistema/03-marco-legal.md`: Ley 29571, campos y plazo de la hoja de reclamación, plazos ARCO, plazo de notificación de incidentes, menores y cookies. | No bloquea: riesgo aceptado el 2026-10-16 por ser un proyecto académico; las specs se aprobaron con los valores propuestos. Imprescindible antes de operar con clientes reales | Responsable del proyecto |
| B10 | Reaprobación de specs | Seis specs vuelven a `Draft` por la reformulación a dos servicios y las nuevas reglas (revisadas y corregidas el 2026-11-06, sin preguntas abiertas; listas para aprobar): `inventario-ajustes` (ingreso de mercadería), `inventario-fuentes` (fuente de recepción y unidades vendidas), `ecommerce-catalogo` (stock inicial y agotado), `identidad-seguridad`, `asistente-compra-ia` y `transversal-seguridad` (contextos entre módulos). La persona responsable debe revisarlas y aprobarlas (ver `progress/R5-reformulacion-a-dos-servicios.md`). | Planes y código de esas features | Responsable del proyecto |
| B11 | Rendimiento de la entrada y latencia entre VMs | Medir con k6 la VM-BORDE (1/8 de OCPU), la latencia entre VM-VENTAS y VM-INVENTARIO y la CPU de cada VM; si la entrada no rinde, cloudflared y Nginx pasan a VM-VENTAS. Decidir también el archivado de WAL y la réplica opcional de PostgreSQL. | Despliegue | Responsable del proyecto |

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
