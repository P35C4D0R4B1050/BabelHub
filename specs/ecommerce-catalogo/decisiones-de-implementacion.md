# Decisiones de implementación — ecommerce-catalogo

Decisiones menores tomadas al implementar que el plan no fija. No cambian el comportamiento de la spec.

| Tarea | Decisión | Motivo |
| :--- | :--- | :--- |
| T01 | Las pruebas de ISBN usan `978-0-00-000001-9`, el ISBN-13 válido: el ejemplo de AC-CAT-02 en la spec aprobada (`978-0-00-000001-1`) tiene el dígito de control inválido (hallazgo heredado ya registrado en `progress/hallazgos.md`; la spec no se edita). | CAT-17: el dígito de control se valida. |
| T01 | `Isbn` y `Precio` son clases inmutables con `TryCrear`/`Crear` (el Dominio no referencia el `Resultado` de la Aplicación). El texto crudo del ISBN tiene un tope de 40 caracteres antes de recortar; la `x` minúscula del ISBN-10 se admite igual que la `X`. `Precio` acepta `0` y hasta `9 999 999 999,99` (el máximo de `numeric(12,2)`). | D02, D09: la spec no impone «mayor que cero» ni un máximo propio. |
