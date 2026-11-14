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

# Segunda ronda sobre B literal (autorizada por la persona, 2026-11-14 noche)

Revisada la primera versión del borrador B literal en `specs/` (Draft). Veredictos: `spec-auditor` necesita otra vuelta (4 Importantes), `arch-reviewer` no cumple (2), `security-reviewer` no cumple (4); sin Bloqueantes, no se aprobó. Todo es de redacción y de propiedad de la regla; no se reabrió Identidad ni se amplió la decisión.

Se corrigió en `spec-B-literal.md` (ahora 49 requisitos y 42 criterios, `SpecCheck` 0 avisos, **sin revisar**):

- WEB-41 «pantalla que su rol permite pero este estado bloquea» (fuera de rol sigue siendo sin permiso); WEB-42, WEB-44 y WEB-46 acotados a «persona con sesión», con las rutas del desafío previo de WEB-34 fuera de la lista de WEB-46 (evita el bloqueo del personal con contraseña temporal); WEB-34 acotado al personal e incluye «presentar» el segundo factor.
- Propiedad del bloqueo: WEB-42 deja de afirmar que «solo refleja»; la Web aplica una política de acceso como defensa en profundidad, prevalece la respuesta de Identidad, Carrito o Pagos, se quita Pedidos y se nombra el código `ECO-TEXTOS-PENDIENTES` (WEB-46).
- Registro: WEB-47 (acciones directas bloqueadas, sin redirecciones, con identificador opaco, agrupadas según AUD-13, fuera de SEG-09) y WEB-48 (falla de la consulta como alerta técnica según AUD-12 y AUD-13; lo fuera de rol o alcance sigue por AUD-05, evaluado antes que los textos); WEB-44 evalúa primero sesión, rol y alcance.
- Redirección: WEB-49 y AC-WEB-42 (destino en el servidor, validado, sin parámetros ni cabeceras, sin reejecutar acciones).
- Criterios: AC-WEB-34 a AC-WEB-42 con cliente en Administración (sin permiso), aceptar con versión nueva, reserva no liberada, cancelación de pedido y desistimiento, tiempo de espera, respuesta mal formada, estado desconocido, renovación, cierre de sesión con revocación pendiente, 11 bloqueos sin alerta de SEG-09 y destinos externos.
- Casos límite: citas de IDE-20, IDE-23 e IDE-42, consecuencia aceptada con revocar consentimientos (WhatsApp), cancelar la cuenta, rechazar cambio de correo, marcar avisos y el plazo de 7 días; origen de las excepciones en NN15 y REC-02 «por confirmar» (§7).

## Menores restantes (a `progress/hallazgos.md` al aprobar)

Contrato de Identidad: la Web como consumidora y el aceptar a mitad de sesión (IDE-43); costo de una consulta por solicitud y su efecto en LCP; HEAD y OPTIONS en WEB-46; solapamiento de CAR-36 con la redirección de la Web y de CAR-22/AC-WEB-24; `docs/05` línea 40 y `docs/06`; registro del riesgo aceptado frente a AC03; CTL-35, CTL-36 y CTL-26 enlazados a WEB-41 a WEB-49; amenaza AM35.

## Para la persona

La spec ya pasó cuatro rondas (dos sobre el texto ampliado y dos sobre B). Cada ronda halla Importantes de redacción nuevos porque el texto crece; conviene una ronda final **acotada a verificar los 49 requisitos del borrador v2**, y confirmar la decisión sobre los derechos y el registro.

# Ronda final sobre el borrador v2 (autorizada, 2026-11-14 noche)

Veredictos: `security-reviewer` **cumple con observaciones** (0 Importantes), `spec-auditor` **con cambios menores** (0 Importantes) y `arch-reviewer` **cumple con observaciones con 2 Importantes**: (I-1) la política de textos pendientes es la única barrera real en Pedidos, Devoluciones, Biblioteca y Asistente y vive en la Presentación, y la redacción «defensa en profundidad» era engañosa; (I-2) las excepciones de aceptar, cerrar sesión, leer los textos legales y usar el Libro (con envío de hoja) siguen «por confirmar» pese a figurar como resueltas, y la puerta exige cero preguntas abiertas.

Ya corregido en `spec-B-literal.md` (49 requisitos, 42 criterios, `SpecCheck` 0 avisos), sin cambiar comportamiento: I-1 (componente transversal de composición que consulta el contrato público de Identidad; única barrera para esos cuatro módulos y hallazgo para sus planes); el canal sin sesión para derechos queda «por definir» en vez de afirmarse; WEB-47 como evento propio (AUD-26) con contador por ventana de 15 minutos y WEB-48 con AUD-29; §6 sin «conservando lo escrito» ante un bloqueo; Libro acotado en WEB-43 (enlace público, envío de hoja y consulta propia).

**Falta I-2, que es decisión de la persona:** confirmar las excepciones (aceptar, cerrar sesión, textos legales, Libro con envío de hoja). Con su confirmación se quitan los dos «por confirmar» (§6 y §7) y queda una verificación acotada de `arch-reviewer` sobre I-1 e I-2 antes de aprobar con `spec-approve`.

## Menores a `progress/hallazgos.md` al aprobar

Orden de la política frente al módulo y estado HTTP de `ECO-TEXTOS-PENDIENTES`; HEAD y OPTIONS en WEB-46 y la lista por prefijo (no por extensión) de estáticos y salud; lista permitida definida una sola vez y patrones EARS (Estado o Evento en WEB-44, 46, 47 y 49) en el plan; AC-WEB-36 y AC-WEB-28 con los casos nombrados (revocar consentimientos, cancelar cuenta, rechazar cambio de correo, marcar avisos, presentar el segundo factor, cancelar el ingreso); ruta de retorno de la pasarela en la lista cerrada; WEB-39 exento de WEB-42 y WEB-46; costo de una consulta por solicitud, ESC-AC03-03 (Identidad en la réplica de gestión) y reintento de la revocación (IDE-61, IDE-64) en el plan; contrato de Identidad con estado cerrado para cualquier persona.
