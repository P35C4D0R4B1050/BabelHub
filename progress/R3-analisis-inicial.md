# R3 — Análisis inicial: revisión de auditoría y alineación de specs y docs

> Fecha: 2026-10-16. Alcance: lectura de las 19 specs (todas en `Draft`) y de los documentos de `docs/` (01, 02, 03, 05, 06 y 07 en lo relevante; 04 está reservada y vacía). `prototype/ui-v2/` se trata solo como referencia de UI/UX (AGENTS.md §1), no como código ni como requisito.
>
> **Límites de esta revisión.** No se ejecutó `SpecCheck` (la ejecución de `dotnet` no se autorizó en esta sesión), así que no se verificaron enlaces, EARS ni trazabilidad automática. No se modificó ninguna spec: los hallazgos que cambian comportamiento necesitan decisión de la persona responsable. Las menciones a normas son orientativas y están marcadas **(verificar)** cuando no pude confirmar el texto vigente.

## 1. Veredicto

La línea base está **bien alineada en lo grueso**: cada RF/RNF tiene spec dueña, los identificadores citados existen y las decisiones AD22–AD30 se reflejan en IDE, SEG y WEB. Los problemas no están en las piezas sueltas sino en **las costuras entre specs** (reservas ↔ traslados ↔ entrega, avisos ↔ reclamos, alertas ↔ servicios separados) y en **huecos legales y de privacidad** que el marco legal actual no cubre. Ninguna spec debería aprobarse aún sin resolver los puntos 🔴.

## 2. `transversal-auditoria` (AUD)

| # | Hallazgo | Gravedad | Propuesta |
| :-: | :--- | :---: | :--- |
| A1 | **Pregunta abierta sin resolver**: dónde se compone la consulta unificada. «Capa web» choca con la regla de que `BabelHub.Web` no tiene reglas de negocio, y AUD-09 (orden cronológico inverso con paginación) sobre tres almacenes exige una mezcla paginada por cursor (fecha + secuencia). | 🔴 | Decidir: la Web hace el *fan-out* y la mezcla por cursor (sin lógica de negocio), y cada servicio expone `GET /auditoria` con filtros y cursor. Registrarlo en AUD §5. |
| A2 | `SEG-09` cuenta accesos denegados «en el sistema», pero AUD §5 dice que cada servicio guarda lo suyo. Nadie es dueño del conteo agregado ni de la lista de alertas (AUD-13). | 🔴 | Definir si la alerta se evalúa por servicio (umbral local) o si un servicio la centraliza. Recomendado: por servicio, y la Web las compone igual que la auditoría. |
| A3 | AUD-05 registra el acceso denegado y AUD-10 lista los eventos de seguridad, pero **AUD-10 no incluye «acceso denegado»**, que es justo lo que cuenta SEG-09/RNF20. No queda claro si es auditoría, evento de seguridad o ambos. | 🟠 | Incluirlo en AUD-10 y aclarar que es un solo registro etiquetado (como ya hace §6 con el cambio de rol). |
| A4 | AUD-02 lista ~14 eventos en una sola frase (requisito compuesto) y AC-AUD-01 solo prueba el precio. | 🟠 | Dividir por familia (stock, pedidos, identidad, datos personales) o declarar que cada spec dueña audita su evento y AUD solo exige el contrato. |
| A5 | **Eventos que faltan** frente a RNF08 («movimientos de stock trazables»): entrega de un pedido (baja de físico), reserva confirmada/liberada, alta/edición de fuentes, alta/baja de obras y licencias (CAT-13 no audita), activación del consentimiento, consulta de hojas de reclamación o de solicitudes ARCO por el administrador. | 🟠 | Ampliar AUD-02 o agregarlos en cada spec. Auditar también las consultas de datos personales por personal, no solo la biblioteca (BIB-12). |
| A6 | AUD-08: «historial de ajustes y traslados de su sucursal» es ambiguo para un traslado (origen y destino son sucursales distintas). | 🟡 | Precisar: el encargado ve los traslados donde su sucursal es origen **o** destino. |
| A7 | «Número de secuencia» y orden entre servicios dependen de relojes distintos; solo hay orden total dentro de un servicio. | 🟡 | Documentarlo como limitación; ordenar entre servicios por fecha UTC y desempatar por servicio + secuencia. |
| A8 | Cosmético: AUD-13 aparece antes que AUD-12; AC-AUD-10 está al final. | 🟡 | Reordenar al editar. |
| A9 | Inmutabilidad solo por permisos de base de datos (sin hash encadenado). Es coherente con el alcance académico y con AM19 (riesgo bajo) pero queda como supuesto. | 🟡 | Dejarlo explícito como riesgo aceptado. |

