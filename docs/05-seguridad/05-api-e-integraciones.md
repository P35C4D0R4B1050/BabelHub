# Seguridad de las API y de las integraciones

Cómo se protegen las interfaces entre servicios, la que expone la Web y las que conectan con terceros. Se apoya en el **OWASP API Security Top 10:2023** y en RNF16, RNF17, RNF21 y AD24 a AD26.

## 1. Principios

1. **Cada servicio protege su propia puerta.** Que otro servicio ya haya validado algo no exime de volver a validarlo.
2. **Nada que venga de fuera es confiable:** ni la entrada del usuario, ni la respuesta de un tercero, ni un aviso entrante.
3. **El contrato es estrecho.** Los esquemas aceptan solo los campos esperados; el resto se rechaza.
4. **Por defecto se niega.** Un error o una dependencia caída nunca abren un acceso (SEG-04).
5. **Los límites existen en dos niveles:** el borde frena el ruido y la Aplicación aplica reglas de negocio (AD24).

## 2. Riesgos de API aplicados a BabelHub

| Riesgo (API Top 10:2023) | Dónde aplica | Control |
| :--- | :--- | :--- |
| **API1 Autorización de objeto** | Pedidos, biblioteca, cuenta, inventario por sucursal | Propiedad y alcance comprobados en la Aplicación con la identidad de la credencial, no con identificadores recibidos (CTL-24). |
| **API2 Autenticación rota** | Ingreso, recuperación, segundo factor | Bloqueo temporal, respuesta uniforme, códigos de un solo uso (IDE-03, IDE-17 a IDE-19). |
| **API3 Autorización de propiedades** | Respuestas con datos de usuarios o de costos | DTOs específicos por rol; no se devuelven entidades de dominio; asignación masiva prohibida (CTL-11). |
| **API4 Consumo de recursos sin límite** | Todas las rutas, búsquedas, asistente | 100 solicitudes por minuto por usuario; paginación con tope; tamaño máximo de cuerpo y de consulta (SEG-05, ASI-14). |
| **API5 Autorización de función** | Funciones administrativas | Rol en la credencial y segundo factor del personal (IDE-20). |
| **API6 Flujos de negocio sensibles sin protección** | Reservas, descargas, recuperación | Una reserva activa por cliente y 5 por hora; 10 solicitudes de descarga por hora; límites en la recuperación (SEL-13, SEL-14, BIB-15, IDE-19). |
| **API7 Falsificación de solicitudes del servidor** | Cualquier llamada que reciba una URL | El sistema no consulta direcciones indicadas por el usuario. Si se agregara, se permite solo una lista de destinos. |
| **API8 Configuración incorrecta** | Cabeceras, CORS, errores, puertos | Cabeceras de seguridad (WEB-18), errores uniformes (SEG-03), bases sin puertos publicados (RNF06). |
| **API9 Gestión inadecuada del inventario de API** | Versiones y rutas | Rutas versionadas (`/api/<servicio>/v1/...`); cada servicio publica su contrato; las versiones retiradas se apagan, no se dejan activas. |
| **API10 Consumo inseguro de API de terceros** | Culqi, WhatsApp, almacén, proveedor de LLM | Tiempos de espera, validación de formato, tratamiento de la respuesta como no confiable (SEG-13, SEG-14). |

## 3. Reglas para las rutas

| Tema | Regla |
| :--- | :--- |
| Autenticación | Toda ruta, salvo catálogo público, Libro de Reclamaciones, ingreso, registro y recuperación, exige credencial válida. |
| CORS | Los servicios no se exponen al navegador; la Web llama desde el servidor. No se habilita CORS abierto. |
| Contenido | Solo `application/json` en las API; la Web usa formularios con protección CSRF. |
| Tamaño | Límite de cuerpo por ruta; las portadas se validan por tipo y tamaño (CTL-14). |
| Paginación | Tope máximo de elementos por página; ordenamiento sobre campos permitidos. |
| Errores | `application/problem+json` con `code` estable e identificador de seguimiento; nunca trazas (SEG-03). |
| Idempotencia | Las operaciones con efectos externos (cobros, reservas, reembolsos) llevan clave de idempotencia persistida (AD23). |
| Registro | Se registra el identificador de la solicitud, la ruta, el usuario y el resultado; no se registran cuerpos con datos sensibles (SEG-10). |

