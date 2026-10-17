# Bloqueos y decisiones pendientes

Se actualiza con la skill `progress-log`. Cada ítem tiene dueño y condición de cierre.

## Abiertos

| ID | Tema | Detalle | Bloquea | Dueño |
| :--- | :--- | :--- | :--- | :--- |
| B01 | Aprobación de specs | Las 19 specs están en `Draft` y sin preguntas abiertas (cerradas el 2026-10-16). Falta que la persona responsable las apruebe (ver `specs/README.md` y `progress/R3-analisis-inicial.md`). | Planes y código de cualquier feature | Responsable del proyecto |
| B02 | Almacén de archivos | Elegir Cloudflare R2 u Oracle Object Storage, confirmar cuotas gratuitas vigentes y elegir un emulador S3 para desarrollo (no se confirmó una imagen oficial vigente de MinIO en Docker Hub). | `ecommerce-biblioteca-digital` (plan) | Responsable del proyecto |
| B03 | Cuenta de Oracle Cloud | La región no se puede cambiar después y la capacidad ARM puede fallar al crear la VM. | Despliegue (no bloquea el desarrollo local) | Responsable del proyecto |
| B04 | Dominio y Cloudflare | Registrar un dominio para el borde de red. | Despliegue | Responsable del proyecto |
| B05 | Culqi (modo de pruebas) | Crear la cuenta de pruebas y confirmar idempotencia, Yape y reembolsos. | `ecommerce-pagos` (plan) | Responsable del proyecto |
| B06 | Proveedor de LLM | Elegir proveedor y obtener clave para el asistente. | `asistente-compra-ia` (plan) | Responsable del proyecto |
| B07 | Número de WhatsApp | Un número dedicado para Evolution API (no se necesita en desarrollo: hay notificador simulado). | Despliegue | Responsable del proyecto |
| B08 | Proveedor de correo | Elegir un proveedor gratuito de envío de correo para los códigos de verificación y recuperación, las respuestas a reclamos, las alertas a la administración y los avisos de pago pendiente, y configurar el dominio remitente (SPF, DKIM y DMARC). | `identidad-seguridad` (plan), `ecommerce-libro-reclamaciones` y despliegue | Responsable del proyecto |
| B09 | Verificación legal | Confirmar con una persona experta lo marcado «a verificar» en `docs/01-analisis-de-sistema/03-marco-legal.md`: Ley 29571, campos y plazo de la hoja de reclamación, plazos ARCO, plazo de notificación de incidentes, menores y cookies. | No bloquea: riesgo aceptado el 2026-10-16 por ser un proyecto académico; las specs se aprobaron con los valores propuestos. Imprescindible antes de operar con clientes reales | Responsable del proyecto |

## Cerrados

| ID | Tema | Cierre |
| :--- | :--- | :--- |
| C01 | Validación de AD08 (reserva con expiración) | Validada con el docente. |
| C02 | Comprobantes electrónicos | Descartados por ser un proyecto académico. |
| C03 | Libro de Reclamaciones | Confirmado (RF25). |
