# Bloqueos y decisiones pendientes

Se actualiza con la skill `progress-log`. Cada ítem tiene dueño y condición de cierre.

## Abiertos

| ID | Tema | Detalle | Bloquea | Dueño |
| :--- | :--- | :--- | :--- | :--- |
| B03 | Cuenta de Oracle Cloud | Una sola cuenta con los recursos gratuitos (2 VMs ARM y 2 AMD micro). La región no se puede cambiar después y la capacidad ARM puede fallar al crear la VM. Pasar la cuenta a PAYG (sin superar el cupo) con presupuesto y alerta de 1 USD. | Despliegue (no bloquea el desarrollo local) | Responsable del proyecto |
| B04 | Dominio y Cloudflare | Registrar un dominio para el borde de red. | Despliegue | Responsable del proyecto |
| B05 | Culqi (modo de pruebas) | Culqi confirmada (C05). La persona responsable está creando la cuenta de pruebas; falta confirmar idempotencia, Yape y reembolsos. Las llaves **no se comparten en el chat**: se configuran en variables de entorno locales. Hasta entonces se usa una pasarela simulada tras el puerto. | La implementación de `ecommerce-pagos` contra Culqi (el plan se escribe con la pasarela simulada) | Responsable del proyecto |
| B06 | Proveedor de LLM | Decidido el 2026-11-12: **OpenCode Go** (API gestionada, ya usada por el responsable) o Claude, a confirmar al implementar. Falta la clave, que **no se comparte en el chat**: se configura en una variable de entorno local. Hasta entonces el asistente usa un proveedor simulado tras su puerto. Datos mínimos al proveedor (`docs/05-seguridad/06-seguridad-del-asistente.md`). | La implementación de `asistente-compra-ia` (el plan se escribe sin la clave) | Responsable del proyecto |
| B07 | Número de WhatsApp | Un número dedicado para Evolution API (no se necesita en desarrollo: hay notificador simulado). | Despliegue | Responsable del proyecto |
| B09 | Verificación legal | Confirmar con una persona experta lo marcado «a verificar» en `docs/01-analisis-de-sistema/03-marco-legal.md`: Ley 29571, campos y plazo de la hoja de reclamación, plazos ARCO, plazo de notificación de incidentes, menores y cookies. | No bloquea: riesgo aceptado el 2026-10-16 por ser un proyecto académico; las specs se aprobaron con los valores propuestos. Imprescindible antes de operar con clientes reales | Responsable del proyecto |
| B11 | Rendimiento de la entrada y latencia entre VMs | Medir con k6 la VM-BORDE (1/8 de OCPU), la latencia entre VM-VENTAS y VM-INVENTARIO y la CPU de cada VM; si la entrada no rinde, cloudflared y Nginx pasan a VM-VENTAS. El archivado de WAL es obligatorio (PLA-15, decidido el 2026-11-12); queda elegir su herramienta y decidir la réplica opcional de PostgreSQL. | Despliegue | Responsable del proyecto |
| B12 | Aprobación de specs | Estado al 2026-11-12 tras las rondas 4 a 11 de la puerta delegada (`docs/07-proceso-de-desarrollo/04-aprobacion-delegada.md`): `transversal-auditoria` y `transversal-plataforma` quedaron **Approved** (registradas en `progress/aprobaciones.md`). `identidad-seguridad` sigue en **Draft** por decisión de la persona responsable: los revisores dejaron de encontrar Bloqueantes e Importantes de fondo y los últimos, de redacción sobre la cuota de correo (IDE-66 a IDE-80), están corregidos sin una última revisión; la aprueba la persona responsable. Los Menores y los valores de la cuota (tamaños, topes, plazo de IDE-68) están en `progress/hallazgos.md` para el plan. | Plan de identidad-seguridad y de las specs que dependen de ella | Responsable del proyecto |
| B13 | Copia de los respaldos fuera de Oracle | Los respaldos, el archivado de WAL y los libros digitales quedan en Oracle Object Storage de la **misma cuenta**: si se pierde la cuenta, se pierde todo. **Riesgo aceptado el 2026-11-12** por falta de recursos para otro proveedor; se revisa antes de operar con clientes reales (B09) o si hay presupuesto. Una opción de costo cero a valorar es exportar el volcado cifrado a un disco de la persona responsable. Cambiaría el destino de respaldo, no el código de negocio. | Nada (mejora posterior) | Responsable del proyecto |

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
| C10 | Almacén de archivos (antes B02) | Decidido por el responsable el 2026-11-12: **Oracle Object Storage** (compatible con S3), dentro de su capa gratuita; en desarrollo, un emulador compatible con S3 o una carpeta local. Queda confirmar la cuota vigente en la consola al crear la cuenta (B03).
| C11 | Proveedor de correo (antes B08) | Decidido por el responsable el 2026-11-12: **MailKit sobre SMTP con una cuenta de Gmail sencilla** (no institucional ni corporativa), sin dominio propio por ahora (ya no hace falta SPF, DKIM ni DMARC; B04 deja de bloquearlo). La credencial de aplicación irá en una variable de entorno y no se comparte en el chat; en desarrollo, un enviador simulado tras el puerto `EnviadorDeCorreo`. Límite conocido: el envío diario de una cuenta gratuita de Gmail es bajo, suficiente para este volumen.
