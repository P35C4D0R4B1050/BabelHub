---
name: security-review
description: Revisa una spec, un plan o un cambio contra los documentos de seguridad de BabelHub (modelo de amenazas, lista de controles ASVS, privacidad, API y asistente). Úsala al terminar una spec o un plan que toque autenticación, datos personales, pagos, archivos digitales, límites de uso o integraciones, y antes de pasar una spec a Done.
---

# security-review — Revisión de seguridad

Para una revisión en contexto aislado, delega en el agente `security-reviewer`. Esta skill describe el método. Complementa a `arch-review` (capas y referencias): aquí se revisa **qué puede salir mal y qué control lo cubre**.

## Qué se lee

1. `docs/05-seguridad/01-modelo-de-amenazas.md` (amenazas `AM`) y `02-controles-asvs.md` (controles `CTL`).
2. Los documentos de `docs/05-seguridad/` que apliquen: autenticación, privacidad, API e integraciones, asistente, operación.
3. `specs/transversal-seguridad/spec.md` (SEG) y la spec o el plan a revisar.
4. `AGENTS.md`, sección 5.

## Lista de comprobación

1. **Amenazas:** ¿qué amenazas `AM` toca la feature? ¿Cada una tiene un requisito o una tarea que la cubra? ¿Hay una amenaza nueva que falte en el modelo?
2. **Controles:** ¿qué controles `CTL` aplican? ¿Están en la spec, el plan o las tareas? ¿Cuáles estaban *Pendiente* y se resuelven aquí?
3. **Entradas y salidas:** validación en el servidor y codificación de salida (SEG-01, SEG-02); consultas con parámetros.
4. **Autenticación y autorización:** alcance y propiedad comprobados en la Aplicación; por defecto se niega; sin identificadores de otro usuario aceptados de la solicitud.
5. **Límites de uso:** los límites de la feature usan la respuesta y el registro de `transversal-seguridad` (SEG-07, SEG-08); atomicidad ante solicitudes simultáneas.
6. **Datos prohibidos en registros:** sin contraseñas, credenciales, códigos, secretos, datos de tarjeta ni enlaces firmados (SEG-10).
7. **Errores:** formato uniforme, sin trazas; fallo seguro (SEG-03, SEG-04).
8. **Privacidad:** datos mínimos; ¿el dato nuevo está en el inventario de `04-privacidad-y-datos-personales.md`? Retención y borrado definidos.
9. **Integraciones:** autenticación de llamadas entrantes, validación de respuestas, tiempos de espera, idempotencia (SEG-12 a SEG-15).
10. **Asistente (si aplica):** solo lectura, salida como texto, topes, datos mínimos al proveedor.
11. **Pruebas:** casos de abuso nombrados con su criterio `AC-XXX-NN`.

## Informe

Hallazgos ordenados de más a menos grave: **Bloqueante**, **Importante**, **Menor**. Cada uno con: archivo e ID (requisito, tarea o línea), amenaza o control afectado (`AMxx`, `CTL-xx`), por qué importa y corrección propuesta. Termina con un veredicto: **Cumple**, **Cumple con observaciones** o **No cumple**. Si no hay hallazgos, dilo y di qué revisaste.

No se cambian archivos: solo se reporta. Si la revisión encuentra una amenaza o un control nuevo, se propone su texto para el siguiente `AM` o `CTL` disponible; no se renumeran los existentes.
