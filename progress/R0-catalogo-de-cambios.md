# R0 — Catálogo de cambios a la línea base

> **Estado: propuesta para aprobación.** Describe, con el texto exacto, qué se agregará o cambiará en `docs/` y en las specs por la incorporación de seguridad, privacidad, UX y calidad. **No se ha editado ningún documento de `docs/` ni ninguna spec.** Los identificadores existentes no se renumeran; todo lo nuevo se agrega al final de su serie.

## 0. Decisiones ya tomadas

| Tema | Decisión |
| :--- | :--- |
| MFA | Obligatoria (TOTP) para el administrador central. |
| Recuperación | Objetivos RPO ≤ 24 h y RTO ≤ 4 h. |
| Metas de UX | Éxito de tarea ≥ 90 %, SUS ≥ 68 y LCP ≤ 2,5 s. |
| Granularidad | 14 RNF agrupados (RNF15 a RNF28). |
| Orden | D1–D2 (mover y dividir) antes de R1 (editar el contenido). |
| Specs | Una nueva spec transversal de seguridad y ajustes acotados en 8 specs existentes. |

**Nota de nombres:** la spec nueva usa el prefijo `SEG`; por eso la lista de verificación de seguridad (basada en ASVS) usará `CTL-NN` y las amenazas `AM-NN`, para no chocar con los identificadores de la spec.

## 1. Necesidades y objetivos (`01`)

| ID | Actor | Necesidad | Prioridad | Justificación |
| :--- | :--- | :--- | :---: | :--- |
| **NN16** | Cliente | Usar la tienda con comodidad desde un celular con conexión variable y sin barreras de accesibilidad. | Alta | La demanda fuera de Lima llega desde el celular; la accesibilidad es parte de servir a todos. |
| **NN17** | Cliente | Descubrir lecturas con orientación y sorpresa: asistente, «Sorpréndeme» y El índice infinito. | Baja | Aporta identidad de marca; respalda NN04. |
| **NN18** | Organización | Proteger su información y su operación frente a abusos, fallos y pérdida de datos. | Alta | Se manejan datos personales, pagos y contenido con licencia. |

| ID | Objetivo | Justificación |
| :--- | :--- | :--- |
| **OB07** | Operar de forma segura y confiable. | Las fallas de seguridad destruyen la confianza que el producto necesita (OB01). |
| **OB08** | Ofrecer una experiencia accesible y usable para todas las personas usuarias. | Reduce abandono y cumple el derecho de acceso. |

**Alcance (`01` §3):** pasan a «dentro» la seguridad de la aplicación, la accesibilidad y la usabilidad medida. Siguen «fuera» la certificación ISO, las auditorías de terceros y las pruebas de penetración externas.

## 2. Actores (`01`)

| Actor | Rol | Qué se modela |
| :--- | :--- | :--- |
| **Actor malicioso** *(modelo)* | Persona o programa que abusa del sistema: acapara stock, adivina contraseñas, intenta descargar sin comprar o manipular el asistente. | Casos de abuso y amenazas (`05-seguridad`). No usa el sistema de forma legítima. |
| **Soporte** | Atiende las ayudas del cliente (límite de descargas, accesos). | Hoy lo cubre la administración central; se separa si crece. |

## 3. Requisitos funcionales nuevos

| ID | Requisito funcional | Prioridad | Origen | Spec |
| :--- | :--- | :---: | :--- | :--- |
| **RF26** | El sistema debe permitir al usuario recuperar el acceso a su cuenta mediante un código o enlace de un solo uso y vigencia corta, enviado a un medio de contacto verificado. | Alta | NN09, NN18 | `identidad-seguridad` |
| **RF27** | El sistema debe permitir al cliente gestionar su cuenta: datos personales, consentimientos y solicitudes de acceso, rectificación, cancelación y oposición. | Alta | NN15 | `identidad-seguridad` |
| **RF28** | El sistema debe limitar las reservas temporales activas por cliente y por origen de red, y rechazar con un mensaje claro las que excedan el límite. **Propuesta:** una reserva activa por cliente y hasta 5 reservas iniciadas por hora. | Alta | NN02, NN18 | `inventario-seleccion-fuente`, `ecommerce-carrito-checkout` |
| **RF29** | El sistema debe ofrecer funciones de descubrimiento separadas de la compra: elegir un libro disponible al azar y explorar páginas generadas, aclarando que no son libros reales. | Baja | NN17 | `web-base`, `web-indice-infinito` |

RF28 cubre un abuso propio del negocio: reservas temporales usadas para dejar sin stock a otros clientes.

