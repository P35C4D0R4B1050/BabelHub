# 2026-11-21 (noche) — CI de GitHub en verde y decisiones de la persona

## Qué se hizo
- `push` de `main` y primera corrida real de `ci` y `secretos`: 135 pruebas fallidas en Linux → 7 → verde (`e0bc86df`, run 44932437329). Causas raíz: permisos de ejecución de los dobles y scripts (22 scripts en 100644), prueba de tiempos del proveedor del LLM, enlaces fuera de `docs/` en la copia de SpecCheck, `x-excepciones: null` del compose nuevo, prueba de arranque de imágenes que no entregaba los secretos exigidos (ahora configuración aleatoria + PostgreSQL desechable por arquitectura) y 11 semillas falsas del historial fijadas en `.gitleaksignore`. Un fallo intermitente posterior (AC-SEL-28, expiración frente a entrega) era una suposición de la prueba, no una carrera de producción; 30/30 verdes con ambas ramas de la carrera medidas (15/15).
- Perfil realista a 2 000 sesiones: 2+1 CPU las sostiene (0,6+0,4, 1,0+0,5 y 1,5+0,75 no); la corrida anterior usaba credenciales de 14 min (el Inventario no admite más de 15): corregido en DOC-1 §6d.
- NOT-02 implementado (`pedido-confirmado`, `libro-digital-disponible`); índice infinito en Done; DD-24 (D6 cerrada, AM01 a 6.000, S3 «pedidos»); DD-25 (nombre y correo de todo usuario); DD-26 a DD-29 (Libro de Reclamaciones por cuenta, rondas 4 a 6 autorizadas, `hojas-por-origen` liberable); Reclamaciones y «Mi cuenta» implementados y verificados con PostgreSQL real.

## Decisiones (de la persona, en chat)
DD-24 a DD-29 en `progress/decisiones-delegadas.md`. Docker: producción usa contenedores; el entorno de referencia de las pruebas es Linux.

## Pendiente / siguiente
Ver la sección 6 de `progress/relevos/coordinador.md` (llaves de Culqi y LLM al final del proyecto, asistente diferido, Menores con dueño la persona, 19 PRs de Dependabot, partes humanas de T32–T34). Abrir un chat nuevo de orquestación desde ese relevo.
