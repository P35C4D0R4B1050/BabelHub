# COMP-1 — composición del Inventario (2026-11-19)

**Hecho.** `Program.cs` del Inventario compone el servicio completo con tres llamadas (`AgregarInventario`, `UsarInventario`, `ExigirComposicionDeProduccion`):

- Plataforma: reloj, actor, huella HMAC, límites sobre PostgreSQL con respaldo local, matriz de alcances declarada por los cinco módulos, credenciales de servicio por alcance, tope general por dirección directa.
- Auditoría, Fuentes, Reservas, Ajustes y Traslados (con la salida al E-commerce) y sus trabajos de fondo, **solo en modo normal** (PLA-19).
- Verificación de credenciales de usuario (identidad-seguridad T41): `CredencialDeUsuarioMiddleware` fija el actor solo desde la credencial verificada; renovación de la clave cada minuto.
- Guarda de producción: `OpcionesDelAnfitrionDelInventario.Leer` junta todos los problemas y falla nombrando la variable (nunca su valor); `ExigirComposicionDeProduccion` comprueba adaptadores reales, clave de bajas, estrategia de selección y que toda operación esté en la matriz.

**Pruebas.** `AnfitrionDelInventarioTests` (30: grafo validado, contratos, modo de restauración sin trabajos, cada variable obligatoria, secretos débiles o repetidos, direcciones) y `CredencialDeUsuarioDelInventarioTests` (10: actor solo de la credencial, rechazos, ninguna cabecera de actor vale, el token no sigue el pipeline). Build `-warnaserror` sin avisos, 230 de arquitectura.

**Pendiente.** Ver la fila COMP-1 de `progress/hallazgos.md` (migraciones y roles en el despliegue, prueba de arranque contra PostgreSQL migrado, compose) y el `Program.cs` del E-commerce.
