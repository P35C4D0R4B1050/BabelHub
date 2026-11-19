# Lista de contraseñas comunes (IDE-15 e IDE-59)

`src/BabelHub.Ecommerce/BabelHub.Ecommerce.Infrastructure/Identidad/Recursos/contrasenas-comunes.txt` es la unión de dos fuentes, sin duplicados y en orden ordinal:

1. La lista generada por patrones del proyecto (construida en IDE-6).
2. **`10k-most-common.txt` de SecLists** (Daniel Miessler y colaboradores), ruta `Passwords/Common-Credentials/10k-most-common.txt` del repositorio `https://github.com/danielmiessler/SecLists`, **licencia MIT** (Copyright (c) 2018 Daniel Miessler). Descargada el 2026-11-19 con autorización de la persona responsable.

La lista se compara con la contraseña ya normalizada y no se copia a ningún registro ni auditoría. Una actualización de la fuente se revisa y se registra aquí (versión, fecha y huella SHA-256 del archivo).
SHA-256 del archivo de SecLists: 68782d6a4a19a4768d5f15dd66bd534e7a33055cc755411e33f16d18c50fdcce

De la fuente 2 se incorporan solo las entradas **de 10 o más caracteres** y **en minúsculas**, que son las que la política de longitud mínima (IDE-15) no rechaza por sí sola y el formato que verifica `CriptografiaDeIdentidadTests`; las demás ya las rechaza la longitud.
