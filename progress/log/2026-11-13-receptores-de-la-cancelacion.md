# 2026-11-13 — Receptores de la cancelación y reaperturas de la fase 3

## Qué se hizo
- Se escribieron los requisitos receptores de la cancelación de cuenta en las ocho specs afectadas y se pasaron por la puerta delegada en oleadas según las dependencias (selección de fuente y reclamaciones; carrito; pagos; biblioteca; pedidos; envíos y devoluciones), con tres revisores por spec y repetición hasta que los tres quedaron sin Bloqueantes ni Importantes sobre el mismo texto.
- Identidad se reabrió dos veces (marcado de hojas y verbo común «anonimizar, desvincular o eliminar»; reembolsos pendientes en IDE-27) y Pagos se reabrió para PAG-20 y PAG-32.
- Se alinearon documentos de privacidad, amenazas (AM30, AM31), modelo de datos y arquitectura.

## Decisiones de la persona responsable
Retención de 2 años, teléfono de entrega, 30 días para el contacto de un checkout abandonado, reembolso pendiente que difiere la cancelación, identificador opaco en el pedido, pedido no recogido como pendiente, retención de 2 años del texto libre de Devoluciones y ambos avisos de envío. Ver `progress/decisiones-previas-al-plan.md`.

## Verificación
`dotnet build BabelHub.sln -warnaserror` sin advertencias; `dotnet test`: 30 pruebas de arquitectura, 8 de la puerta de aprobación y las unitarias, sin errores; SpecCheck: 20 specs, 0 avisos.

## Siguiente
Planes y tareas de la fase 3 y la resolución de los Importantes heredados al planificar. Sin push.
