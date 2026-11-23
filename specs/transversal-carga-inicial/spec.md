---
id: transversal-carga-inicial
titulo: Carga inicial del catálogo y del inventario
estado: InProgress
servicio: Transversal
prefijo: CIN
origen: [RF10, RF17, RF24, RNF05, RNF08, RNF16]
calidad: [RNF05, RNF08, RNF16]
depende: [ecommerce-catalogo, inventario-fuentes, inventario-ajustes, transversal-plataforma, transversal-auditoria, transversal-seguridad]
aprobado_por: Claude (delegado por Diego Amiquero)
aprobado_en: 2026-11-23
plan_aprobado_por: Claude (delegado por Diego Amiquero)
plan_aprobado_en: 2026-11-23
---

# Spec — Carga inicial del catálogo y del inventario

## 1. Propósito

Dejar la tienda, al desplegarla, con un catálogo y un inventario iniciales verosímiles: obras con portada, descripción y precio, y existencias en varias sucursales, sin teclear cada obra en la administración y de forma repetible y verificable. La necesita la revisión de la pasarela de pagos (la tienda debe mostrar como mínimo cinco productos, cada uno con foto, descripción clara y precio visible) y la demostración académica. Es una **operación del despliegue**, no una pantalla: la ejecuta la persona operadora, con las mismas reglas que la administración del catálogo y del inventario, y nunca escribe por un camino distinto de ellos (`ecommerce-catalogo`, `inventario-fuentes`, `inventario-ajustes`).

## 2. Alcance

| Dentro | Fuera |
| :--- | :--- |
| Cargar obras, ediciones, descripciones, precios y portadas desde un archivo de datos versionado y una carpeta de portadas. | Editar o dar de baja lo ya cargado (lo hace la administración del catálogo). |
| Generar el archivo de muestra de las ediciones digitales de ejemplo. | Los archivos reales de los libros digitales y los contratos con editoriales (proyecto académico). |
| Dejar un plan de inventario y cargar las sucursales, las rutas y el stock inicial por sucursal. | Trasladar stock entre sucursales, reservas y devoluciones (`inventario-traslados`, `inventario-seleccion-fuente`); los contactos de aviso de las sucursales. |
| Resumir lo cargado, lo ya cargado y lo rechazado, y distinguir el éxito, el éxito parcial y el error. | Crear cuentas, sesiones, roles o credenciales del primer despliegue, y la configuración de la pasarela de pagos. |

## 3. Requisitos (EARS)

Una **fila** del archivo de datos es una obra con su edición impresa y, si la declara, su edición digital: se carga completa o no se carga (CAT-04).

