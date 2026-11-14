# Ronda sobre la spec de web-base con la opción B literal (2026-11-14)

Texto revisado: la redacción de WEB-34 y WEB-41 a WEB-45 con la opción B literal de la persona. Veredictos: `spec-auditor` necesita otra vuelta (3 Importantes), `arch-reviewer` cumple con observaciones (5 Importantes), `security-reviewer` no cumple (4 Importantes). Sin Bloqueantes; no se aprobó. **No se reabre `identidad-seguridad`.** `specs/web-base/spec.md` sigue Approved con el WEB-34 original.

El borrador corregido es `spec-B-literal.md` (47 requisitos, 40 criterios, `SpecCheck` sin errores ni avisos al probarlo en `specs/` y restaurar). Aplica estas correcciones, que no requieren decisión de negocio:

- WEB-41 solo para clientes; WEB-43 con su lista permitida (cuenta, aceptar, cierre de sesión, textos, Libro) y, ante una pantalla que el rol permite, redirección a aceptar (no «sin permiso»); un rol que no la permite sigue viendo la pantalla de sin permiso (WEB-03).
- WEB-42: «no ofrecer» y rechazo directo como defensa en profundidad, sin sustituir a Identidad, Carrito, Pagos y Pedidos; excepción expresa del envío de una hoja de reclamación (REC-02) en WEB-42 y WEB-45.
- WEB-44 reescrito (consulta «si aceptó la versión vigente» por el contrato público, sin estáticos ni salud, sin estado anterior); negación por defecto por ruta y método en WEB-46 (nuevo); registro en WEB-47 (nuevo).
- WEB-45: «no responde» definido, sesión y carrito conservados, cierre de sesión local, riesgo aceptado frente a AC03.
- WEB-34 acotado a «cancelación del ingreso» (no hay sesión), citas IDE-21, IDE-23 e IDE-42, y la aceptación solo después de completar esos pasos.
- Criterios divididos (AC-WEB-34 a AC-WEB-40) con POST directo, rutas y métodos, versión vigente guardada (IDE-57), reserva y carrito conservados, y registros; `depende` con `ecommerce-pagos`, `ecommerce-pedidos`, `ecommerce-devoluciones` y `asistente-compra-ia`; `calidad` con AC03; casos límite de redirección (WEB-25), reserva a mitad de pago y Identidad caída; pregunta resuelta en §7.

## Pendiente de la persona antes de otra ronda

1. **Derechos sobre datos personales bloqueados.** B literal bloquea las solicitudes ARCO, revocar consentimientos y cancelar la cuenta hasta aceptar los textos (consecuencia que la seguridad marcó Importante por la Ley 29733). Quedó escrita como consecuencia aceptada; si la persona prefiere excepciones, hay que definirlas en IDE-58 (Identidad).
2. **Registro de lo bloqueado.** WEB-47 propone un evento propio que no cuenta para el umbral de SEG-09 (evita falsas alertas con Identidad caída o al publicar una versión nueva); AUD-05 solo cubre acceso fuera de rol, de sucursal o sin sesión. Confirmar o reabrir `transversal-auditoria`.
3. **Estado previo a la sesión.** La garantía de que aceptar nunca salta el segundo factor ni la contraseña temporal depende de que Identidad no emita credencial antes (IDE-21, IDE-23, IDE-42 y el plan de Identidad); la seguridad pide un IDE explícito que lo exija. Registrar como hallazgo de Identidad (carril A).
4. Otra ronda de revisión (la puerta ya agotó sus tres) y, después, la verificación del plan de `web-base`.

## Menores a pasar a `progress/hallazgos.md` al aprobar

Enumeración de «cambios sensibles» de IDE-58 y que Pedidos no consume IDE-58 (dueño Identidad); fila de la Web como consumidora del contrato de Identidad en `docs/03/04`; caché y costo de una consulta por solicitud y tiempo de espera (plan); posible amenaza AM35 (evadir el bloqueo o aprovechar la caída de Identidad); IDE-43 menciona solo términos y privacidad (IDE-57 incluye el reembolso); `docs/03/01` §4.3 («por la capa de Aplicación») frente a la regla 13; AC-WEB-28 solo ejemplifica al personal que no es encargado.
