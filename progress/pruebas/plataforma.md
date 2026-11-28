# Pruebas de infraestructura y plataforma: recorte y tiempos (carril 2, 2026-11-27)

Rama `claude/pruebas-plataforma-d98404`, sobre `d96d9873`. Criterios: estrategia de pruebas §3.3 del carril 1 (`e6db3226`) y las indicaciones de la persona transmitidas por la coordinación.

## 1. Resultado

- **Casos:** integración de mis carpetas, **1 880 → 1 728 (−152)**. `SpecCheck.Tests` (23) y arquitectura (264), sin cambios de casos.
- **Criterios:** 646 nombrados antes y después. Simulando la fusión de los tres carriles (sin conflictos): 647, ninguno perdido.
- **Tiempo:** la ganancia grande está en `SpecCheck.Tests` y en poder filtrar la integración. Quitar pruebas casi no ahorra tiempo (los 151 casos localizados en la partida sumaban 113 s, 88 s de ellos en la fase en serie).

| Qué | Antes | Después | Condiciones |
| :--- | :--- | :--- | :--- |
| `SpecCheck.Tests` (23) | 9 min 14 s (mía, con carga); 10 min 32 s (coordinación, sola) | **51 a 53 s** (dos corridas, máquina casi libre); 1 min 30 s a 2 min 6 s con carga | Comparar con la referencia de la coordinación es aproximado |
| Mis carpetas de integración completas | 1 880 casos, **1 h 12 min** con otros carriles activos | No medida | La mide la coordinación con los tres carriles |
| Mis carpetas sin `Guiones` ni `Lanzamiento` | No se podía filtrar | **1 194 casos, 2 min 27 s**, todos en verde | Máquina libre. No equivale a la corrida completa |
| `AlmacenR2ShTests` (32) | 6 min 25 s | **3 min 49 s y 3 min 17 s** | Mismo equipo, libre, antes y después |
| Arquitectura (264) | 48 s con carga | 25 a 26 s | Sin cambios: la diferencia es la carga |

**Efecto esperado en la suite completa (estimación, no medida):** unos minutos menos sobre los 45 min 33 s, casi todos por `AlmacenR2ShTests` (está en la fase en serie).

### Dónde se va el tiempo (partida, con carga)

- La **fase en serie** (colecciones sin paralelismo) duró **66 de los 72 minutos**; la paralela, unos 6.
- En serie y en `Guiones`: `DesplegarShTests` 2 057 s, `AlmacenR2ShTests` 558 s, `EndurecimientoDeVmAuxTests` 481 s, `AlmacenShTests` 227 s, `SellosDelRegistroTests` 105 s.
- `Lanzamiento` (26 casos) sumaba 654 s: humo de imágenes 339 s, `SinRed` 115 s, Compose en ejecución 74 s.

## 2. Vicios encontrados

| Tipo | Casos quitados | Ejemplos |
| :--- | ---: | :--- |
| Copia entre servicios (mismo código y mismas aserciones) | 110 | `ArranqueSeguroInventarioTests` repetía 56 casos del E-commerce; `LatidoDeLaBaseInventarioTests` solo cambiaba el `using` |
| Filas de teoría por la misma rama y la misma clase de entrada | 33 | `""` y `"   "`; dos códigos 409 que el cliente solo copia; filas iguales al caso por defecto |
| Solapadas con otra prueba | 4 | La URI válida de Evolution ya la usa el despliegue por defecto |
| Triviales | 4 | Reflexión sobre un constructor; una prueba que solo ejercitaba el doble |
| Vacías | 1 | Escribía un objeto viejo y lo borraba en la línea siguiente |
| **Total** | **152** | |

Otros vicios, sin quitar casos: `dotnet run` en cada llamada de `SpecCheck.Tests` y copia de todo `progress/` por caso; `r2-aplicar` repetido para preparar el mismo estado. Quedan: una aserción tautológica (`CargaInicialDelCatalogoTests`, ida y vuelta UTF-8 del mismo texto) y `ElLimiteDeIngresos_EsIdenticoExistaONoLaCuenta`, que pide dos veces la misma URL (es de límites: no se tocó).

## 3. Pruebas quitadas y motivo

### 3.1 Copia entre servicios (commit `fd907d2d`, salvo las dos últimas)

El superviviente es el método del mismo nombre del E-commerce. `CopiasDeAdaptadoresTests` obliga a que el código de producción siga igual en los dos servicios.

