# Módulos y contratos

Describe los módulos de cada servicio y la forma de sus contratos REST. Es un diseño de **nivel de módulo**: las rutas exactas, los esquemas y los códigos de error se fijan en el `plan.md` de cada spec; aquí se establece el marco común.

## 1. Mapa de módulos

| Servicio | Módulos (casos de uso agrupados) | Specs |
| :--- | :--- | :--- |
| **Inventario** | Fuentes y stock; Reservas y selección de fuente; Ajustes; Traslados | `inventario-fuentes`, `inventario-seleccion-fuente`, `inventario-ajustes`, `inventario-traslados` |
| **E-commerce** | Catálogo; Carrito y checkout; Pagos; Pedidos; Biblioteca digital; Envíos; Devoluciones; Libro de reclamaciones; Notificaciones (puerto) | `ecommerce-*`, `transversal-notificaciones` |
| **Identidad** | Registro y verificación; Sesión y renovación; Recuperación; Segundo factor; Usuarios y roles; Cuenta y derechos ARCO | `identidad-seguridad` |
| **Asistente** | Consulta; Recomendación; Estado de pedidos | `asistente-compra-ia` |
| **Web** | Áreas Tienda, Sucursal y Administración; Libro de reclamaciones; Índice infinito | `web-base`, `web-indice-infinito` |
| **Transversal** | Auditoría (contrato único); Seguridad (errores, límites, eventos) | `transversal-auditoria`, `transversal-seguridad` |

Cada módulo es una carpeta dentro de las capas Aplicación y Dominio del servicio; no es un proyecto aparte.

## 2. Convenciones de las rutas

| Tema | Convención |
| :--- | :--- |
| Prefijo y versión | `/api/<servicio>/v1/<recurso>` (RNF28). Una versión nueva convive con la anterior hasta retirarla. |
| Recursos | Sustantivos en plural en español, en minúsculas y con guiones (`/fuentes`, `/reservas`, `/traslados`). |
| Verbos | `GET` leer; `POST` crear o ejecutar una acción con nombre; `PUT` reemplazar; `PATCH` cambios parciales; `DELETE` baja lógica donde aplique. |
| Acciones de dominio | Subrecurso con verbo: `POST /traslados/{id}/aprobar`, `POST /reservas/{id}/confirmar`. |
| Identificadores | Opacos (GUID u identificador de base); el ISBN es un atributo, no el identificador de ruta. |
| Paginación | `?pagina=1&tamano=20` con tope; respuesta con total. |
| Filtros y orden | `?q=`, `?orden=`, campos permitidos y documentados. |
| Idempotencia | Cabecera `Idempotency-Key` en cobros, reservas y reembolsos. |
| Trazabilidad | Cabecera de correlación (`traceId`) en todas las respuestas, incluidos los errores. |

## 3. Contrato de errores

Todos los servicios responden los errores con `application/problem+json`:

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

## 4. Contratos entre servicios

| De → a | Operaciones principales | Autenticación | Falla |
| :--- | :--- | :--- | :--- |
| E-commerce → Inventario | Consultar disponibilidad; crear, confirmar, liberar reservas; descontar al entregar; ajustar al devolver | Credencial del usuario o de servicio | Si Inventario no responde, el checkout informa y no cobra (CAR-06). |
| Web → todos | Operaciones de las pantallas | Credencial del usuario (solo servidor) | Estado de error con reintento (WEB-07). |
| Asistente → E-commerce e Inventario | Consultar catálogo, disponibilidad y pedidos | Credencial de servicio de solo lectura | El asistente informa que no tiene datos. |
| Servicios → Notificador | Enviar aviso | Secreto compartido | Pendiente y reintento; no interrumpe. |
| Pasarela → E-commerce | Aviso de pago | Firma o secreto verificado | Se rechaza y registra; el estado se confirma con la pasarela. |
| Todos → Auditoría local | Registrar operación | Interna | Si falla, la operación sensible no se aplica (AUD-04). |

## 5. `BabelHub.Shared`

Contiene solo contratos: DTOs de solicitud y respuesta, constantes (códigos de error, nombres de rol y de política) y enumeraciones públicas. **No** contiene reglas, validaciones de negocio ni clases de acceso a datos; no lo referencian el Dominio ni la Aplicación (AGENTS 6). Una prueba de arquitectura lo hace cumplir.

## 6. Evolución de contratos

1. Un cambio compatible (campo nuevo opcional) no cambia la versión.
2. Un cambio incompatible crea `v2` y mantiene `v1` durante un plazo acordado.
3. Los campos retirados se marcan como obsoletos antes de eliminarse.
4. Cada contrato tiene una prueba de contrato en el servicio que lo publica y en el que lo consume.
