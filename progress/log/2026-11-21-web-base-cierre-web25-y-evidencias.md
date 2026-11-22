# 2026-11-21 — web-base: WEB-25 en reclamaciones y evidencia automática de T32 a T34

Carril `cierre-web` (rama `claude/cierre-web-4790bf`).

**Hecho.** (1) WEB-25 en `/reclamaciones/nueva` con pruebas primero: `FormularioDeLaHojaDeReclamacion` con `[Conservar]` en bien, monto, referencia, tipo y detalle; recuperación en el `GET` con sesión; 8 pruebas de integración (`BorradorDeLaHojaDeReclamacionTests`, `PantallaDeLaHojaConBorradorTests`). Decisión DD-30 (pendiente de ratificar): nunca nombre, documento, domicilio ni correo (REC-04). (2) Corrida automática de T32 a T34 en Chromium de escritorio y teléfono y WebKit, con evidencia y protocolo manual en `progress/evidencias/web-base/`. (3) `verificacion.md`, `hallazgos.md`, `CHANGELOG.md`, `bloques.md` y `decisiones-de-implementacion.md` actualizados.

**Hallazgos de la corrida.** LCP de escritorio 2,5 a 3,2 s con TLS de Kestrel en Debug (por HTTP cumple); CSS sin reconstruir provoca desplazamiento horizontal en `/reclamaciones/nueva`; las pruebas con sesión necesitan el archivo de entorno exportado; WebKit supera 30 s en «secciones de la cuenta» con el equipo cargado.

**No hecho (de personas).** Navegadores y móviles reales (Firefox no inicia aquí, Edge no instalado), lector de pantalla, dos rondas de usabilidad. `web-base` sigue en `Planned`.