| Archivo | Casos | Se quitó | Se conserva en el Inventario |
| :--- | ---: | :--- | :--- |
| `ArranqueSeguroInventarioTests` | 56 | Marca de restauración, resolución del modo, ensayo (lista de huellas, credencial de producción, secretos propios), adaptadores por modo, trabajos de fondo y salud | Las 2 filas con las bases propias del Inventario y los 4 casos del proceso real |
| `LatidoDeLaBaseInventarioTests` (borrado) | 19 | Todo: el fixture aplica el SQL y el rol del E-commerce | Nada |
| `ManejoDeErroresInventarioTests` | 19 | Solicitud inválida, código genérico, errores del marco, solicitud sin errores, identificador de seguimiento y tabla de categorías | Límites, redacción del dato prohibido, dos de humo y la cobertura de todas las categorías |
| `TrabajosDeFondoPostgresInventarioTests` | 12 | Historial, nombres inestables, restricciones, permisos de rol y estado del umbral (usaban el SQL del E-commerce) | El registro de la última corrida y el umbral tras un reinicio, que ejercitan la unidad de trabajo del Inventario (esta sí difiere) |
| `BaseCaidaEs503Tests` (teoría del E-commerce) | 3 | Mismo manejador | La teoría del Inventario y los dos casos que prueban ambos |
| `ImagenesDeContenedorTests` (fila `Inventario`) | 1 | Lo cubren «los dos Dockerfile son el mismo» y la fila del E-commerce | |

### 3.2 Filas de teoría (commit `c4281a6b`)

| Archivo y método | Filas | Superviviente |
| :--- | ---: | :--- |
| `OpcionesDeContactoTests` (valor vacío ×2, dirección ×2, teléfono ×2, correo ×2) | 8 | La otra fila de la misma regla en cada teoría |
| `DesplegarShTests`: Staging con `simulador` | 2 | El despliegue por defecto ya escribe `simulador` |
| `DesplegarShTests`: generador `ninguna`, imagen sin etiqueta, dirección no numérica | 3 | Valor por defecto; `:latest` y digest corto; `10.0.0` y `0.0.0.0` |
| `AnfitrionDelEcommerceTests` sin contacto: `Ensayo`, `Restauracion` | 2 | Entorno desconocido `""` |
| `HttpClienteDelInventarioTests`: rechazos 409 y 404 | 4 | Otro código de la misma rama |
| `RestauracionCheckTests`: reserva `confirmada`, `cerrada`; salud `404/404` | 3 | `pendiente`, `liberada`, `expirada`; `503` y `500` |
| `ReintentoPorInterbloqueoInventarioTests`: `23505`, `57014` | 2 | `55P03` |
| `CifradoDeDiscoTests`: `PROVISIONING`, fallo total | 2 | `TERMINATED` y los dos fallos parciales |
| `RespaldarAhoraTests`: código `1`, sin argumentos | 2 | `75`; falta `--destino` |
| `AdaptadoresBaseTests` (hora de Lima), `OrigenDelBordeTests`, `ModoCargarStockTests`, `EspacioReservadoTests` (`""`), `AvisosDeVmAuxTests` (sin argumentos) | 5 | La otra fila de la misma rama |

### 3.3 Solapadas, triviales y vacías (commit `c4281a6b`)

| Prueba | Motivo |
| :--- | :--- |
| `EntornoStagingTests.En_Production_el_simulador_no_arranca…` | `AnfitrionDelEcommerceTests.EnProduccion_ConUnAdaptadorProvisional…` afirma lo mismo y más |
| `TrabajosDeFondoPostgresTests.LoGuardadoSobreviveAUnReinicio` | Cubierta por el registro de la última corrida y el umbral tras un reinicio |
| `DesplegarShTests.LaUriDeEvolution…SiSeDespliega` | La usa el despliegue correcto de ventas |
| `DesplegarShTests.SinLaVersionDeLosTextos…` | La prueba del orden de migración exige la orden contigua, sin esa variable |
| `TrabajoDeUmbralesDeAlertaTests.EstadoDeUmbralesEnMemoria…` | Trivial: un diccionario que otra prueba ya usa |
| `AdaptadoresBaseTests.RelojDelSistema_SinProveedor…` | Trivial: prueba el reloj del marco |
| `OpcionesDeContactoTests.El_constructor…no_es_publico…` | Trivial: reflexión |
| `RetencionTests.UnRespaldoBajoDemanda…` | Solo probaba el doble; lo real está en `RespaldosTests` |
| `VigilarRetencionTests.LosContenedoresQueNoExpiran…` | Vacía |

**Controles protegidos** (auditoría en la misma transacción, ingreso y sesión, límites, secretos, Notificador, arquitectura): no se quitó ninguna prueba que fuera la única en detectarlos, así que no hubo que recuperar ninguna. Las de secretos de `ArranqueSeguroInventarioTests` las detecta la gemela del E-commerce junto con la comprobación de copias.

## 4. Pruebas categorizadas (nada sale de la corrida completa)

