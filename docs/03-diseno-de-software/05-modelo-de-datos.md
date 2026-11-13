# Modelo de datos lógico

Traduce el [modelo de dominio](../01-analisis-de-sistema/07-modelo-de-dominio.md) a tablas, restricciones e índices. Es un **diseño lógico de referencia**: el plan de cada spec fija los nombres definitivos y las migraciones. Las bases siguen AD03 y AD18: una instancia PostgreSQL con **una base por servicio** (`ecommerce` e `inventario`), un **esquema por módulo** dentro de cada base y un **rol por esquema**. No hay llaves foráneas ni consultas entre las dos bases: solo identificadores y contratos REST.

## 1. Convenciones

| Tema | Regla |
| :--- | :--- |
| Identificadores | Clave sustituta (GUID o secuencia) y, cuando existe, clave natural única (ISBN, número de hoja). |
| Fechas | `timestamptz` en UTC; la interfaz muestra la hora de Lima. |
| Dinero | `numeric(12,2)` en soles; nunca `float`. |
| Nombres | Tablas y columnas en español, minúsculas con guion bajo (`reservas`, `fecha_expiracion`). |
| Borrado | Baja lógica para catálogo y usuarios; auditoría y hojas de reclamación nunca se borran (solo se anonimiza lo que corresponda). |
| Concurrencia | Columna de versión por fila (`version`) en agregados con escritura concurrente (stock). |
| Permisos | Un rol por esquema que solo puede usar su propio esquema (`REVOKE` a los demás); el rol de la aplicación no es dueño de las tablas y no puede modificar ni borrar la auditoría; solo un rol de depuración, distinto, con permiso limitado a las columnas de datos personales, anonimiza (AUD-19). En el Inventario, además, seguridad por filas por sucursal (RLS). |
| Datos personales | Marcados en el diccionario; el borrado o la anonimización se prueba (RNF23). |

## 2. Inventario (base `inventario`, un esquema por módulo)

```mermaid
erDiagram
FUENTES ||--o{ STOCK : tiene
STOCK ||--o{ RESERVAS : "se aparta con"
FUENTES ||--o{ TRASLADOS : origen
FUENTES ||--o{ TRASLADOS : destino
STOCK ||--o{ AJUSTES : "se corrige con"
RESERVAS }o--|| PEDIDOS_REF : "pertenece a"
```

| Tabla | Columnas principales | Restricciones |
| :--- | :--- | :--- |
| `fuentes` | id, nombre, region, direccion, contacto_aviso, activa, es_recepcion | Nombre único; `activa` por defecto verdadero; **una sola fuente con `es_recepcion`** (índice único parcial), por defecto Lima (FUE-19). |
| `stock` | fuente_id, edicion_id, fisico, reservado, version | PK (fuente_id, edicion_id); **CHECK (fisico >= 0 AND reservado >= 0 AND reservado <= fisico)**, que impide el vendible negativo incluso ante concurrencia. |
| `reservas` | id, pedido_id, cliente_id, huella_origen, fuente_id, edicion_id, cantidad, estado, fecha_expiracion, fecha_confirmacion, clave_idempotencia, fecha_creacion | Estado en (pendiente, confirmada, cerrada, liberada, expirada); `cerrada` se usa al entregar, pasar a en reparto o consumirse en el despacho de un traslado (el destino recibe una reserva nueva); `clave_idempotencia` única por cliente; índice parcial sobre estado pendiente por cliente (límite de una pendiente). |
| `traslados` | id, edicion_id, origen_id, destino_id, cantidad, estado, motivo, pedido_id, fechas por estado | Origen distinto de destino; estado en el ciclo definido (incluye `cancelado`); cantidad positiva; un traslado con `pedido_id` no pasa a `rechazado`. |
| `rutas` | origen_id, destino_id, plazo_dias, actualizada_en | PK (origen_id, destino_id); origen distinto de destino; **CHECK (plazo_dias > 0)**; direccional. |
| `ajustes` | id, fuente_id, edicion_id, motivo, cantidad, conteo_real, usuario_id, fecha | Motivo en (merma, danado, correccion_conteo, ingreso_inicial, devolucion); `devolucion_id` único para no reingresar dos veces; solo inserción. |
| `ingresos` | id, fuente_id, edicion_id, cantidad, referencia, nota, usuario_id, fecha | Solo en la fuente de recepción; **CHECK (cantidad entre 1 y 5000)**; solo inserción; el ajuste con motivo ingreso de mercadería se crea en la misma transacción (AJU-13, AJU-19). |
| `eventos_limite` | clave, instante | Un registro por reserva iniciada, por cliente y por origen (SEL-14, SEL-19); el conteo usa una ventana deslizante (SEG-21) y se inserta en la misma transacción de la reserva; los registros viejos se purgan a las 24 horas. La clave del origen es una huella no reversible, no la IP (SEG-25). |
| `auditoria` | id, fecha_utc, servicio, es_auditoria, es_seguridad, actor, rol, accion, objeto, sucursal_origen_id, sucursal_destino_id, origen, motivo, detalle (máx. 1000 caracteres; campos personales marcados), truncado, resultado, secuencia | Solo inserción; sin permisos de modificación para la cuenta de aplicación. Solo un rol de depuración, distinto, anonimiza los datos personales (AUD-19). Los contadores de límites y el estado de las alertas viven aparte (AUD, §5). |