| ID | Patrón | Requisito | Origen |
| :--- | :--- | :--- | :--- |
| CIN-01 | Evento | Cuando la persona operadora ejecuta la carga del catálogo con un archivo de datos y una carpeta de portadas, el sistema deberá registrar cada obra con su título, autor, editorial, categoría, descripción, ediciones, precio en soles y portada, con las mismas reglas y límites que el alta de la administración del catálogo (CAT-01, CAT-02, CAT-16, CAT-17). | RF10 |
| CIN-02 | Ubicuo | El sistema deberá ser idempotente en la carga del catálogo: repetirla con el mismo archivo no deberá duplicar obras, ediciones ni portadas, y una fila cuyo ISBN ya pertenece a la misma obra, por título y autor, deberá informarse como «ya cargada» sin modificarla. | RF10 |
| CIN-03 | No deseado | Si una fila es inválida (un campo obligatorio vacío, un ISBN inválido o repetido, un precio fuera de lo permitido, una descripción de más de 600 caracteres, una licencia incoherente o una portada inválida), entonces el sistema deberá rechazar solo esa fila indicando su número y el campo, seguir con las demás y terminar con un resumen que distinga el éxito del éxito parcial. | RF10 |
| CIN-04 | No deseado | Si un archivo de datos, de sucursales o de rutas tiene más de 500 filas o 2 MB, no es texto válido, trae columnas desconocidas o faltantes, o un carácter de control o de dirección de texto, entonces el sistema deberá rechazarlo completo antes de escribir nada. | RNF16 |
| CIN-05 | No deseado | Si el nombre de una portada no es el de un archivo regular simple dentro de la carpeta indicada (con separadores, ruta absoluta, `..`, enlace simbólico, flujo alterno o nombre reservado), entonces el sistema deberá rechazar la fila como portada ausente, y el tipo de la imagen deberá decidirse por su contenido y nunca por su nombre. | RNF16, RF10 |
| CIN-06 | Ubicuo | El sistema deberá ejecutar la carga con la identidad de operación «carga inicial», que tiene el rol de administración central, no es una cuenta ni una sesión, solo la construye la propia orden de operación y ni la interfaz web ni la de programación aceptan, y no deberá crear cuentas, sesiones ni roles. | RNF05 |
| CIN-07 | Ubicuo | El sistema deberá auditar cada alta de la carga en la misma operación (AUD-04) con la identidad «carga inicial» y sin datos personales, y deberá dejar en la auditoría, que es inmutable, un registro por corrida con su identificador, la huella del archivo de datos y los conteos. | RNF08 |
| CIN-08 | Evento | Cuando el archivo de datos declara una edición digital, el sistema deberá generar un archivo de muestra válido en formato EPUB que diga que no es el libro y deberá dejar la edición con la licencia del archivo de datos, sin que ningún libro viaje en él. | RF10, RF24 |
| CIN-09 | Ubicuo | El sistema no deberá guardar en el repositorio ni en el archivo de datos las portadas ni los archivos digitales: las portadas y los archivos de muestra se guardan solo en el almacén de objetos, tras la verificación de su contenido (CAT-16). | RF10 |
| CIN-10 | Evento | Cuando termina la carga del catálogo, el sistema deberá dejar un plan de inventario con el identificador y el ISBN de cada edición impresa del archivo, ya cargada o recién cargada, y su stock por sucursal, sin datos personales. | RF17 |
| CIN-11 | No deseado | Si el plan de inventario no tiene la forma esperada (versión, tamaño, más de 500 ediciones, identificadores, stock fuera de 1 a 5 000 o una sucursal desconocida), entonces el sistema deberá rechazarlo completo antes de escribir nada. | RNF16 |
| CIN-12 | Evento | Cuando la persona operadora ejecuta la carga del inventario con un plan válido y los archivos de sucursales y rutas, el sistema deberá asegurar esas sucursales y rutas, sin modificar las que existen, hacer que el Inventario conozca cada edición impresa del plan (contrato de registro de ediciones de `docs/03-diseno-de-software/08-contratos-entre-servicios-v1.md`) y dejar su stock físico declarado, con las operaciones y reglas del Inventario (AJU-05, AJU-13, AJU-15). | RF17 |
| CIN-13 | No deseado | Si una edición ya tiene movimientos de stock en una sucursal (un ajuste previo o existencias), entonces el sistema deberá no modificar esa sucursal y deberá informarla como «ya cargada». | RF17 |
| CIN-14 | No deseado | Si la persona operadora agota el presupuesto por hora de ajustes del personal (SEG-30), entonces el sistema deberá detener la carga del inventario sin perder lo ya cargado, dejar el exceso como evento de seguridad, indicar cuándo puede continuar y terminar con el resultado «parcial por límite», distinto del error, y, si se pidió esperar, esperar hasta 2 horas y continuar. | RF17, RNF05 |
| CIN-15 | Ubicuo | El sistema deberá exigir en el archivo de datos un precio de S/ 25,00 a S/ 120,00 por edición impresa, de S/ 5,00 a S/ 120,00 por edición digital y un stock de 1 a 5 000 unidades por sucursal, y deberá usar solo esos valores, sin generar otros. | RF10, RF17 |
| CIN-16 | Ubicuo | Los precios y el stock del archivo de datos versionado del repositorio deberán ser reproducibles por una regla determinista y documentada, con precios impresos de S/ 25,00 a S/ 120,00 y stock de 3 a 25 unidades por sucursal. | RF10, RF17 |
| CIN-17 | No deseado | Si falta o es inválida alguna configuración del servicio que la carga necesita, entonces el sistema deberá terminar antes de escribir nada, nombrando la variable de configuración y nunca su valor. | RNF05 |
| CIN-18 | Ubicuo | El sistema no deberá ofrecer la carga como pantalla ni como operación de la interfaz web ni de la interfaz de programación, y su ejecución no deberá abrir ningún puerto de red. | RNF05 |
| CIN-19 | Ubicuo | El sistema no deberá duplicar nada cuando dos ejecuciones de la misma carga corren a la vez: la unicidad del ISBN, de la sucursal y de la ruta la garantiza el sistema, no la orden. | RF10, RF17 |
| CIN-20 | Ubicuo | El sistema no deberá escribir en el resumen ni en los registros de la carga cadenas de conexión, claves, enlaces firmados, rutas de la máquina, contenido de archivos ni mensajes de excepción de las bibliotecas. | RNF08 |