**Decisión que necesito de ti para cerrar AUD:** A1 (y por arrastre A2). El resto son ajustes de redacción que puedo aplicar con tu visto bueno.

## 3. Incoherencias entre specs (alineación)

| # | Hallazgo | Specs | Gravedad |
| :-: | :--- | :--- | :---: |
| C1 | **Reserva y traslado no encajan.** La reserva confirmada vive en el stock de la fuente de origen y reduce su *vendible*. Para despachar el traslado de ese mismo pedido, TRA-06 exige vendible suficiente en el origen, que puede ser 0 por esa misma reserva; TRA-07 baja el físico del origen sin liberar la reserva; y SEL-11 descuenta «de la reserva confirmada» cuando la unidad ya está en el destino. Además, si el origen **rechaza** el traslado (TRA-05) de un pedido ya pagado no hay reasignación ni reembolso definidos. | SEL, TRA, PED | 🔴 |
| C2 | **El físico no baja en entregas a domicilio.** SEL-11 y PED-11 solo cubren el recojo; ENV-07 marca el pedido entregado sin pedir el descuento al Inventario. La reserva confirmada queda abierta para siempre. (SEL §7 lo deja como pregunta abierta, pero es una fuga de stock.) | SEL, PED, ENV | 🔴 |
| C3 | **Pedido de recojo que nadie recoge**: la reserva confirmada reduce el vendible indefinidamente (PED §7 lo difiere). | PED, SEL | 🟠 |
| C4 | **Respuesta a un reclamo**: REC-07 avisa «por el medio de contacto indicado» (correo o teléfono), pero NOT-02 lo envía por WhatsApp, NOT-03 exige consentimiento y un visitante sin cuenta no tiene consentimiento registrado. NOT deja el correo fuera de alcance y RC18 limita el correo a códigos de seguridad. | REC, NOT, RC18 | 🔴 |
| C5 | **CAR-09 pide informar el traslado y su plazo antes de pagar**, pero la selección de fuente solo ocurre al reservar (SEL-02) y no existe un requisito de «consultar la selección sin reservar» ni de dónde sale el plazo estimado (no hay tabla de cercanía ni plazos entre sucursales). | CAR, SEL, FUE | 🟠 |
| C6 | La matriz de actores da al **administrador central** «Ajustar inventario ●», pero AJU solo contempla al encargado (AJU-09) y FUE-11/AC-FUE-09 le prohíben modificar stock desde esa vista. | 05-actores, AJU, FUE | 🟠 |
| C7 | Un cliente no puede cancelar su propio pedido confirmado (PED-13 es solo del administrador) y no hay requisito de cancelación por el cliente antes de la preparación. | PED, DEV | 🟡 |
| C8 | Secuencia reembolso/reingreso en devoluciones de impresos: DEV-07 reingresa tras recibir, DEV-08 reembolsa «al aprobar»; no se define si el reembolso espera la recepción. | DEV | 🟡 |
| C9 | PAG-07 deja el pago «pendiente de confirmación» pero la reserva sigue expirando a los 15 minutos; solo PAG §6 cubre el reembolso automático, sin requisito ni criterio. | PAG, SEL | 🟡 |
| C10 | Un `modelo-de-datos` usa `eventos_limite` (§2) y luego `contadores_limite` (§5) para lo mismo; `drivers` dice «AD01–AD29» aunque existe AD30; `01-flujo-sdd` dice «18 features» y «19 specs» en distintos lugares; `05-despliegue` propone Ubuntu mientras la propuesta base dice Debian. | docs | 🟡 |

## 4. Vacíos de seguridad