**Regla de oro:** `reservado` se actualiza en la misma transacción que la fila de `reservas`, y el `CHECK` es la última defensa contra la sobreventa (AD23). El vendible se calcula como `fisico - reservado`; no se almacena.

**Unidades vendidas** (FUE-21): no se guardan; se calculan por fuente y periodo a partir de las reservas confirmadas y cerradas, sin contar las liberadas, las anuladas ni el consumo por traslado, para que cada unidad cuente una sola vez para la fuente que despacha (domicilio) o entrega (recojo).

## 3. E-commerce (base `ecommerce`, un esquema por módulo)

Esquemas: `catalogo`, `carrito`, `pagos`, `pedidos`, `envios`, `devoluciones`, `biblioteca`, `reclamaciones`, `web` (sesiones) e `identidad` (sección 4). Cada tabla vive en el esquema de su módulo; una transacción puede abarcar varios esquemas del mismo servicio (por ejemplo, pedido, pago y auditoría).

| Tabla | Columnas principales | Restricciones |
| :--- | :--- | :--- |
| `obras` | id, titulo, autor, editorial, categoria, portada, activa | Baja lógica. |
| `ediciones` | id, obra_id, isbn, formato, precio, activa | `isbn` único; formato en (impreso, digital). |
| `licencias` | edicion_id, titular, territorios, vigente_desde, vigente_hasta | Solo para ediciones digitales; vigencia coherente. |
| `carritos` / `carrito_lineas` | cliente_id; carrito_id, edicion_id, cantidad | Una línea por edición; cantidad 1 para digitales. |
| `pedidos` | id, cliente_id, tipo_entrega, estado, costo_envio, total, fecha, fecha_listo_recojo | Estado derivado de las líneas (incluye `no_recogido`); total en soles; `costo_envio` fijo y configurable. |
| `pedido_lineas` | pedido_id, edicion_id, titulo, isbn, cantidad, precio, estado, fuente_id | Una línea por edición y pedido. |
| `intenciones_pago` | id, cliente_id, carrito_ref, costo_envio, versiones_aceptadas, estado, fecha_inicio | Un solo cobro pendiente o aprobado por intención (índice único parcial). |
| `pagos` | id, intencion_id, pedido_id, estado (incluye `pendiente_confirmacion` y `vencido`), monto, referencia_pasarela, clave_idempotencia, fecha_inicio | **Un solo cobro aprobado por pedido** (índice único parcial); sin datos de tarjeta. |
| `envios` | id, pedido_id, guia, estado | Estados en orden; guía única. |
| `devoluciones` / `devolucion_lineas` | pedido_id, motivo, estado; linea_id, cantidad | Cantidad devuelta ≤ comprada. |
| `accesos_digitales` | id, cliente_id, edicion_id, descargas_usadas, revocado | Único (cliente_id, edicion_id); `descargas_usadas` ≤ 5 por restricción. |
| `descargas` | id, acceso_id, fecha, resultado | Solo inserción. |
| `ayudas_descarga` | id, acceso_id, motivo, estado | Solicitudes de ayuda (BIB-16). |
| `hojas_reclamacion` | id, numero, tipo, detalle, datos_consumidor (nombre, documento, domicilio, correo), monto_reclamado, pedido_id, estado, respuesta, aviso_estado | `numero` único; sin borrado; estado en (pendiente, respondido, descartada); `aviso_estado` en (pendiente, enviado). |
| `avisos` | id, evento_id, destinatario, estado, intentos, proximo_intento | Único (evento_id, destinatario) para no duplicar (NOT-05). |
| `sesiones_web` | id opaco, usuario_id, rol, sucursal_id, ultima_actividad, expira_absoluta, host | Esquema `web`. Sesión del servidor con cookie opaca (WEB-04, WEB-27); compartida por la réplica de gestión; se purga al vencer. |
| `auditoria` | como en Inventario | Solo inserción; la comparten los módulos del E-commerce salvo Identidad, que tiene la suya en su esquema aislado. |
| `alertas` | id, servicio, motivo, clase (seguridad o técnica), primera_ocurrencia, ocurrencias, estado (pendiente o revisada), revisada_por, revisada_en | **No es auditoría**: se actualiza al agrupar (ventana fija de 15 minutos) y al revisar (AUD-13, AUD-34). Existe también en el Inventario. |

## 4. Identidad (esquema `identidad` de la base `ecommerce`)

