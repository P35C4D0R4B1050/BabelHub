---
spec: ecommerce-biblioteca-digital
---

# Decisiones de implementación — Biblioteca y descarga de libros digitales

Lo que el código decidió al implementar y que el plan aprobado no fija (`decisions.md` no se edita).

| ID | Decisión | Motivo |
| :--- | :--- | :--- |
| I01 | **Se conserva el nombre `MotivoDeRevocacionDeAcceso`** (declarado por Pedidos y usado por Pedidos y Devoluciones) y se le **añade `Reembolso`** (cambio aditivo). No se renombra a `MotivoDeRevocacion` ni se mueve de archivo. | Un renombrado tocaría el código y las pruebas de otros dos carriles sin cambiar el comportamiento; el plan (D01) pide un único tipo del motivo propiedad de Biblioteca, y lo cumple: es el único tipo. Dentro de Biblioteca, `Acceso` usa `CausaDeRevocacion` y `MapeoDeBiblioteca.Causa` los une. |
| I02 | Los motivos de lista cerrada que el plan no enumera: **restitución** `descarga-fallida`, `archivo-defectuoso`, `error-del-sistema`, `otro`; **ayuda** (los del plan) `archivo-no-abre`, `archivo-no-disponible`, `descargas-agotadas`, `otro`. | D06 pide lista cerrada sin texto libre; AC-BIB-07 nombra «descarga fallida». |
| I03 | `ClaveDeDescarga` solo acepta de 8 a 128 caracteres ASCII imprimibles sin espacios (la interfaz genera 32 hexadecimales); se guarda su SHA-256 y `ToString` no la muestra. | La forma exacta la fija `web-base` (D18); aquí solo se acota. |
| I04 | El **formato** de «Mi biblioteca» es el del archivo (`EPUB` o `PDF`, de `IReferenciaDeArchivosDelCatalogo`); si esa lectura falla, el acceso se muestra sin formato (no se rompe la lista); si falla `IConsultaDeCatalogo`, la lista falla cerrada. | `EdicionConsultada.Formato` solo distingue impresa de digital. |
| I05 | `RevocarAcceso` y `ConcederAcceso` **propagan** la excepción de la auditoría o de la base cuando ya hubo un efecto, y devuelven `Fallo` solo antes de cualquier efecto. | Participan en la transacción del llamador (D04): un `Fallo` tras el primer efecto la confirmaría a medias. |
