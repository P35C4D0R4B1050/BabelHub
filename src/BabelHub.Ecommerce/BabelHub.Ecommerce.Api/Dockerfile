# Imagen del servicio Ecommerce (PLA-03, D06): varias etapas, para linux/amd64 y linux/arm64 con el mismo comportamiento.
# Contexto de construcción: la raíz del repositorio.
#   docker buildx build --platform linux/amd64,linux/arm64 -f src/BabelHub.Ecommerce/BabelHub.Ecommerce.Api/Dockerfile .
# Las imágenes base van fijadas por etiqueta y por digest del índice multi-arquitectura (PLA-26); un cambio de versión es un cambio revisado.

# La compilación corre en la arquitectura de quien construye (sin emulación) y publica un resultado independiente de la arquitectura.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0@sha256:fe0fd4e93bd2fbb47c883b7a6369e5c8d3db8bf01cdc5981f6ee35fbdf3ff4fb AS publicar
WORKDIR /fuentes

# Primero lo que decide las dependencias (propiedades, versiones centralizadas y bloqueos), para aprovechar la caché de capas.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/BabelHub.Shared/BabelHub.Shared.csproj src/BabelHub.Shared/packages.lock.json src/BabelHub.Shared/
COPY src/BabelHub.Ecommerce/BabelHub.Ecommerce.Domain/BabelHub.Ecommerce.Domain.csproj src/BabelHub.Ecommerce/BabelHub.Ecommerce.Domain/packages.lock.json src/BabelHub.Ecommerce/BabelHub.Ecommerce.Domain/
COPY src/BabelHub.Ecommerce/BabelHub.Ecommerce.Application/BabelHub.Ecommerce.Application.csproj src/BabelHub.Ecommerce/BabelHub.Ecommerce.Application/packages.lock.json src/BabelHub.Ecommerce/BabelHub.Ecommerce.Application/
COPY src/BabelHub.Ecommerce/BabelHub.Ecommerce.Infrastructure/BabelHub.Ecommerce.Infrastructure.csproj src/BabelHub.Ecommerce/BabelHub.Ecommerce.Infrastructure/packages.lock.json src/BabelHub.Ecommerce/BabelHub.Ecommerce.Infrastructure/
COPY src/BabelHub.Ecommerce/BabelHub.Ecommerce.Api/BabelHub.Ecommerce.Api.csproj src/BabelHub.Ecommerce/BabelHub.Ecommerce.Api/packages.lock.json src/BabelHub.Ecommerce/BabelHub.Ecommerce.Api/
# Restaura con el bloqueo: si una versión cambió sin un cambio revisado del bloqueo, la construcción falla.
RUN dotnet restore src/BabelHub.Ecommerce/BabelHub.Ecommerce.Api/BabelHub.Ecommerce.Api.csproj --locked-mode

COPY src/BabelHub.Shared src/BabelHub.Shared
COPY src/BabelHub.Ecommerce src/BabelHub.Ecommerce

# --- inicio: estilos de la interfaz web (solo el E-commerce tiene interfaz; ImagenesDeContenedorTests ignora este bloque al comparar los Dockerfiles) ---
# El CSS de la interfaz (Tailwind CSS 4 + daisyUI 5, AD21 y web-base D09) se genera aquí, sin Node, y no se versiona. La CLI independiente
# y los dos archivos de daisyUI se fijan por versión y por huella SHA-256 (Web/versiones.json, que una prueba compara con estas líneas):
# si una descarga no coincide, la construcción falla. Solo existen en esta etapa; no llegan a la imagen final.
ARG BUILDARCH
RUN set -eu; \
    case "$BUILDARCH" in \
      amd64) TW_ARCHIVO=tailwindcss-linux-x64; TW_SHA=dc61b3ac6b8c9ca874c0cc4c57b2409791a64c5540404ca5f5367360babc313a ;; \
      arm64) TW_ARCHIVO=tailwindcss-linux-arm64; TW_SHA=55fd0b241214eff3de1e8ee4f22796662f2d2e7a49bcfca7477cfd0bac398195 ;; \
      *) echo "arquitectura de construcción no soportada: $BUILDARCH" >&2; exit 1 ;; \
    esac; \
    mkdir -p .herramientas/tailwind .herramientas/daisyui; \
    curl --proto '=https' --tlsv1.2 -fsSL -o .herramientas/tailwind/tailwindcss "https://github.com/tailwindlabs/tailwindcss/releases/download/v4.3.3/$TW_ARCHIVO"; \
    curl --proto '=https' --tlsv1.2 -fsSL -o .herramientas/daisyui/daisyui.mjs "https://github.com/saadeghi/daisyui/releases/download/v5.7.50/daisyui.mjs"; \
    curl --proto '=https' --tlsv1.2 -fsSL -o .herramientas/daisyui/daisyui-theme.mjs "https://github.com/saadeghi/daisyui/releases/download/v5.7.50/daisyui-theme.mjs"; \
    printf '%s  %s\n' "$TW_SHA" .herramientas/tailwind/tailwindcss > .herramientas/sumas.txt; \
    printf '%s  %s\n' d69de07c7463bb1f41548e2688f88ee105cdadf9779fce5b3c47b3bfaa6be472 .herramientas/daisyui/daisyui.mjs >> .herramientas/sumas.txt; \
    printf '%s  %s\n' e48a35d8326d650181ca600560e8520cf6b44cf9242bbd9d45825607e641f251 .herramientas/daisyui/daisyui-theme.mjs >> .herramientas/sumas.txt; \
    sha256sum -c .herramientas/sumas.txt; \
    chmod +x .herramientas/tailwind/tailwindcss; \
    mkdir -p src/BabelHub.Ecommerce/BabelHub.Ecommerce.Api/wwwroot/css; \
    .herramientas/tailwind/tailwindcss \
      -i src/BabelHub.Ecommerce/BabelHub.Ecommerce.Api/Web/Estilos/app.css \
      -o src/BabelHub.Ecommerce/BabelHub.Ecommerce.Api/wwwroot/css/app.css --minify; \
    rm -rf .herramientas
# --- fin: estilos de la interfaz web ---

RUN dotnet publish src/BabelHub.Ecommerce/BabelHub.Ecommerce.Api/BabelHub.Ecommerce.Api.csproj -c Release -o /app --no-restore /p:UseAppHost=false
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

ENTRYPOINT ["dotnet", "BabelHub.Ecommerce.Api.dll"]
