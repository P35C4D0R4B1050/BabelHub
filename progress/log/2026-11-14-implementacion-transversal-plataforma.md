# 2026-11-14 — Implementación de `transversal-plataforma` (T06 a T25)

## Qué se hizo
- Se implementó `transversal-plataforma` tarea por tarea, con pruebas primero. **20 de 42 tareas** (T06 a T25), commits de `980454d` (T06) a T25; sin `push`. Las cinco tareas T01 a T05 son spikes que necesitan la cuenta de Oracle, las VMs o datos reales y las ejecuta la persona responsable.
- **Puertas de calidad (T06 a T11):** `SpecCheck` exige `[Trait("Criterio", …)]` como evidencia, puerta de cobertura (`tools/CoverageCheck`: 85 % de Dominio y Aplicación y 70 % del total, con los informes unidos por archivo), reglas de dependencia y separación de `RestauracionCheck`, escáner de secretos (gitleaks y Trivy fijados por digest), versiones fijadas (acciones por SHA, NuGet con `packages.lock.json`, imágenes por digest) y pruebas de Dominio y Aplicación sin red.
- **Registro de supresiones y restauración (T12 a T18):** entradas y sellos, casos de uso y contrato público, reaplicación y verificación del registro, adaptador sobre el almacén de objetos (creación única atómica), tablas de trabajos de fondo y latido, modos de arranque (`Restauracion`, `Ensayo`), marca de un solo uso y salud, y script de rotación de credenciales (PLA-43).
- **Imágenes y despliegue (T19 a T23):** Dockerfiles multi-arquitectura, imagen de PostgreSQL con `age` y `rclone`, un Compose por VM con contenedores endurecidos (usuario sin privilegios, límites, salud, solo lectura y excepciones registradas), definición del almacén de objetos (contenedores, credenciales mínimas, retención con bloqueo y ciclo de vida, `scripts/almacen.sh`), respaldo diario y archivado de WAL cifrados con `age` (restauración real de las dos bases con solo la clave privada), respaldo bajo demanda por `ProxyJump` y `scripts/desplegar.sh <vm>`.
- **Retención (T24):** simulación de 800 días con el ciclo de vida publicado y vigilante de VM-AUX (`scripts/vm-aux/vigilar-retencion.sh`).

## Decisiones
- Todas están en `specs/transversal-plataforma/decisiones-de-implementacion.md` (una fila por tarea).
- **El plazo mensual pasa de 90 a 92 días:** la simulación de T24 demostró que 90 dejaba solo 2 fines de mes algunos días, contra los 3 que promete AC-PLA-18; con 92 quedan 3 o 4. Registrado en `progress/hallazgos.md` para reformular la spec al reabrirla.
- `supresiones` del plan es dos contenedores (`supresiones-entradas` y `supresiones-sellos`) porque no se asumió el permiso por prefijo de Oracle (hallazgo para el spike T02).
- Las pruebas pesadas (Docker, bash) corren en una colección sin paralelismo y las de tiempo real tienen márgenes de 1,5 s o más: bajo carga fallaban de forma intermitente. Para validar conviene correr la integración sola.

## Verificación
`dotnet build BabelHub.sln -warnaserror` sin advertencias; 541 pruebas unitarias del E-commerce, 404 del Inventario, 45 de arquitectura, 1422 de integración (con PostgreSQL, Redis, Nginx, un almacén S3 y Docker Compose reales), 12 de `CoverageCheck` y 23 de `SpecCheck`; `SpecCheck` con 20 specs y 0 avisos; gitleaks sin fugas.

## Pendiente / siguiente
- **Avance de la fase 3 (`docs/07-proceso-de-desarrollo/01-flujo-sdd.md` §6): 48 de 177 tareas planificadas (27 %).** Pendientes: `transversal-plataforma` 22 (5 spikes y T26 a T42), `transversal-seguridad` 2 (T27b y T29, esperan a Auditoría), `identidad-seguridad` 50, `transversal-auditoria` 31 y `transversal-notificaciones` 25.
- Siguiente: T26 en adelante (endurecimiento de VM-AUX, espacio local, custodia, `restaurar.sh`, `RestauracionCheck`, ensayo, datos sintéticos, restauración con carga, puerta PLA-11, Trivy y SBOM, lanzamiento, discos cifrados, medición, alertas, constancias y contratos).
- Lo que no se puede probar sin Oracle ni VMs (políticas IAM, `OBJECT_CREATE` sin sobrescribir, cuándo corre el ciclo de vida, arranque conjunto de cada VM): spikes T01 a T05 y ensayo.
- Abierto para la persona: activar en GitHub la protección de la rama principal (`Specs y trazabilidad` y `Compilación y pruebas` obligatorias).

## Adenda — T25 (VM-AUX)
- Scripts de VM-AUX en `scripts/vm-aux/` y su programación en `infra/vm-aux/`: comprobación de respaldos (24 h) y del atraso del archivado (15 min) con aviso por correo desde fuera de la VM de datos, sellos horarios del registro de supresiones (conformes con `VerificadorDelRegistro` del E-commerce, con prueba), copia diaria del registro, ancla semanal por correo, `alertar.sh` y latido externo sin datos. Cada script usa solo lo que permite su credencial (la prueba aplica `credenciales.txt`).
- Avance de la fase 3: 48 de 177 tareas. Quedan en `transversal-plataforma` 5 spikes y T26 a T42.

## Adenda — Cambio de proceso (desarrollo por bloques)
- La persona decidió llegar al producto completo con **rigor graduado por riesgo, bloques y carriles en paralelo** (`docs/07-proceso-de-desarrollo/05-desarrollo-por-bloques.md`; lista de bloques y medición en `progress/bloques.md`). Desde ahora se pide «Ejecuta el bloque X», con informe por bloque.
- No se tocó `AGENTS.md`, `04-aprobacion-delegada.md` ni `SpecCheck`: la puerta graduada de planes y «una tarea a la vez por carril» quedaron como propuesta con texto exacto, pendientes de la persona.
