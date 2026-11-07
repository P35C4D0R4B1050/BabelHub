---
id: transversal-plataforma
titulo: Calidad de la plataforma y operación
estado: Draft
servicio: Transversal
prefijo: PLA
origen: [RNF10, RNF11, RNF12, RNF19, RNF24, RNF28]
calidad: [RNF09, AC02, AC03, AC04, AC05]
depende: []
---

# Spec — Calidad de la plataforma y operación

> **Qué y por qué, nunca cómo.** Esta spec reúne los requisitos no funcionales de plataforma que no pertenecen a ninguna feature de negocio y que antes no tenían dueño ni criterio de aceptación (auditoría R6, H07). Describe lo que debe cumplirse al construir, liberar y operar el sistema; las herramientas concretas (integración continua, escáneres, respaldos, medición) se eligen en el plan y en `docs/07-proceso-de-desarrollo/03-estrategia-de-pruebas.md`.

## 1. Propósito

Garantizar que el sistema se pueda probar, ejecutar igual en las dos arquitecturas de procesador, operar dentro de los recursos gratuitos, liberar sin vulnerabilidades conocidas, recuperar tras una falla y evolucionar sin romper sus contratos. Responde a RNF10, RNF11, RNF12, RNF19, RNF24 y RNF28.

## 2. Alcance

| Dentro | Fuera |
| :--- | :--- |
| Verificación automática de pruebas, dependencias entre capas y cobertura. | Las pruebas de cada feature (viven en su spec). |
| Imágenes que funcionan en ARM64 y x86_64 y despliegue con un comando por VM. | El reparto de servicios entre VMs (`docs/02-arquitectura-de-software/05-despliegue.md`). |
| Presupuesto de recursos y alerta de costo. | Las medidas de seguridad de datos y accesos (`transversal-seguridad`). |
| Control de vulnerabilidades, lista de materiales de software y contenedores sin privilegios. | La respuesta a incidentes (`docs/05-seguridad/07-operacion-segura.md`). |
| Respaldo, archivado continuo y restauración probada. | Alta disponibilidad con redundancia (no hay SLA, DA03). |
| Versionado de los contratos entre servicios. | Los contratos concretos de cada feature. |

## 3. Requisitos (EARS)

| ID | Patrón | Requisito | Origen |
| :--- | :--- | :--- | :--- |
| PLA-01 | Ubicuo | El sistema deberá permitir probar sus reglas de negocio sin base de datos ni servicios externos. | RNF10 |
| PLA-02 | Ubicuo | El sistema deberá tener, para cada criterio de aceptación de cada spec que se implementa, al menos una prueba que lo nombre, y la verificación automática deberá fallar si queda alguno sin prueba. | RNF10 |
| PLA-03 | Ubicuo | El sistema deberá publicarse en imágenes de contenedor que se comporten igual en procesadores ARM64 y x86_64. | RNF11 |
| PLA-04 | Evento | Cuando se despliega una VM, el sistema deberá levantar todo lo que le corresponde con un solo comando. | RNF11 |
| PLA-05 | Ubicuo | El sistema completo deberá operar con los recursos gratuitos de una cuenta, con un uso sostenido de memoria y de procesador menor al 70 % en cada VM durante la prueba de carga. | RNF12, AC02 |
| PLA-06 | No deseado | Si el costo mensual de la cuenta supera cero, entonces el sistema deberá avisar a la administración mediante una alerta de presupuesto de 1 USD. | RNF12 |
| PLA-07 | Evento | Cuando se prepara un lanzamiento, el sistema deberá comprobar que sus dependencias e imágenes no tienen vulnerabilidades críticas ni altas conocidas y rechazar el lanzamiento si las tienen. | RNF19, AC04 |
| PLA-08 | Ubicuo | El sistema deberá generar una lista de materiales de software por cada lanzamiento, fijar la versión de cada dependencia e imagen y ejecutar sus contenedores sin privilegios. | RNF19, AC04 |
| PLA-09 | Ubicuo | El sistema deberá hacer un respaldo diario cifrado de sus datos y archivar de forma continua sus cambios en un lugar fuera de la cuenta de la nube, de modo que se pierdan como máximo 24 horas de datos. | RNF24, AC03 |
| PLA-10 | Evento | Cuando una falla obliga a restaurar el sistema, el sistema deberá poder volver a operar en un máximo de 4 horas a partir del último respaldo. | RNF24, AC03 |
| PLA-11 | Evento | Cuando se prepara un despliegue mayor, el sistema deberá haber restaurado antes un respaldo reciente en un entorno de prueba y comprobado que sus datos son consistentes. | RNF24, AC03 |
| PLA-12 | Ubicuo | El sistema deberá versionar en la ruta los contratos entre servicios, de modo que un cambio incompatible use una versión nueva y la anterior siga funcionando durante la transición. | RNF28, AC05 |
| PLA-13 | Ubicuo | El sistema deberá verificar de forma automática en cada cambio las reglas de dependencia entre capas, módulos y servicios, y rechazar el cambio que las incumpla. | RNF28, RNF09, AC05 |
| PLA-14 | Ubicuo | El sistema deberá mantener una cobertura de líneas de al menos 85 % en Dominio y Aplicación y de al menos 70 % en el sistema completo, y la verificación automática deberá fallar si baja de esos umbrales. | RNF28, AC05 |

## 4. Criterios de aceptación

