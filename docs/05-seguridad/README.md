# Seguridad

Documentación de seguridad y privacidad de BabelHub. Se orienta por **OWASP** (Top 10:2025, ASVS 5.0, API Security Top 10:2023, Top 10 para LLM 2025) y toma como referencia **ISO/IEC 27001/27002:2022**, **ISO/IEC 25010:2023** y **NIST SSDF**, sin buscar certificación (RC16). Es proporcionada a un proyecto académico con datos y pagos reales en el diseño.

| Documento | Para qué sirve |
| :--- | :--- |
| [01 Modelo de amenazas](01-modelo-de-amenazas.md) | Activos, fronteras de confianza, amenazas `AM01`–`AM24` y casos de abuso. |
| [02 Lista de controles (ASVS 5.0)](02-controles-asvs.md) | Controles `CTL-01`–`CTL-38` con nivel, spec y estado. |
| [03 Autenticación y autorización](03-autenticacion-y-autorizacion.md) | Credenciales, sesión, segundo factor, alcance por sucursal e identidades de servicio. |
| [04 Privacidad y datos personales](04-privacidad-y-datos-personales.md) | Inventario de datos, minimización, derechos ARCO, transferencias y retención. |
| [05 API e integraciones](05-api-e-integraciones.md) | Riesgos de API, límites de uso, webhooks y terceros. |
| [06 Seguridad del asistente](06-seguridad-del-asistente.md) | Aislamiento del LLM, riesgos y pruebas de abuso. |
| [07 Operación segura](07-operacion-segura.md) | CI de seguridad, endurecimiento, registro y alertas, respaldo y respuesta a incidentes. |
| [08 Mapeo a estándares](08-mapeo-a-estandares.md) | Trazabilidad con OWASP e ISO. |

## Cómo se usa

1. Al escribir una spec que toca una frontera de confianza, se consulta el modelo de amenazas y se citan las `AM` relevantes.
2. Al planificar, se enlazan los controles `CTL` que aplican y se marcan los *Pendiente* que la spec resuelve.
3. Al verificar, `spec-verify` y el agente de seguridad contrastan el cambio con estos documentos.
4. Los identificadores `AM` y `CTL` no se renumeran; los nuevos se agregan al final.

## Fuera de alcance

Certificación ISO 27001, auditorías de terceros y pruebas de penetración externas (véase la [necesidad del negocio](../01-analisis-de-sistema/01-necesidad-del-negocio.md)).
