# Identidad y sistema de diseño

Este documento define cómo se ve y cómo se comporta `BabelHub.Web` (decisión AD15): identidad visual, sistema de diseño, mapa de pantallas por rol, recorridos clave, estados, accesibilidad y criterios de aceptación visual. No cambia la estructura funcional del análisis, la arquitectura y el diseño; la traduce a pantallas. Parte del documento de dirección visual del equipo (fuera del repositorio) y de los prototipos de `prototype/`.

## 1. Principios

| Principio | Qué implica |
| :--- | :--- |
| **Stock real, a la vista** | La disponibilidad se muestra siempre con texto e icono, nunca solo con color, y refleja datos reales (OB01). No se finge infinito ni stock. |
| **Conservar la estructura probada** | Cabecera, buscador, selector de sucursal, banner, filtros laterales, ordenamiento, cuadrícula de libros, carrito y perfil. Cambia la identidad, no el recorrido. |
| **Las portadas son las protagonistas** | La interfaz las enmarca; no compite con ellas. Sin fondos de color aleatorios por tarjeta. |
| **Dos temas, una sola marca** | Claro («Papel y tinta») y oscuro («Archivo nocturno») comparten jerarquía, espaciado y estados; solo cambian los tokens. |
| **Movimiento con propósito** | Cada animación comunica una respuesta del sistema. Todo funciona sin movimiento (`prefers-reduced-motion`). |
| **Móvil primero** | La demanda en provincias llega desde celulares y conexiones variables (AC08). |
| **Una sola app, tres áreas** | Tienda, Sucursal y Admin comparten sistema de diseño, sesión y componentes (AD15). |

## 2. Identidad: «Babel cromático + laberinto de historias»

Concepto: una librería donde cada libro abre la puerta a otros mundos (referencia: *La biblioteca de Babel*, de Borges). Editorial y literaria, vibrante sin ser estridente, contemporánea y cálida. Los motivos gráficos son caminos finos, nodos, círculos y coordenadas que conectan libros y categorías. El universo «cósmico» (órbitas, puntos) es solo un detalle secundario.

### 2.1 Paleta de marca

| Token de marca | Color | Uso |
| :--- | :--- | :--- |
| **Azul biblioteca** | `#172554` | Texto fuerte en tema claro; base del tema oscuro; cabecera y pie. |
| **Coral Babel** | `#FF665A` | Acción primaria y acento de marca. |
| **Papel antiguo** | `#F5EBD8` | Fondo y superficies editoriales del tema claro; texto del tema oscuro. |
| **Verde descubrimiento** | `#A9E85E` | Indicadores positivos de stock y disponibilidad. |
| **Violeta infinito** | `#7857D9` | Colecciones especiales y descubrimiento. |
| **Cian portal** | `#36C8D4` | Foco, enlaces activos y conexiones gráficas. |

Reglas: azul y papel son la base y el coral el acento principal; verde, violeta y cian se reservan para estados y contextos concretos. No se usan los seis colores con la misma intensidad.

### 2.2 Contraste medido (WCAG, mínimo 4,5:1 para texto normal)

La paleta inicial no cumple en todos los pares. Estos son los valores calculados y la decisión que se tomó:

| Par | Contraste | Decisión |
| :--- | :---: | :--- |
| Azul sobre papel | 12,4 | Texto principal del tema claro. |
| Papel sobre azul oscuro | 14,4 | Texto principal del tema oscuro. |
| Blanco sobre coral | **2,9** | **No se usa.** Los botones coral llevan texto azul. |
| Azul sobre coral | 5,1 | Botón primario en ambos temas. |
| Coral sobre papel | **2,4** | **No se usa como texto ni borde fino en el tema claro.** Se usa «coral tinta» `#B5352A` (5,1). |
| Violeta `#7857D9` sobre azul oscuro | **2,9** | En el tema oscuro se usa un violeta más claro, `#A78BFA` (6,3). |
| Blanco sobre violeta `#7857D9` | 5,0 | Botón o insignia violeta del tema claro. |
| Cian sobre azul / azul sobre cian | 7,3 | Foco y enlaces en el tema oscuro; texto azul sobre cian. |
| Azul sobre verde | 10,1 | Insignias de stock positivo con texto azul. |
| Cian oscuro `#1A6F80` sobre papel | 4,9 | Variante del cian para texto en el tema claro. |

Regla general: **ningún color de marca es la única señal de un estado**. El stock se acompaña de texto («En tu sucursal», «En 3 sucursales más», «Agotado») y de icono.

### 2.3 Temas

| Tema | Fondo | Superficies | Texto | Acción | Foco y enlaces | Carácter |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| **Claro: «Papel y tinta»** | Marfil `#FBF5E8` | Papel `#F5EBD8` y bordes cálidos | Azul `#172554` | Coral con texto azul | Cian oscuro `#1A6F80` | Librería editorial, cálida y abierta. |
| **Oscuro: «Archivo nocturno»** | Azul muy oscuro `#0F1A3D` | Tarjetas `#14214A` y `#1B2A57` | Papel `#F5EBD8` | Coral con texto azul | Cian `#36C8D4` y violeta `#A78BFA` | Biblioteca profunda e inmersiva, sin perder legibilidad. |

Se respeta la preferencia de tema del sistema operativo y se recuerda la elección del usuario. Se verifican hover, foco, deshabilitado, error, stock y selección en ambos temas.

## 3. Sistema de diseño

| Aspecto | Decisión (AD21) |
| :--- | :--- |
| **CSS** | Tailwind CSS 4 con daisyUI 5, compilado con la CLI independiente de Tailwind (sin Node) en el `Dockerfile` de `BabelHub.Web`. El CSS generado no se versiona. |
| **Tokens** | Los colores viven como tokens semánticos de daisyUI (`primary`, `secondary`, `accent`, `base-100/200/300`, `base-content`, `info`, `success`, `warning`, `error`) en dos temas con los mismos nombres. Ningún componente tiene colores propios. |
| **Componentes** | Clases de daisyUI para botones, tarjetas, formularios, insignias y modales; Tailwind para layout, espaciado y responsive. |
| **Interactividad** | htmx para fragmentos (carrito, disponibilidad, estado de pedido y filtros); JavaScript propio mínimo. |
| **Tipografía** | **DM Serif Display** para títulos y precios destacados; **Inter** para interfaz y texto. En producción se autoalojan las fuentes (sin depender de servidores externos). |
| **Iconos** | Conjunto de trazo fino (estilo Heroicons outline), siempre con texto o `aria-label`. |
| **Portadas** | Proporción 2:3 uniforme. En el catálogo real vendrán de cada edición; en los prototipos se generan con motivos abstractos. |
| **Fondos y motivos** | Laberinto de líneas finas, casi imperceptible y solo en áreas amplias (banner, pie, páginas vacías); nunca detrás de párrafos ni de tarjetas. |
