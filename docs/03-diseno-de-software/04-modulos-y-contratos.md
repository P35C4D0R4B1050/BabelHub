# Módulos y contratos

Describe los módulos de cada servicio y la forma de sus contratos REST. Es un diseño de **nivel de módulo**: las rutas exactas, los esquemas y los códigos de error se fijan en el `plan.md` de cada spec; aquí se establece el marco común.

## 1. Mapa de módulos

| Servicio | Módulos (casos de uso agrupados) | Specs |
| :--- | :--- | :--- |
| **Inventario** | **Fuentes** (fuentes, stock vendible, rutas, unidades vendidas, fuente de recepción); **Selección y reservas** (consulta previa, reservas con expiración); **Traslados**; **Ajustes** (merma, daño, conteo, ingreso de mercadería, devoluciones) | `inventario-fuentes`, `inventario-seleccion-fuente`, `inventario-traslados`, `inventario-ajustes` |
| **E-commerce** | **Catálogo**; **Carrito y checkout**; **Pagos**; **Pedidos**; **Envíos**; **Devoluciones**; **Biblioteca digital**; **Libro de Reclamaciones**; **Identidad** (registro, sesión, segundo factor, usuarios y roles, derechos ARCO); **Asistente** (consulta, recomendación, estado de pedidos); **Interfaz web** (áreas Tienda, Sucursal y Admin, índice infinito) | `ecommerce-*`, `identidad-seguridad`, `asistente-compra-ia`, `web-base`, `web-indice-infinito` |
| **Transversal** | Auditoría (contrato único; cada servicio guarda sus registros y Identidad los suyos, aislados; con filtros y paginación estable); Seguridad (errores, límites, eventos y alertas); Notificaciones (puerto) | `transversal-auditoria`, `transversal-seguridad`, `transversal-notificaciones` |

Cada módulo es una carpeta dentro de las capas Aplicación y Dominio del servicio; no es un proyecto aparte. Tiene un **contrato público** en la raíz de su carpeta y un interior privado (`Interno`).

### 1.1 Dependencias entre módulos

Un módulo solo llama a los contratos públicos de otros. Las flechas siguen la columna `depende` de las specs y no forman ciclos:

| Módulo | Depende de |
| :--- | :--- |
| Identidad | Nada: todos los módulos usan su contexto de actor; para los pendientes de una cuenta expone un puerto que implementan los demás módulos (sin depender de ellos). |
| Catálogo | Cliente del Inventario (disponibilidad e ingreso de mercadería). |
| Carrito y checkout | Catálogo, Identidad y cliente del Inventario. |
| Pagos | Carrito y checkout, Identidad y cliente del Inventario (reserva). |
| Biblioteca digital | Catálogo, Carrito y checkout y Pagos. |
| Pedidos | Pagos, Biblioteca digital y cliente del Inventario. |
| Envíos | Pedidos, Carrito y checkout, Identidad y cliente del Inventario. |
| Devoluciones | Pedidos, Pagos, Biblioteca digital y cliente del Inventario. |
| Libro de Reclamaciones | Identidad. |
| Asistente | Catálogo, Pedidos e Identidad (solo lectura) y cliente del Inventario. |
| Interfaz web | Todos los módulos y el cliente del Inventario. |
| Inventario: Selección y reservas, Traslados y Ajustes | Fuentes; Traslados además Selección y reservas. |

## 2. Convenciones de las rutas

| Tema | Convención |
| :--- | :--- |
| Prefijo y versión | `/api/<servicio>/v1/<recurso>` (RNF28). Una versión nueva convive con la anterior hasta retirarla. |
| Recursos | Sustantivos en plural en español, en minúsculas y con guiones (`/fuentes`, `/reservas`, `/traslados`). |
| Verbos | `GET` leer; `POST` crear o ejecutar una acción con nombre; `PUT` reemplazar; `PATCH` cambios parciales; `DELETE` baja lógica donde aplique. |
| Acciones de dominio | Subrecurso con verbo: `POST /traslados/{id}/aprobar`, `POST /reservas/{id}/confirmar`. |
| Identificadores | Opacos (GUID u identificador de base); el ISBN es un atributo, no el identificador de ruta. |
| Paginación | `?pagina=1&tamano=20` con tope; respuesta con total. La auditoría usa un cursor estable (instante de apertura y último registro) para no repetir ni omitir registros (AUD-30) y un plazo de respuesta entre servicios de 10 segundos (AUD-18). |
| Filtros y orden | `?q=`, `?orden=`, campos permitidos y documentados. |
| Idempotencia | Cabecera `Idempotency-Key` en cobros, reservas y reembolsos. |
| Trazabilidad | Cabecera de correlación (`traceId`) en todas las respuestas, incluidos los errores. |

