# Cierre del carril A (2026-11-20)

Encargo del coordinador: (1) lo que quede de COMP-1; (2) hallazgos Importantes del Sprint 2 de plataforma, identidad y auditoría; (3) `spec-verify` de las specs que apliquen. Condiciones: revisores sin Bloqueantes ni Importantes, pruebas verdes, máximo tres rondas, sin Docker. Rama `claude/competent-khayyam-1d6d28`. Sin push.

## 1. COMP-1

- `UsarEcommerce` mapea ya `MapearAuditoria`, `MapearClavesDeIdentidad` (publicación en los dos hosts; rotar y declarar comprometida solo en gestión) y los estados de traslado de Pedidos. El documento OpenAPI publicado se regeneró y `PendientesDeImplementar` quedó vacío: todas las operaciones del contrato v1 del E-commerce están servidas y documentadas.
- `/health/ready` de los dos servicios comprueba la base de datos (modo normal).
- Los dos `Program.cs` componen el servicio completo (Inventario: `AgregarInventario`/`UsarInventario`/`ExigirComposicionDeProduccion`; E-commerce: ídem, con Pagos, Envíos, Reclamaciones, Pedidos, Biblioteca y Notificaciones). **Lo que no se puede cerrar sin la persona responsable o sin Docker:**
  - la prueba de arranque contra PostgreSQL con **todas** las migraciones aplicadas y una solicitud de punta a punta (necesita contenedores);
  - la topología de despliegue (réplica de gestión de Identidad frente al Inventario, TLS a PostgreSQL y Redis, `SslMode`) y los pasos de despliegue que crean roles y aplican migraciones en la máquina real (la herramienta existe: modo `migrar`, COMP-2);
  - el adaptador de Pagos «culqi» (hoy solo el simulador y la pasarela nula) y la primera clave de firma y los textos legales en producción (hallazgo FIN-2).

## 2. Hallazgos Importantes del Sprint 2 (carril A)

| Hallazgo | Estado |
| :--- | :--- |
| `IDestinatariosDeAlertas` de logística (TRA-22) | Ya estaba (`DeLogistica` y `MotivosParaLogistica`, con prueba). **Cerrado** junto con los motivos propios de la entrega del buzón. |
| Motivos de alerta de la entrega del buzón del Inventario | **Hecho:** `aviso-de-traslado-no-entregado`, `estado-de-traslado-sin-entregar`, `entrega-de-traslado-con-error-permanente` y `credencial-del-ecommerce-rechazada` en los dos catálogos espejo y en `Shared`; `EntregarMensajesSalientes` publica el motivo de cada caso; pruebas unitarias y de integración. |
| Motivos de alerta de Pedidos | **No faltan:** los motivos que usan Pedidos, Envíos, Devoluciones y Biblioteca están en el catálogo cerrado (comprobado por búsqueda). |
| Contrato de sistema del correo verificado (Reclamaciones, IDE-84, PLA-19) | **Hecho:** `ICorreoVerificadoDeSistema` (contrato público de Identidad, solo lee, sin comprobar titularidad ni actor; una cuenta cancelada no entrega correo) y `CorreoVerificadoDeLaCuenta` lo usa. |
| SEG-31 (verificación de persona) en registro e ingreso | **Ya está:** `[VerificacionDePersona]` en `RegistroController` e `IngresoController` (`AccionProtegida.Registro` e `Ingreso`) con pruebas (`VerificacionDePersonaWebTests`, `ExigirVerificacionDePersonaTests`). El hallazgo estaba desactualizado. |
| Tope de 24 correos al día (IDE-82) | **Ya está** en el puerto de correo (`EnvioDeCorreo`, `CorreoPostgres`) con `EnvioDeCorreoDeAlertasDeProduccionTests`. Queda el ajuste de **texto** de las specs de auditoría y seguridad (AUD-29, SEG-09), que exige reabrirlas: lo decide la persona responsable. |
| Lista de usuarios, sesión reconocida, sucursal por código (D20), `Id` de fuente derivado | Hechos en rondas anteriores (commits `cf856c3b`, `7fc74b69`, `41cc7a6b`). |
| Renovación de la sesión actual | Es del carril C (`ISesionesWeb`). Sin cambios. |

## 3. spec-verify

