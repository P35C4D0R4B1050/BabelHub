# 2026-11-23 — Preparación del primer despliegue en Oracle (carril `despliegue-oracle`)

## Qué se hizo
- **Flujo de publicación de imágenes** (`.github/workflows/lanzamiento.yml`, DD-57): `ecommerce`, `inventario` y `postgres` para `linux/amd64` y `linux/arm64` (ejecutores nativos `ubuntu-24.04` y `ubuntu-24.04-arm`), revisión en local antes de subir (arquitectura, usuario, secretos con `escanear-secretos.sh`, Trivy informativo), subida por digest, índice con la etiqueta de la versión, procedencia firmada, resumen con las tres líneas `IMAGEN_*`. Verificado con `actionlint` y con una prueba local de `push-by-digest` + `imagetools create` contra un registro local; **no se publicó nada ni se creó ninguna etiqueta**.
- **Ensayo local con las imágenes reales** (`scripts/ensayo-local.sh`): las tres imágenes se construyen, se publican en un registro local y se despliegan VM-INVENTARIO y VM-VENTAS con `desplegar.sh`. Resultados: PostgreSQL (nuestra imagen, endurecida) sano; migraciones de las dos bases; **Inventario en `Production` sano, `/health/ready` por HTTPS con el certificado validado**; réplica de gestión y E-commerce sanos **solo en `Development`**; Redis de contadores con TLS y n8n sanos; un `HttpClient` de .NET confía en la CA privada con el paquete de raíces y no sin él.
- **Defectos encontrados al ensayar y corregidos:** HTTPS entre servicios no estaba cableado en los Compose (DD-55); el «HTTP hacia la propia máquina» del Inventario no funciona entre contenedores; `ClaveDeHuellaDeNumero` exige `1:clave` y no `v1:clave`; la réplica de gestión exige la Biblioteca (la tabla de secretos no lo decía); el SMTP de Gmail (16 caracteres) no debía tener mínimo de 32; las llaves TLS montadas como secretos eran ilegibles para el usuario del contenedor; el segundo conector de cloudflared no podía resolver el destino del túnel (DD-56); `HostsPermitidos` rechazaba las llamadas por IP privada.
- **Scripts:** `scripts/generar-secretos.sh` (DD-59), `desplegar.sh` (`--sin`, `--extra`, `CAMBIAR`, `ca-bundle.pem`, entrega de llaves, claves privadas ajenas, espera de «starting»).
- **Guía** `docs/07-proceso-de-desarrollo/08-guia-primer-despliegue.md` y filas en `infra/README.md` y `docs/07/README.md`; reglas de red de `docs/02/05-despliegue.md` §7 actualizadas.
- **Pruebas:** `EscanerDeSecretosTests` (excepción acotada de AC-PLA-28 y prueba del flujo), `DesplegarShTests` (CAMBIAR, `--sin`, `--extra`, paquete de raíces, generador que pasa las comprobaciones de `desplegar.sh`).
- **Revisiones:** `security-reviewer` (0 Bloqueantes, 5 Importantes: 3 corregidos —escaneo antes de subir, entorno y etiquetas protegidas, restricción de nombres de la CA, clave privada ajena en la carpeta— y la falta de TLS en PostgreSQL registrada como hallazgo) y `arch-reviewer` (ver el informe final).

## Hallazgos
- **El E-commerce no arranca en `Production`** hasta que exista el adaptador de Culqi (T37/T38 de `ecommerce-pagos`); es la decisión que más importa para la persona (DD-58).
- PostgreSQL sin TLS entre VMs. Menores en `progress/hallazgos.md`.

## No se probó
Oracle, Cloudflare (túnel, Access, WAF), el almacén de objetos real, Culqi, WhatsApp, la ejecución real de GitHub Actions y la capacidad ARM; los permisos de archivos con otro usuario (en Windows no se pueden ejercitar).

## Pendiente (la persona)
Ratificar DD-55 a DD-59; decidir cómo resolver el bloqueo del E-commerce en producción; crear el entorno `lanzamiento` y el ruleset de etiquetas en GitHub; subir la etiqueta `v*` cuando quiera publicar (el agente no hace `push` ni etiquetas).
