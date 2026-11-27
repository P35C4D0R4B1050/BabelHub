# Tablero de coordinación de la interfaz

Reglas: [`docs/07-proceso-de-desarrollo/10-colaboracion-ui.md`](../docs/07-proceso-de-desarrollo/10-colaboracion-ui.md). La versión vigente es la de `main`. Claude y la persona escriben «Tareas», «Reservado» y «Respuestas»; Codex escribe «Entregas» y «Peticiones». Solo se agregan filas.

**Dónde está cada documento** (en `main` desde `v0.1.2`): reglas en `docs/07-proceso-de-desarrollo/10-colaboracion-ui.md` (con el prefijo `10-`; no existe un `colaboracion-ui.md` sin él), este tablero en `progress/coordinacion-ui.md`, control en `scripts/guardia-ui.sh` y reparto entre agentes en `progress/coordinacion.md` §4. El despliegue no es tarea de Codex.

Estados: `Pendiente` (no empezar) → `Asignada` → `En curso` → `Entregada` → `Corregir` o `Integrada`.

## Tareas

| N.º | Tarea | Responsable | Estado | Notas |
| :--- | :--- | :--- | :--- | :--- |
| UI-0 | **Propuesta de rediseño y animaciones** de la tienda y los paneles: solo en `prototype/ui-v2/` y en un documento nuevo, `docs/06-experiencia-de-usuario/11-propuesta-de-rediseno.md`. No toca la aplicación. | Codex | Entregada fuera de rama (carpeta `prototype/v/`), sin revisar | Cubre O-6, O-7 y O-8; O-9 solo como alternativa marcada «requiere decisión». El documento separa lo que es solo apariencia de lo que cambia comportamiento (necesita spec) y dice cómo hacer cada cosa en la aplicación sin estilos ni scripts en línea, con movimiento reducido y contraste AA. El prototipo debe seguir funcionando. La persona aprueba la propuesta antes de trasladarla; no se publica en la aplicación antes de la entrega ni durante la revisión de Culqi. |
| UI-1 | Títulos gigantes en los paneles: `.bh-display` ocupa media pantalla; comparar con `prototype/ui-v2` y revisar también la tienda. | Codex | Asignada (2026-11-27) | En la aplicación, cuando esta versión del tablero esté en `main`. |
| UI-2 | Menú de los paneles: el prototipo usa menú lateral y la aplicación uno superior que se parte en dos filas. | Codex | Asignada (2026-11-27) | Conservar destinos por rol; si cambia el recorrido, pedirlo antes. |
| UI-3 | Animaciones (la persona ve la interfaz «muy estática»). | Codex | Asignada (2026-11-27) | Con `prefers-reduced-motion`; pedir a la persona sus observaciones concretas. |
| UI-4 | Trasladar a la aplicación la apariencia del prototipo (tienda, inicio, paneles, ingreso y cuenta), según UI-0 aprobada por la persona. | Codex | Asignada (2026-11-27) | Ficha, carrito, checkout y pedidos quedan para UI-5. Se publica solo lo revisado. |
| UI-5 | Apariencia de ficha, carrito, checkout, pedidos y tarifas de envío del modelo de venta por sucursal. | Codex | Pendiente | Empieza cuando los carriles de venta por sucursal estén en `main`. Contexto: `progress/relevos/venta-por-sucursal.md`. |

## Reservado (Codex no lo toca)

| Rama | Responsable | Archivos de interfaz | Estado (2026-11-27) | Hasta |
| :--- | :--- | :--- | :--- | :--- |
| `claude/acceso-v012-7467d0` | Claude | Ingreso, registro, cuenta, segundo factor, pie y plantilla común. | Integrada: **liberada** al llegar esta versión a `main` | Liberada |
| `claude/dazzling-lewin-3cbe83` | Claude | Ficha del libro, catálogo, carrito y checkout. | En `main` desde `v0.1.2`: liberada | Liberada |
| Carriles de venta por sucursal | Claude | Ficha del libro, carrito, checkout, pedidos, envíos, devoluciones, administración de tarifas y **el selector de sucursal de la cabecera** (se quita). | En curso | Integrados en `main` |
| Carril de accesos pendientes | Claude | Raíz de la gestión, retorno tras ingresar y enlaces a pantallas sin acceso. Cambios mínimos en menús: avisa en «Respuestas». | En curso | Integrado en `main` |

