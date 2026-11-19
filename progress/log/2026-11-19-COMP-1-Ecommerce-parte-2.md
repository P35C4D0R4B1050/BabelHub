# COMP-1 — composición del E-commerce, parte 2 (2026-11-19)

**Hecho.** El anfitrión del E-commerce compone también Pagos, Envíos, Reclamaciones y Pedidos (hasta donde existen) con sus trabajos de fondo (solo en modo normal):

- **Pagos:** `AgregarPagos` con las opciones de `LectorDeOpcionesDePagos` (`BABELHUB__Pagos__Pasarela`, obligatoria, y `BABELHUB__Pagos__ClaveDeIntentos`, secreto de 32 caracteres o más que entra en la comparación contra los demás secretos); en el ensayo y al restaurar la pasarela es siempre la nula; «culqi» falla el arranque nombrando la variable (sin adaptador, T37) y el simulador solo vale en Development y Test.
- **Ráfaga de avisos de la pasarela** (pendiente de PLA-Y): `LimiteDeAvisosDeLaPasarelaMiddleware` reserva `avisos-de-la-pasarela-por-origen` (120 por minuto por origen) **antes** de verificar la firma y la ruta queda **exenta** del tope previo y del límite sin sesión de 60 por minuto (SEG-18); `MapearAvisosDeLaPasarela` sigue deshabilitada (no hay esquema de firma verificable hasta el adaptador de Culqi).
- **Plataforma:** el procesador de entradas de integración (firma HMAC, sin proveedor habilitado: todo se rechaza) y la verificación de persona (Turnstile real en modo normal, nulo en el ensayo y al restaurar; adaptador saliente «turnstile»).
- **Envíos:** `AgregarEnvios` y el operador logístico (nulo por defecto; el simulador solo en Development y Test; siempre nulo en el ensayo y al restaurar), con `BABELHUB__Envios__Operador`.
- **Reclamaciones** y **Pedidos** (con sus dobles provisionales, que en producción en modo normal fallan el arranque); `INotificador` provisional (`NotificadorPendiente`, responde «no se guardó») hasta que exista Notificaciones.
- Los trabajos de Pagos y de Envíos publican sus alertas en su propio alcance (`AlertasTecnicasPorCorrida`).

**Pruebas.** `AnfitrionDelEcommerceTests` (44: grafo validado con todos los módulos, pasarela y verificador nulos en el ensayo, pasarela sin adaptador o desconocida, operador logístico, Pagos obligatorio y su clave contra los demás secretos) y `LimiteDeAvisosDeLaPasarelaTests` (la ráfaga antes de la firma, 429 con `Retry-After`, solo el POST de esa ruta y la exención). `infra/secretos-requeridos.txt` suma las variables de Pagos y Envíos.

**Pendiente.** Producción sigue sin arrancar (la guarda nombra `ICancelacionDeAvisos`, `INotificador`, los dobles de Carrito, Pagos y Pedidos); el adaptador de Culqi (T37); Notificaciones; el esquema de Pedidos; las pantallas de Pagos, Envíos y Reclamaciones por sus controladores (ya mapeados por `MapDefaultControllerRoute`).
