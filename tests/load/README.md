# Pruebas de carga (k6)

Escenarios para verificar los objetivos de rendimiento: disponibilidad con 50 usuarios concurrentes con p95 menor a 500 ms (RNF02, `AC-FUE-08`) y confirmación del checkout con p95 menor a 3 s, sin contar la pasarela. También la prueba de sobreventa con solicitudes simultáneas sobre la última unidad (AC01).

Se ejecutan contra un entorno con los datos de ejemplo de `docs/07-proceso-de-desarrollo/02-convenciones-tecnicas.md` y una sola VM de 1 OCPU simulada con límites de CPU y memoria.
