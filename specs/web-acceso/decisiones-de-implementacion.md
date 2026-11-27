---
spec: web-acceso
---

# Decisiones de implementación — Ayudas de contraseña, acceso del personal y código QR del segundo factor

Lo que se decidió al construir, sin cambiar la spec ni el plan aprobados.

| Fecha | Decisión | Motivo |
| :--- | :--- | :--- |
| 2026-11-27 | Un único parcial, `_CampoDeContrasena`, escribe todos los `input` de contraseña; una prueba falla si otra vista escribe uno. | Que ACC-17 y el botón de ACC-07 valgan también para las pantallas futuras. |
| 2026-11-27 | La ayuda de ACC-01 la escribe ese parcial (sin parcial aparte `_AyudaDeContrasenaNueva`) y las cifras salen de `ReglasDeContrasena`; el guion recibe el mínimo en `data-bh-nueva`. | Menos piezas; una sola fuente de las cifras. |
| 2026-11-27 | Las regiones que se anuncian usan `aria-live="polite"` sin rol `status`. | Las pantallas ya usan ese rol para sus mensajes y un recorrido E2E existente los distinguía por él. |
| 2026-11-27 | `ZXing.Net` 0.16.11 comprobado al restaurar: editor Michael Jahn, licencia Apache-2.0, sin dependencias para `net9.0` o superior; `Net.Codecrete.QrCodeGenerator` 3.2.1: Manuel Bleichenbacher, MIT, sin dependencias. | Cierra el Menor de la puerta del plan sobre D05. |
| 2026-11-27 | El `CodigoQr` vive en `IngresoVista.Qr` y `CuentaVista.Qr`, nunca en `ModeloBaseDePagina`. | Es el secreto del segundo factor (Menor de la puerta). |
| 2026-11-27 | El guion oculta también al salir de la página (`pagehide`) y la dirección del personal solo admite ASCII sin «%» (revisión de seguridad del código). | Un campo visible no debe quedar en el historial; un homógrafo en la dirección no debe pasar. |
| 2026-11-27 | Las pruebas del navegador se corrieron en Chromium (escritorio y teléfono) y WebKit contra un arnés temporal con las vistas, el guion, la hoja de estilos y la política de contenido reales y dobles de Identidad, servido por HTTPS; el arnés no se versiona. La corrida contra el entorno completo queda para quien tenga ese entorno. | No hace falta base de datos para estas pantallas y los pasos del personal necesitan cuentas que el entorno local no siembra. |
