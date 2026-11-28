# Recorte de pruebas de los módulos de negocio (carril 3)

Fecha: 2026-11-27. Rama: `claude/pruebas-negocio-c1f63f`. Alcance: `tests/unit/**` y las carpetas de negocio de `tests/integration/BabelHub.IntegrationTests/`. Sin cambios en `src/`.

Tres pasos:

1. Recorte de los módulos no críticos con los pisos 85/70 y 60/40.
2. Por decisión de la persona (2026-11-27), los porcentajes pasan a ser **solo referencia** y se recorta por el valor de cada prueba, también en los críticos.
3. Revisión contra la estrategia del carril 1 (`e6db3226`, §3.3): **se recuperaron 220 pruebas** que protegían pagos, ingreso y sesión, auditoría en la misma transacción, el Notificador o un control de seguridad o datos personales.

## 1. Resultado

| Casos | Antes | Después | Quitado |
| :--- | ---: | ---: | ---: |
| Unitarias | 8511 | 8081 | −430 (−5,1 %) |
| Integración (mis carpetas) | 4259 | 4074 | −185 (−4,3 %) |
| De ellos, no críticos: unitarias | 1887 | 1555 | −332 (−18 %) |
| De ellos, no críticos: integración | 879 | 805 | −74 (−8 %) |
| De ellos, críticos y soporte: unitarias | 6624 | 6526 | −98 (−1,5 %) |
| De ellos, críticos: integración | 3380 | 3269 | −111 (−3,3 %) |

403 métodos de prueba menos y 73 teorías con 182 filas menos; 162 archivos tocados y unas 5800 líneas menos.

**Tiempo.** Lo quitado sumaba en la línea base **97 s en integración** (de 1970 s sumados en mis carpetas, −5 %) y 12 s en unitarias. Dos pruebas explican 66 s: el tope de 7 días de la sesión (46 s) y el libro de 50 MiB contra el adaptador S3 (20 s). La suite completa sobre `d96d9873` tarda 45 min 33 s en integración (dato de la coordinación) y mi corrida de línea base, solas mis carpetas, unos 6 minutos: **la mayor parte del tiempo está fuera de mi alcance**. La medición «después» la hará la coordinación con los tres carriles integrados.

## 2. Vicios encontrados (métodos que quedan quitados)

La clasificación es aproximada: se asignó por el nombre y el archivo de cada método.

| Vicio | Métodos | Ejemplos | Por qué aporta poco |
| :--- | ---: | :--- | :--- |
| Variantes redundantes de la misma regla | 223 | «Repetir no cambia nada», «sin datos no hace nada», el mismo camino feliz con otro actor | Fallan junto con la prueba principal del criterio. |
| Composición, constantes y forma del código | 70 | `ElContenedor_ResuelveTodos…`, `CadaConstanteDeAccion_TieneElNombreDeSuValor`, `ClaveDePolitica_ConElMismoValor_EsIgual`, «la raíz expone los contratos», nombres e intervalos de trabajos | Lo detectan el compilador, `tests/architecture` o las pruebas con el host real. |
| El mismo comportamiento en otro nivel | 47 | API y pantallas con la aplicación simulada que repiten validaciones, estados vacíos, pantallas de textos pendientes ya probadas con la sesión real | El segundo nivel no demuestra nada propio. |
| Pruebas del soporte, de los dobles o de la propia prueba | 33 | `ActoresDePruebaTests` (en los dos servicios), `DoblesDeEnviosTests`, dobles de reservas, `PostgreSQL_NoLanzaErrorSinAlcance…`, `ElIdentificadorQueUsaElFixture…`, «la revisión de colores los detecta» | Prueban código de pruebas o a PostgreSQL, no el producto. |
| Simulador de la pasarela | 12 | Cobro lento, el simulador no es idempotente, reembolsos simulados | Prueban un doble que no se admite en producción. |
| Persistencia repetida o trivial | 7 | Estado guardado y leído que otra prueba ya recorre | Sin fallo propio. |
| Guardas de argumentos de programación | 6 | `Consultar_ConArgumentosInvalidos_Lanza`, `Los_topes_deben_ser_positivos` | Ninguna entrada de usuario las alcanza. |
| El mismo bucle de trabajos de fondo por módulo | 5 | «La cancelación del apagado no es un fallo», «corre en cada intervalo» | Se conservan las alertas por fallos. |

En las teorías, el vicio fue **muchas filas de la misma clase** (formatos inválidos, páginas, códigos de error): se conservan las clases y los bordes.

## 3. Lo que protege los puntos pedidos

