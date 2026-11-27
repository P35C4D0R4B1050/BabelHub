# Tablero de coordinación de la interfaz

Reglas: [`docs/07-proceso-de-desarrollo/10-colaboracion-ui.md`](../docs/07-proceso-de-desarrollo/10-colaboracion-ui.md). La versión vigente es la de `main`. Claude y la persona escriben «Tareas», «Reservado» y «Respuestas»; Codex escribe «Entregas» y «Peticiones». Solo se agregan filas.

Estados: `Pendiente` (no empezar) → `Asignada` → `En curso` → `Entregada` → `Corregir` o `Integrada`.

## Tareas

| N.º | Tarea | Responsable | Estado | Notas |
| :--- | :--- | :--- | :--- | :--- |
| UI-1 | Títulos gigantes en los paneles: `.bh-display` ocupa media pantalla; comparar con `prototype/ui-v2` y revisar también la tienda. | Codex | Pendiente | Esperar a que se integren las ramas reservadas. |
| UI-2 | Menú de los paneles: el prototipo usa menú lateral y la aplicación uno superior que se parte en dos filas. | Codex | Pendiente | Si cambia el recorrido, pedirlo antes. |
| UI-3 | Animaciones (la persona ve la interfaz «muy estática»). | Codex | Pendiente | Falta que la persona detalle sus observaciones. |

## Reservado (Codex no lo toca)

| Rama | Responsable | Archivos de interfaz | Hasta |
| :--- | :--- | :--- | :--- |
| `claude/acceso-v012-7467d0` | Claude | Ingreso, registro, cuenta, segundo factor, pie y plantilla común; estilos y JavaScript que agregue. | Integrada en `main` |
| `claude/dazzling-lewin-3cbe83` | Claude | Ficha del libro, catálogo, carrito y checkout. | Integrada en `main` |

## Entregas

| Fecha | Tarea | Rama y commits | Qué cambió | Verificado / sin verificar |
| :--- | :--- | :--- | :--- | :--- |

## Peticiones

| Fecha | Tarea | Qué se necesita y por qué |
| :--- | :--- | :--- |

## Respuestas

| Fecha | Tarea | Respuesta o corrección pedida |
| :--- | :--- | :--- |
