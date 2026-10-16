# Mapeo a estándares

Relaciona los controles de BabelHub con los estándares de referencia (RC16). Es una **tabla de trazabilidad**, no una declaración de conformidad ni una certificación. Los nombres de las categorías se tomaron de las publicaciones de OWASP e ISO; antes de citar números exactos de requisito en un plan, se verifican contra el texto del estándar.

## 1. OWASP Top 10:2025

| Categoría | Cómo se atiende | Dónde |
| :--- | :--- | :--- |
| **A01 Control de acceso roto** | Alcance por sucursal en la Aplicación; propiedad por credencial; por defecto se niega. | RNF05, AM04, AM05; [autenticación y autorización](03-autenticacion-y-autorizacion.md) |
| **A02 Configuración de seguridad incorrecta** | Cabeceras, puertos cerrados, contenedores sin privilegios, entornos separados. | RNF06, RNF19, WEB-18; [operación segura](07-operacion-segura.md) |
| **A03 Fallas en la cadena de suministro de software** | Versiones fijas, escaneo, SBOM, Dependabot, imágenes con versión. | RNF19, AM16 |
| **A04 Fallas criptográficas** | TLS, resumen robusto de contraseñas, respaldos cifrados, firma de credenciales. | RNF18, CTL-16, CTL-27 a CTL-29 |
| **A05 Inyección** | Validación, parámetros en consultas, codificación de salida. | RNF16, SEG-01, SEG-02, AM08 |
| **A06 Diseño inseguro** | Modelo de amenazas, límites de flujos de negocio, idempotencia, fallo seguro. | [modelo de amenazas](01-modelo-de-amenazas.md), AD23 a AD25 |
| **A07 Fallas de autenticación** | Política de contraseñas, bloqueo, recuperación segura, segundo factor. | RNF15, IDE-03, IDE-15 a IDE-20 |
| **A08 Fallas de integridad de software y datos** | Webhooks firmados, validación de respuestas, auditoría de solo inserción. | RNF21, SEG-12, SEG-13, AUD-03 |
| **A09 Fallas de registro y alertas** | Eventos de seguridad, alertas por umbral, sin datos sensibles. | RNF20, AUD-10, SEG-08 a SEG-10 |
| **A10 Manejo inadecuado de condiciones excepcionales** | Fallo seguro, errores uniformes, tiempos de espera. | RNF20, SEG-03, SEG-04, SEG-14 |

## 2. OWASP API Security Top 10:2023

Véase la tabla de [API e integraciones](05-api-e-integraciones.md) (sección 2), que cubre API1 a API10.

## 3. OWASP Top 10 para aplicaciones con LLM (2025)

Véase la tabla de [seguridad del asistente](06-seguridad-del-asistente.md) (sección 3), que cubre LLM01 a LLM10 (LLM08 no aplica hoy).

## 4. ASVS 5.0

El nivel 1 completo y un subconjunto selectivo del nivel 2 se detallan en la [lista de controles](02-controles-asvs.md) (CTL-01 a CTL-38), con su estado.

## 5. ISO/IEC 27002:2022 (referencia, sin certificación)

Se toman como orientación los controles más relevantes para el alcance del proyecto. La norma agrupa 93 controles en cuatro temas (organizacionales, de personas, físicos y tecnológicos); aquí solo se citan los que el proyecto aborda.

| Control (ISO/IEC 27002:2022) | Cómo se aborda | Dónde |
| :--- | :--- | :--- |
| 5.15 Control de acceso | Roles y alcance por sucursal. | RNF05; [autenticación y autorización](03-autenticacion-y-autorizacion.md) |
| 5.17 Información de autenticación | Política de contraseñas, códigos de un solo uso. | RNF15, IDE-15 |
| 5.19–5.23 Proveedores y servicios en la nube | Inventario de proveedores y transferencias. | RC17; [privacidad](04-privacidad-y-datos-personales.md) |
| 5.24–5.28 Gestión de incidentes | Procedimiento de respuesta documentado. | [operación segura](07-operacion-segura.md) |
| 5.30 Preparación de las TIC para la continuidad | Respaldo, RPO y RTO, contingencia de despliegue. | RNF24, AD27 |
| 5.34 Privacidad y protección de datos personales | Inventario de datos, minimización, retención, ARCO. | RNF23; [privacidad](04-privacidad-y-datos-personales.md) |
| 8.2 Derechos de acceso privilegiado | Personal (incluido el administrador central) con segundo factor. | IDE-20 |
| 8.5 Autenticación segura | Bloqueo, recuperación, segundo factor. | RNF15 |
| 8.8 Gestión de vulnerabilidades técnicas | Escaneo de dependencias e imágenes. | RNF19 |
| 8.9 Gestión de la configuración | Compose versionado, endurecimiento. | [operación segura](07-operacion-segura.md) |
| 8.13 Respaldo de la información | Volcado diario cifrado y prueba de restauración. | RNF24, AD27 |
| 8.15–8.16 Registro y monitoreo | Eventos de seguridad y alertas. | RNF20 |
| 8.24 Uso de criptografía | TLS, resumen de contraseñas, cifrado de respaldos. | RNF18 |
| 8.25–8.29 Desarrollo seguro y pruebas de seguridad | SDD, requisitos de seguridad en specs, SAST y pruebas por criterio. | [flujo SDD](../07-proceso-de-desarrollo/01-flujo-sdd.md), RNF19 |
| 8.31 Separación de entornos | Desarrollo, pruebas y producción separados. | [operación segura](07-operacion-segura.md) |

## 6. ISO/IEC 25010:2023 (modelo de calidad)

Los atributos de calidad se clasifican por las características de este modelo en el documento de [atributos de calidad](../01-analisis-de-sistema/04-atributos-de-calidad.md). Seguridad se desglosa en AC05, AC09, AC11 y AC13.

## 7. Privacidad

Ley N.° 29733 y DS 016-2024-JUS: véase [privacidad](04-privacidad-y-datos-personales.md) y el [marco legal](../01-analisis-de-sistema/03-marco-legal.md). **PCI DSS 4.0.1:** la tarjeta se captura en el formulario de la pasarela y BabelHub solo recibe un identificador; el alcance probable es el de la declaración más simple (SAQ A) si la captura se aloja en la pasarela, o SAQ A-EP si el formulario se incrusta en la página. Esa distinción se define en el plan de `ecommerce-pagos` (pregunta pendiente con el proveedor).