| Protección | Estado | Pruebas que la sostienen |
| :--- | :--- | :--- |
| Pagos | Recuperadas todas las bajas de integración de Pagos salvo el simulador (12) y los dobles de reservas (6), y el flujo del paso al pago de Carrito. Queda quitada una unitaria de Pagos que solo comprobaba la forma de un enumerado. | `Pagos/*`, `PasarAlPagoTests`, `PrepararPasoAlPagoTests`, `IntencionYConfirmacionTests`, `ReservarYAbortarTests` |
| Ingreso y sesión | Recuperadas todas las bajas de integración de Identidad, las filas de ingreso de Web y las cookies de visitante. Sigue quitado el tope de 7 días de extremo a extremo. | `SesionWebTests`, `CargarSesionWebTests.El_cliente_que_actua_cada_20_minutos_sigue_hasta_los_7_dias…`, fila de 8 horas de `SesionDeExtremoAExtremoTests` |
| Auditoría en la misma transacción | Recuperada entera `AuditoriaInventario`. No se quitó ninguna prueba de «con la auditoría caída». | `LaAuditoriaSeConfirmaConLaOperacion_YSeRevierte…` y `SiLaTablaDeAuditoriaFalla…` (dos servicios), `Si_el_almacen_no_responde_IAuditoria_falla…` |
| Una falla del Notificador no interrumpe | Recuperadas «si la alerta también falla» y «solicitud nula». | `Con_el_notificador_caido_el_estado_cambia_igual`, `Un_Notificador_que_falla_no_interrumpe_el_cambio…`, `SiLaBaseFalla_NuncaLanza…`, `Si_el_Notificador_no_puede_guardar_el_aviso…`, `UnErrorForzadoDelInsert_NoAbortaLaOperacionDelModulo…` |
| Arquitectura | No se tocó (no es de este carril). | 264 superadas |

También se recuperaron los casos de concurrencia de límites y descargas, retención y supresión, consentimiento, redacción, secretos, topes de entrada y codificación de salida. Las pantallas de textos pendientes que siguen quitadas las cubren `ClienteYPersonal_AlAbrirPantallas…`, `UnEncargadoDeOtraSucursal_VeSinPermiso…`, `LaHojaDeReclamacionYElLibro_SiguenDisponibles…`, `UnVisitante_NoConsultaAIdentidad` y `ElClienteQueAbreAdministracion…`; las de descarga caducada en S3, `Un_enlace_emitido_hace_diez_minutos_no_completa…`.

**Límite de esta revisión:** se hizo por nombres, datos de las filas y líneas que dejaron de cubrirse, sin sembrar defectos. La estrategia pide nombrar, para cada prueba quitada, la que detecta su fallo; solo se hizo para estas protecciones, no para las 403.

## 4. Cómo se midió

- Casos y segundos: informes `trx`. Los segundos son la **suma de las duraciones de cada prueba**, no el reloj de pared. Las corridas posteriores tuvieron más carga, así que no se comparan segundos antes y después: solo se cuenta la duración base de lo quitado.
- Cobertura: `tools/CoverageCheck` de `231a1961` (carril 1), ejecutado desde una copia temporal, sobre las unitarias y **solo mis** carpetas de integración. **El total por módulo con la suite de integración completa nunca se midió.**
- Criterios: el conjunto de `[Trait("Criterio", …)]` de todo `tests/` es el mismo antes y después (646).

## 5. Casos por módulo

| Módulo | Unitarias | Integración |
| :--- | ---: | ---: |
| Ajustes (Inventario) | 165 | 48 |
| Auditoria (Inventario) | 814 → 803 | 90 |
| Auditoria | 877 → 866 | 187 → 180 |
| Biblioteca | 155 → 153 | 104 → 96 |
| Carrito | 679 → 563 | 248 → 230 |
| Catalogo | 466 → 364 | 204 → 192 |
| Devoluciones | 297 → 263 | 156 → 142 |
| Envios | 257 → 197 | 132 → 112 |
| Fuentes (Inventario) | 367 → 343 | 146 → 140 |
| Identidad | 750 → 749 | 342 |
| Notificaciones | 188 → 168 | 139 → 129 |
| Otros | 14 → 2 | 2 |
| Pagos | 583 → 582 | 401 → 383 |
| Pedidos | 351 | 226 → 223 |
| Plataforma (Inventario) | 421 | 0 |
| Plataforma | 646 → 637 | 0 |
| Reclamaciones | 312 → 311 | 161 → 154 |
| Reservas (Inventario) | 500 | 245 → 237 |
| Traslados (Inventario) | 263 | 155 → 151 |
| Web | 406 → 380 | 1273 → 1223 |
| **Total** | 8511 → 8081 | 4259 → 4074 |

Las carpetas de integración de Plataforma son de otro carril.

## 6. Cobertura por módulo (referencial; Dominio y Aplicación / total, %)

Medición parcial (ver §4): sirve para comparar antes y después, no como cifra del módulo.

