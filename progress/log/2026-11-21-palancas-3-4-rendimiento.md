# 2026-11-21 — Palancas de rendimiento 3 y 4 (carril de cierre de rendimiento)

## Qué se hizo
- La caché de disponibilidad (palanca 4) **ya existía** en el catálogo del E-commerce (D03: 30 s, invalidación, respaldo de 10 minutos); se corrigió DOC-1 §5 y se midió su efecto con el resolutor real (`CacheDeDisponibilidadDeCargaTests`: 97,8 % de acierto con 20 ediciones, 56,8 % con 5 000 de popularidad Zipf) y con un modelo en k6 (`FALLOS_DE_CACHE` en `perfil-realista.js`).
- Palanca 3: se encendió la memoria de referencias de 5 s del Inventario (`infra/docker-compose.inventario.yml` y entorno de carga; `CARGA_REFERENCIAS_S`). Medición alternada: +45 % de solicitudes en el peor caso; rampa consecutiva con p95 menor hasta 500 usuarios. El matiz a D05 de `inventario-fuentes` consta solo en DD-51 (editar `decisions.md` rompe la huella del plan aprobado; igual que DD-17).
- Se implementó y **se revirtió** mover los límites generales del Inventario a memoria (sin efecto: la consulta de servicio no los evalúa).
- Decisiones DD-50 a DD-54, pendientes de ratificar. Evidencias en `progress/evidencias/k6/2026-11-21-palancas-3-4-*`.

## Hallazgos
- Las comparaciones separadas por horas no son válidas: el mismo build dio p95 de 70 ms y de 15 s (carga de otros carriles o base de la carga muy usada; no se aisló). Tras recrear el volumen de la base, `checkout-confirmacion` cumplió en las cuatro corridas.
- RNF02 con 0,6 + 0,4 CPU sigue sin cumplirse en el peor caso de estrés; las palancas no cambian ese veredicto.
- No se hizo la fusión del cambio de rol con la primera sentencia (un comando por lote por repositorio): toca archivos del carril de inventario.

## Pendiente
- Ratificar DD-50 a DD-54 (la persona).
