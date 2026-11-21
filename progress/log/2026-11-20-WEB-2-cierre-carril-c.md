# 2026-11-20 — Cierre del carril C: E2E de WEB-2 y Reclamaciones, correcciones y estado

**Turno de Docker (≈35 min).** Entorno E2E aislado (`bhe2e`), host de la tienda por HTTPS, Inventario y datos sembrados; apagado al terminar.

- E2E `acceso/acceso-y-cuenta.spec.ts` (10 recorridos de visitante y cliente más el tema oscuro) y `reclamaciones/libro-de-reclamaciones.spec.ts` (8): verdes en Chromium de escritorio, Chromium de teléfono y WebKit (Firefox, informativo, no se lanza en este equipo). PostgreSQL de la renovación de la sesión (62 pruebas de sesiones web e Identidad) y `IntegrationTests.Web` (924) verdes.
- **Defectos reales corregidos:** huella de solicitud de código incompatible con la columna (registro y recuperación fallaban contra la base real); `aria-current` solo en «Resumen»; contraste AA de pestañas inactivas y etiquetas; desborde a 375 px de las etiquetas del formulario de reclamaciones.
- Revisiones `arch-reviewer` y `security-reviewer`: sin Bloqueantes ni Importantes; los Menores quedaron corregidos o en `progress/hallazgos.md`.
- **Abierto:** E2E de la administración (T18 de Reclamaciones, T30 y T31 de web-base) y del ingreso del personal; T31 de Pagos con el Carrito real; T36 (E2E de Pagos); T37 y T38 esperan B05. Ninguna tarea nueva se marcó. `verificacion.md` de las cuatro specs: **No converge**, con lo que falta.
- Ajustes de E2E posteriores a la corrida (filtro de Turnstile, `baseURL`, contenedor vacío) comprobados solo con `--list`: correrlos en la próxima ventana.
- Rama `claude/keen-ishizaka-8f8cfa`; nada se subió.

## Segundo turno de Docker (2026-11-21)

- `PagosConElCarritoRealTests` (T31 de Pagos, anfitrión real): 8 de 8. E2E de administración, de reclamaciones de la administración y de pago: junto con los anteriores, 113 de 113 en Chromium de escritorio y de teléfono y WebKit. Integración de Pagos, Reclamaciones y Web: 1282 verdes.
- **Defectos reales corregidos:** `<br>` dentro de `<dl>` en el detalle de la auditoría; regiones desplazables de las tablas de administración sin foco de teclado (teléfono); la pantalla de estado del pago no avisaba del rechazo; los E2E fijaban versiones de textos distintas de las vigentes de Identidad (en el test de integración).
- **Marcadas:** Pagos T31 y T36, Reclamaciones T22, web-base T29, T30 y T31. **Abiertas:** Pagos T37 y T38 (B05), Reclamaciones T19 a T21 (contrastar el texto), web-base T28 (E2E del ingreso del personal) y T32 a T35. `verificacion.md` de las cuatro specs: No converge, con lo pendiente y su dueño.
- Nota de entorno: los E2E de pago consumen stock sembrado; reponerlo con `sembrar-datos.sh --limpiar` antes de cada corrida (si no, «Ir al pago» vuelve a `/checkout`).
