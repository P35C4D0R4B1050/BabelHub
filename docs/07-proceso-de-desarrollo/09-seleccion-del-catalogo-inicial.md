# Selección del catálogo inicial

Qué obras carga `transversal-carga-inicial`, por qué esas y no otras, y de dónde salen los precios y el stock. Decisiones DD-70 a DD-74 de `progress/decisiones-delegadas.md` (pendientes de ratificar por la persona). Los datos están en `tools/carga-inicial/catalogo.csv`; las imágenes **no** están en el repositorio.

## 1. Para qué

La revisión de la pasarela de pagos exige que la tienda muestre como mínimo cinco productos, cada uno con foto, descripción clara y precio visible. Se cargan **40 obras visibles** (rango pedido: 20 a 40): 35 libros reales con portada y 5 genéricos del prototipo. Es un proyecto académico: se usan portadas y datos de libros reales (indicación de la persona); las sinopsis son de redacción propia y no copian reseñas.

## 2. Cómo se eligió

Origen: 68 capturas de portada en `D:\Trabajo\BHRecursos` y un borrador de títulos hecho por un modelo mirando las portadas (puede tener errores; se revisó la selección final con una hoja de contactos). Se midió el tamaño real de cada archivo.

| Regla | Efecto |
| :--- | :--- |
| Menos de 300 px de ancho | Se descartan 5 (lista abajo). |
| Problemas de recorte, marca de agua o maqueta | Se descartan 5: La broma infinita, Siddhartha, La guerra del fin del mundo, El Estado contra la democracia (marca de agua), Yo Asimov (maqueta 3D). |
| Edición duplicada de la misma obra | Se deja la de mejor resolución (Un mago de Terramar, En deuda, El Señor de los Anillos). |
| Exceso de un mismo autor o editorial | Se recorta hasta 40 obras dejando variedad (Bradbury de 10 a 5, Asimov de 6 a 3, Žižek de 6 a 2…). |
| Todas las portadas pesan menos de 2 MB (el tope de CAT-16) y son PNG | Ninguna se recomprime. |

**Excluidas por resolución (< 300 px de ancho):** Frankenstein (269×408), El surgimiento de Sendero Luminoso (299×432), Otras crónicas marcianas (200×301), Como un ladrón en pleno día (264×418), Mis chistes mi filosofía (267×423).

**Excluidas por recorte, marca de agua o maqueta:** La broma infinita, Siddhartha, La guerra del fin del mundo, El Estado contra la democracia, Yo Asimov. Memorias.

**Excluidas como edición duplicada:** Un mago de Terramar (la edición antigua escaneada), En deuda (la de menor resolución), El Señor de los Anillos (edición ilustrada y Las dos torres: se deja La Comunidad del Anillo), Trilogía de la Fundación (302 px y repite Fundación).

**Excluidas por variedad o por datos dudosos** (editorial no visible en la portada y obra ya bien representada): Los libros de Terramar, Posibilidades, El barco de los niños, El reverso de la utopía, Qué difícil es ser Dios, La década de la antipolítica, Todo nos une nada nos divide, Para calmar la ira de los dioses, Civilizaciones extraterrestres, Las palabras y los mitos, Ahora y siempre, El maravilloso traje de color vainilla, La feria de las tinieblas, Cuentos (Páginas de Espuma), Un sonido atronador, Incontinencia del vacío, La nueva lucha de clases.

**Incluidas (35 reales + 5 genéricas):** las 40 filas de `tools/carga-inicial/catalogo.csv`. Las portadas se copiaron fuera del repositorio a `D:\Trabajo\BHPortadas\` con el nombre de la columna `portada`; las cinco genéricas las genera `tools/carga-inicial/generar-portadas-genericas.ps1` con la paleta de la marca.

## 3. Datos dudosos

- **Editorial.** La que consta en la portada. Donde el modelo puso «?» se escribió «Editorial no indicada» (solo 1984). Ninguna se inventó.
- **ISBN.** El catálogo exige un ISBN con dígito de control y no permite cambiarlo después. Para no inventar el de una edición real, cada edición lleva un **identificador provisional del rango 979-0** (el de las partituras, que no se asigna a libros, así que no puede coincidir con el de un título real), escrito en el CSV por `completar-datos.ps1`. **La tienda mostrará ese identificador como ISBN**: si la persona conoce el ISBN real de una edición, debe escribirlo en `isbn_impreso` **antes** de cargar.
- **Digitales.** Solo las cinco genéricas del prototipo tienen edición digital (tres de dominio público y dos de licencia comercial de ejemplo hasta 2028). Su archivo es un EPUB de muestra generado al cargar, que dice que no es el libro: **no debe venderse con pagos reales** sin reemplazarlo.
- **Autores y títulos ficticios.** `A. Quispe (ficticio)` y `L. Huamán (ficticio)` vienen del prototipo y se dejan marcados.

## 4. Precios y stock (determinista)

`completar-datos.ps1` llena solo las celdas vacías con `u(clave, etiqueta)` = FNV-1a de 32 bits de `babelhub-carga-inicial-2026-11|<clave>|<etiqueta>` entre 2^32 (la semilla fija):

- **Precio impreso:** `piso(25 + u(clave,"precio") × 95) + 0,90`, de S/ 25,90 a S/ 119,90. **Digital:** `piso(impreso × 0,45) + 0,90`.
- **Stock:** Lima (la recepción) siempre más las dos sucursales de menor `u(clave,"orden-<código>")` entre Arequipa, Trujillo, Cusco, Piura y Ayacucho; en cada una, `3 + piso(u(clave,"stock-<código>") × 23)`, de 3 a 25 unidades. Una carga completa son como máximo 120 operaciones, dos ventanas del presupuesto de 60 ajustes por hora (SEG-30).
- La regla depende de la `clave` y no del orden de las filas: agregar una obra no cambia las demás.