| # | Vacío | Gravedad |
| :-: | :--- | :---: |
| S1 | **Identidad delegada del asistente.** ASI-04/ASI-05 asumen que el asistente solo ve los pedidos del cliente, pero la credencial de servicio es de solo lectura general (IDE-30, `03-autenticacion`). Si el filtrado por cliente depende del modelo, es un *confused deputy*. El control debe estar en la API de Pedidos (identidad del cliente delegada), no en el LLM. | 🔴 |
| S2 | **Registro masivo de cuentas.** IDE-29 permite comprar y reservar con correo sin verificar y no hay límite de registros por origen (SEG-19 solo limita códigos). Debilita AM01: cuentas desechables esquivan «una reserva pendiente por cliente». | 🟠 |
| S3 | **Estado de sesión de la Web.** La credencial vive solo en el servidor y el navegador tiene una cookie opaca, pero la Web no tiene base de datos ni se le asigna Redis; no se define dónde se guarda la sesión ni la renovación. | 🟠 |
| S4 | **Libro de Reclamaciones público + «nunca se elimina» (REC-06).** Con solo el límite general de visitantes (60/min), un script puede llenar la base de hojas imposibles de borrar y con datos personales de terceros ajenos. | 🟠 |
| S5 | **Subida de archivos digitales por el administrador**: CTL-14 valida solo portadas. No hay requisito sobre tipo, tamaño, integridad (hash) ni análisis de malware de los EPUB/PDF, ni por dónde pasa la subida (BIB-14 solo cubre la descarga). | 🟠 |
| S6 | Segundo factor solo para el administrador, mientras el encargado ajusta stock, aprueba traslados y ve pedidos con datos personales. Está documentado como mejora; conviene decidirlo explícitamente. | 🟡 |
| S7 | Política de contraseñas con reglas de composición (mayúscula, número, símbolo): la guía vigente (NIST SP 800-63B) las desaconseja a favor de longitud + lista de claves filtradas **(verificar)**. | 🟡 |
| S8 | Distribución de la clave pública para validar JWT sin consultar a Identidad (IDE-10) y rotación de la clave (CTL-29) sin definir. | 🟡 |
| S9 | Los límites por ventana deslizante a 100/min por usuario (SEG-05) no dicen dónde viven los contadores; el modelo de datos solo prevé tablas en PostgreSQL, costoso en 1 OCPU. | 🟡 |
| S10 | El canal de alertas fuera del panel sigue abierto (SEG §7 y `07-operacion-segura` §8) y, sin él, AUD-12 («alerta técnica») no tiene a quién llegar. | 🟡 |

## 5. Vacíos legales y de privacidad

> No es asesoría legal; el proyecto es académico. Lo marcado **(verificar)** no pude confirmarlo contra el texto vigente.

| # | Vacío | Gravedad |
| :-: | :--- | :---: |
| L1 | **El marco legal no menciona el Código de Protección y Defensa del Consumidor (Ley 29571)**, base de la información al consumidor, de los reclamos y de las devoluciones. Conviene añadirlo y revisar con él la política de devoluciones y de reembolso digital (incluida la pérdida de la biblioteca al cancelar la cuenta, IDE §6). | 🟠 |
| L2 | **Campos de la hoja de reclamación** (REC-03) frente a lo que exige el reglamento (p. ej., documento de identidad, domicilio, monto reclamado) **(verificar)**, que choca con la regla de minimización de `04-privacidad` («no se pide documento de identidad»). | 🟠 |
| L3 | **Plazo de respuesta del reclamo**: la spec propone 15 días; el máximo legal que conozco es 30 días calendario **(verificar)**. 15 queda dentro, pero hay que documentar de dónde sale. | 🟡 |
| L4 | **Plazos ARCO**: la propuesta de 20 días hábiles para todo; el reglamento anterior daba plazos menores para rectificación, cancelación y oposición **(verificar contra el DS 016-2024-JUS)**. | 🟠 |
| L5 | **Prueba del consentimiento**: CAR-12 exige aceptar términos y tratamiento de datos, pero no se guarda *qué versión* del texto ni cuándo (solo se guarda el consentimiento de WhatsApp, IDE-11). Tampoco existe una spec dueña del contenido y versionado de términos, privacidad y política de reembolso (WEB-08 solo enlaza). | 🟠 |
| L6 | **Dirección IP / origen** se guarda en reservas, eventos de ingreso y de seguridad (2 años) pero no figura en el inventario de datos personales de `04-privacidad`. | 🟠 |
| L7 | **Notificación de incidentes**: el procedimiento no fija el plazo de aviso a la autoridad; el reglamento nuevo lo establece **(verificar)**. | 🟡 |
| L8 | Sin tratamiento de **menores de edad** en el registro (edad mínima o consentimiento del representante) **(verificar)**. | 🟡 |
| L9 | Sin política de **cookies** (la de sesión es necesaria, pero las páginas legales deberían decirlo). | 🟡 |
| L10 | Las **sucursales físicas** tienen su propia obligación de libro de reclamaciones; el docs solo cubre el virtual **(verificar)**. Fuera de alcance del software, pero conviene dejarlo escrito. | 🟡 |
| L11 | Evolution API es un canal no oficial de WhatsApp: riesgo contractual con el proveedor (ya registrado en DA11); no hay plan de salida más allá del puerto `Notificador`. | 🟡 |