| Módulo | Nivel | Antes | Después |
| :--- | :--- | ---: | ---: |
| Ecommerce/Auditoria | crítico | 96.2 / 95.1 | 96.2 / 95.1 |
| Ecommerce/Biblioteca | crítico | 91.7 / 90.4 | 91.7 / 90.4 |
| Ecommerce/Carrito | no crítico | 94.3 / 94.8 | 92.8 / 93.4 |
| Ecommerce/Catalogo | no crítico | 94.8 / 94.6 | 92.9 / 92.8 |
| Ecommerce/Devoluciones | no crítico | 96.3 / 94.4 | 94.3 / 92.8 |
| Ecommerce/Envios | no crítico | 92.2 / 93.8 | 91.4 / 92.8 |
| Ecommerce/Identidad | crítico | 97.0 / 97.0 | 97.0 / 97.0 |
| Ecommerce/Notificaciones | no crítico | 96.5 / 94.0 | 95.2 / 92.9 |
| Ecommerce/Pagos | crítico | 96.5 / 96.6 | 96.5 / 96.6 |
| Ecommerce/Pedidos | crítico | 96.0 / 93.2 | 96.0 / 93.2 |
| Ecommerce/Plataforma | crítico | 94.9 / 78.7 | 93.8 / 78.1 |
| Ecommerce/Reclamaciones | crítico | 94.5 / 94.7 | 94.5 / 94.7 |
| Ecommerce/Web | crítico | 97.8 / 94.2 | 97.8 / 94.2 |
| Inventario/Ajustes | crítico | 92.3 / 93.4 | 92.3 / 93.4 |
| Inventario/Auditoria | crítico | 95.1 / 93.5 | 95.1 / 93.5 |
| Inventario/Fuentes | crítico | 98.7 / 98.8 | 98.1 / 98.5 |
| Inventario/Plataforma | crítico | 94.7 / 56.7 | 94.7 / 56.7 |
| Inventario/Reservas | crítico | 96.5 / 96.7 | 96.5 / 96.7 |
| Inventario/Traslados | crítico | 96.4 / 97.5 | 96.4 / 97.5 |

`Inventario/Plataforma` queda bajo la referencia porque sus pruebas de integración están en carpetas que no ejecuté; como no pude confirmar que supere 70 %, **no se recortó**.

## 7. Qué más se conservó a propósito

- En los módulos críticos: rechazos, matrices de autorización, concurrencia sobre base real, dinero y datos personales.
- Guardas de arranque en producción, permisos y restricciones de base de datos e inmutabilidad de la auditoría.
- Las repeticiones que fija un plan aprobado: 21 pares × 20 en Pedidos, 50 carreras en Pagos y 50 pares en Devoluciones (≈245 s en la línea base).
- `CargaDelAcumuladoTests` (≈49 s): es concurrencia real de un límite, aunque sea inestable.
- `AuditoriaRealDelCatalogoTests`: sus filas prueban la atomicidad de la auditoría operación por operación.

## 8. Decisiones delegadas (por ratificar)

Se anotan aquí para no chocar con los otros carriles en `progress/decisiones-delegadas.md`.

| Id | Decisión | Motivo |
| :--- | :--- | :--- |
| DD-170 | Sigue quitada la fila del cliente (7 días) de la sesión de extremo a extremo, aunque la tarea T19 de `web-base` menciona los dos topes en integración. | Lo prueban las unitarias de sesión; la fila de 8 horas demuestra el mismo cableado real. |
| DD-171 | Las copias de pruebas entre los dos servicios no se consideran repetidas cuando ejecutan código propio: `AuditoriaInventario` quedó entera. Solo siguen quitadas las de forma y constantes. | Una prueba del otro servicio no detecta ese fallo. |
| DD-172 | No se reducen las repeticiones fijadas por un plan aprobado. | No es quitar una prueba repetida, sino debilitarla. |
| DD-173 | Se quitan segundos niveles y variantes aunque cubran un caso de rechazo, si queda otra prueba del mismo comportamiento. | Criterio de valor de cada prueba. |
| DD-174 | `Inventario/Plataforma` no se recorta. | Mi medición parcial no llega a 70 %. |
| DD-175 | Ante la duda en las protecciones, se recupera la prueba (a menudo el archivo entero). | Recuperar cuesta segundos; perder un control no. |
| DD-176 | Siguen quitadas las pruebas del simulador de la pasarela y de los dobles de reservas. | No protegen pagos reales: la guarda rechaza el simulador en producción y el adaptador real tiene sus pruebas. |

## 9. Verificación y pendientes

**Verificado** sobre el estado final: `dotnet build BabelHub.sln -warnaserror` sin advertencias; unitarias (5585 y 2496, 0 fallos); arquitectura (264); `SpecCheck` sin errores ni avisos; 646 criterios nombrados. Mis carpetas de integración: 4069 superadas, 4 omitidas y **1 fallo** en una prueba que no cambié, que pasó 3 veces sola y dentro de toda la carpeta Reservas (237 de 237).

**No verificado:** la suite completa, las carpetas de otros carriles, E2E y carga.

**Pruebas inestables, en código que no toqué:**

- `ConcentracionDeReservasPostgresTests.ConElMaximoEnDiez_OnceUnidades…`: falló en la corrida final porque el aviso no contenía el identificador de la edición. Probable causa, sin comprobar: la redacción de registros toma las cifras de un identificador al azar por un dato prohibido.
- `EjecucionDeLaCancelacionTests.Al_ejecutarse_la_cancelacion_llama_a_los_receptores_en_el_orden_decidido…`: la lista estática `AplicadorDePrueba.OrdenDeLlamada` recibió llamadas duplicadas en una corrida.
- `CargaDelAcumuladoTests`: falló en la línea base por `lock timeout` con carga.
