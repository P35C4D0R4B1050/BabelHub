# 2026-11-25 — Respaldos comprimidos en Cloudflare R2 (carril `dos-vms-y-r2`, entrega 2)

## Qué se hizo

- Se reabrió `transversal-plataforma` y se reaprobaron la spec y el plan por la delegación (filas en `progress/aprobaciones.md`; sin Bloqueantes ni Importantes; batería completa en verde antes de firmar). La persona responsable decidió R2 como provisional y respondió los puntos de riesgo (DD-106 y DD-107, ratificadas solo en esos puntos).
- Medición local de tamaños comprimidos (`progress/ensayos/medicion-respaldos-comprimidos-2026-11-25.md`): la retención cabe en los 10 GB gratuitos; no se propusieron alternativas.
- T43: `zstd` antes de `age`, extracción con límites, `scripts/descifrar-wal.sh` y restauración real de extremo a extremo.
- T44 y T45: definiciones y órdenes de R2, Oracle sin respaldos, secretos, despliegue, rotación, vigilancia y guía.
- Revisiones de arquitectura y seguridad de la implementación: 5 Importantes cada una, corregidos. Los principales: un hueco en el archivo de cambios pasaba por completo, una historia de línea de tiempo falsa podía desviar la recuperación, otra compresión se saltaba los topes de extracción y la prueba de la cuenta de la guía podía aprobar sin probar nada.

- En la verificación de los arreglos apareció un fallo más (introducido por la corrección): la historia de línea de tiempo se exigía en el almacén, que la retira a los 7 días; ahora sale de la copia (`--restaurada`). Las dos revisiones terminaron en «Cumple con observaciones», sin Bloqueantes ni Importantes.

## Validación

`dotnet build BabelHub.sln -warnaserror`, `SpecCheck` (22 specs, 0 avisos), escáner de secretos sin hallazgos, 851 pruebas de integración afectadas (almacén, respaldos, VM-AUX, despliegue, constancias, restauración e imágenes) y 261 de arquitectura en verde, y después de los últimos cambios las de restauración y respaldo otra vez (79). La batería completa (G9) se pasó antes de firmar, sobre el código anterior a esta implementación.

## Lo que no se probó

- Cloudflare R2 real: la API, el bloqueo, el alcance de los tokens, el ciclo de vida y `rclone` contra R2 (T46 y T47, de la persona responsable, antes de poner datos que importen).
- Oracle real, ARM real, los comandos de la guía en la cuenta y la restauración sobre una VM (T01, T29, T31).
