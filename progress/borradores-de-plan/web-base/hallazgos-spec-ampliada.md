# Hallazgos de la ronda acotada sobre la spec ampliada de web-base (2026-11-14)

Texto revisado: `spec-ampliada-WEB-34.md` (WEB-34, WEB-41 a WEB-45, AC-WEB-28 y AC-WEB-34 a AC-WEB-37, con la lista de WEB-42 ampliada a derechos y contraseña por decisión de la persona). `specs/web-base/spec.md` queda en su estado aprobado. Veredictos: `spec-auditor` necesita otra vuelta (4 Importantes), `arch-reviewer` cumple con observaciones (5 Importantes, 1 de negocio), `security-reviewer` no cumple (5 Importantes). Ningún Bloqueante.

## Parada: reabrir `identidad-seguridad` (decisión de la persona)

Los tres revisores coinciden: **IDE-58 no enumera los «cambios sensibles»** y la lista de lo permitido con textos pendientes (WEB-42, WEB-43) vive solo en la Web, que no puede ser la guardiana de una regla de negocio (AGENTS §3.4, §5; regla 9). Con la ampliación a derechos y contraseña, Identidad podría bloquear lo que la Web ofrece. Salidas: (a) reabrir IDE-58 y AC-IDE-39 (volver a Draft, definir la lista cerrada para cliente y personal, exponer en el contrato público de Identidad el estado de aceptación y las acciones permitidas, alinear `docs/05/03` línea 40); (b) B literal, que coincide con IDE-58 tal cual (ver cuenta y pedidos, sin pagar) y no exige reabrir. No lo toqué.

## Importantes a corregir en el texto (cuando se decida lo anterior)

1. **WEB-42 y «impedir pagar»:** contradice la nota «la Web solo lo refleja» y duplica CAR-36, CAR-37 y PAG-31. Reformular como «no ofrecer» y dejar el bloqueo de pagos a Carrito y Pagos; quitar «no puede pagar» de AC-WEB-35 o probar solo que no se ofrece.
2. **WEB-45 sin excepciones:** con Identidad caída deben seguir disponibles el cierre de sesión, los textos legales y el Libro de Reclamaciones; el servidor debe negar toda acción con sesión (también POST directo) y no usar un estado previo en caché; AC-WEB-37 debe probar un POST directo con Identidad caída.
3. **WEB-34 y WEB-41 describen estados sin sesión:** el segundo factor y la contraseña temporal se resuelven antes de emitir credencial (IDE-42, plan de Identidad). Precisar que son un desafío previo a la sesión; extender WEB-44 y WEB-45 a WEB-34; WEB-44 debe decir «por el contrato público de Identidad» y consultar el estado de la cuenta (aceptación, segundo factor, contraseña temporal), no solo la aceptación; excluir estáticos y `/salud`.
4. **Aceptar no resuelve otra pendiente:** la acción de aceptar se rechaza en el servidor mientras haya una pendiente de WEB-34; añadir el caso a AC-WEB-28.
5. **Negación por defecto verificable:** una ruta o acción nueva queda denegada salvo marca de permitida; añadir un criterio de recorrido de todas las rutas (como D18 de `transversal-seguridad`); cubrir endpoints de acciones, asistente, contexto de lectura (IDE-33), biblioteca y devoluciones.
6. **WEB-42 sin condicionar a IDE-54, IDE-29 ni WEB-37:** añadir «sin perjuicio de IDE-54, IDE-29 y WEB-37; lo que Identidad niegue se niega» y los negativos (dispositivo no reconocido o congelado, correo sin verificar, cambiar número de WhatsApp o correo, desactivar el segundo factor).
7. **Respuesta ante lo denegado:** una sola regla (pantalla de otra área sin permiso por rol: sin permiso WEB-03; pantalla permitida al rol pero bloqueada por textos: redirección a aceptar; acción bloqueada: rechazo con `code` estable) y declarar si cuentan como AUD-05 / SEG-09.
8. **Criterios demasiado compuestos:** dividir WEB-42 (permitidos y denegados) y AC-WEB-35 en varios, con POST directo en los negativos; AC-WEB-34 debe probar ingreso y renovación por separado, que el carrito se conserva y la versión vigente guardada (IDE-57); AC-WEB-36 debe cubrir Tienda y que el personal no cambia contraseña.

## Menores

`depende` sin `ecommerce-devoluciones` y `calidad` sin AC03; WEB-43 y WEB-41 desalineadas («una persona» incluye personal); registrar la decisión del 2026-11-14 en §7; destino de la redirección fijo (sin redirección abierta) y regreso tras aceptar; registro de denegaciones y de la falla de consulta sin datos personales; aclarar en WEB-42 «cerrar todas las sesiones» (IDE-60), sucursal preferida (WEB-16) y aviso mostrado (WEB-39); caché breve del estado como nota del plan; alinear `docs/03/01` §4.3 («por la capa de Aplicación») con la regla 13.

## Lo no decidido por la persona

Catálogo y carrito en lectura, biblioteca y descargas de lo ya comprado, devoluciones, asistente y las acciones protectoras (rechazar un cambio de correo, marcar avisos): la spec ampliada los trata como denegados por defecto salvo rechazar el cambio de correo y marcar avisos, que **sí incluí en WEB-42 sin decisión de la persona**; hay que quitarlos o decidirlos. La coordinación indicó no ampliarlos.
