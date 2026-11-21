# Imagen del servicio Inventario (PLA-03, D06): varias etapas, para linux/amd64 y linux/arm64 con el mismo comportamiento.
# Contexto de construcción: la raíz del repositorio.
#   docker buildx build --platform linux/amd64,linux/arm64 -f src/BabelHub.Inventario/BabelHub.Inventario.Api/Dockerfile .
# Las imágenes base van fijadas por etiqueta y por digest del índice multi-arquitectura (PLA-26); un cambio de versión es un cambio revisado.

# La compilación corre en la arquitectura de quien construye (sin emulación) y publica un resultado independiente de la arquitectura.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0@sha256:fe0fd4e93bd2fbb47c883b7a6369e5c8d3db8bf01cdc5981f6ee35fbdf3ff4fb AS publicar
WORKDIR /fuentes

# Primero lo que decide las dependencias (propiedades, versiones centralizadas y bloqueos), para aprovechar la caché de capas.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/BabelHub.Shared/BabelHub.Shared.csproj src/BabelHub.Shared/packages.lock.json src/BabelHub.Shared/
COPY src/BabelHub.Inventario/BabelHub.Inventario.Domain/BabelHub.Inventario.Domain.csproj src/BabelHub.Inventario/BabelHub.Inventario.Domain/packages.lock.json src/BabelHub.Inventario/BabelHub.Inventario.Domain/
COPY src/BabelHub.Inventario/BabelHub.Inventario.Application/BabelHub.Inventario.Application.csproj src/BabelHub.Inventario/BabelHub.Inventario.Application/packages.lock.json src/BabelHub.Inventario/BabelHub.Inventario.Application/
COPY src/BabelHub.Inventario/BabelHub.Inventario.Infrastructure/BabelHub.Inventario.Infrastructure.csproj src/BabelHub.Inventario/BabelHub.Inventario.Infrastructure/packages.lock.json src/BabelHub.Inventario/BabelHub.Inventario.Infrastructure/
COPY src/BabelHub.Inventario/BabelHub.Inventario.Api/BabelHub.Inventario.Api.csproj src/BabelHub.Inventario/BabelHub.Inventario.Api/packages.lock.json src/BabelHub.Inventario/BabelHub.Inventario.Api/
# Restaura con el bloqueo: si una versión cambió sin un cambio revisado del bloqueo, la construcción falla.
RUN dotnet restore src/BabelHub.Inventario/BabelHub.Inventario.Api/BabelHub.Inventario.Api.csproj --locked-mode

COPY src/BabelHub.Shared src/BabelHub.Shared
COPY src/BabelHub.Inventario src/BabelHub.Inventario
RUN dotnet publish src/BabelHub.Inventario/BabelHub.Inventario.Api/BabelHub.Inventario.Api.csproj -c Release -o /app --no-restore /p:UseAppHost=false
# El sembrador de sesiones de desarrollo solo existe en una compilación Debug: si el binario publicado lo nombra, la construcción falla.
RUN ! grep -rl --include=*.dll SembradorDeSesiones /app

# La imagen final es de la arquitectura de destino. Corre sin privilegios (el usuario «app» de la imagen base).
FROM mcr.microsoft.com/dotnet/aspnet:11.0@sha256:bc0d6653979efba491ab3d6f589d691f96c309c3c3ef81325efa6978e7b7f242 AS final
WORKDIR /app
COPY --from=publicar /app .

ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_EnableDiagnostics=0
EXPOSE 8080
USER app

# La imagen base no trae curl ni wget: la comprobación abre un socket con bash y pide /health/live (sin detalle interno).
HEALTHCHECK --interval=30s --timeout=5s --start-period=40s --retries=3 \
  CMD ["bash", "-c", "exec 3<>/dev/tcp/127.0.0.1/8080 && printf 'GET /health/live HTTP/1.0\\r\\nHost: localhost\\r\\n\\r\\n' >&3 && head -n 1 <&3 | grep -q ' 200 '"]

ENTRYPOINT ["dotnet", "BabelHub.Inventario.Api.dll"]