## Observaciones de la persona (2026-11-27)

Lista común para que ambos agentes partan de lo mismo. «Decide la persona» significa que nadie la implementa todavía.

| N.º | Observación | Quién | Estado |
| :--- | :--- | :--- | :--- |
| O-1 | Ver la contraseña, avisar en vivo si coinciden y orientar con una barra de rojo a verde, sin obligar composición. | Claude | En `main` con esta versión; falta publicar `v0.1.3` |
| O-2 | Código QR para configurar el segundo factor. | Claude | En `main` con esta versión; falta leer el QR con un teléfono |
| O-3 | Un solo botón «Ingresar» en la tienda: el personal no sabe que entra por la gestión. | Claude | En `main` con esta versión (enlace «Acceso del personal») |
| O-4 | No se podía comprar: la ficha no tenía botón. | Claude | En `main` (`v0.1.2`); falta desplegar |
| O-5 | La raíz de la gestión responde 404. | Claude | Asignada al carril de accesos pendientes |
| O-6 | Títulos gigantes en los paneles, distintos del prototipo. | Codex (UI-1) | Asignada |
| O-7 | Menú superior de los paneles que se parte en dos filas; el prototipo usa menú lateral. | Codex (UI-2) | Asignada |
| O-8 | La interfaz se ve muy estática; faltan animaciones. | Codex (UI-3) | Asignada; pedir a la persona el detalle |
| O-9 | El selector de sucursal de la cabecera no convence; elegirla en la ficha o al comprar. | Claude | **Decidida (2026-11-27):** se elige al comprar y se quita de la cabecera |
| O-10 | Elegir sucursal por precio, envío y cercanía. | Claude | **Decidida (2026-11-27):** el cliente elige sucursal y cada una cobra su envío; el precio del libro es único y no se calcula cercanía |
| O-11 | Tras ingresar desde el carrito se cae en la portada. | Claude | Asignada al carril de accesos pendientes |
| O-12 | El único administrador no puede recuperar su segundo factor desde la aplicación. | Claude | Asignada al carril de accesos pendientes |
| O-13 | El máximo debe ser el stock de la sucursal elegida, con lotes de hasta 20 por línea. | Claude | Decidida: venta por sucursal |
| O-14 | El carrito debe actualizarse al cambiar la cantidad, sin pulsar «Actualizar», y mostrar la sucursal en cada tarjeta. | Claude (función) y Codex (apariencia) | Decidida: venta por sucursal |

## Entregas

| Fecha | Tarea | Rama y commits | Qué cambió | Verificado / sin verificar |
| :--- | :--- | :--- | :--- | :--- |

## Peticiones

| Fecha | Tarea | Qué se necesita y por qué |
| :--- | :--- | :--- |

## Respuestas

| Fecha | Tarea | Respuesta o corrección pedida |
| :--- | :--- | :--- |
| 2026-11-27 | Accesos pendientes (aviso a Codex) | Cambios mínimos de interfaz del regreso a la compra, que UI-1 y UI-2 deben conservar: `Views/Ingreso/Ingreso.cshtml` (aviso «Al ingresar vuelves a tu carrito» y campo oculto `continuar` en los formularios de credenciales y de segundo factor) y `Carrito/Index.cshtml` (solo el `href` de «Ingresar para continuar»). No se tocó el menú ni los estilos. |
| 2026-11-27 | Accesos pendientes (aviso a Codex) | Enlaces nuevos, que UI-1 y UI-4 deben conservar: `Views/Cuenta/Cuenta.cshtml` (lista «Tus trámites» en el resumen del cliente) y `Devoluciones/Lista.cshtml` (un enlace bajo la descripción). No se tocó el menú ni los estilos. |
