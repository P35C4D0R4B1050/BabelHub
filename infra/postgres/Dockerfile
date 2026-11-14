# PostgreSQL con lo que necesita el archivado de cambios y el respaldo (D06, D10): `age` para cifrar con la clave pública y `rclone` como
# cliente S3 para subir sin sobrescribir (`--immutable`) al almacén de objetos. Multi-arquitectura (linux/amd64 y linux/arm64) y sin
# privilegios: el proceso corre como el usuario «postgres». `archive_command` corre dentro de este contenedor.
#   docker buildx build --platform linux/amd64,linux/arm64 -t babelhub/postgres infra/postgres
# La imagen base va fijada por etiqueta y por digest del índice multi-arquitectura, y los paquetes por versión exacta (PLA-26): si el
# repositorio de Alpine ya no ofrece esa versión, la construcción falla y se actualiza con un cambio revisado.
FROM postgres:17-alpine@sha256:d3244e8ce3f165ee3c93d9c4a4b1d0fe48b3a89e2287b717fdcbbde92d405065

USER root
RUN apk add --no-cache age=1.3.2-r1 rclone=1.75.2-r0

# Los scripts de archivado y respaldo (infra/postgres/respaldos, POSIX sh) y el volumen local donde espera lo cifrado antes de su subida.
COPY respaldos/ /opt/babelhub/
RUN chmod 0555 /opt/babelhub /opt/babelhub/*.sh && chmod 0444 /opt/babelhub/comun.sh \
    && mkdir -p /var/respaldos && chown postgres:postgres /var/respaldos && chmod 0700 /var/respaldos
USER postgres