## 6. Vacíos de diseño y proceso

| # | Vacío | Gravedad |
| :-: | :--- | :---: |
| D1 | **`docs/04-modelo-c4` está vacía**, pero la propuesta base declara C4 + IEEE 1016 como marco (RC11). Es un entregable del curso pendiente. | 🟠 |
| D2 | **Falta el dato de cercanía y plazos entre sucursales** (usado por SEL-02 y CAR-09); sin él, «la más cercana» no es implementable. | 🟠 |
| D3 | El cálculo del costo de despacho (fijo S/ 12 en el prototipo o por zona) sigue abierto y afecta CAR y PAG. | 🟡 |
| D4 | Hay unas 35 preguntas abiertas repartidas en las specs (la mayoría son confirmaciones de valores propuestos). Una spec con preguntas abiertas no puede pasar a `Approved` (SpecCheck). | 🟡 |
| D5 | Bloqueos externos que frenan planes: B02 (almacén), B05 (Culqi), B06 (LLM), B08 (correo). B03/B04/B07 solo frenan el despliegue. | — |

## 7. Qué está bien (para no rehacerlo)

- Trazabilidad NN → RF/RNF → AC → DA → AD → spec consistente; ninguna spec cita un identificador inexistente en lo que leí.
- Reglas de Clean Architecture y de dependencias bien reflejadas en `docs/03` y verificables por la prueba de arquitectura.
- Concurrencia del stock bien pensada (CHECK en base, actualización condicional, idempotencia, orden de bloqueos).
- La seguridad ya cubre los riesgos principales con trazabilidad (AM01–AM24, CTL-01–38).
- Los fallos de dependencias externas (pasarela, notificador, correo, LLM) tienen comportamiento definido.

## 7.1 Bloque núcleo resuelto (2026-10-16)

| Hallazgo | Decisión | Dónde quedó |
| :-- | :-- | :-- |
| C1 | El despacho de un traslado vinculado a un pedido consume la reserva confirmada del origen; al recibir, el destino la recrea para ese pedido. El origen no puede rechazarlo; solo se cancela al anular el pedido (nuevo estado `cancelado`). El traslado nace al confirmarse el pago, no al reservar. | SEL-03; TRA-03, 05 a 07, 14 a 17; AC-TRA-12 a 16; dominio y estados |
| C2 | En domicilio el físico baja cuando el envío pasa a en reparto; si el inventario falla, el envío no avanza. | SEL-21; ENV-11, ENV-12; AC-SEL-21, AC-ENV-10, 11 |
| C3 | Pedido de recojo con más de 15 días calendario en listo para recojo pasa a no recogido, libera sus reservas y queda a revisión; la administración puede anularlo con reembolso. | PED-15, PED-16, SEL-07, NOT-02; AC-PED-13, 14, AC-SEL-22 |
| C5 | Consulta previa de selección sin reservar, con traslado y plazo; si no hay plazo, «a confirmar». | SEL-20; CAR-09; AC-SEL-20, AC-CAR-08 |
| D2 | Tabla `rutas` direccional (origen, destino, días) mantenida por la administración; ordena las fuentes por cercanía. En domicilio se prioriza la región de la dirección. | FUE-12 a FUE-14; SEL-02; AC-FUE-10, 11, AC-SEL-23 |

