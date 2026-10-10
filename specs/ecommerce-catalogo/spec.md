---
id: ecommerce-catalogo
titulo: Catálogo de obras y ediciones
estado: Draft
servicio: E-commerce
prefijo: CAT
origen: [RF01, RF02, RF10, RF24, NN01, NN08, NN13, NN14]
calidad: [AC02, AC03, RNF03, RNF09]
depende: [inventario-fuentes, transversal-auditoria]
---

# Spec — Catálogo de obras y ediciones

## 1. Propósito

Un único catálogo nacional para toda la red. Una **obra** puede tener una edición impresa, una digital o ambas; cada **edición** tiene su ISBN y su precio. El catálogo permite buscar, ver disponibilidad real y, para los libros digitales, respeta la licencia del titular de derechos.

## 2. Alcance

| Dentro | Fuera |
| :--- | :--- |
| Alta, edición y baja de obras y ediciones por la administración central. | Stock por sucursal (`inventario-fuentes`). |
| Búsqueda y filtros del cliente, ficha de la obra y su disponibilidad. | El carrito y el pago (`ecommerce-carrito-checkout`). |
| Licencia de las ediciones digitales: titular, territorio y vigencia. | Contratos reales con editoriales y regalías (proyecto académico). |

## 3. Requisitos (EARS)

| ID | Patrón | Requisito | Origen |
| :--- | :--- | :--- | :--- |
| CAT-01 | Ubicuo | El sistema deberá permitir a la administración central registrar una obra con título, autor, editorial, categoría e imagen de portada. | RF10 |
| CAT-02 | Ubicuo | El sistema deberá permitir registrar ediciones impresas y digitales de una obra, cada una con su ISBN y su precio. | RF10 |
| CAT-03 | No deseado | Si se registra una edición con un ISBN que ya existe en cualquier edición del catálogo, entonces el sistema deberá rechazarla indicando el título que ya lo usa. | RF10 |
| CAT-04 | Ubicuo | El sistema deberá exigir que toda obra tenga al menos una edición. | RF10 |
| CAT-05 | Ubicuo | El sistema deberá exigir, para una edición digital, el formato, el archivo, el titular de derechos, el tipo de licencia, los territorios y la vigencia. | RF10, NN14 |
| CAT-06 | No deseado | Si una licencia comercial se registra con una vigencia que no es posterior a la fecha actual, entonces el sistema deberá rechazar la edición digital. | RF24 |
| CAT-07 | Estado | Mientras la licencia de una edición digital esté vencida o no cubra el territorio del comprador, el sistema deberá no ofrecerla a la venta y explicar el motivo. | RF24 |
| CAT-08 | Evento | Cuando una licencia vence o la administración retira una edición digital de la venta, el sistema deberá mantener los accesos ya concedidos a quienes la compraron. | RF24 |
| CAT-09 | Evento | Cuando un cliente busca libros, el sistema deberá permitir filtrar por título, autor, editorial, categoría, formato, rango de precio y disponibilidad. | RF01 |
| CAT-10 | Evento | Cuando un cliente abre la ficha de una obra, el sistema deberá mostrar sus ediciones y, para la impresa, la disponibilidad consolidada y por sucursal, y para la digital, la disponibilidad inmediata. | RF02, NN13 |
| CAT-11 | Evento | Cuando cambia el precio de una edición, el sistema deberá registrarlo en la auditoría. | RF10 |
| CAT-12 | No deseado | Si el servicio de inventario no responde, entonces el sistema deberá seguir mostrando el catálogo con la última disponibilidad conocida y avisar que puede estar desactualizada. | AC03, RNF03 |
| CAT-13 | Evento | Cuando la administración da de baja una obra, el sistema deberá dejar de mostrarla en el catálogo y conservar sus datos en los pedidos históricos. | RF10 |
| CAT-14 | Ubicuo | El sistema deberá permitir publicar, editar y dar de baja ediciones solo a la administración central. | NN08 |
| CAT-15 | Evento | Cuando la administración retira una edición digital de la venta, el sistema deberá registrarlo en la auditoría. | RF24 |

