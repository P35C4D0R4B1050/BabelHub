---
id: web-indice-infinito
titulo: El índice infinito (experimento opcional)
estado: Draft
servicio: Web
prefijo: IND
origen: [RC01, NN15]
calidad: [AC08]
depende: [web-base]
---

# Spec — El índice infinito (experimento opcional)

## 1. Propósito

Una pieza literaria de marca, inspirada en *La biblioteca de Babel* de Borges: páginas de texto aleatorio que se generan siempre iguales para la misma dirección. Es opcional, se construye al final y no forma parte del flujo de compra ni del catálogo.

## 2. Alcance

| Dentro | Fuera |
| :--- | :--- |
| Generar una página a partir de una dirección y repetirla igual. | Mezclar el contenido con búsquedas, inventario, reseñas o títulos reales. |
| Elegir una página al azar y compartir su enlace. | Guardar páginas en una base de datos. |
| Avisar que el texto es generado y volver al catálogo real. | Reutilizar código de terceros con otra licencia. |

## 3. Requisitos (EARS)

| ID | Patrón | Requisito | Origen |
| :--- | :--- | :--- | :--- |
| IND-01 | Evento | Cuando el visitante abre una dirección, el sistema deberá mostrar una página de texto generada de forma determinista a partir de esa dirección. | RC01 |
| IND-02 | Ubicuo | El sistema deberá generar siempre la misma página para la misma dirección. | RC01 |
| IND-03 | Evento | Cuando el visitante pide otra página al azar, el sistema deberá elegir una dirección nueva y mostrar su página. | RC01 |
| IND-04 | Evento | Cuando el visitante copia el enlace, el sistema deberá incluir la dirección para que otra persona vea la misma página. | RC01 |
| IND-05 | Ubicuo | El sistema deberá indicar en la pantalla que el texto es generado, que puede carecer de sentido y que no son libros reales ni están a la venta. | NN15 |
| IND-06 | Ubicuo | El sistema deberá generar las páginas sin almacenarlas ni consultar una base de datos. | RC01 |
| IND-07 | No deseado | Si la dirección no es válida, entonces el sistema deberá mostrar un mensaje amable y ofrecer una página al azar en lugar de un error. | AC08 |
| IND-08 | Ubicuo | El sistema deberá ofrecer un enlace de regreso al catálogo real y mantener la experiencia separada del carrito y de los resultados de búsqueda. | RC01 |

## 4. Criterios de aceptación

| ID | Cubre | Escenario |
| :--- | :--- | :--- |
| AC-IND-01 | IND-01, IND-02 | **Dado** una dirección, **cuando** se abre dos veces, **entonces** muestra exactamente el mismo texto. |
| AC-IND-02 | IND-03 | **Dado** una página mostrada, **cuando** el visitante pide otra al azar, **entonces** cambia la dirección y el texto. |
| AC-IND-03 | IND-04 | **Dado** una página, **cuando** el visitante copia el enlace y otra persona lo abre, **entonces** ve la misma página. |
| AC-IND-04 | IND-05, IND-08 | **Dado** cualquier página, **cuando** se muestra, **entonces** incluye el aviso de texto generado y un enlace al catálogo real. |
| AC-IND-05 | IND-06 | **Dado** el sistema, **cuando** se generan cien páginas, **entonces** no se consulta ni se escribe ninguna base de datos. |
| AC-IND-06 | IND-07 | **Dado** una dirección con caracteres inválidos, **cuando** se abre, **entonces** se muestra un mensaje amable y una página al azar. |

## 5. Reglas e invariantes

- El texto se calcula con un algoritmo propio a partir de la dirección; no se guarda.
- Nada de lo generado aparece en el catálogo, el buscador, el carrito ni los avisos.
- La experiencia es accesible y respeta el movimiento reducido.

## 6. Casos límite y errores

- Una dirección muy larga se acepta y se acorta en pantalla sin cambiar el resultado.
- Una dirección vacía equivale a pedir una página al azar.

## 7. Preguntas abiertas

- [ ] ¿Se construye en esta versión o queda para el final del proyecto? Propuesta: al final y solo con el catálogo estable. Responde: responsable del proyecto.
