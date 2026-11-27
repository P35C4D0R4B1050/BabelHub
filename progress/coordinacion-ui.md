# Tablero de coordinación de la interfaz

Reglas: [`docs/07-proceso-de-desarrollo/10-colaboracion-ui.md`](../docs/07-proceso-de-desarrollo/10-colaboracion-ui.md). La versión vigente es la de `main`. Claude y la persona escriben «Tareas», «Reservado» y «Respuestas»; Codex escribe «Entregas» y «Peticiones». Solo se agregan filas.

**Dónde está cada documento** (en `main` desde `v0.1.2`): reglas en `docs/07-proceso-de-desarrollo/10-colaboracion-ui.md` (con el prefijo `10-`; no existe un `colaboracion-ui.md` sin él), este tablero en `progress/coordinacion-ui.md`, control en `scripts/guardia-ui.sh` y reparto entre agentes en `progress/coordinacion.md` §4. El despliegue no es tarea de Codex.

Estados: `Pendiente` (no empezar) → `Asignada` → `En curso` → `Entregada` → `Corregir` o `Integrada`.

## Tareas

| N.º | Tarea | Responsable | Estado | Notas |
| :--- | :--- | :--- | :--- | :--- |
| UI-0 | **Propuesta de rediseño y animaciones** de la tienda y los paneles: solo en `prototype/ui-v2/` y en un documento nuevo, `docs/06-experiencia-de-usuario/11-propuesta-de-rediseno.md`. No toca la aplicación. | Codex | Asignada (2026-11-27) | Cubre O-6, O-7 y O-8; O-9 solo como alternativa marcada «requiere decisión». El documento separa lo que es solo apariencia de lo que cambia comportamiento (necesita spec) y dice cómo hacer cada cosa en la aplicación sin estilos ni scripts en línea, con movimiento reducido y contraste AA. El prototipo debe seguir funcionando. La persona aprueba la propuesta antes de trasladarla; no se publica en la aplicación antes de la entrega ni durante la revisión de Culqi. |
| UI-1 | Títulos gigantes en los paneles: `.bh-display` ocupa media pantalla; comparar con `prototype/ui-v2` y revisar también la tienda. | Codex | Pendiente | Esperar a que se integren las ramas reservadas. |
| UI-2 | Menú de los paneles: el prototipo usa menú lateral y la aplicación uno superior que se parte en dos filas. | Codex | Pendiente | Si cambia el recorrido, pedirlo antes. |
| UI-3 | Animaciones (la persona ve la interfaz «muy estática»). | Codex | Pendiente | Falta que la persona detalle sus observaciones. |

## Reservado (Codex no lo toca)

| Rama | Responsable | Archivos de interfaz | Estado (2026-11-27) | Hasta |
| :--- | :--- | :--- | :--- | :--- |
| `claude/acceso-v012-7467d0` | Claude | Ingreso, registro, cuenta, segundo factor, pie y plantilla común; estilos y JavaScript que agregue; `docs/06-experiencia-de-usuario/` 01, 02 y 08. | En curso | Integrada en `main` |
| `claude/dazzling-lewin-3cbe83` | Claude | Ficha del libro, catálogo, carrito y checkout. | En `main` desde `v0.1.2`: **ya no está reservada**, salvo lo que cambie la rama de acceso | Liberada |

## Observaciones de la persona (2026-11-27)

Lista común para que ambos agentes partan de lo mismo. «Decide la persona» significa que nadie la implementa todavía.

| N.º | Observación | Quién | Estado |
| :--- | :--- | :--- | :--- |
| O-1 | Ver la contraseña, avisar en vivo si coinciden y orientar con una barra de rojo a verde, sin obligar composición. | Claude | En la rama de acceso |
| O-2 | Código QR para configurar el segundo factor. | Claude | En la rama de acceso |
| O-3 | Un solo botón «Ingresar» en la tienda: el personal no sabe que entra por la gestión. | Claude | En la rama de acceso (enlace «Acceso del personal») |
| O-4 | No se podía comprar: la ficha no tenía botón. | Claude | En `main` (`v0.1.2`); falta desplegar |
| O-5 | La raíz de la gestión responde 404. | Decide la persona | Abierta |
| O-6 | Títulos gigantes en los paneles, distintos del prototipo. | Codex (UI-1) | Pendiente |
| O-7 | Menú superior de los paneles que se parte en dos filas; el prototipo usa menú lateral. | Codex (UI-2) | Pendiente |
| O-8 | La interfaz se ve muy estática; faltan animaciones. | Codex (UI-3) | Falta el detalle de la persona |
| O-9 | El selector de sucursal de la cabecera no convence; elegirla en la ficha o al comprar. | Decide la persona | Abierta: cambia el diseño y el prototipo |
| O-10 | Elegir sucursal por precio, envío y cercanía. | Decide la persona | Abierta: cambia requisitos |
| O-11 | Tras ingresar desde el carrito se cae en la portada. | Decide la persona | Abierta |
| O-12 | El único administrador no puede recuperar su segundo factor desde la aplicación. | Decide la persona | Abierta; no es de interfaz solamente |

## Entregas

| Fecha | Tarea | Rama y commits | Qué cambió | Verificado / sin verificar |
| :--- | :--- | :--- | :--- | :--- |

## Peticiones

| Fecha | Tarea | Qué se necesita y por qué |
| :--- | :--- | :--- |

## Respuestas

| Fecha | Tarea | Respuesta o corrección pedida |
| :--- | :--- | :--- |
