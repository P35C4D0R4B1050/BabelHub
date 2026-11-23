# 2026-11-23 — Catálogo inicial (descripción de la obra) y carga inicial del catálogo y del inventario

Carril `catalogo-inicial` (worktree `agent-aa5cef21224bfb34a`). Motivo: la revisión de la pasarela de pagos exige al menos cinco productos con foto, descripción y precio visibles.

## Qué se hizo
- **Selección** de 40 obras (35 reales con portada de buena resolución y 5 genéricas del prototipo con portada generada) a partir de 68 capturas: `docs/07-proceso-de-desarrollo/09-seleccion-del-catalogo-inicial.md` (cada exclusión con su motivo). Datos en `tools/carga-inicial/` (`catalogo.csv`, `sucursales.csv`, `rutas.csv`, `plan-de-referencia.json` y los scripts `completar-datos.ps1` —ISBN provisional 979-0, precio y stock por FNV-1a con semilla fija— y `generar-portadas-genericas.ps1`). Las imágenes viven fuera del repositorio (`D:\Trabajo\BHPortadas`).
- **SDD.** `ecommerce-catalogo` volvió a Draft por la descripción opcional (CAT-01, CAT-14, CAT-24, CAT-25, AC-CAT-20; 3 rondas de spec y 2 de plan con los tres revisores; T29 a T33 hechas y spec en `Done`). Spec nueva `transversal-carga-inicial` (CIN-01 a CIN-20, AC-CIN-01 a 13; Approved y Planned; 3 rondas de spec y 3 de plan): `InProgress` con la reserva de T08 parcial. Decisiones DD-70 a DD-74.
- **Código.** `Descripcion` de la obra; en el E-commerce `cargar-catalogo` (lector del archivo de datos, caso de uso idempotente por ISBN+título+autor, carpeta de portadas, EPUB de muestra, plan de inventario JSON v1, registro de la corrida); en el Inventario `cargar-stock` (lectores estrictos, sucursales, rutas, registro de la edición, ingreso en la recepción y corrección de conteo en las demás, pre-comprobación, presupuesto SEG-30 con `--esperar`); `Shared` con `IdentidadesDeOperacion.CargaInicial` y la acción `CargaInicialEjecutada`; modo `Operacion` en las dos copias de `ModoDeArranque`; guardias de credencial.
- **Guía de despliegue** §14b (comandos exactos para VM-VENTAS y VM-INVENTARIO y la subida de portadas); `docs/03/04` §7, `docs/05` (excepción de la identidad de operación).

## Qué se probó de verdad
Build `-warnaserror` sin advertencias; unitarias del E-commerce y del Inventario; arquitectura 261; integración con PostgreSQL y almacén S3 reales (`CargaInicial` y `Catalogo`); **ejecución real de punta a punta** en un entorno aislado: `cargar-catalogo` con el CSV y las 40 portadas cargó 40 obras (≈11 s), la repetición dio 40 «ya cargadas» y un plan idéntico byte a byte; `cargar-stock` cargó 6 sucursales, 10 rutas, 40 ediciones y 60 operaciones antes de agotar el presupuesto (salida 2, reanudable); el catálogo mostró 40 títulos con portada, autor y precio, y la ficha, su descripción.

## Qué no
`ui-review` y Playwright; la integración completa del repositorio; el `cargar-stock` completo con `--esperar` (más de una hora); la prueba de integración de los guardias de credencial (T08 parcial). Ver `progress/ajustes-al-implementar.md` §12 y `progress/hallazgos.md`.

## Defectos reales hallados
La ficha mostraba el precio de una digital que no se ofrece; el catálogo escribía los libros digitales en el contenedor de supresiones (`ContenedorDeLibros`); la auditoría redacta un hexadecimal de 64 (huella): se guardan 16.
