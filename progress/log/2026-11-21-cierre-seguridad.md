# 2026-11-21 — Cierre de identidad-seguridad y transversal-seguridad (carril de cierre de seguridad)

| Pieza | Resultado |
| :--- | :--- |
| D5 (IDE-27) | La cancelación diferida indica cuáles pendientes faltan por grupo (DD-35). `PendientesDeCuentaCompuestos.Desglosar`, `EstadoDeLaCancelacionDeCuenta.Desglose`, `Cuenta.cshtml`. |
| AC-IDE-70 con un pedido real | `CuentaConUnPedidoRealTests`: compra real por Carrito, Checkout, Pagos y Pedidos; el pedido conserva el contacto tras corregir el nombre y cambiar el correo, y el correo anterior no queda en auditoría, eventos ni alertas. También verifica D5 con módulos reales. |
| SEG-17 y SEG-23 en el Inventario (D2) | Llamada de cliente sin dirección reenviada: origen desconocido con límites por origen y evento `OrigenIndeterminable` (DD-36); espejado en el E-commerce. Pruebas unitarias y HTTP en ambos. |
| SEG-31 del asistente | Diferido (DD-37); `transversal-seguridad` no pasa a Done. |
| Estado | `identidad-seguridad` → **Done** (T57 marcada, matriz regenerada). `transversal-seguridad` sigue InProgress con la reserva declarada. |
| Revisiones | `security-reviewer` y `arch-reviewer`: sin Bloqueantes ni Importantes; Menores en `progress/hallazgos.md`. |

Comprobado: build con `-warnaserror` sin advertencias, SpecCheck 0 avisos, arquitectura 259, unitarias E-commerce 5668 e Inventario 2388, integración dirigida verde. Las pruebas de integración existentes que no reenviaban origen se ajustaron para enviarlo (el caso sin origen tiene ahora pruebas propias).