**Nuevas preguntas abiertas** (no estaban en el informe): fuente para direcciones en una región sin sucursal y desempate entre rutas iguales (SEL §7); envío por fuente en pedidos a domicilio multi-fuente (SEL y ENV §7); conteo de los 15 días con o sin feriados (PED §7). Las specs afectadas siguen en `Draft`.

## 7.2 Bloque legal/UX resuelto (2026-10-16)

| Hallazgo | Decisión | Dónde quedó |
| :-- | :-- | :-- |
| C4 | La respuesta a una hoja sale por correo (contacto obligatorio); si el envío falla, la respuesta queda guardada y la administración reintenta. Se quita del Notificador (WhatsApp) y se amplía RC18 y AD30. | REC-07, REC-13; NOT-02; RC18; AD30; AC-REC-06, 11 |
| L1 | Se agrega el Código del Consumidor (Ley 29571) al marco legal, a validar con una persona experta. | `03-marco-legal` |
| L2 | La hoja pide nombre, documento, domicilio, correo, monto y detalle, como excepción documentada a la minimización. Queda pregunta para verificar los campos contra el formato oficial. | REC-03; `04-privacidad`; AC-REC-02 |
| L3 | Plazo informado de 15 días calendario, dentro del máximo legal (30 días, a verificar), impreso en la constancia. | REC-05; AC-REC-04 |
| L4 | ARCO: 20 días hábiles para acceso y 10 para el resto, con fecha límite y lista ordenada con vencidas marcadas. | IDE-12, IDE-32; AC-IDE-11, 29; `04-privacidad` |
| S4 | Límite de 3 hojas por hora por origen y descarte de spam sin borrado; los datos se anonimizan al vencer la retención. | REC-11, REC-12; AC-REC-09, 10; `05-api-e-integraciones` |

**Nuevas preguntas abiertas:** confirmar los campos de la hoja contra el formato oficial (REC §7) y qué es un día hábil (IDE §7). Quedan pendientes de este bloque, por no haber sido tratados: L5 a L11 (prueba de consentimiento de términos, IP como dato personal, notificación de incidentes, menores, cookies, libro físico y canal no oficial de WhatsApp).

## 7.3 Bloque transversal resuelto (2026-10-16)

| Hallazgo | Decisión | Dónde quedó |
| :-- | :-- | :-- |
| A1 | La Web mezcla por fecha las páginas que cada servicio entrega con los mismos filtros; si un servicio no responde, muestra el resto con aviso. | AUD-09, AUD-17, AUD-18; AC-AUD-15, 16 |
| A2 | Cada servicio evalúa su umbral y genera su alerta; la Web las presenta juntas. Riesgo aceptado: ataque repartido bajo cada umbral. | SEG-09, SEG §6; AUD-13, §5 |
| A3 | El acceso denegado es un único evento de seguridad (no un registro doble). | AUD-05, AUD-10; AC-AUD-04 |
| A4 | AUD-02 dividido por familias (negocio, stock, catálogo y fuentes, identidad y datos personales). | AUD-02, AUD-14 a AUD-16 |
| A5 | Se auditan movimientos de stock, catálogo y licencias, fuentes y rutas, y consultas de datos personales por la administración. | AUD-14 a AUD-16; AC-AUD-11, 13, 14 |
| A6, A7, A9 | Traslados del historial del encargado: origen o destino; orden entre servicios por fecha UTC; inmutabilidad por permisos como riesgo aceptado. | AUD-08, AUD §6 |
| S10 | Alertas de seguridad y técnicas llegan también por correo a los administradores activos, agrupadas cada 15 minutos. | SEG-09, AUD-12; RC18; AD30 |

**Pendiente de este bloque:** el cosmético A8 quedó resuelto al reordenar AUD-12 y AUD-13. AUD-02 ya no genera aviso de longitud.

## 7.4 Bloque identidad y asistente resuelto (2026-10-16)

