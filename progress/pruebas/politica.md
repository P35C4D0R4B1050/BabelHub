# Recorte de pruebas — carril 1: política y comprobaciones estáticas (2026-11-27)

Rama `claude/pruebas-politica-fd6970`. La persona responsable decidió el 2026-11-27 (comunicado por la sesión coordinadora) mantener la cobertura de 85 % y 70 % solo en lo crítico, con una base «40-60» en el resto.

## 1. Política

| Criticidad | Dominio y Aplicación | Total del módulo | Módulos |
| :--- | :---: | :---: | :--- |
| Crítico (y sin clasificar) | ≥ 85 % | ≥ 70 % | Inventario: Reservas, Fuentes, Traslados, Ajustes, Auditoría, Plataforma. E-commerce: Pagos, Pedidos, Identidad, Biblioteca, Auditoría, Plataforma, Web, Reclamaciones |
| No crítico | ≥ 60 % | ≥ 40 % | E-commerce: Catálogo, Carrito, Envíos, Devoluciones, Notificaciones, Asistente |

- Detalle y criterio para recortar: `docs/07-proceso-de-desarrollo/03-estrategia-de-pruebas.md` §3.
- Lista que se hace cumplir: `tools/CoverageCheck/PoliticaDeCobertura.cs`.
- Por ratificar: DD-150 a DD-153 (`progress/decisiones-delegadas.md`).

## 2. Herramienta de cobertura (commit `2bec81b2`)

```bash
dotnet test BabelHub.sln --collect:"XPlat Code Coverage" --settings tests/coverlet.runsettings --results-directory TestResults
dotnet run --project tools/CoverageCheck -- TestResults --html TestResults/reporte-de-cobertura/index.html
```

Sale con 0, 1 (algún módulo no cumple) o 2 (uso o informes inválidos). Con informes parciales, los módulos críticos sin datos fallan: para un carril, lo útil es la fila de su módulo.

| | Antes | Después |
| :--- | :--- | :--- |
| Medición | Por servicio | Por módulo según criticidad |
| Mínimos desde la línea de órdenes | `--minimo-*` | No se pueden cambiar |
| Pruebas de la herramienta | 15 | 30 |

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

## 4. Para otros dueños

- Las `verificacion.md` de `transversal-seguridad` citan líneas de `CodificacionDeSalidaTests` y `SecretosEnElRepositorioTests` que se movieron; no se tocaron (no son de este carril).
- `progress/evidencias/cobertura/` y `07-evidencias-sprint-2.md` son fotos anteriores, con la regla por servicio.
