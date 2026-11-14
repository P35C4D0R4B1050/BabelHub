# 2026-11-13 — Implementación de `transversal-seguridad` (T01 a T30)

## Qué se hizo
- Se implementó el plan de `transversal-seguridad` tarea por tarea, con pruebas primero, en los dos servicios (el motor de Redis solo en el E-commerce, el de PostgreSQL solo en el Inventario). Commits de `7acd9fb` (T01) a `86882ba` (T30); 28 de 30 tareas marcadas.
- **Dominio y Aplicación (T01 a T12):** catálogo de políticas de límite, huellas, reserva y umbrales de alerta, contrato público de `Plataforma`, `EvaluarLimite`, entradas de integración con firma, dependencias externas, matriz de alcances de servicio, falla segura, verificación de persona, umbrales de alerta, correo de un código y redacción de datos prohibidos.
- **Infraestructura (T13 a T22):** motor de Redis con scripts Lua atómicos, adaptadores base, trabajo de umbrales, motor local, huella HMAC con clave versionada, motor de PostgreSQL con bloqueos asesores, almacén de entradas procesadas y verificador de firma HMAC, verificador de Turnstile, cliente HTTP resiliente con credenciales de salida y carga y validación de secretos con registro redactado.
- **Api (T23 a T25):** errores uniformes y correlación, tope previo y límites generales, origen determinado en el borde con reglas de Nginx, y verificación de la credencial de servicio (HTTPS, matriz de alcances, rotación en dos pasos, origen reenviado y tope de 120 por minuto de la dirección directa).
- **Web y pruebas (T26 a T28):** atributo y tag helper de Turnstile solo en las páginas que lo exigen, revisión automática contra salida sin codificar, ingreso doble con límites sobre Redis real (T27a), y revisión del repositorio y de los registros con secretos y cabeceras sembrados.
- **Docs (T30):** autenticación (alcances, rotación, límites de entrada), modelo de datos (`eventos_limite`, `entradas_procesadas`), privacidad (huella), lista de secretos y Redis de contadores con su ACL.
- Se reparó la codificación de acentos de comentarios de T25 (`a019924`).

## Decisiones
- El detalle de cada decisión de implementación está en `specs/transversal-seguridad/decisiones-de-implementacion.md` y las correcciones al plan, en `progress/ajustes-al-implementar.md` (T04, T23 y T27 se parten).
- Hallazgos reales de las pruebas: la redacción de registros dejaba el valor de un campo sensible dentro del texto del mensaje y no reconocía `signing-key` y similares (corregido en T28); la prueba existente de AC-SEG-07 no comprobaba nada sobre cuentas existentes o no (reemplazada en T27a).
- Los umbrales provisionales (verificación de persona 20 por 5 minutos, origen desconocido 3000 por minuto) y los pendientes menores están en `progress/hallazgos.md`.

## Verificación
`dotnet build BabelHub.sln -warnaserror` sin advertencias; pruebas: 30 de arquitectura, 461 unitarias del E-commerce, 396 del Inventario, 825 de integración (con Redis, PostgreSQL y Nginx en contenedores) y 14 de SpecCheck, sin errores; SpecCheck: 20 specs, 0 avisos.

## Pendiente / siguiente
- **T27b y T29** (50 rechazos con un solo evento, reinicio sin perder ni duplicar, bordes de 10/11 y 20/21 con alerta y correo agrupado) necesitan el acumulador persistente de `transversal-auditoria` (T10, T18 y T25): se cierran después y luego se pasa `spec-verify` a `transversal-seguridad`.
- Orden siguiente: `transversal-plataforma`, `transversal-auditoria`, `identidad-seguridad`, `transversal-notificaciones`; falta planificar `web-base` y `web-indice-infinito`. Sin push.
