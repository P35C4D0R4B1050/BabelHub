# Decisiones de implementación — ecommerce-catalogo

Decisiones menores tomadas al implementar que el plan no fija. No cambian el comportamiento de la spec.

| Tarea | Decisión | Motivo |
| :--- | :--- | :--- |
| T01 | Las pruebas de ISBN usan `978-0-00-000001-9`, el ISBN-13 válido: el ejemplo de AC-CAT-02 en la spec aprobada (`978-0-00-000001-1`) tiene el dígito de control inválido (hallazgo heredado ya registrado en `progress/hallazgos.md`; la spec no se edita). | CAT-17: el dígito de control se valida. |
| T01 | `Isbn` y `Precio` son clases inmutables con `TryCrear`/`Crear` (el Dominio no referencia el `Resultado` de la Aplicación). El texto crudo del ISBN tiene un tope de 40 caracteres antes de recortar; la `x` minúscula del ISBN-10 se admite igual que la `X`. `Precio` acepta `0` y hasta `9 999 999 999,99` (el máximo de `numeric(12,2)`). | D02, D09: la spec no impone «mayor que cero» ni un máximo propio. |
| T02, T03, T04 | Orden de implementación: T03 y T04 antes de T02, porque `Obra` y `Edicion` (T02) usan `LicenciaDigital` (T03) y `ArchivoDeEdicion` (T04); cada una compila y se commitea por separado. | T02 «una digital tiene licencia y archivo» no compila sin ellas. |
| T03 | «Vigente hasta» es inclusivo (el día del vencimiento todavía rige; vence al día siguiente) y registrar o cambiar una licencia exige `vigenteHasta` **posterior** a hoy en Lima (un día igual a hoy se rechaza). El `MotivoDeNoDisponible` de una licencia vencida que además no incluye Perú es `LicenciaVencida`. Los territorios son códigos ISO de dos letras en mayúsculas (se normalizan, sin repetir, hasta 250). La licencia devuelve los errores de campo con una clave estable (`obligatorio`, `formato-invalido`, `vigencia-no-posterior-a-hoy`, `no-aplica`, `demasiado-largo`). | CAT-05 a CAT-08, AC-CAT-03, AC-CAT-13: la spec no fija el borde del día ni la prioridad del motivo. |
