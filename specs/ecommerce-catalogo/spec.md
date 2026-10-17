---
id: ecommerce-catalogo
titulo: Catálogo de obras y ediciones
estado: Approved
servicio: E-commerce
prefijo: CAT
origen: [RF01, RF02, RF10, RF24, NN01, NN08, NN13, NN14]
calidad: [AC02, AC03, RNF03, RNF09]
depende: [inventario-fuentes, transversal-auditoria]
aprobado_por: Diego Amiquero
aprobado_en: 2026-10-16
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
| CAT-07 | Estado | Mientras la licencia de una edición digital esté vencida o no incluya Perú, el sistema deberá no ofrecerla a la venta y explicar el motivo. | RF24 |
| CAT-08 | Evento | Cuando una licencia vence o la administración retira una edición digital de la venta, el sistema deberá mantener los accesos ya concedidos a quienes la compraron, salvo reembolso, anulación u orden legal (`ecommerce-biblioteca-digital`, BIB-09). | RF24 |
| CAT-09 | Evento | Cuando un cliente busca libros, el sistema deberá permitir filtrar por título, autor, editorial, categoría, formato, rango de precio y disponibilidad, entendida como stock vendible en la red para un impreso o licencia vigente para un digital. | RF01 |
| CAT-10 | Evento | Cuando un cliente abre la ficha de una obra, el sistema deberá mostrar sus ediciones y, para la impresa, la disponibilidad consolidada y por sucursal, y para la digital, la disponibilidad inmediata. | RF02, NN13 |
| CAT-11 | Evento | Cuando cambia el precio de una edición, el sistema deberá registrarlo en la auditoría. | RF10 |
| CAT-12 | No deseado | Si el servicio de inventario no responde, entonces el sistema deberá seguir mostrando el catálogo con la última disponibilidad conocida, si tiene menos de 10 minutos, avisando que puede estar desactualizada, o indicando que no se puede confirmar si es más antigua; agregar al carrito sigue permitido y la disponibilidad real se valida al iniciar el checkout. | AC03, RNF03 |
| CAT-13 | Evento | Cuando la administración da de baja una obra, el sistema deberá dejar de mostrarla en el catálogo y conservar sus datos en los pedidos históricos. | RF10 |
| CAT-14 | Ubicuo | El sistema deberá permitir publicar, editar y dar de baja ediciones solo a la administración central. | NN08 |
| CAT-15 | Evento | Cuando la administración retira una edición digital de la venta, el sistema deberá registrarlo en la auditoría. | RF24 |
| CAT-16 | Evento | Cuando la administración sube el archivo de una edición digital o una portada, el sistema deberá verificar su tipo según su contenido (EPUB o PDF para libros; JPEG, PNG o WebP para portadas), rechazar los que superen 50 MB (libros) o 2 MB (portadas) y guardar su huella SHA-256 antes de publicarlos, rechazando la subida si el almacén no responde. | RF10, RF24 |
| CAT-17 | No deseado | Si el ISBN de una edición no tiene 10 o 13 dígitos con un dígito de control válido, entonces el sistema deberá rechazarla; y ante registros simultáneos con el mismo ISBN, deberá aceptar solo uno. | RF10 |
| CAT-18 | Evento | Cuando la administración da de baja una edición, el sistema deberá quitarla de los carritos avisando a cada cliente, y conservarla en los pedidos y traslados en curso hasta que se cierren. | RF10 |
| CAT-19 | Evento | Cuando la administración da de alta, edita o da de baja una obra o edición, el sistema deberá registrarlo en la auditoría. | RF10, RNF08 |

## 4. Criterios de aceptación

