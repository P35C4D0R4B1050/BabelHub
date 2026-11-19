# Relevo del carril V (Envíos y Devoluciones)

Lee primero `AGENTS.md`, `progress/coordinacion.md` y `progress/relevos/coordinador.md` §6 (trampas). Esta sesión empezó como planificadora (ECO-3, ECO-7, FIN-1) y pasó a implementar.

## Hecho (todo en `main`)
Planes de 7 specs de comercio y de asistente e índice infinito (aprobados). `spec-verify` de 6 specs (`specs/<spec>/verificacion.md`). `ecommerce-envios` 20/25 (ECO-9a a 9d y parte de 9e; `RestReservasDelEnvio` sobre `CerrarReserva`). `ecommerce-devoluciones` 22/34 (ECO-10a a 10d).

## Pendiente
1. **ECO-10d (T19–T22) sin revisiones de arquitectura y seguridad** (la sesión se detuvo por límite): pasar `arch-reviewer` y `security-reviewer`, corregir los Importantes e informar.
2. **Devoluciones ECO-10e–10f (T23–T34).** Dependen de Pagos (`IReembolsos`), Pedidos (`IPedidosParaDevoluciones`, de B) y Biblioteca (`IAccesosParaDevoluciones`, declarada por V como aditivo; su dueño será quien implemente ECO-8).
3. **Envíos:** T14b (cliente real, diferido por D02), T20 y T22 (esperan el Pedidos real de B), T21b y T24 (esperan T14b/T22 y el host compuesto).

## Trampas
Fixtures de PostgreSQL sin literales de contraseña; los contratos de Pedidos son de B (al hacer `git merge main`, te quedas con los suyos); espejar catálogos de auditoría en los dos servicios.

## Cómo avisar
Mensaje de una línea al coordinador al cerrar cada bloque; turno de integración para pruebas con Docker.
