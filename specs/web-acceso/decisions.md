---
spec: web-acceso
---

# Decisiones locales — Ayudas de contraseña, acceso del personal y código QR del segundo factor

| ID | Decisión | Motivo | Alternativa descartada | Enlace |
| :--- | :--- | :--- | :--- | :--- |
| D01 | **Dependencia nueva:** `Net.Codecrete.QrCodeGenerator` 3.2.1 (licencia MIT, de Manuel Bleichenbacher, publicada el 2026-09-07, sin dependencias propias; comprobado en el índice público de paquetes el 2026-11-27), solo en la Infraestructura del E-commerce, detrás de `IGeneradorDeCodigoQr` (DD-120). | Generar el código en el servidor sin servicios de terceros (ACC-14). Es pequeña y entrega los módulos, así que la vista dibuja un SVG propio sin imágenes ni `data:` y la política de contenido no cambia. | Escribir un codificador propio (mucho código delicado de probar); un servicio externo (envía el secreto fuera); un guion de terceros en el navegador; `QRCoder` (MIT, también válida; más grande de lo que hace falta). | RNF19, PLA-26 |
| D02 | La barra, el aviso y los botones los **crea el guion**; el servidor escribe el texto de ayuda, los atributos de los campos y las cifras de la política. | Sin guiones no queda nada inerte en la pantalla (ACC-08). | Dibujarlos en el servidor y ocultarlos con una clase (se verían sin estilos o sin función). | RNF13 |
| D03 | El cálculo del nivel vive solo en el navegador y se prueba ejecutando el archivo real con Node. | La contraseña no sale del dispositivo al escribirse (ACC-05) y la regla no se duplica en dos lenguajes. Es orientación visual: la política sigue solo en Identidad. | Calcularlo en el servidor (enviaría la contraseña en cada tecla); una biblioteca de estimación (peso y dependencia sin necesidad). | RNF15 |
| D04 | La dirección del personal es configuración opcional; los Compose no la fijan. | Los dominios son del despliegue; las carpetas de secretos ya generadas siguen siendo válidas. | Deducirla del `Host` de la solicitud (manipulable); fijarla en el Compose (ata el repositorio a un dominio). | RNF06 |
| D05 | **Dependencia solo de pruebas:** `ZXing.Net` 0.16.11 (licencia Apache-2.0, publicada el 2025-10-25), en `tests/integration`, para leer el código dibujado (DD-120). | La prueba no puede comparar la biblioteca consigo misma: un lector independiente demuestra que lo dibujado se lee y dice la dirección. | Comparar módulos con la misma biblioteca (circular); solo la comprobación manual. | RNF19 |