## 4. Criterios de aceptación

| ID | Cubre | Escenario |
| :--- | :--- | :--- |
| AC-CIN-01 | CIN-01, CIN-09 | **Dado** un archivo con tres obras (una con edición digital, con su descripción y precios) y una carpeta con sus tres portadas, **cuando** la persona operadora ejecuta la carga del catálogo, **entonces** el catálogo muestra las tres obras con portada, descripción y precio, las portadas y el archivo de muestra están solo en el almacén de objetos y el resumen informa tres cargadas y ninguna rechazada. |
| AC-CIN-02 | CIN-02, CIN-19 | **Dado** el catálogo ya cargado por AC-CIN-01, **cuando** se repite la carga, **entonces** el resumen informa tres «ya cargadas» y no cambia el número de obras, ediciones, portadas ni objetos del almacén; y **cuando** dos ejecuciones corren a la vez sobre un catálogo vacío, **entonces** quedan tres obras y no hay duplicados. |
| AC-CIN-03 | CIN-03 | **Dado** un archivo con once filas (ISBN con dígito de control inválido, precio de S/ 10,00, portada ausente, portada de 3 MB, campo vacío, descripción de 601 caracteres, portada de un tipo no admitido, ISBN repetido en el archivo, una licencia comercial sin vigencia, el ISBN de otra obra ya cargada y una fila válida), **cuando** se carga, **entonces** diez filas se rechazan con su número y su campo, la válida se carga y el resultado es el de éxito parcial; y con el almacén de objetos caído ninguna fila se carga y el resultado sigue siendo el de éxito parcial. |
| AC-CIN-04 | CIN-04 | **Dado** un archivo de más de 500 filas, otro con una columna desconocida, otro con un carácter de control y otro que no es texto válido, **cuando** se cargan, **entonces** cada uno se rechaza completo y no se escribe nada. |
| AC-CIN-05 | CIN-05 | **Dado** filas cuya portada es `../otra.png`, una ruta absoluta, un enlace simbólico a un archivo fuera de la carpeta y un archivo de texto con nombre `.png`, **cuando** se cargan, **entonces** las cuatro se rechazan por portada y nada se escribe en el almacén de objetos. |
| AC-CIN-06 | CIN-06, CIN-07 | **Dado** una carga realizada, **cuando** se consulta la auditoría, **entonces** cada obra y cada edición cargada tiene su entrada con la identidad «carga inicial» y sin datos personales, hay un registro de la corrida con su identificador, la huella del archivo y los conteos, no existe ninguna cuenta, sesión ni rol nuevos, y con la auditoría caída la fila no se carga. |
| AC-CIN-07 | CIN-08 | **Dado** una fila con edición digital de dominio público y otra con licencia comercial vigente, **cuando** se cargan, **entonces** cada una queda con su licencia y un archivo de muestra EPUB válido cuyo contenido dice que no es el libro, y la edición se ofrece a la venta. |
| AC-CIN-08 | CIN-10, CIN-11 | **Dado** el plan que dejó AC-CIN-01, **cuando** se lee, **entonces** trae las tres ediciones impresas con su identificador, su ISBN y su stock; y **cuando** se entrega a la carga del inventario un plan con una versión desconocida, una sucursal desconocida, un stock de 6 000 o un identificador inválido, **entonces** se rechaza completo y no se escribe nada. |
| AC-CIN-09 | CIN-12 | **Dado** el plan de AC-CIN-01 y archivos de sucursales y rutas con tres sucursales, **cuando** se ejecuta la carga del inventario en una base sin sucursales, **entonces** existen las tres sucursales con la primera como fuente de recepción y sus rutas, el Inventario conoce cada edición impresa y cada sucursal tiene su stock físico declarado, con sus operaciones auditadas; y repetirla, o ejecutarla dos veces a la vez, no duplica ni cambia nada ni modifica una sucursal o ruta ya existente con otros datos. |
| AC-CIN-10 | CIN-13 | **Dado** una sucursal con un movimiento previo de una edición (por ejemplo, una merma), **cuando** se repite la carga del inventario, **entonces** esa sucursal y esa edición no se modifican y se informan como «ya cargadas», y el resto no cambia. |
| AC-CIN-11 | CIN-14 | **Dado** un presupuesto de ajustes agotado a mitad de la carga del inventario, **cuando** continúa, **entonces** se detiene sin deshacer lo cargado, deja el evento de exceso, informa a partir de cuándo puede continuar y termina con el resultado de «parcial por límite»; repetir la orden tras ese plazo, o pedir esperar, completa lo que faltaba sin duplicar nada. |
| AC-CIN-12 | CIN-15, CIN-16 | **Dado** el archivo de datos versionado del repositorio, **cuando** se valida, **entonces** tiene entre 20 y 40 obras, sus ISBN son válidos y distintos, los precios impresos están entre S/ 25,00 y S/ 120,00, el stock declarado entre 3 y 25 unidades, cada descripción tiene como máximo 600 caracteres y ninguna fila repite una portada y regenerar con la regla documentada los precios y el stock produce el mismo archivo; y una fila con un precio impreso de S/ 10,00, un precio digital de S/ 4,00 o un stock de 0 o de 6 000 se rechaza. |
| AC-CIN-13 | CIN-17, CIN-18, CIN-20 | **Dado** un entorno sin una variable obligatoria, **cuando** se ejecuta cualquiera de las dos cargas, **entonces** termina antes de escribir nada y nombra la variable sin su valor; ni la interfaz web ni la de programación exponen una ruta de la carga ni aceptan como actor el identificador de la carga presentado en una cabecera o una credencial falsa, la orden no abre puertos y el resumen de una carga con el almacén caído no contiene cadenas de conexión, claves, rutas ni mensajes de excepción. |

