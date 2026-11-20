# Documentos OpenAPI de los dos servicios (OAS-1)

| Archivo | Servicio | Qué describe |
| :--- | :--- | :--- |
| [`inventario.v1.json`](inventario.v1.json) | Inventario (`/api/inventario/v1`) | Las 41 operaciones del contrato v1 que consume el E-commerce, más 3 rutas de usuario aún fuera del catálogo y las sondas de salud |
| [`ecommerce.v1.json`](ecommerce.v1.json) | E-commerce (`/api/ecommerce/v1`) | Las rutas que el host mapea hoy: la solicitud de avisos del Inventario, la administración de avisos (sesión web) y las sondas de salud. Las rutas de claves públicas de Identidad (JWKS), n8n, pasarela y auditoría están descritas en `DocumentacionOpenApi` pero aparecen en el documento cuando el host las mapee (ver el informe OAS-1) |

OpenAPI 3.0, **generados desde el código**, no escritos a mano:

- Las **rutas, los métodos y los parámetros de ruta** salen de los endpoints realmente mapeados (`Microsoft.AspNetCore.OpenApi`).
- El **alcance de la credencial, el cuerpo, la respuesta, los parámetros de consulta, la `Idempotency-Key` y los códigos de error** salen del catálogo de contratos v1 de `BabelHub.Shared` (`CatalogoDeOperaciones.V1`), cuyos DTOs se convierten en esquemas en `components/schemas`.
- Los errores se describen como `application/problem+json` con el esquema `ProblemaDto` (`code` estable y `traceId`).
- **Seguridad:** `credencialDeServicio` (cabecera `X-Credencial-De-Servicio`, alcance en la extensión `x-alcance`), `tokenDeUsuario` (JWT ES256 de Identidad) y, solo en el E-commerce, `sesionWeb` (cookie con antifalsificación). Las rutas públicas (sondas, JWKS) declaran `security: []`.
- La interfaz web (vistas Razor del E-commerce) **no** es parte del contrato REST y no se documenta.

## Dónde se sirve

En `Development`, cada servicio sirve su documento en `/openapi/v1.json`. En producción no se expone.

## Cómo se mantiene al día

Las pruebas `DocumentacionOpenApiInventarioTests` y `DocumentacionOpenApiEcommerceTests` (`tests/integration`, sin Docker) componen el servicio real y:

1. **fallan si una ruta pública no está documentada** (no está en el documento, no está en el catálogo de `Shared` ni en las rutas complementarias de la Api, o no declara su seguridad);
2. verifican que cada operación del catálogo aparece con su alcance, su cuerpo, su `Idempotency-Key` y sus errores `problem+json`;
3. comparan estos archivos con el documento generado. Si cambia una ruta o un DTO, se regeneran con:

```powershell
$env:BABELHUB_ACTUALIZAR_OPENAPI = "1"
dotnet test tests/integration/BabelHub.IntegrationTests --filter "FullyQualifiedName~DocumentacionOpenApi"
Remove-Item Env:BABELHUB_ACTUALIZAR_OPENAPI
```

Una ruta nueva se documenta **agregando su operación al catálogo** (`OperacionesDelInventario` / `OperacionesDelEcommerce`, cambio aditivo en `Shared`) o, si no es del contrato entre servicios, a las `Complementarias` de `DocumentacionOpenApi` de su Api.