| Hallazgo | Decisión | Dónde quedó |
| :-- | :-- | :-- |
| S1 | El asistente consulta pedidos con una credencial de 5 minutos, de solo lectura y limitada al cliente, emitida por Identidad a pedido de la Web; el servicio de pedidos filtra, no el asistente. Catálogo y disponibilidad siguen con credencial de servicio. | ASI-19, IDE-33; AC-ASI-18, AC-IDE-30; `03-autenticacion`, `06-seguridad-del-asistente` |
| S2 | 3 cuentas nuevas por hora por origen y correo verificado para iniciar un pago (cambia IDE-29: ya no se compra sin verificar). | SEG-24, IDE-29, CAR-21; AC-SEG-21, AC-CAR-19; AM01 |
| S3 | Sesión web de 30 min de inactividad renovables por uso, con tope absoluto de 7 días (clientes) u 8 h (personal); identificador nuevo al ingresar e invalidado al cerrar. El almacenamiento de la sesión queda para el plan. | WEB-27, WEB-28; AC-WEB-22, 23; `03-autenticacion` |
| L5 | Términos, privacidad y reembolso versionados con el código; el registro y cada pedido guardan versión y fecha aceptadas. | IDE-34, CAR-22, WEB-29; AC-IDE-31, AC-CAR-20, AC-WEB-24; tabla `aceptaciones` |
| L6 | La IP se declara dato personal: los contadores guardan una huella no reversible que se purga a las 24 h; los eventos de seguridad guardan la IP 2 años y luego se anonimiza. | SEG-25; AC-SEG-22; `04-privacidad`, `05-modelo-de-datos` |

**Nota:** la sesión de 30 minutos reduce el riesgo en equipos abandonados y con cookies robadas sin uso; no evita el robo si quien la roba la usa de forma continua, por eso se agregó el tope absoluto. Nueva pregunta abierta: confirmar 3 cuentas por hora por origen (SEG §7).

## 7.5 Pendientes de docs resueltos (2026-10-16)

| Hallazgo | Decisión | Dónde quedó |
| :-- | :-- | :-- |
| C10 | Sistema operativo documentado: Ubuntu Server LTS (imagen oficial para Ampere A1); se menciona que la propuesta decía Debian. Corregidos «19 features», AD01–AD30 y `eventos_limite`. | `05-despliegue` §5; `01-flujo-sdd`; `01-drivers`; `05-modelo-de-datos` |
| D1 | Modelo C4 niveles 1 a 3 en los cinco contenedores, como referencia a nivel de módulo; el nivel 4 no se elabora. | `docs/04-modelo-c4/` (README, contexto y contenedores, componentes) |
| S5 | Archivos digitales (EPUB/PDF, máx. 50 MB) y portadas (JPEG/PNG/WebP, máx. 2 MB): tipo por contenido y huella SHA-256; sin antivirus por la RAM de la VM. | CAT-16; AC-CAT-12; CTL-14 |
| S7 | Contraseña de 10 caracteres, fuera de listas comunes o filtradas, sin reglas de composición (revisa la decisión de R0). | IDE-15; RNF15; CTL-15; AC-IDE-13 |
| S8 | Clave de firma que solo posee Identidad, con clave de verificación pública y rotación cada 90 días o ante sospecha (la comprometida se rechaza de inmediato). Queda pendiente la custodia de la clave privada, para el plan. | IDE-35, IDE-36; AC-IDE-32, 33; CTL-29 |
| Redacción | Identificadores y criterios reordenados por número en las 19 specs; BIB-07 dividido (BIB-07 titular, BIB-18 enlace vencido); AUD-02 ya no se marca como muy largo. | specs |

### 7.6 C7, C8, C9 y D3 resueltos (2026-10-16)