## 4. Límites de uso (RNF17, AD24)

| Operación | Límite | Nivel | Spec |
| :--- | :--- | :--- | :--- |
| API en general | 100 solicitudes por minuto por usuario | Borde y Aplicación | SEG-05 |
| Ingreso | 5 fallos en 15 minutos por combinación de cuenta y origen; bloqueo de 15 minutos | Aplicación | SEG-06 |
| Reservas | 1 temporal pendiente y 5 por hora por cliente; 10 por hora por origen | Aplicación | SEL-13, SEL-14, SEL-19 |
| Descargas | 10 solicitudes de enlace por hora | Aplicación | BIB-15 |
| Asistente | 10 consultas por minuto | Aplicación | ASI-09 |
| Códigos de un solo uso | 3 solicitudes por hora por correo y por origen; 5 intentos por código | Aplicación | SEG-19, IDE-19 |
| Segundo factor | 5 códigos incorrectos en 15 minutos bloquean 15 minutos | Aplicación | SEG-20 |
| Libro de Reclamaciones | 3 hojas por hora por origen | Aplicación | REC-11 |
| Registro de cuentas | 3 cuentas nuevas por hora por origen | Aplicación | SEG-24 |
| Origen no determinable | Un «origen desconocido» único con los mismos límites por origen | Aplicación | SEG-23 |
| Caída del almacén de contadores | Ingreso, registro, códigos, reservas y descargas se rechazan; las demás operaciones usan un límite local | Aplicación | SEG-26 |

**Respuesta uniforme:** al exceder un límite la respuesta tiene siempre la misma forma, incluye una espera sugerida y no revela información de cuentas. Cada exceso se registra como evento de seguridad (SEG-08).

## 5. Integraciones con terceros

| Integración | Dirección | Autenticación | Validación | Falla |
| :--- | :--- | :--- | :--- | :--- |
| **Culqi (pago)** | Saliente: crear cargo y reembolso. Entrante: aviso de estado. | Llave privada en el entorno; el aviso entrante se verifica por firma o por consulta posterior. | El importe lo calcula el servidor; la respuesta se valida contra el formato esperado. | El pago queda pendiente de confirmación y se consulta más tarde (PAG-07). |
| **n8n y Evolution API (WhatsApp)** | Saliente: enviar mensaje. Entrante: estado de entrega. | Secreto compartido; paneles no expuestos a Internet. | Se ignoran mensajes sin firma o fuera de formato. | Reintentos con esperas crecientes; nunca interrumpe un caso de uso (NOT-04). |
| **Almacén de archivos** | Saliente: generar URL firmada; entrante: ninguna (el navegador descarga directo). | Llave del almacén en el entorno; permisos solo de lectura para firmar. | La URL firmada caduca en 5 minutos como máximo. | Se informa el problema, no se consume una descarga (BIB-13). |
| **Servicio de envío** | Saliente: crear guía y consultar seguimiento. | Llave en el entorno. | Formato de respuesta validado. | El envío queda pendiente y se reintenta. |
| **Proveedor de correo** | Saliente: enviar el código de un solo uso o la respuesta a una hoja de reclamación. | Llave en el entorno; dominio con SPF, DKIM y DMARC. | Se valida el estado de entrega; el mensaje de seguridad solo lleva el código. | Código: la persona reintenta, sin cola ni almacenamiento fuera de Identidad (AD30). Respuesta a un reclamo: queda guardada y la administración reintenta (REC-13). |
| **Proveedor de LLM** | Saliente: consulta. | Llave en el entorno. | La respuesta se trata como texto no confiable (RNF22). | El asistente informa que no está disponible (ASI-07). |

## 6. Webhooks entrantes

Un aviso entrante debe: (1) venir de una ruta que no se anuncie públicamente, (2) llevar un secreto o una firma que se verifique en tiempo constante, (3) tener un formato validado, (4) ser idempotente (repetirlo no duplica el efecto) y (5) registrarse. Si falla cualquier paso, se rechaza y se registra (SEG-12). Un aviso de pago nunca basta por sí solo para entregar un libro digital: el estado se confirma con la pasarela.