## 5. Reglas e invariantes

- La carga **nunca** escribe por un camino distinto de los casos de uso del catálogo y del inventario: cada alta pasa por las mismas validaciones, límites, transacciones y auditoría que la hecha por la administración.
- La identidad «carga inicial» es una **excepción acotada** a la exigencia de sesión y segundo factor del personal (IDE-20): no inicia sesión, no tiene credencial y solo existe dentro del proceso de la orden, que ya exige el acceso a las cuentas de base del servicio (el mismo que tiene el modo de migración). La auditoría registra al actor como persona con el rol de administración central cuyo identificador es el de la identidad «carga inicial» (AUD-23: el actor sale del contexto de la orden, nunca de una petición), y el presupuesto de SEG-30 se cuenta contra ese identificador.
- El ISBN de una edición no cambia después de cargarla: un ISBN que el archivo de datos declare como provisional es el que queda (la persona responsable lo sabe antes de cargar).
- Una edición digital de la carga nunca tiene stock, reserva ni fuente; su archivo es de muestra y no es el libro, y no debe venderse con pagos reales sin reemplazarlo.
- La descripción, el titular y los demás textos del archivo de datos son texto plano: la carga no los interpreta como una orden ni como marcado, y una celda que empieza con `=`, `+`, `-` o `@` es texto.
- El archivo de datos y los de sucursales y rutas no tienen datos personales (los autores y las editoriales son datos públicos de las obras; las sucursales se cargan sin contacto de aviso, que se configura después en la administración); ningún secreto aparece en ellos.
- Una carga completa cabe en el presupuesto por hora del personal (SEG-30) en dos ventanas: el archivo versionado declara stock en la recepción y en dos sucursales más por edición impresa (como máximo 120 operaciones, dos ventanas completas).

## 6. Casos límite y errores

- Una fila cuyo ISBN pertenece a otra obra se rechaza como ISBN repetido indicando el título que ya lo usa (CAT-03).
- Si el almacén de objetos no responde, la fila no se carga y se informa; la carga sigue con las demás y el resultado es el de éxito parcial.
- Si falta el archivo de sucursales, la carga del inventario termina sin escribir.
- Una sucursal o ruta que ya existe con datos distintos no se modifica; se informa «ya existe».
- Una celda de stock vacía no carga esa sucursal; un stock declarado de 0 se rechaza (CIN-15).

## 7. Preguntas abiertas

- [x] ¿Qué ISBN llevan las obras reales sin ISBN conocido? Resuelto el 2026-11-23 con la decisión delegada DD-70 (`progress/decisiones-delegadas.md`): un identificador provisional de formato válido del rango reservado 979-0, que no se asigna a libros, declarado en el archivo de datos y no inventado por la carga; la persona puede escribir el ISBN real en el archivo antes de cargar.
- [x] ¿Cuántas sucursales con stock lleva cada obra? Resuelto el 2026-11-23 con la decisión delegada DD-72 (`progress/decisiones-delegadas.md`): la recepción y dos sucursales más por edición impresa, de modo que una carga completa cabe en dos ventanas del presupuesto por hora de ajustes del personal sin debilitar ese límite.