## 4. Requisitos no funcionales nuevos

Cada uno agrega **método de verificación** y **spec**. Los valores marcados con *(propuesta)* se confirman al aprobar.

| ID | Categoría | Requisito | Meta | Verificación | Spec | Estándar |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| **RNF15** | Seguridad: autenticación y sesión | Las credenciales deben protegerse y la sesión controlarse. | Contraseña de 10 o más caracteres y fuera de listas comunes *(propuesta)*; bloqueo temporal tras 5 fallos en 15 minutos; cookie `HttpOnly`, `Secure` y `SameSite`; credencial de 15 minutos con renovación de 7 días; MFA TOTP para el administrador central. | Pruebas automatizadas e inspección | `identidad-seguridad`, `web-base` | ASVS 5.0 (autenticación, sesión), Top 10:2025 (fallas de autenticación) |
| **RNF16** | Seguridad: validación y salida | Toda entrada debe validarse y toda salida codificarse, con cabeceras de seguridad. | 0 hallazgos de inyección y XSS en las pruebas; CSP, HSTS, `X-Content-Type-Options` y `Referrer-Policy` presentes; la CSP admite el script de la pasarela. | SAST, ZAP baseline y prueba E2E de cabeceras | `transversal-seguridad`, `web-base` | ASVS, Top 10:2025 (inyección, configuración) |
| **RNF17** | Seguridad: límites de uso | El sistema debe limitar el uso por usuario y por origen en las operaciones sensibles. | Ingreso: 5 por 15 min; API: 100 solicitudes por minuto por usuario; descargas: 10 por hora; asistente: 10 consultas por minuto; respuesta uniforme con espera sugerida *(propuesta)*. | Pruebas de integración y carga | `transversal-seguridad`, `ecommerce-biblioteca-digital`, `asistente-compra-ia` | API Top 10:2023 (consumo y flujos de negocio), LLM10 |
| **RNF18** | Seguridad: criptografía y reposo | Los datos deben protegerse en tránsito y en reposo. | TLS 1.2 o superior hacia el exterior; volúmenes y respaldos cifrados; contraseñas con función de resumen robusta y sal. | Inspección y pruebas | `identidad-seguridad` | Top 10:2025 (fallas criptográficas) |
| **RNF19** | Seguridad: cadena de suministro y configuración | Las dependencias y las imágenes deben controlarse y los contenedores endurecerse. | 0 vulnerabilidades críticas o altas conocidas al liberar; versiones fijas; contenedores sin privilegios; SBOM generado en CI. | Escaneo en CI | Transversal (CI e infraestructura) | Top 10:2025 (A02, A03) |
| **RNF20** | Seguridad: registro, alertas y fallo seguro | Los eventos de seguridad deben registrarse con alertas mínimas, y ante errores el sistema debe negar y no filtrar. | Registro de ingresos fallidos, accesos denegados, límites excedidos y cambios de rol; alerta ante más de 10 accesos denegados o 20 ingresos fallidos en 5 minutos *(propuesta)*; mensajes genéricos; logs sin datos sensibles. | Pruebas e inspección | `transversal-seguridad`, `transversal-auditoria` | Top 10:2025 (A09, A10) |
| **RNF21** | Seguridad: integridad de integraciones | Las integraciones deben autenticarse y sus respuestas tratarse como no confiables. | Webhooks con secreto o firma verificada; respuestas validadas contra un esquema; tiempos de espera; credenciales de servicio a servicio. | Pruebas de integración | `transversal-seguridad`, `transversal-notificaciones` | Top 10:2025 (A08), API10 |
| **RNF22** | Seguridad: asistente de compra | El asistente debe ser de solo lectura y su salida tratarse como no confiable. | Sin herramientas de escritura; salida como texto sin HTML ejecutable; sin revelar instrucciones internas; datos personales mínimos; tope de consumo por consulta. | Pruebas con casos de abuso | `asistente-compra-ia` | Top 10 para LLM 2025 |
| **RNF23** | Privacidad | El tratamiento de datos personales debe ser mínimo, documentado y con retención definida. | Inventario de datos y base legal; plazos de retención *(propuesta: 2 años para auditoría y hojas de reclamación)*; procedimiento de incidentes; transferencias fuera del país documentadas. | Inspección documental | `identidad-seguridad`, `transversal-auditoria` | Ley 29733 y DS 016-2024-JUS |
| **RNF24** | Continuidad | Debe existir respaldo y recuperación probados. | RPO ≤ 24 h; RTO ≤ 4 h; respaldo diario fuera de la VM de datos; restauración probada antes de cada despliegue mayor. | Prueba de restauración | Despliegue | ISO 27002 (continuidad) |
| **RNF25** | Accesibilidad | La interfaz debe conformar WCAG 2.2 nivel AA. | 0 incumplimientos AA críticos en revisión automática; lista manual de teclado y lector de pantalla superada. | axe en E2E e inspección manual | `web-base` | WCAG 2.2 |
| **RNF26** | Rendimiento percibido | Las pantallas clave deben cargar rápido en móvil. | LCP ≤ 2,5 s (p75) en 4G simulada, INP ≤ 200 ms, CLS ≤ 0,1 en inicio, catálogo y detalle. | Lighthouse en CI | `web-base` | Core Web Vitals |
| **RNF27** | Usabilidad medible | La compra debe ser fácil de completar sin ayuda. | ≥ 90 % de personas de prueba completan una compra con recojo sin ayuda; SUS ≥ 68; menos de 1 error por tarea en promedio; rondas de 5 personas. | Prueba de usabilidad | `web-base` | ISO 9241-210 |
| **RNF28** | Evolucionabilidad | Los contratos deben evolucionar sin romper clientes y el diseño mantenerse verificable. | Contratos versionados en la ruta; reglas de dependencia verificadas automáticamente; cobertura de líneas del Dominio ≥ 90 % *(propuesta)*. | Pruebas de arquitectura y cobertura | Todas | Refina RNF09 y RNF10 |

