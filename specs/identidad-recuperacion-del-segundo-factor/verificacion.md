---
spec: identidad-recuperacion-del-segundo-factor
fecha: 2026-11-27
---

# Verificación — Restablecimiento del segundo factor de la única administración desde la consola

## Resultado: Converge

## 1. Comprobaciones automáticas

| Comprobación | Comando | Resultado |
| :--- | :--- | :--- |
| Compilación sin advertencias | `dotnet build BabelHub.sln -warnaserror` | 0 advertencias y 0 errores. |
| Pruebas | `dotnet test BabelHub.sln` (un proyecto cada vez) | Unitarias 5831 y 2454, cobertura 15 y `SpecCheck` 23: todas correctas. Integración: 8887 correctas, 4 omitidas (cobros reales con Culqi) y **1 con error causado por este cambio**: el escáner de secretos marcó el nombre de una variable de `RestablecimientoDesdeLaConsolaTests` (falso positivo). Se renombró la variable, sin tocar el escáner, y el escáner y las pruebas del restablecimiento, repetidos, dan 2799 de 2799. La suite entera no se repitió tras ese renombrado. |
| Prueba de arquitectura | `dotnet test tests/architecture` (incluida en la suite) | 261 de 261. |
| Specs, planes, tareas y trazabilidad | `dotnet run --project tools/SpecCheck` | 24 specs, 0 errores y 0 avisos. |
| Búsqueda de secretos | `git grep -nIiE "password\s*=\|api[_-]?key\s*=\|secret\s*=" -- src` y el escáner de la suite | Sin secretos: las dos coincidencias de `src` son anteriores (la plantilla de la dirección `otpauth` y una asignación desde la configuración). |
| Archivos del cambio | `git status` | Sin `.env`, archivos digitales ni datos personales; los correos de las pruebas son ficticios. |
| Consola y navegador | Entorno local aislado (PostgreSQL y Redis en contenedores propios, borrados al terminar), entorno `Staging` | Alta del administrador, primer ingreso con segundo factor, orden de consola (código 0 y mensajes esperados; repetida, código 1), correo ausente de la salida, base con el factor borrado, sin renovaciones vivas y una fila de auditoría, y nueva configuración al ingresar. El aviso se probó con un transporte que no entrega: quedó en cola. **No** se ejecutó en las VMs. |
| Revisión aislada del código | `arch-reviewer` y `security-reviewer` | 0 Bloqueantes y 0 Importantes; sus Menores, corregidos o en `progress/hallazgos.md`. |

## 2. Requisito por requisito

Rutas: `Dom` = `src/BabelHub.Ecommerce/BabelHub.Ecommerce.Domain/Identidad/RestablecimientoDesdeLaInstalacion.cs`; `Caso` = `…Application/Identidad/Interno/RestablecimientoDelSegundoFactorDesdeLaInstalacion.cs`; `Modo` = `…Api/Plataforma/ModoRestablecerSegundoFactor.cs`; `U` = `tests/unit/BabelHub.Ecommerce.UnitTests/Identidad` (`RestablecimientoDesdeLaInstalacionTests` y `…ReglasTests`); `I` = `tests/integration/BabelHub.IntegrationTests/Identidad` (`RestablecimientoDesdeLaConsolaTests` y `ModoDeArranqueDelRestablecimientoTests`).

| Requisito | Evidencia en código | Criterio y prueba | Estado |
| :--- | :--- | :--- | :--- |
| RSF-01 | `Caso:55-58` | AC-RSF-01 · `U`, `I` | Cumple |
| RSF-02 | `Dom:34-35`, `Caso:25-29`, `Modo` (`Ejecutar`), `Program.cs:24-26` | AC-RSF-02 · `U`, `I` (también el modo leído de la variable y que solo la consola usa el contrato) | Cumple |
| RSF-03 | `Caso:31-35` | AC-RSF-03 · `U` | Cumple |
| RSF-04 | `Caso:37-40`, `Dom:42-45` | AC-RSF-04 · `U`, `I` | Cumple |
| RSF-05 | `Dom:17`, `Dom:47-50` | AC-RSF-05 · `U`, `I` | Cumple |
| RSF-06 | `Dom:52` | AC-RSF-06 · `U`, `I` | Cumple |
| RSF-07 | `Caso:58` | AC-RSF-01, AC-RSF-10 · `U`, `I` | Cumple |
| RSF-08 | `Caso:57`, `…Infrastructure/Identidad/RepositoriosDeSesiones.cs:128`, `…Application/Identidad/Interno/Ingreso.cs` (`Ingresar` e `IniciarConfiguracionDelSegundoFactor`) | AC-RSF-01, AC-RSF-10 · `U`, `I` | Cumple |
| RSF-09 | `Caso:59-61` | AC-RSF-07 · `U`, `I` | Cumple |
| RSF-10 | `Caso:71-79`, `AvisosDeCuenta.SegundoFactorRestablecido` | AC-RSF-08 · `U` | Cumple |
| RSF-11 | `…Application/Plataforma/Interno/RenderizadorDeCorreoDeProduccion.cs:18` | AC-RSF-08 · `U` | Cumple |
| RSF-12 | `Caso:72-79` | AC-RSF-08 · `U`, `I` | Cumple |
| RSF-13 | `Modo` (`Informar`) | AC-RSF-09 · `I` | Cumple |
| RSF-14 | `Modo` (argumentos y mensajes fijos), `AnfitrionDeConsola` (`silenciarRegistros`) | AC-RSF-09 · `I` | Cumple |
| RSF-15 | `Caso:42-64` (una unidad de trabajo), `Modo` (`NoSeCompleto`) | AC-RSF-07, AC-RSF-11 · `U`, `I` | Cumple |
| RSF-16 | `Caso:46-49`, `…Infrastructure/Identidad/RepositoriosDeCuentas.cs:178` | AC-RSF-10 · `U`, `I` (dos órdenes, reactivación e ingresos a la vez) | Cumple |

## 3. Diferencias encontradas

- El nombre de la regla de la temporal difiere del plan, y el ingreso compara además la contraseña verificada: anotado en `decisiones-de-implementacion.md`.
- El caso límite de una administración dada de alta en el mismo instante (spec §6) es una limitación aceptada y no tiene prueba.
- El texto de la spec que conviene precisar al reabrirla está en `progress/hallazgos.md`; no cambia el comportamiento.

## 4. Conclusión

Los 16 requisitos se cumplen con pruebas que los nombran; el único fallo de la corrida completa venía del nombre de una variable de prueba y se corrigió. Quedan fuera de esta verificación la ejecución en la VM y el envío real del correo.
