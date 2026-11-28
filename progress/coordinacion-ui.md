# Tablero de coordinación de la interfaz

Reglas: [`docs/07-proceso-de-desarrollo/10-colaboracion-ui.md`](../docs/07-proceso-de-desarrollo/10-colaboracion-ui.md). La versión vigente es la de `main`. Claude y la persona escriben «Tareas», «Reservado» y «Respuestas»; Codex escribe «Entregas» y «Peticiones». Solo se agregan filas.

**Dónde está cada documento** (en `main` desde `v0.1.2`): reglas en `docs/07-proceso-de-desarrollo/10-colaboracion-ui.md` (con el prefijo `10-`; no existe un `colaboracion-ui.md` sin él), este tablero en `progress/coordinacion-ui.md`, control en `scripts/guardia-ui.sh` y reparto entre agentes en `progress/coordinacion.md` §4. El despliegue no es tarea de Codex.

**Contexto vigente (2026-11-27):** antes de cualquier tarea lee [`docs/06-experiencia-de-usuario/12-estado-de-la-interfaz.md`](../docs/06-experiencia-de-usuario/12-estado-de-la-interfaz.md): decisiones que no se revierten, qué cambió en `v0.1.3`, qué no se puede romper y cómo verificarlo. Trabaja desde `main` con `v0.1.3`.

Estados: `Pendiente` (no empezar) → `Asignada` → `En curso` → `Entregada` → `Corregir` o `Integrada`.

## Tareas

**Decisión de la persona (2026-11-27): Codex reconstruye la identidad visual completa** (reglas en el §2 ter de la guía de colaboración). Va por fases, UI-6 a UI-9; las tareas UI-1 a UI-5 quedan incluidas en ellas.

| N.º | Tarea | Responsable | Estado | Notas |
| :--- | :--- | :--- | :--- | :--- |
| UI-0 | **Propuesta de rediseño y animaciones** de la tienda y los paneles: solo en `prototype/ui-v2/` y en un documento nuevo, `docs/06-experiencia-de-usuario/11-propuesta-de-rediseno.md`. No toca la aplicación. | Codex | Entregada fuera de rama (carpeta `prototype/v/`), sin revisar | Cubre O-6, O-7 y O-8; O-9 solo como alternativa marcada «requiere decisión». El documento separa lo que es solo apariencia de lo que cambia comportamiento (necesita spec) y dice cómo hacer cada cosa en la aplicación sin estilos ni scripts en línea, con movimiento reducido y contraste AA. El prototipo debe seguir funcionando. La persona aprueba la propuesta antes de trasladarla; no se publica en la aplicación antes de la entrega ni durante la revisión de Culqi. |
| UI-1 | Títulos gigantes en los paneles (`.bh-display`). | Codex | Incluida en UI-9 | — |
| UI-2 | Menú de los paneles (lateral en el prototipo, superior en la aplicación). | Codex | Incluida en UI-9 | Conservar destinos por rol. |
| UI-3 | Animaciones (la interfaz se ve «muy estática»). | Codex | Incluida en UI-6 a UI-9 | Con `prefers-reduced-motion`. |
| UI-4 | Trasladar la apariencia a tienda, inicio, paneles, ingreso y cuenta. | Codex | Incluida en UI-8 y UI-9 | — |
| UI-5 | Apariencia de la venta por sucursal: ficha, carrito, checkout, pedidos y la pantalla de costos de envío. | Codex | Incluida en UI-8 | La función ya está en `v0.1.3`. Contexto: documento 12 y `progress/relevos/venta-por-sucursal.md`. |
| UI-6 | **Fase 1 · Propuesta de identidad completa:** concepto, paleta de los dos temas con su contraste medido, tipografía, logotipo e icono del sitio, componentes, estados, movimiento y maqueta de inicio, catálogo, ficha, carrito, checkout, ingreso y un panel, en escritorio y teléfono. Solo `prototype/ui-v3/` y `docs/06-experiencia-de-usuario/11-propuesta-de-rediseno.md`. | Codex | Asignada (2026-11-27) | La entrega UI-0 (`prototype/v/`) sirve de insumo. **La persona aprueba antes de pasar a la fase 2.** |
| UI-7 | **Fase 2 · Fundamentos:** tokens de los temas, tipografía, logotipo, componentes compartidos, cabecera, pie y plantilla; actualiza el documento de identidad (01). | Codex | Pendiente (tras aprobar UI-6) | Las fuentes nuevas se piden en «Peticiones». |
| UI-8 | **Fase 3 · Tienda:** inicio, catálogo, ficha, carrito, checkout, pago, pedidos, biblioteca, devoluciones, reclamaciones, ingreso, registro y cuenta. | Codex | Pendiente (tras revisar UI-7) | Por tandas pequeñas, cada una revisable. |
| UI-9 | **Fase 4 · Gestión:** paneles de sucursal, logística y administración. | Codex | Pendiente (tras revisar UI-8) | Incluye menú y títulos. |