| ID | Cubre | Escenario |
| :--- | :--- | :--- |
| AC-CAT-01 | CAT-01, CAT-02, CAT-04 | **Dado** un administrador, **cuando** registra una obra con una edición impresa y su ISBN, **entonces** la obra aparece en el catálogo; y si intenta registrar una obra sin ninguna edición, se rechaza. |
| AC-CAT-02 | CAT-03 | **Dado** una edición con ISBN 978-0-00-000001-1, **cuando** el administrador registra otra edición con ese ISBN, **entonces** se rechaza indicando el título que ya lo usa. |
| AC-CAT-03 | CAT-05, CAT-06 | **Dado** un administrador que registra una edición digital con licencia comercial vigente hasta una fecha pasada, **cuando** guarda, **entonces** se rechaza; y con una fecha futura, titular, territorios y archivo, se acepta. |
| AC-CAT-04 | CAT-07 | **Dado** una edición digital con licencia vencida y otra con licencia que no incluye Perú, **cuando** un cliente abre su ficha, **entonces** ninguna se ofrece y se explica el motivo de cada una. |
| AC-CAT-05 | CAT-08, CAT-15 | **Dado** una edición digital ya comprada por un cliente, **cuando** la administración la retira de la venta, **entonces** el cliente conserva su acceso y existe una entrada de auditoría; y si el pedido se anula o reembolsa, el acceso se revoca. |
| AC-CAT-06 | CAT-09 | **Dado** un catálogo de 16 obras, **cuando** el cliente filtra por formato digital, categoría Técnico y precio máximo S/ 100, **entonces** ve solo las obras que cumplen las tres condiciones; y al filtrar por disponibilidad, un impreso sin stock vendible en la red no aparece. |
| AC-CAT-07 | CAT-10 | **Dado** una obra con edición impresa y digital, **cuando** el cliente abre su ficha, **entonces** ve las dos ediciones, la disponibilidad por sucursal de la impresa y «disponible al instante» para la digital. |
| AC-CAT-08 | CAT-11 | **Dado** una edición con precio S/ 129,00, **cuando** el administrador la cambia a S/ 139,00, **entonces** existe una entrada de auditoría con ambos precios. |
| AC-CAT-09 | CAT-12 | **Dado** que el inventario no responde, **cuando** un cliente busca libros, **entonces** el catálogo se muestra con un aviso de disponibilidad posiblemente desactualizada si tiene menos de 10 minutos, o «no se puede confirmar» si es más antigua. |
| AC-CAT-10 | CAT-13 | **Dado** una obra con pedidos históricos, **cuando** se da de baja, **entonces** deja de aparecer en el catálogo y sus pedidos siguen mostrando su título. |
| AC-CAT-11 | CAT-14 | **Dado** un encargado de sucursal, **cuando** intenta publicar o editar una edición, **entonces** se rechaza por falta de permiso. |
| AC-CAT-12 | CAT-16 | **Dado** un administrador, **cuando** sube un ejecutable renombrado como EPUB, un libro de 60 MB y una portada de 3 MB, **entonces** los tres se rechazan con su motivo; un EPUB, un PDF de exactamente 50 MB y portadas JPEG, PNG y WebP válidas de hasta 2 MB se aceptan y quedan con su huella guardada; y si el almacén no responde, la subida se rechaza. |
| AC-CAT-13 | CAT-05 | **Dado** una edición digital sin titular, y otra de dominio público sin vigencia, **cuando** se guardan, **entonces** la primera se rechaza indicando el campo faltante y la segunda se acepta. |
| AC-CAT-14 | CAT-17 | **Dado** un ISBN con dígito de control inválido, y dos administradores que registran a la vez el mismo ISBN válido, **cuando** se guardan, **entonces** el primero se rechaza por formato y de los otros dos solo uno se acepta. |
| AC-CAT-15 | CAT-18, CAT-19 | **Dado** una edición impresa en el carrito de dos clientes y en un pedido en curso, **cuando** la administración la da de baja, **entonces** se quita de los carritos con aviso, el pedido en curso sigue mostrándola y queda la auditoría. |

## 5. Reglas e invariantes

- El ISBN es único entre todas las ediciones, impresas y digitales.
- Una edición digital nunca tiene stock, reserva ni fuente.
- Una licencia de dominio público no tiene vigencia; una licencia comercial siempre la tiene.
- Una licencia vencida o que no incluye Perú retira la oferta pero no el acceso ya concedido.
- Los precios de los libros digitales se muestran sin IGV (referencial, ver `docs/01-analisis-de-sistema/03-marco-legal.md`).

## 6. Casos límite y errores

- Una obra con una edición impresa agotada y una digital vigente sigue visible y compra solo la digital.
- Una búsqueda sin resultados muestra un estado vacío con acción de limpiar filtros.
- La disponibilidad por sucursal solo se muestra para impresos.

## 7. Preguntas abiertas

- [x] ¿Cómo se determina el territorio del comprador? Resuelto el 2026-10-16 (confirmado por el responsable): el territorio se registra en la licencia y se asume Perú; no se valida el del comprador en esta versión.
- [x] ¿La imagen de portada se sube al almacén de objetos o se enlaza una URL externa? Resuelto el 2026-10-16: se sube al almacén de objetos y se valida (CAT-16).