| Hallazgo | Decisión | Dónde quedó |
| :-- | :-- | :-- |
| C7 | El cliente puede cancelar un pedido formado solo por impresos confirmados (antes de la preparación): se anula, se libera, se cancela el traslado y se reembolsa. Con digitales o líneas ya preparadas, se rechaza y se indica la devolución. | PED-17, PED-18; AC-PED-15, 16; actores y permisos |
| C8 | El impreso se reembolsa después de confirmada la recepción y el reingreso (nuevo estado «recibida»); el digital, al aprobarse. | DEV-08, DEV-10; AC-DEV-07, 08; estados |
| C9 | El pago pendiente extiende la reserva una vez, hasta 30 minutos desde el inicio del pago, y avisa al cliente en pantalla, por correo y, con consentimiento, por WhatsApp; si se aprueba con la reserva vencida, se reembolsa. | SEL-22, PAG-13 a PAG-15, NOT-02; AC-SEL-24, AC-PAG-11 a 13; RC18, AD30 |
| D3 | Costo de envío fijo de S/ 12,00, configurable sin tocar código, uno por pedido. | CAR-23; AC-CAR-21 |

**Nuevas preguntas abiertas:** cancelación parcial de pedidos mixtos (PED) y plazo para llevar el impreso a la sucursal tras aprobarse la devolución (DEV). Interpretación a confirmar en C9: «todos los medios posibles» se aplicó a pantalla, correo y WhatsApp con consentimiento.

### 7.7 S6 y L7 a L11 resueltos (2026-10-16)

| Hallazgo | Decisión | Dónde quedó |
| :-- | :-- | :-- |
| S6 | Segundo factor obligatorio para todo el personal (encargado, logística, administrador) y opcional para el cliente; recuperar la contraseña no lo desactiva; la administración puede restablecerlo. *(Interpretación de tu respuesta; confírmala.)* | IDE-20 a IDE-22, IDE-37, IDE-38; AC-IDE-18 a 20, 34, 35; AD22, RNF15, CTL-19 |
| L7 | Meta interna de notificar un incidente con datos personales en 48 horas (a verificar contra el reglamento). | `04-privacidad` §6, `07-operacion-segura` §6, marco legal |
| L8 | Solo clientes mayores de 18 años, con declaración al registrarse. | IDE-39; AC-IDE-36; marco legal, privacidad |
| L9 | Solo cookies necesarias (sesión, tema, sucursal); la privacidad las informa; sin banner mientras no haya otras. | WEB-29; marco legal |
| L10 | Ya documentado: el libro físico de las sucursales es una obligación fuera del software. | Marco legal (Libro de Reclamaciones) |
| L11 | Plan de salida de WhatsApp documentado, sin implementar. | AD16 en `02-decisiones`; drivers |

### 7.8 Barrido de `docs/` y «confirmo» (2026-10-16)

Se alinearon los documentos con todas las decisiones anteriores y se cerraron las preguntas abiertas de las specs con las propuestas confirmadas; las decisiones técnicas pasaron a «Nota para el plan». Cambios de comportamiento al confirmar: SEL-23 (región sin sucursal y desempate), TRA-18 (cancelar un traslado no vinculado), CAR-24 (fusión del carrito de visitante), DEV-13 a DEV-15 (quién aprueba y plazos de devolución), ENV-01 y ENV-07 (un envío por fuente). Corrección a una propuesta: el traslado perdido no se cubre con una merma en el origen.

### 7.9 Revisión con agentes y segunda ronda (2026-10-16)

`spec-auditor` (AUD, SEG) y `security-reviewer` (SEG, IDE) devolvieron «necesita otra vuelta» y «cumple con observaciones». Decisiones tomadas con el responsable y aplicadas:

