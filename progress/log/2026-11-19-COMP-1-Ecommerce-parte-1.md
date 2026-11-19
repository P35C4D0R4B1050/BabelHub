# COMP-1 — composición del E-commerce, parte 1 (2026-11-19)

**Hecho.** `Program.cs` del E-commerce compone el servicio con `AgregarEcommerce`, `UsarEcommerce` y `ExigirComposicionDeProduccion` (más el modo de restauración que ya tenía):

- Plataforma (`ComposicionDeLaPlataformaDelEcommerce`): reloj, unidad de trabajo sobre PostgreSQL, huella HMAC, credenciales de servicio, matriz de alcances (vacía: el E-commerce aún no expone operaciones de servicio), límites (Redis dedicado con el contador local como respaldo; sin Redis solo fuera de producción), origen del borde con los proxies conocidos. El actor lo registra la Api (solo ella lo fija).
- Auditoría con sus tres trabajos, el cliente de auditoría del Inventario, el cliente del Inventario con el token del usuario (regla 9) y el registro de supresiones sobre el almacén de objetos.
- Correo (`ComposicionDelCorreo`): cuota, cola cifrada, transporte SMTP (sin él, solo fuera de producción, no sale nada), renderizador con las plantillas de seguridad, Pagos y reclamaciones, enviador de producción.
- Identidad (composición real de PLA-X) con sus trabajos; Catálogo real (`AgregarCatalogoPendiente` ya no se llama); Carrito real con sus trabajos y `UsarLimiteDeCuerpoDelCarrito`; la sesión de la web (`ComposicionDeLaSesionWeb`: cifrador, almacenes, `SesionesWeb`, purga con alcance por corrida, claves de Data Protection en la base); verificación de persona (Turnstile).
- Guarda de producción: `OpcionesDelAnfitrionDelEcommerce.Leer` junta todos los problemas y falla nombrando la variable (nunca su valor); un adaptador provisional (`ICancelacionDeAvisos`, los dobles del Carrito) falla el arranque en producción **solo en el modo normal**.
- `infra/secretos-requeridos.txt`: las variables nuevas de `ecommerce.env` y `ecommerce-gestion.env` (la clave de sesiones es por host).

**Pruebas.** `AnfitrionDelEcommerceTests` (31: grafo validado con `ValidateOnBuild`, superficie de servicio, modo de restauración sin trabajos, cada variable obligatoria, secretos repetidos, Redis/SMTP/proxies solo opcionales fuera de producción, guarda de producción). `ArranqueSeguroEcommerceTests` arranca el proceso con configuración completa; la restauración corre sus pasos reales (sin base de datos falla en el primero, nombrándolo).

**Cambios a piezas existentes.** `TrabajoDePurgaDeSesionesWeb` acepta un `IServiceScopeFactory` (un alcance por corrida: la purga depende de Identidad y Auditoría, que son de alcance de solicitud); los trabajos del Carrito publican sus alertas en su propio alcance (`AlertasTecnicasPorCorrida`); `EjecutorDePasosDeRestauracion` pasa a ser de alcance.

**Pendiente (parte 2 y despliegue).** Pagos (incluida la política de ráfaga de avisos y su exención de SEG-18), Envíos, Reclamaciones y Pedidos, que aún no tienen composición; las migraciones y los roles de PostgreSQL no los crea ningún paso del despliegue (sin ellos el host no tiene esquema para el E2E); Notificaciones (NOT-2 a NOT-4) y Biblioteca siguen provisionales, así que **producción no arranca todavía**.