## Reservado (Codex no lo toca)

| Rama | Responsable | Archivos de interfaz | Estado (2026-11-27) | Hasta |
| :--- | :--- | :--- | :--- | :--- |
| `claude/acceso-v012-7467d0` | Claude | Ingreso, registro, cuenta, segundo factor, pie y plantilla común. | En `main` con `v0.1.3` | Liberada |
| `claude/dazzling-lewin-3cbe83` | Claude | Ficha del libro, catálogo, carrito y checkout. | En `main` desde `v0.1.2`: liberada | Liberada |
| Carriles de venta por sucursal | Claude | Ficha del libro, carrito, checkout, pedidos, envíos, devoluciones, costos de envío y cabecera. | En `main` con `v0.1.3` | Liberada |
| Carril de accesos pendientes | Claude | Raíz de la gestión, retorno tras ingresar y enlaces. | En `main` con `v0.1.3` | Liberada |

## Observaciones de la persona (2026-11-27)

Lista común para que ambos agentes partan de lo mismo. «Decide la persona» significa que nadie la implementa todavía.

| N.º | Observación | Quién | Estado |
| :--- | :--- | :--- | :--- |
| O-1 | Ver la contraseña, avisar en vivo si coinciden y orientar con una barra de rojo a verde, sin obligar composición. | Claude | En `v0.1.3` |
| O-2 | Código QR para configurar el segundo factor. | Claude | En `v0.1.3`; falta leer el QR con un teléfono |
| O-3 | Un solo botón «Ingresar» en la tienda: el personal no sabe que entra por la gestión. | Claude | En `v0.1.3` (enlace «Acceso del personal») |
| O-4 | No se podía comprar: la ficha no tenía botón. | Claude | Resuelta (`v0.1.2`) |
| O-5 | La raíz de la gestión responde 404. | Claude | En `v0.1.3` |
| O-6 | Títulos gigantes en los paneles, distintos del prototipo. | Codex (UI-1) | Asignada |
| O-7 | Menú superior de los paneles que se parte en dos filas; el prototipo usa menú lateral. | Codex (UI-2) | Asignada |
| O-8 | La interfaz se ve muy estática; faltan animaciones. | Codex (UI-3) | Asignada; pedir a la persona el detalle |
| O-9 | El selector de sucursal de la cabecera no convence; elegirla en la ficha o al comprar. | Claude | En `v0.1.3`: se elige al comprar y **se quitó de la cabecera** |
| O-10 | Elegir sucursal por precio, envío y cercanía. | Claude | En `v0.1.3`: el cliente elige sucursal y cada una cobra su envío; precio único y sin cercanía |
| O-11 | Tras ingresar desde el carrito se cae en la portada. | Claude | En `v0.1.3` |
| O-12 | El único administrador no puede recuperar su segundo factor desde la aplicación. | Claude | En `v0.1.3` (orden de consola; no es una pantalla) |
| O-13 | El máximo debe ser el stock de la sucursal elegida, con lotes de hasta 20 por línea. | Claude | En `v0.1.3` |
| O-14 | El carrito debe actualizarse al cambiar la cantidad, sin pulsar «Actualizar», y mostrar la sucursal en cada tarjeta. | Claude (función) y Codex (apariencia, UI-5) | Función en `v0.1.3` |

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