## 5. Restricciones nuevas

| ID | Restricción | Descripción | Justificación |
| :--- | :--- | :--- | :--- |
| **RC14** | Navegadores y dispositivos | Se soportan las dos últimas versiones estables de Chrome, Edge, Firefox y Safari, y Chrome en Android y Safari en iOS recientes. | Define el alcance de las pruebas de compatibilidad. |
| **RC15** | Herramientas de seguridad gratuitas | Las pruebas y los escaneos usan herramientas gratuitas (CodeQL, Dependabot, ZAP, Trivy y detección de secretos). | Respeta el presupuesto (NN12). |
| **RC16** | Estándares de referencia | OWASP Top 10:2025, API Security Top 10:2023, Top 10 para LLM 2025, ASVS 5.0 (nivel 1 completo y nivel 2 selectivo), WCAG 2.2 AA e ISO/IEC 25010:2023 como modelo de calidad; ISO/IEC 27002:2022 y NIST SSDF como referencia. Sin certificación. | Da criterios objetivos sin el costo de una certificación. |
| **RC17** | Transferencias de datos | Los proveedores fuera del Perú (almacén de archivos, Cloudflare, WhatsApp y proveedor de LLM) se documentan con los datos que reciben y su base legal. | DS 016-2024-JUS y minimización. |

## 6. Atributos de calidad (`03`)

Se reorganizan por las características de ISO/IEC 25010:2023 *(nombres a confirmar contra el estándar)* y cada escenario se redacta con fuente, estímulo, entorno, artefacto, respuesta y medida.

| Característica | Atributos |
| :--- | :--- |
| Adecuación funcional | AC01 |
| Eficiencia de desempeño | AC02 |
| Compatibilidad | **AC14** (nuevo) |
| Capacidad de interacción | AC08, **AC10** (nuevo) |
| Fiabilidad | AC03, **AC12** (nuevo) |
| Seguridad | AC05, AC09, **AC11**, **AC13** (nuevos) |
| Mantenibilidad | AC06 |
| Flexibilidad | AC04, AC07 |

| ID | Atributo | Prioridad | Escenario | Medida | RNF |
| :--- | :--- | :---: | :--- | :--- | :--- |
| **AC10** | Accesibilidad | Alta | Una persona usa solo teclado y lector de pantalla para buscar un libro y completar una compra con recojo. | Todos los controles alcanzables con foco visible; 0 incumplimientos AA críticos. | RNF25 |
| **AC11** | Privacidad | Alta | Un cliente pide acceder a sus datos y luego cancelar su cuenta; una persona no autorizada intenta ver su biblioteca. | Solicitud registrada y atendida; 0 accesos ajenos; transferencias documentadas. | RNF23 |
| **AC12** | Recuperabilidad | Media | La VM de datos se pierde. Se restaura desde el último respaldo en un entorno limpio. | RPO ≤ 24 h; RTO ≤ 4 h. | RNF24 |
| **AC13** | Resistencia al abuso | Alta | Un programa intenta reservar todo el stock, adivinar contraseñas o consultar sin límite al asistente. | Límites aplicados y registrados; ningún cliente legítimo queda sin servicio. | RNF17, RF28 |
| **AC14** | Compatibilidad | Media | El catálogo y el checkout se usan en Chrome, Firefox, Safari y Edge recientes, en móvil y en escritorio. | Recorrido de compra exitoso en cada combinación soportada. | RC14 |

