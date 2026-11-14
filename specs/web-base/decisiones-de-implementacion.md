# Decisiones de implementación — web-base

Decisiones menores tomadas al implementar que el plan no fija. No cambian el comportamiento de la spec.

| Tarea | Decisión | Motivo |
| :--- | :--- | :--- |
| T21 | Los documentos describen la interfaz como «Presentación sin reglas de negocio» más el módulo `Web` (Dominio, Aplicación e Infraestructura, esquema `web`) con las reglas de la sesión, el borrador y el área por rol. La enumeración de cookies vive en `docs/05-seguridad/04-privacidad-y-datos-personales.md` §1.1 y de ahí sale el texto de privacidad de WEB-29 (T26). Las amenazas nuevas son AM35 (credencial y renovación cifradas en la base), AM36 (borrador plantado o filtrado) y AM37 (clave de sesiones). | Plan §3, §6 y D17. |
| T21 | `docs/05-seguridad/03-autenticacion-y-autorizacion.md` §3 deja anotado que `rotar-credenciales.sh` debe cambiar también las dos claves de sesiones (`BABELHUB__Web__ClaveDeSesionesTienda` y `…Gestion`): el script es del carril A (`transversal-plataforma` T29, `PLA-R2`) y queda como hallazgo para ese carril. | El script no se toca desde este carril. |
| T21 | `docs/05-seguridad/07-operacion-segura.md` §3.3 fija las dependencias del front que el SBOM no ve y nombra `src/BabelHub.Ecommerce/BabelHub.Ecommerce.Api/Web/versiones.json` como el archivo de versiones y huellas (lo crea T23). | PLA-26, D09. |
| T21 | La spec `web-base` **no** pasa a `InProgress` en este carril: cambiar su `estado` regenera `specs/TRACEABILITY.md`, que es del integrador (`docs/07/05` §4). Se pasa a `InProgress` al fundir. | Reparto de archivos entre carriles. |
