# Recorte de pruebas — carril 1: política y comprobaciones estáticas (2026-11-27)

Rama `claude/pruebas-politica-fd6970`. Decisiones de la persona responsable del 2026-11-27, con sus palabras en `progress/decisiones-delegadas.md` (DD-150 a DD-153).

## 1. Política

- **La cobertura es referencia, no obligación:** ningún porcentaje bloquea un cambio ni un lanzamiento.
- **Se recorta por el valor de cada prueba** (vigencia, qué fallo detecta, si otra ya lo detecta), no por un mínimo. Un módulo crítico por debajo de 70 % se deja como está.
- Se mantiene: al menos una prueba por criterio de aceptación, concurrencia real de stock, límites y descargas, y los controles de seguridad y datos personales que ninguna otra prueba detecte.

| Referencia | Dominio y Aplicación | Total del módulo | Módulos |
| :--- | :---: | :---: | :--- |
| Crítico (y sin clasificar) | 85 % | 70 % | Inventario: Reservas, Fuentes, Traslados, Ajustes, Auditoría, Plataforma. E-commerce: Pagos, Pedidos, Identidad, Biblioteca, Auditoría, Plataforma, Web, Reclamaciones |
| No crítico | 60 % | 40 % | E-commerce: Catálogo, Carrito, Envíos, Devoluciones, Notificaciones, Asistente |

Detalle: `docs/07-proceso-de-desarrollo/03-estrategia-de-pruebas.md` §3. La lista la aplica `tools/CoverageCheck/PoliticaDeCobertura.cs`.

## 2. Herramienta de cobertura (commits `2bec81b2` y `231a1961`)

```bash
dotnet test BabelHub.sln --collect:"XPlat Code Coverage" --settings tests/coverlet.runsettings --results-directory TestResults
dotnet run --project tools/CoverageCheck -- TestResults --html TestResults/reporte-de-cobertura/index.html
```

Sale con 0 si pudo informar (lo que queda bajo su referencia sale como `AVISO`) y con 2 si el uso es inválido, no puede leer los informes o falta un servicio. El `2bec81b2` hacía fallar por porcentajes; lo sustituye `231a1961`.

| | Antes | Ahora |
| :--- | :--- | :--- |
| Medición | Por servicio | Por módulo |
| Porcentajes | Puerta (85 % y 70 %) | Referencia, solo avisa |
| Pruebas de la herramienta | 15 | 31 |

Probada sobre informes reales de las pruebas unitarias (5 974 del E-commerce y 2 537 del Inventario): asigna todos los archivos a un módulo y los 19 módulos con código quedan sobre su referencia en Dominio y Aplicación (el más bajo, Biblioteca, 88,8 %). El total por módulo con integración no se ha medido: la línea base con cobertura aún no existe y la puerta ya no depende de ella.

## 3. Comprobaciones estáticas (commit `bf26079b`)

Medido en esta máquina con `dotnet test --no-build --filter` por clase, con otros carriles corriendo a la vez (los tiempos son orientativos).

| Clase | Casos antes | Casos después | Duración antes | Duración después |
| :--- | ---: | ---: | ---: | ---: |
| `Registros/SecretosEnElRepositorioTests` | 2 856 | 5 | 1 min 51 s | 4 s |
| `Web/CodificacionDeSalidaTests` | 234 | 10 | 0,2 s | 0,2 s |
| `Plataforma/CopiasDeAdaptadoresTests` | 71 | 1 | 0,1 s | 0,04 s |
| **Total** | **3 161** | **16** | | |

El ahorro de tiempo está en la de secretos; las otras dos solo reducen el número de casos. Los patrones, las listas de excepciones y los archivos revisados no cambian. Las pruebas nuevas fallan además si no hay archivos que revisar.

### Defectos sembrados (temporales, retirados después)

| Clase | Sembrado | Resultado |
| :--- | :--- | :--- |
| Secretos | Una contraseña en una cadena de conexión y una clave de AWS falsa en dos archivos nuevos | Falla e informa los 2 |
| Codificación | `Html.Raw` en una vista, un valor del modelo en `onclick` en otra y `HtmlString` en un `.cs` del Inventario | Falla e informa los 3 |
| Copias | Una línea de más en dos copias del Inventario y una copia ausente | Falla e informa las 3, con la línea |

### Suite completa «antes» (medida por la coordinadora)

Sobre `d96d9873`, una corrida sola y en verde, sin cobertura ni `trx`: integración 45 min 33 s, `SpecCheck.Tests` 10 min 32 s, arquitectura 1 min 8 s, unitarias 17 s y 12 s. No habrá línea base con cobertura aparte; la medición «después» la hace la coordinadora con los tres carriles integrados.

## 4. Aprobación y verificación

| Puerta | Auditor | Arquitectura | Seguridad | Graves / Menores | Huella |
| :--- | :--- | :--- | :--- | :---: | :--- |
| Spec (2 rondas) | Con cambios menores | Cumple con observaciones | Cumple con observaciones | 0 / 8 | `faa36a564fdf42c7` |
| Plan (1 ronda) | Con cambios menores | Cumple con observaciones | Cumple | 0 / 5 | `bc4d142154dcf384` |

- La ronda 1 de la spec dio 2 Importantes (decisiones sin ratificar); la persona los resolvió y de paso cambió la política. Menores en `progress/hallazgos.md`.
- G9: la suite completa en verde la corrió la coordinadora sobre `d96d9873` (base de la rama; sin cobertura). En esta rama se corrieron solo las pruebas afectadas: compilación estricta, 264 de arquitectura, 31 de la herramienta, las 23 de `SpecCheck` y las clases de integración tocadas más `ContactoFueraDelRepositorioTests`. **No se corrió la suite completa.**
- No se escribió entrada en `progress/CHANGELOG.md` para no chocar con los otros carriles.

## 5. Para otros dueños

- Las `verificacion.md` de `transversal-seguridad` citan por número líneas de `CodificacionDeSalidaTests` y `SecretosEnElRepositorioTests` que se movieron; no se tocaron (no son de este carril).
- `progress/evidencias/cobertura/` y `07-evidencias-sprint-2.md` son fotos anteriores, con la regla por servicio.
- El criterio de recorte tiene un Menor abierto: la lista «No se quita» no nombra pagos, autenticación y sesión, auditoría transaccional, arquitectura ni el Notificador. Conviene aplicarla igualmente a esos casos.
- `SpecCheck.Tests` usa `transversal-plataforma` como spec de ejemplo: falla mientras esa spec tenga una pregunta abierta, y tarda unos 10 minutos (un proceso por caso); es candidata del carril de plataforma.