- **`Categoria=Lanzamiento`:** `HumoDeImagenesTests`, `ComposeEnEjecucionTests`, `RestauracionConArchivoTests`, `UnaImagenDeTerceros…`, los dos `ConDockerDeVerdad_…`, `ConElAlmacenCaido…` y los dos casos de `SinRed` que compilan proyectos. Motivo: construyen imágenes, usan Docker real o registros remotos, compilan o restauran por completo.
- **`Categoria=Guiones`:** `DesplegarShTests`, `AlmacenShTests`, `AlmacenR2ShTests`, las clases de `Respaldos` que ejecutan guiones o contenedores y las cinco de `VmAux`. Motivo: corren en serie y concentran el tiempo.
- Aviso: la rama final de «algún componente no quedó sano» de `desplegar.sh` solo la ejercita un `ConDockerDeVerdad_…`.

## 5. Pedidos al carril 1 (por la coordinación)

1. En `.github/`, correr la integración en **dos trabajos paralelos**: `--filter "Categoria!=Guiones"` y `--filter "Categoria=Guiones"`, uniendo los informes de cobertura. Es la mayor ganancia disponible sin que una prueba pesada quite CPU a las de ventanas de tiempo.
2. Si `Lanzamiento` sale de la corrida de cada cambio, debe correr antes de lanzar (hoy `lanzamiento.yml` solo exige `ci.yml` en verde) y al tocar `infra/`, `scripts/` o un Dockerfile. `SinRed` ya tiene su paso propio en `ci.yml`.
3. Añadir a `CopiasDeAdaptadoresTests` los archivos que la auditoría halló iguales y sin proteger: `AutorizarLlamadaDeServicio.cs`, `EvaluarUmbralesDeAlerta.cs`, `CategoriaDeError.cs`, `ResultadoDeLimite.cs`, `ComprobacionDeLaBaseDeDatos.cs`, `ComprobacionDeSaludDeLaBase.cs`.

## 6. Decisiones delegadas (pendientes de ratificar)

| Id | Decisión | Motivo |
| :--- | :--- | :--- |
| DD-160 | Se quita la copia del Inventario de una prueba cuando las aserciones son las mismas y el código está protegido por la comprobación de copias; se conservan límites, redacción y proceso real. `CredencialDeServicioInventarioTests` y `RegistrosSinSecretosInventarioTests` **no** se tocan | Accesos y secretos con una parte del código sin proteger o sin prueba del anfitrión real |
| DD-161 | Dos categorías (`Lanzamiento`, `Guiones`) en vez de quitar pruebas lentas | Nada deja de correr en la suite completa |
| DD-162 | `SpecCheck.Tests` ejecuta el ensamblado, en seis clases paralelas, y copia solo los registros de `progress/` que la herramienta lee | Mismas reglas y aserciones |
| DD-163 | El estado conforme de R2 se genera una vez por clase | Misma comprobación de código de salida |
| DD-164 | No se cambió el paralelismo de las colecciones en serie | Exige repetir la suite completa para descartar intermitencias; ver §5.1 |

## 7. Condiciones y pendientes

- **Cobertura de `Inventario/Plataforma`:** la estrategia §3.3 dice no recortar un módulo crítico que ya esté bajo 70 %. No hay cobertura de partida y no la medí (el carril 3 vio 56,7 % con informes parciales). Si la medición completa lo muestra bajo 70 %, se revierte solo `fd907d2d` (ningún commit posterior toca sus archivos).
- **Intermitente:** `ArranqueSeguroEcommerceTests.FueraDeProduccion_LaRestauracionCorreSusPasosReales…` falló una vez en la partida (con carga) y pasó en dos corridas sueltas y en la corrida filtrada. No se cambió. Siguen abiertas las intermitencias ya registradas en `progress/hallazgos.md`.
- **Para revisar en `src/` (no se tocó):** ningún anfitrión llama a `UsarRedaccionDeRegistros` (AC-SEG-10) ni registra `EvaluarUmbralesDeAlerta` o `TrabajoDeUmbralesDeAlerta` (AC-SEG-09); lo comprobé por búsqueda de nombres. Según la auditoría, ninguna prueba envía HTTP al anfitrión real del Inventario para comprobar credencial o errores.
- **Ahorros no aplicados:** generar una vez los secretos en `DesplegarShTests`; PostgreSQL perezoso en `RotacionDeCredencialesTests` (solo 2 de 17 casos lo usan).

## 8. Verificación

| Comprobación | Resultado |
| :--- | :--- |
| `dotnet build BabelHub.sln -warnaserror` | 0 advertencias, 0 errores |
| `dotnet run --project tools/SpecCheck` | 24 specs, 0 avisos |
| Clases con bajas (con otra corrida a la vez) | 453 en verde, 5 min 4 s |
| Mis carpetas sin `Guiones` ni `Lanzamiento` | 1 194 en verde |
| `AlmacenR2ShTests` | 32 en verde, dos veces |
| `SpecCheck.Tests`, versión final | 23 en verde, cuatro veces |
| Arquitectura | 264 en verde |

**Sin verificar:** la suite completa; las categorías `Guiones` y `Lanzamiento` enteras después de los cambios (de `Guiones` corrieron las clases y métodos con bajas, no todas); Linux e integración continua; cobertura.