## 3. Contrato de errores

Los dos servicios responden los errores con `application/problem+json`:

```json
{
  "type": "https://babelhub/errores/stock-insuficiente",
  "title": "Stock insuficiente",
  "status": 409,
  "code": "INV-STOCK-INSUFICIENTE",
  "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01"
}
```

| Campo | Regla |
| :--- | :--- |
| `code` | Estable, en mayúsculas con guiones, con prefijo del servicio; la interfaz y las pruebas dependen de él, no del texto. |
| `title` y mensaje | Genéricos y en español; sin trazas, rutas internas ni nombres de tablas. |
| `status` | Códigos estándar: 400 validación, 401 sin credencial, 403 sin permiso, 404 no existe, 409 conflicto de estado, 422 regla de negocio, 429 límite de uso, 503 dependencia no disponible. |
| Límites de uso | 429 con `Retry-After` y el mismo cuerpo siempre (SEG-05, SEG-07). |
| Validación | Lista de campos inválidos con su motivo, sin eco del valor sensible. |

## 4. Contratos entre servicios y entre módulos

| De → a | Operaciones principales | Autenticación | Falla |
| :--- | :--- | :--- | :--- |
| E-commerce → Inventario | Consultar disponibilidad; consultar la selección de fuentes sin reservar; crear, confirmar, extender (pago pendiente) y liberar reservas; descontar al entregar o al pasar a en reparto; liberar al marcar no recogido; ajustar al devolver; registrar un ingreso de mercadería; consultar unidades vendidas; gestión de sucursal (inventario, ajustes y traslados) | Credencial de servicio con la identidad del actor | Se informa «no se puede validar el stock» sin cobrar; el catálogo usa la última disponibilidad (CAR-06, CAT-12). |
| Inventario → E-commerce | Avisar que un traslado vinculado a un pedido fue recibido | Credencial de servicio | Se reintenta; el pedido espera. |
| E-commerce (Identidad) → Inventario | Entregar la baja del titular de una cuenta cancelada, con solo su identificador opaco; persistente, repetible sin efecto duplicado y reintentada (IDE-64, AUD-32) | Credencial de servicio | Se reintenta hasta que el Inventario la acepte; la cancelación no se deshace. |
| E-commerce → Inventario (alertas) | Consultar las alertas de seguridad y técnicas del Inventario y marcar una de seguridad como revisada (AUD-13, AUD-29, AUD-34); el E-commerce envía el correo a los administradores centrales | Credencial del administrador y de servicio | Se muestra lo del E-commerce y el aviso de AUD-18. |
| Módulos del E-commerce entre sí | Operaciones de cada módulo por su contrato público, en el mismo proceso | Contexto del actor (no se pasa por la red) | Excepción de la capa de Aplicación traducida por la Presentación. |
| Interfaz web → módulo Identidad | Pedir un contexto de lectura de 5 minutos para el asistente en nombre del cliente | Contexto del usuario | Sin él el asistente solo responde catálogo y disponibilidad. |
| Interfaz web → E-commerce e Inventario | Auditoría y alertas: pedir a cada servicio su página con los mismos filtros y mezclarlas por fecha | Credencial del administrador | Se muestra el otro servicio y se indica el que no respondió (AUD-18). |
| Asistente → Catálogo, Pedidos e Inventario | Consultar catálogo y disponibilidad (contexto de solo lectura) y pedidos (contexto del cliente, de lectura y 5 minutos) | Ver la columna anterior | El asistente informa que no tiene datos. |
| Servicios → Notificador | Enviar aviso | Secreto compartido | Pendiente y reintento; no interrumpe. |
| Pasarela → E-commerce | Aviso de pago | Firma o secreto verificado | Se rechaza y registra; el estado se confirma con la pasarela. |
| Todos → Auditoría local | Registrar operación | Interna | Si falla, la operación sensible no se aplica (AUD-04). |

## 5. `BabelHub.Shared`

Contiene solo contratos: DTOs de solicitud y respuesta, constantes (códigos de error, nombres de rol y de política) y enumeraciones públicas. **No** contiene reglas, validaciones de negocio ni clases de acceso a datos; no lo referencian el Dominio ni la Aplicación (AGENTS 6). Una prueba de arquitectura lo hace cumplir.

## 6. Evolución de contratos

1. Un cambio compatible (campo nuevo opcional) no cambia la versión.
2. Un cambio incompatible crea `v2` y mantiene `v1` durante al menos 30 días (PLA-12).
3. Los campos retirados se marcan como obsoletos antes de eliminarse.
4. Cada contrato tiene una prueba de contrato en el servicio que lo publica y en el que lo consume.