## 7. Drivers (`04`)

| ID | Driver | Tipo | Origen | Influye en | Decisiones |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **DA13** | Superficie pública con flujos de negocio abusables (reservas, descargas, asistente). | Calidad | RF28, RNF17, AC13 | Límites de uso en el borde y en la Aplicación; registro y alertas. | AD24, AD25 |
| **DA14** | Experiencia móvil accesible con conexión variable. | Calidad | NN16, RNF25, RNF26, AC10 | Interfaz móvil primero, presupuesto de rendimiento y pruebas de accesibilidad. | AD26, AD29 |
| **DA15** | Privacidad y transferencias de datos fuera del país. | Restricción legal | RNF23, RC17, AC11 | Minimización, retención y qué datos reciben los proveedores. | AD27 |
| **DA16** | Recuperación con recursos mínimos. | Calidad | RNF24, AC12 | Respaldos fuera del dominio de falla y pruebas de restauración. | AD27 |
| **DA17** | Contenido generado por un LLM no es confiable. | Calidad | RNF22 | Aislamiento del asistente, solo lectura y salida como texto. | AD28 |

Riesgos nuevos para `04`: acaparamiento de stock con reservas, fuerza bruta de contraseñas, inyección de prompt, fuga de datos hacia proveedores externos, dependencias vulnerables y pérdida de respaldos.

## 8. Decisiones nuevas (`05`)

| ID | Decisión | Justificación | Alternativa descartada | Drivers |
| :--- | :--- | :--- | :--- | :--- |
| **AD22** | **Autenticación y autorización:** Identidad emite credenciales firmadas de 15 minutos con renovación revocable de 7 días; MFA TOTP obligatoria para el administrador central; la autorización es por política (rol y alcance) evaluada en la Aplicación. | Vigencia corta limita el daño de un robo; el alcance se comprueba donde vive la regla. | Sesiones de servidor propias en cada servicio; un proveedor de identidad externo (costo y complejidad). | DA05, DA13 |
| **AD23** | **Concurrencia e idempotencia:** las operaciones de stock usan transacciones con control de concurrencia en la base de datos (la técnica exacta se fija en el plan de `inventario-seleccion-fuente`); las operaciones con efectos externos usan una clave de idempotencia persistida. | Garantiza AC01 sin un segundo punto de falla. | Bloqueos distribuidos en Redis. | DA01 |
| **AD24** | **Límites de uso en dos niveles:** reglas de tasa en el borde (Cloudflare y Nginx) y límites de negocio en la Aplicación con contadores (por ejemplo, RF28). | El borde frena el ruido; la Aplicación conoce el negocio. | Solo límites en el borde. | DA13 |
| **AD25** | **Modelo de errores y fallo seguro:** `problem+json` con `code` estable y `traceId`; ante una excepción inesperada se niega y se devuelve un error genérico; las dependencias usan tiempos de espera y cortacircuitos. | Evita fugas y estados a medias. | Mensajes de error detallados. | DA03, DA13 |
| **AD26** | **Cabeceras y política de contenido:** la Web aplica una CSP estricta (con el script de la pasarela en lista permitida), HSTS y las demás cabeceras; los scripts de terceros se limitan. | Reduce el riesgo de XSS y de scripts hostiles en la página de pago. | Sin CSP o con una CSP permisiva. | DA05, DA14 |
| **AD27** | **Respaldo y retención:** volcado diario cifrado de las bases hacia el almacén de objetos, con retención escalonada, prueba de restauración y plazos de retención de datos personales. | Los respaldos salen del dominio de falla de la VM de datos. | Solo instantáneas del volumen. | DA15, DA16 |
| **AD28** | **Seguridad del asistente:** servicio sin acceso a bases de datos, con credencial de solo lectura, salida tratada como texto, instrucciones sin secretos y topes de consumo. | Limita lo que un modelo comprometido o manipulado puede hacer. | Un agente con herramientas de escritura. | DA17 |
| **AD29** | **UX y accesibilidad como requisito verificable:** móvil primero, WCAG 2.2 AA comprobado con axe en las pruebas E2E, presupuesto de rendimiento y una evaluación de usabilidad por iteración. | Hace medible la experiencia en lugar de revisarla solo al final. | Revisión manual al final del proyecto. | DA14 |