| Tabla | Columnas principales | Restricciones |
| :--- | :--- | :--- |
| `usuarios` | id, nombre, correo, correo_verificado, resumen_contrasena, rol, sucursal_id, activo, whatsapp, segundo_factor_activo, mayor_de_edad_declarado_en, fecha_cancelacion | Correo único; **CHECK**: sucursal obligatoria solo para encargado; un encargado activo por sucursal (índice único parcial). |
| `renovaciones` | id, usuario_id, huella, expira_absoluta, revocada | La huella reemplaza al valor de la renovación; nunca se guarda el valor; `expira_absoluta` se hereda en cada uso. |
| `codigos_un_solo_uso` | id, usuario_id o correo, proposito, huella, expira, intentos, usado | Se guarda solo la huella; caduca; se borra tras vencer. |
| `segundo_factor` | usuario_id, secreto_cifrado | Personal (obligatorio) y clientes que lo activaron; cifrado en reposo. |
| `codigos_respaldo` | usuario_id, huella, usado | 8 por usuario con segundo factor; un solo uso. |
| `consentimientos` | usuario_id, finalidad, vigente, fecha | Historial por cambio. |
| `dispositivos` | usuario_id, huella_marca, region, primera_vez, ultima_vez | Solo la huella de la marca y la región del borde; se eliminan al cancelar la cuenta. |
| `cambios_correo_pendientes` | id, usuario_id, correo_nuevo, dispositivo_huella, region, estado, vence_en | Estados: pendiente, confirmado, rechazado, vencido; un cambio inusual vence a las 24 horas. |
| `versiones_legales` | texto (términos, privacidad, reembolso), version, vigente_desde | La versión vigente es la última; Identidad rechaza aceptaciones de otra. |
| `aceptaciones` | usuario_id o pedido_id, texto (términos, privacidad, reembolso, mayoría de edad), version, fecha | Solo inserción; una fila por texto aceptado en el registro (IDE-34) o en el checkout (CAR-22). |
| `solicitudes_arco` | id, usuario_id, tipo, estado, respuesta, fecha, fecha_limite | Estados: recibida, en atención, atendida, rechazada. `fecha_limite` = 20 días hábiles (acceso) o 10 (resto). |
| `eventos_ingreso` | clave (cuenta+origen), instante | Un registro por ingreso fallido (SEG-06); cuenta también para cuentas inexistentes; ventana deslizante. |
| `bajas_titulares` | id_opaco, fecha, estado (pendiente o entregada), intentos | Buzón de salida de Identidad para la baja al Inventario (IDE-64, AUD-32): se escribe en la misma operación que la cancelación y se reintenta; el Inventario guarda su copia por `id_opaco` para reaplicarla y no duplicar. |
| `auditoria` | como en Inventario | Solo inserción; propia de Identidad por el aislamiento del esquema. La interfaz web la presenta junto con la del resto del E-commerce y la del Inventario (AUD-09). |

## 4.1 Asistente (sin tablas propias)

El Asistente no tiene datos de negocio (los obtiene de los contratos de otros módulos y del Inventario, ASI-06) y **no guarda el texto de las consultas ni las respuestas** (ASI-18). Sus eventos de seguridad (acceso denegado, límite excedido) se guardan en la auditoría del E-commerce; el contador de límites de ASI-09 vive en Redis (o, si el plan lo decide, en una tabla `eventos_limite` como la de Inventario).

## 5. Índices y rendimiento

| Consulta crítica | Índice |
| :--- | :--- |
| Disponibilidad por edición | (edicion_id) sobre `stock` y agregación por fuente. |
| Reservas pendientes por expirar | (estado, fecha_expiracion) parcial sobre pendientes. |
| Límite por cliente y por origen | Índices sobre `eventos_limite.clave` e `instante`. |
| Pedidos de un cliente / de una sucursal | (cliente_id, fecha), (fuente_id, estado). |
| Búsqueda de catálogo | Índice de texto sobre título, autor y editorial. |
| Auditoría por fecha, usuario y acción | (fecha_utc DESC), (actor), (accion). |

## 6. Datos personales y retención

| Tabla | Datos personales | Tratamiento |
| :--- | :--- | :--- |
| `usuarios` | Nombre, correo, WhatsApp | Se anonimizan al cancelar la cuenta. |
| `pedidos`, `envios` | Dirección, contacto | Se anonimiza el contacto a los 2 años *(propuesta)*. |
| `hojas_reclamacion` | Datos del consumidor | Se conservan al menos 2 años. |
| `auditoria` | Usuario, origen, campos personales del detalle | Se anonimiza a los 2 años o al cancelar la cuenta (AUD-11, AUD-19, AUD-32). |
| Respaldos | Todos | Cifrados; rotan según la retención. |

## 7. Migraciones

Las migraciones son versionadas, hacia adelante y reversibles cuando es posible; se ejecutan con una cuenta distinta de la de aplicación. Cada plan incluye su migración y una prueba de integración con la base real (Testcontainers) que verifica las restricciones críticas (`CHECK` de stock, unicidad de cobro, unicidad de ISBN).