## 4. Criterios de aceptación

| ID | Cubre | Escenario |
| :--- | :--- | :--- |
| AC-CAT-01 | CAT-01, CAT-02, CAT-04 | **Dado** un administrador, **cuando** registra una obra con una edición impresa y su ISBN, **entonces** la obra aparece en el catálogo; y si intenta registrar una obra sin ninguna edición, se rechaza. |
| AC-CAT-02 | CAT-03 | **Dado** una edición con ISBN 978-0-00-000001-1, **cuando** el administrador registra otra edición con ese ISBN, **entonces** se rechaza indicando el título que ya lo usa. |
| AC-CAT-03 | CAT-05, CAT-06 | **Dado** un administrador que registra una edición digital con licencia comercial vigente hasta una fecha pasada, **cuando** guarda, **entonces** se rechaza; y con una fecha futura, titular, territorios y archivo, se acepta. |
| AC-CAT-04 | CAT-07 | **Dado** una edición digital con licencia vencida, **cuando** un cliente abre su ficha, **entonces** la edición no se ofrece y se explica que la licencia venció. |
| AC-CAT-05 | CAT-08, CAT-15 | **Dado** una edición digital ya comprada por un cliente, **cuando** la administración la retira de la venta, **entonces** el cliente conserva su acceso y existe una entrada de auditoría. |
| AC-CAT-06 | CAT-09 | **Dado** un catálogo de 16 obras, **cuando** el cliente filtra por formato digital, categoría Técnico y precio máximo S/ 100, **entonces** ve solo las obras que cumplen las tres condiciones. |
| AC-CAT-07 | CAT-10 | **Dado** una obra con edición impresa y digital, **cuando** el cliente abre su ficha, **entonces** ve las dos ediciones, la disponibilidad por sucursal de la impresa y «disponible al instante» para la digital. |
| AC-CAT-08 | CAT-11 | **Dado** una edición con precio S/ 129,00, **cuando** el administrador la cambia a S/ 139,00, **entonces** existe una entrada de auditoría con ambos precios. |
| AC-CAT-09 | CAT-12 | **Dado** que el inventario no responde, **cuando** un cliente busca libros, **entonces** el catálogo se muestra con un aviso de disponibilidad posiblemente desactualizada. |
| AC-CAT-10 | CAT-13 | **Dado** una obra con pedidos históricos, **cuando** se da de baja, **entonces** deja de aparecer en el catálogo y sus pedidos siguen mostrando su título. |
| AC-CAT-11 | CAT-14 | **Dado** un encargado de sucursal, **cuando** intenta publicar o editar una edición, **entonces** se rechaza por falta de permiso. |

## 5. Reglas e invariantes

- El ISBN es único entre todas las ediciones, impresas y digitales.
- Una edición digital nunca tiene stock, reserva ni fuente.
- Una licencia de dominio público no tiene vigencia; una licencia comercial siempre la tiene.
- Una licencia vencida o fuera de territorio retira la oferta pero no el acceso ya concedido.
- Los precios de los libros digitales se muestran sin IGV (referencial, ver `docs/02`, sección 5).

## 6. Casos límite y errores

- Una obra con una edición impresa agotada y una digital vigente sigue visible y compra solo la digital.
- Una búsqueda sin resultados muestra un estado vacío con acción de limpiar filtros.
- La disponibilidad por sucursal solo se muestra para impresos.

## 7. Preguntas abiertas

- [ ] ¿Cómo se determina el territorio del comprador: por su dirección, por su país de registro o no se valida en esta versión? Propuesta: se registra en la licencia pero se asume Perú. Responde: responsable del proyecto.
- [ ] ¿La imagen de portada se sube al almacén de objetos o se enlaza una URL externa? Propuesta: almacén de objetos. Se decide en el plan.
