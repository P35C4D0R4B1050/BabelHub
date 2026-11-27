# 2026-11-27 — Accesos pendientes para la versión 0.1.3 (rama `claude/accesos-pendientes-cb9684`)

Pendientes señalados que no son solo apariencia, pedidos por la persona responsable para `v0.1.3`. Interfaz mínima y funcional; la apariencia queda para el carril de interfaz.

## Qué se hizo

- **Raíz de la gestión (O-5):** `/` lleva al ingreso o, con sesión, al destino del rol (DD-143). Solo en el host de gestión. Guía 08 §13.3 y §15.5.
- **Volver a la compra tras ingresar (O-11):** causa: «Ingresar para continuar» llevaba a `/ingreso` sin ninguna señal y el ingreso acababa siempre en el destino del rol. Ahora viaja la marca cerrada `continuar=compra` y el cliente vuelve al carrito (DD-144). Cambio mínimo en el carrito: solo el enlace.
- **Pantallas sin enlace:** la cuenta del cliente enlaza «Mis devoluciones» y «Mis hojas de reclamación»; las devoluciones de la administración y sus reportes se enlazan entre sí (DD-145).
- **Segundo factor de la única administración (O-12):** spec nueva `identidad-recuperacion-del-segundo-factor` (aprobación delegada de spec y plan) y la orden `restablecer-segundo-factor --correo` (DD-140 a DD-142). Corregidos el comentario de `PrimerAdministradorDeLaInstalacion`, la guía §15.1 y los documentos de seguridad.
- **Ingreso:** el paso siguiente se decide bajo el bloqueo de la cuenta y no continúa si entretanto se cerraron las sesiones o cambió la contraseña.

## Decisiones

- DD-140 a DD-145, **pendientes de ratificar** (`progress/decisiones-delegadas.md`).
- Menores de las puertas y de las revisiones de código (0 Bloqueantes y 0 Importantes) en `progress/hallazgos.md`.

## Verificación

- `dotnet build BabelHub.sln -warnaserror` limpio. Suite completa: unitarias 5831 y 2454, arquitectura 261, cobertura 15, `SpecCheck` 23 e integración 8887, 4 omitidas (Culqi real) y 1 con error del propio cambio (el escáner de secretos marcó el nombre de una variable de prueba); corregida, el escáner y las pruebas afectadas dan 2799 de 2799. La suite entera no se repitió después.
- Antes de los cambios, la integración tuvo dos intermitentes que pasan aisladas (una nueva en `hallazgos.md`).
- La spec nueva queda `Done` (`verificacion.md`: Converge).
- Navegador integrado contra un entorno local aislado (PostgreSQL y Redis en contenedores propios, borrados al terminar): raíz de la gestión, regreso al carrito, enlaces, alta del administrador por consola, restablecimiento de su segundo factor, auditoría en la base y nueva configuración al ingresar.
- El aviso por correo se probó con un transporte que no entrega (quedó en cola): no se ha visto un envío real.

## Pendiente / siguiente

- Persona: ratificar DD-140 a DD-145 y publicar `v0.1.3`.
- Sin enlazar y para decidir: `/administracion/clientes/{id}/biblioteca` (ninguna pantalla muestra el identificador del cliente) y `POST /checkout/reiniciar` (no se tocó el checkout).
- Visto y no tocado: el ingreso de la gestión muestra la navegación de la tienda.