## 9. Marco legal (`02` §5)

Se agrega una fila para el **DS 016-2024-JUS** (reglamento de la Ley N.° 29733, vigente desde el 31 de marzo de 2025 según las fuentes consultadas): registro del banco de datos personales, notificación de incidentes de seguridad, transferencias internacionales y, en ciertos casos, oficial de datos personales. Estado: a validar con una persona experta y aplicado con rigor laxo por ser un proyecto académico. Implicancia: la biblioteca y el historial de compra son datos personales; los proveedores fuera del país reciben solo los datos mínimos (RC17).

## 10. Impacto en las specs

**La respuesta a «¿solo se agrega una spec?»: no.** Se agrega una y se ajustan 8. Todas están en `Draft`, así que los ajustes no requieren reaprobación; `SpecCheck` valida cada cambio.

**Por qué no basta una sola spec:** una spec transversal puede describir *cómo se protege todo el sistema*, pero un comportamiento propio de una feature debe vivir en su feature. Por ejemplo, «recuperar la contraseña» pertenece a Identidad y «una reserva activa por cliente» pertenece a las reservas. Si solo existiera la spec transversal, `identidad-seguridad` quedaría incompleta (hoy no tiene recuperación), cada plan tendría que leer dos specs y RF26 a RF28 no tendrían una spec que los cubra, y `SpecCheck` fallaría.

| Spec | Cambio | Detalle |
| :--- | :--- | :--- |
| **`transversal-seguridad`** (nueva, prefijo `SEG`) | Nueva | Límites de uso y respuesta uniforme, cabeceras, validación con error uniforme, registro y alertas de seguridad, fallo seguro, integraciones autenticadas y secretos fuera de los registros. Cubre RNF16, RNF17, RNF20 y RNF21. |
| `identidad-seguridad` | Ajuste | Recuperar contraseña (RF26), gestión de cuenta (RF27), política de contraseñas y bloqueo (RNF15, respondiendo sus preguntas abiertas), MFA del administrador central. |
| `inventario-seleccion-fuente` | Ajuste | Límite de reservas activas (RF28). |
| `ecommerce-carrito-checkout` | Ajuste | Mensaje y comportamiento ante el límite de reservas (RF28). |
| `ecommerce-biblioteca-digital` | Ajuste | Límite de descargas por hora y por origen (RNF17). |
| `asistente-compra-ia` | Ajuste | Controles del asistente: solo lectura, salida como texto, sin revelar instrucciones y tope por consulta (RNF22). |
| `transversal-auditoria` | Ajuste | Eventos de seguridad y plazo de retención (RNF20, RNF23). |
| `web-base` | Ajuste | Accesibilidad AA, presupuesto de rendimiento, usabilidad y cabeceras en la Web (RNF16, RNF25, RNF26, RNF27). |
| `web-indice-infinito` | Ajuste menor | Su origen pasa a NN17 y RF29 en lugar de RC01 y NN15. |
| Las otras 10 | Sin cambios | `inventario-fuentes`, `inventario-ajustes`, `inventario-traslados`, `ecommerce-catalogo`, `ecommerce-pagos`, `ecommerce-pedidos`, `ecommerce-envios`, `ecommerce-devoluciones`, `ecommerce-libro-reclamaciones` y `transversal-notificaciones`. |

## 11. Preguntas abiertas para aprobar

1. **Canal de recuperación de contraseña (RF26):** hoy el único canal de avisos es WhatsApp y el número es opcional. Opciones: (a) código por WhatsApp y número obligatorio para poder recuperar; (b) correo con un proveedor gratuito de envío. **Recomendación: (a)**, porque reutiliza Evolution API y no agrega un proveedor; el número se vuelve obligatorio al registrarse.
2. **Valores numéricos** marcados como *propuesta* (límites de tasa, reservas por hora, longitud de contraseña, umbrales de alerta, retención de 2 años): ¿te sirven como metas iniciales?
3. **Cobertura del Dominio ≥ 90 % (RNF28):** ¿la mantienes como meta o prefieres otro umbral?

## 12. Cómo se aplica una vez aprobado

1. **D1–D2:** mover y dividir los documentos al esquema de 7 carpetas.
2. **R1:** aplicar las secciones 1 a 9 en sus nuevos archivos; actualizar la lectura de identificadores de `SpecCheck`.
3. **R2:** aplicar la sección 10 en las specs; ejecutar `SpecCheck` y el agente `spec-auditor` sobre las afectadas.
4. Cada paso en commits pequeños y sin `push` hasta que lo pidas.
