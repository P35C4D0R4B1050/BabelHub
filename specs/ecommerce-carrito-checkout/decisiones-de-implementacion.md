# Decisiones de implementación — ecommerce-carrito-checkout

Decisiones menores tomadas al implementar que el plan no fija. No cambian el comportamiento de la spec.

| Tarea | Decisión | Motivo |
| :--- | :--- | :--- |
| T01 | El Dominio devuelve `ResultadoDeLinea` con un `RechazoDeCarrito` (no excepciones) para los flujos esperados (cantidad fuera de 1 a 99, línea 51, suma de más de 99 unidades, edición ausente, carrito fusionado); la Aplicación los traduce a `ECO-…`. Una digital pedida con más de una unidad se **acepta y queda en una** (AC-CAR-02: «deja la digital en una unidad»), no se rechaza. | CAR-02, CAR-03, D14. |
| T01 | «A los 30 días» es inclusivo hacia la caducidad: un carrito de visitante con la última acción hace exactamente 30 días **ya caducó** (a 29 días y 23 horas no); la misma regla usa el contacto del checkout (T06). La caducidad solo se mueve con una operación que cambia el carrito y es aceptada (un rechazo o un `GET` no la mueven). | CAR-29, CAR-34, AC-CAR-25. |
| T01 | El carrito no lleva precios (los lee el servidor del Catálogo): `Total` recibe el precio de cada edición y falla si falta el de una línea, en vez de suponer cero. El importe de una línea es precio por cantidad redondeado a dos decimales alejándose de cero (`Soles`). | D17: el total se calcula en el servidor. |
