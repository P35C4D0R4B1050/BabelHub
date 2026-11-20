# 2026-11-20 — Cierre del carril C: E2E de WEB-2 y Reclamaciones, correcciones y estado

**Turno de Docker (≈35 min).** Entorno E2E aislado (`bhe2e`), host de la tienda por HTTPS, Inventario y datos sembrados; apagado al terminar.

- E2E `acceso/acceso-y-cuenta.spec.ts` (10 recorridos de visitante y cliente más el tema oscuro) y `reclamaciones/libro-de-reclamaciones.spec.ts` (8): verdes en Chromium de escritorio, Chromium de teléfono y WebKit (Firefox, informativo, no se lanza en este equipo). PostgreSQL de la renovación de la sesión (62 pruebas de sesiones web e Identidad) y `IntegrationTests.Web` (924) verdes.
- **Defectos reales corregidos:** huella de solicitud de código incompatible con la columna (registro y recuperación fallaban contra la base real); `aria-current` solo en «Resumen»; contraste AA de pestañas inactivas y etiquetas; desborde a 375 px de las etiquetas del formulario de reclamaciones.
- Revisiones `arch-reviewer` y `security-reviewer`: sin Bloqueantes ni Importantes; los Menores quedaron corregidos o en `progress/hallazgos.md`.
- **Abierto:** E2E de la administración (T18 de Reclamaciones, T30 y T31 de web-base) y del ingreso del personal; T31 de Pagos con el Carrito real; T36 (E2E de Pagos); T37 y T38 esperan B05. Ninguna tarea nueva se marcó. `verificacion.md` de las cuatro specs: **No converge**, con lo que falta.
- Ajustes de E2E posteriores a la corrida (filtro de Turnstile, `baseURL`, contenedor vacío) comprobados solo con `--list`: correrlos en la próxima ventana.
- Rama `claude/keen-ishizaka-8f8cfa`; nada se subió.