| Hallazgo | Decisión | Dónde quedó |
| :-- | :-- | :-- |
| Origen no determinable evade límites | Un «origen desconocido» único con los mismos límites por origen. | SEG-23 |
| Cabecera de reenvío falsificada | Se ignoran las cabeceras del cliente; la cabecera concreta queda para el plan. | SEG-17, AC-SEG-15 |
| Inundación del registro | Los límites excedidos se agrupan por origen, límite y ventana de 5 minutos con contador; los accesos denegados siguen uno a uno. | SEG-08, AUD §6 |
| Caída del almacén de contadores | Se cierran ingreso, registro, códigos, reservas y descargas; lo demás usa un límite local. | SEG-26 |
| TOTP | Un uso por ventana, desfase de ±1, sesión parcial, secreto cifrado y respaldo solo como resumen; el bloqueo es por cuenta y origen. | IDE-40 a 42, SEG-20 |
| Clave de firma | Identificador de clave, algoritmo fijo y revocación en 5 minutos (también de las renovaciones); AM25 nueva. | IDE-35, IDE-36 |
| Credencial del asistente | Alcance único de lectura de pedidos y audiencia solo de pedidos. | IDE-33 |
| Contraseña | 10 a 128 caracteres Unicode. | IDE-15 |
| Textos legales | Reaceptación en el siguiente ingreso o pago. | IDE-43 |
| Sesiones | Cambiar la contraseña o el segundo factor revoca las demás renovaciones; aviso al correo; renovación de 8 horas para el personal. | IDE-02, IDE-44, IDE-45 |
| Auditoría | Actores «sistema, integración, anónimo»; IP anonimizada en la depuración (AUD-19); más eventos (AUD-16, AUD-21); cambio de rol único (AUD-20); alertas con estado y alerta técnica visible; páginas de 20 y tiempo de 10 s. | AUD-01, 09, 12, 13, 16, 18 a 21 |
| Otros | Pantallas de auditoría y alertas (WEB-30); dependencias de `ecommerce-envios` y `ecommerce-devoluciones`; agrupación de alertas (SEG-27); SEG-10 dividido (SEG-28). | specs |

Riesgo aceptado nuevo: los límites por origen pueden bloquear a varias personas de una red compartida (SEG §6).

### 7.10 Revisión de Inventario (2026-10-16)

`spec-auditor` revisó fuentes, selección, ajustes y traslados («necesita otra vuelta»). Decisiones del responsable aplicadas:

| Hallazgo | Decisión | Dónde quedó |
| :-- | :-- | :-- |
| Stock inicial sin vía | Nuevo motivo «ingreso inicial», solo de la administración central. | AJU-02, AJU-13, AJU-14 |
| Carrera entre anulación, entrega, reparto y despacho | Gana la primera que confirma; las demás se rechazan por estado inválido. | SEL-29, SEL-28, TRA-23 |
| Reservas vencidas sin procesar | No cuentan como activas y se liberan al reservar la edición. | FUE-03, SEL-27 |
| Conteo bajo lo reservado | Se rechaza y se listan los pedidos afectados. | AJU-06 |
| Pedido no recogido | No puede entregarse después; solo se anula con reembolso. | SEL-28 |
| Ajuste por la administración | Sí, con auditoría. | AJU-15 |
| Traslado vinculado sin aprobar | A las 48 horas se avisa a la administración y a logística. | TRA-22 |

Sin pregunta (criterio técnico): reasignación al perder la última unidad (SEL-26), atomicidad de recepción y despacho (TRA-07, TRA-08), traslado por fuente y edición (SEL-03), domicilio sin traslado y una línea en una sola fuente (SEL-24, SEL-25), alcance y validaciones de traslados (TRA-09, TRA-20), fecha estimada (TRA-21), auditoría de movimientos (SEL-32), cobertura de criterios de concurrencia, `cerrada` como estado de reserva y dependencias de identidad. **Supuesto a confirmar:** un encargado solo puede solicitar traslados hacia su propia sucursal (TRA-20).

**Siguen abiertos de R3:** custodia de la clave de firma (plan) y S9 (dónde viven los contadores de límites, plan). Quedan además las preguntas abiertas de confirmación en cada spec (valores propuestos y las nuevas de esta ronda), que son las que impiden pasar a `Approved`.

## 8. Orden de resolución propuesto

1. **Bloque núcleo (antes de aprobar SEL/TRA/PED/ENV):** C1, C2, C3, C5, D2. Redefinir cómo se comporta la reserva cuando hay traslado y cuándo baja el físico en cada tipo de entrega.
2. **Bloque transversal (antes de aprobar AUD/SEG):** A1–A5, S10.
3. **Bloque identidad y asistente:** S1, S2, S3, L5, L6.
4. **Bloque legal/UX (antes de aprobar REC/NOT/IDE/CAR):** C4, L1–L4, S4.
5. **Docs:** C10, D1, S5, S7, S8 y los ajustes de redacción.

Cada bloque termina con `SpecCheck`, `spec-auditor` y (si toca seguridad o privacidad) `security-review`, y luego la aprobación por la persona responsable.