Estos criterios se comprueban con la verificación automática (integración continua), con las pruebas de carga y con un ensayo de restauración; no hay pantalla asociada.

| ID | Cubre | Escenario |
| :--- | :--- | :--- |
| AC-PLA-01 | PLA-01 | **Dado** una regla de negocio de Dominio o Aplicación, **cuando** se ejecutan sus pruebas sin base de datos ni red, **entonces** se ejecutan y pasan. |
| AC-PLA-02 | PLA-02 | **Dado** una spec en implementación con un criterio que ninguna prueba nombra, **cuando** corre la verificación automática, **entonces** falla e indica el criterio sin prueba. |
| AC-PLA-03 | PLA-03 | **Dado** cada imagen de contenedor del sistema, **cuando** se inspecta su publicación, **entonces** existe para ARM64 y para x86_64, y las pruebas de humo de ambas dan el mismo resultado. |
| AC-PLA-04 | PLA-04 | **Dado** una VM nueva con su configuración, **cuando** se ejecuta el comando de despliegue, **entonces** quedan en marcha todos los componentes de esa VM y responde su comprobación de salud sin pasos manuales adicionales. |
| AC-PLA-05 | PLA-05 | **Dado** la prueba de carga con el perfil previsto, **cuando** termina, **entonces** el uso sostenido de memoria y de procesador de cada VM es menor al 70 % y no hubo errores por falta de recursos. |
| AC-PLA-06 | PLA-06 | **Dado** una cuenta configurada con la alerta de presupuesto, **cuando** el costo mensual supera cero, **entonces** la administración recibe el aviso; y si el costo es cero, no hay aviso. |
| AC-PLA-07 | PLA-07 | **Dado** una dependencia con una vulnerabilidad alta conocida, **cuando** se prepara el lanzamiento, **entonces** el lanzamiento se rechaza y se indica la dependencia; sin vulnerabilidades críticas ni altas, pasa. |
| AC-PLA-08 | PLA-08 | **Dado** un lanzamiento, **cuando** se revisa su resultado, **entonces** incluye la lista de materiales de software, las versiones están fijadas (ninguna es «la última») y ningún contenedor corre con privilegios. |
| AC-PLA-09 | PLA-09 | **Dado** el respaldo diario y el archivado continuo en funcionamiento, **cuando** se revisa el lugar externo, **entonces** hay un respaldo cifrado de las últimas 24 horas y el archivo de cambios llega hasta pocos minutos antes. |
| AC-PLA-10 | PLA-10 | **Dado** una base destruida en un entorno de prueba, **cuando** se ejecuta el procedimiento de restauración desde el último respaldo, **entonces** el sistema vuelve a operar en 4 horas o menos y sin perder más de 24 horas de datos. |
| AC-PLA-11 | PLA-11 | **Dado** un despliegue mayor, **cuando** no existe un ensayo de restauración reciente que haya terminado bien, **entonces** el despliegue no continúa; con un ensayo reciente exitoso, continúa. |
| AC-PLA-12 | PLA-12 | **Dado** un cambio incompatible en un contrato, **cuando** se publica, **entonces** aparece una versión nueva en la ruta y las llamadas a la versión anterior siguen respondiendo igual durante la transición. |
| AC-PLA-13 | PLA-13 | **Dado** un cambio en el que un módulo usa el interior de otro, **cuando** corre la verificación automática, **entonces** falla señalando la dependencia, y el cambio no se integra. |
| AC-PLA-14 | PLA-14 | **Dado** un cambio que baja la cobertura de Dominio y Aplicación a 84 %, **cuando** corre la verificación automática, **entonces** falla; con 85 % o más en Dominio y Aplicación y 70 % o más en total, pasa. |

## 5. Reglas e invariantes

- Los umbrales (70 % de recursos, 85 % y 70 % de cobertura, 24 horas de datos y 4 horas de recuperación) son los de los RNF y no se debilitan para que algo pase.
- Un lanzamiento solo sale si pasan a la vez la compilación sin advertencias, las pruebas, la verificación de arquitectura, el control de vulnerabilidades y los umbrales de cobertura.
- El respaldo nunca se guarda en el mismo lugar que los datos que respalda: el archivo externo es lo que permite recuperar si se pierde la cuenta de la nube o la VM de datos.
- Ningún secreto, respaldo en claro ni dato personal se guarda en el repositorio ni en los resultados de la verificación automática.

## 6. Casos límite y errores

- Una vulnerabilidad sin corrección disponible se registra como excepción documentada, con fecha de revisión y aprobación de la persona responsable; no se ignora en silencio.
- Si la prueba de carga no cabe en los recursos de una VM, se reduce el consumo (límites de memoria, caché) antes de pedir recursos de pago (`docs/02-arquitectura-de-software/07-presupuesto-de-recursos.md`).
- Si falla la restauración de ensayo, el despliegue mayor queda detenido y se registra un bloqueo.
- La herramienta de archivado y el lugar externo del respaldo se eligen en el plan (bloqueo B11 y bloqueo B02 por la cuota gratuita del almacén).
- El sistema no tiene SLA ni redundancia (DA03): PLA-10 mide la recuperación, no la continuidad del servicio.

## 7. Preguntas abiertas

- [x] ¿Los RNF de plataforma necesitan una spec propia o basta con declararlos fuera de las specs? Resuelto el 2026-11-06 en la auditoría R6 (H07): se escribe esta spec para que cada uno tenga requisito, criterio y prueba.