No se pudo ejecutar el `spec-verify` completo: exige `dotnet test BabelHub.sln` entero, que incluye las pruebas de integración con PostgreSQL, Redis y Testcontainers, y Docker estuvo caído durante este encargo. Ninguna spec pasa a `Done`. Lo que sí se comprobó en esta pasada:

- Compilación con `-warnaserror` sin avisos; unitarias del E-commerce y del Inventario, arquitectura (243) y `SpecCheck` (20 specs, 0 avisos) en verde.
- **Trazabilidad:** todos los criterios de aceptación de `identidad-seguridad` (66), `transversal-auditoria` (32), `transversal-seguridad` (29), `transversal-notificaciones` (16), `inventario-fuentes` (18), `inventario-seleccion-fuente` (40), `inventario-ajustes` (18) e `inventario-traslados` (24) tienen al menos una prueba con su `Trait`. Sin prueba que los nombre solo quedan 13 criterios de `transversal-plataforma` (AC-PLA-05 a 08, 10, 12, 25, 29 a 34: los de los spikes y de PLA-R2, R3 y R4, fuera de este encargo).
- Búsqueda de secretos en `src`: sin hallazgos (la única coincidencia lee la contraseña de una variable de entorno).
- Los `verificacion.md` existentes (`inventario-*`, `transversal-auditoria`, `transversal-seguridad`) siguen con **No converge** por la misma razón (integración no ejecutada) y por Menores abiertos; este informe no los cambia. `identidad-seguridad` y `transversal-notificaciones` no tienen `verificacion.md` todavía: se escriben con la corrida completa de integración.

**Siguiente paso recomendado:** con Docker, `dotnet test BabelHub.sln` completo; si converge, `spec-verify` requisito por requisito de `identidad-seguridad` (88 requisitos: conviene un carril dedicado), y pasar a `Done` solo con `Resultado: Converge`.

## 4. Revisión

Ronda 1 (seguridad y arquitectura, solo lectura): **sin Bloqueantes ni Importantes**. Menores atendidos: una cuenta cancelada no entrega correo (con prueba), la excepción del repositorio sube (con prueba), la composición real resuelve el contrato nuevo, regla de arquitectura que prohíbe a la Api los contratos de sistema de Identidad (`ICorreoVerificadoDeSistema` e `IContactoDeAvisosDelCliente`), lista de interfaces del módulo, nombres y trazas de pruebas, textos de la alerta y comentarios. Menores abiertos: clase de `credencial-del-ecommerce-rechazada` (técnica o de seguridad) y la cuota de correo de alertas del Inventario (se anota en el seguimiento).

**Estado de la verificación:** los cambios de código y las unitarias de la ronda 1 están compilados y en verde (`3c0c0989`). Quedan **editados sin compilar ni commitear** cuatro archivos de prueba (la regla de arquitectura de `ReglasDeCredencialesEnLaApiTests` y los de integración `ComposicionDeIdentidadTests`, `ComposicionYTrabajosDeReclamacionesTests` y `DocumentacionOpenApiEcommerceTests`) hasta poder correr el `.sln` completo, la arquitectura y la integración filtrada.

## 5. `spec-verify` por correr y por qué

| Spec | Falta | Motivo |
| :--- | :--- | :--- |
| `identidad-seguridad` | `verificacion.md` (requisito por requisito, IDE-1 a IDE-88) | Integración con PostgreSQL y revisión larga: un carril dedicado, con Docker. |
| `transversal-notificaciones` | `verificacion.md` | Integración con PostgreSQL y Redis; y la prueba de humo del runtime real de n8n (FIN-2). |
| `transversal-auditoria`, `transversal-seguridad` | Repetir la verificación existente (No converge) | Integración no ejecutada y Menores abiertos. |
| `inventario-fuentes`, `inventario-seleccion-fuente`, `inventario-ajustes`, `inventario-traslados` | Repetir (No converge, salvo ajustes) | Integración con PostgreSQL; D20 cambió el `Id` de las fuentes y los motivos de alerta de traslados: hay que re-ejecutar sus pruebas de PostgreSQL. |
| `transversal-plataforma` | Cerrar PLA-R2, R3, R4 y los spikes | Fuera de este encargo y de Docker. |
