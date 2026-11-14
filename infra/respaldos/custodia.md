# Custodia de la clave de cifrado de los respaldos

Registro de quién guarda la clave que **descifra** los respaldos de BabelHub y dónde (PLA-17, D10, AC-PLA-17). Los respaldos y el archivo de cambios se cifran con `age` usando una clave **pública**: en la VM de datos solo se puede cifrar. La clave **privada** (la que descifra) vive **fuera de la VM, del repositorio y del contenedor de respaldos**, en poder de la persona responsable, con una copia fuera de línea. **Perder esa clave equivale a perder todos los respaldos**: por eso la segunda copia es obligatoria y el descifrado se prueba cada cierto tiempo.

> **Este archivo solo guarda datos públicos**: el destinatario `age1…` (público), dónde está cada copia y fechas. **Nunca** una clave privada (`AGE-SECRET-KEY-…`), una contraseña ni una frase de paso: `scripts/comprobar-custodia.sh` lo rechaza si aparece una.

## 1. Registro

La persona responsable llena esta tabla al generar la clave (paso 2 del procedimiento) y la actualiza en cada prueba de descifrado y en cada rotación. Los campos con `pendiente` hacen fallar `scripts/comprobar-custodia.sh`, a propósito: hasta que la clave exista y esté registrada, el despliegue de la VM de datos no tiene con qué cifrar ni cómo restaurar.

| Campo | Valor |
| :--- | :--- |
| Titular de la clave | pendiente |
| Destinatario público (age) | pendiente |
| Ubicación de la copia fuera de línea | pendiente |
| Ubicación de la segunda copia (obligatoria, en otro lugar) | pendiente |
| Última prueba de descifrado | pendiente |
| Periodicidad de la prueba (días) | 90 |

## 2. Procedimiento

1. **Dónde se genera.** En el equipo de la persona responsable, nunca en una VM ni en un contenedor de la plataforma: `age-keygen -o clave-respaldos.txt`. El archivo muestra la clave pública (`Public key: age1…`) y contiene la privada.
2. **Qué se registra.** Solo el destinatario público en la tabla de arriba y en `postgres.env` de la VM de datos (`BABELHUB_AGE_DESTINATARIO`). `scripts/desplegar.sh` rechaza ahí una clave privada; `scripts/comprobar-custodia.sh --entorno <postgres.env>` comprueba que ambos coinciden: si no coinciden, los respaldos se cifrarían con una clave que nadie tiene registrada.
3. **Dónde se guarda.** Una copia fuera de línea (dispositivo cifrado, sin conexión) y **una segunda copia en otro lugar físico distinto**. Ninguna en el repositorio, en una VM, en el almacén de objetos, en el portapapeles, en un correo ni en una herramienta de notas conectada. Las ubicaciones se describen en la tabla sin revelar nada que sirva a un atacante (por ejemplo, «caja fuerte de la oficina» y «dispositivo cifrado en casa»).
4. **Prueba periódica de descifrado** (cada `Periodicidad` días; 90 por defecto): se baja un respaldo reciente y se descifra **solo con la copia fuera de línea**, en otra máquina:

   ```bash
   # La clave privada entra por la entrada estándar, leída del dispositivo cifrado de la copia fuera de línea (montado solo para esto); nunca se copia a disco ni queda en el historial.
   scripts/descifrar-respaldo.sh --entrada <respaldo>.tar.age --probar --registrar < /ruta/al/dispositivo/clave-respaldos.txt
   ```

   `--probar` descifra el respaldo completo a la nada y, si descifra, `--registrar` anota la fecha de hoy en «Última prueba de descifrado». Un cambio revisado en este archivo, como cualquier otro.
5. **Cuando se restaura o se ensaya** (`scripts/restaurar.sh`, ensayo): se usa `scripts/descifrar-respaldo.sh` con la misma copia fuera de línea y se extrae con `scripts/extraer-respaldo.sh` (sin rutas absolutas, `..`, enlaces peligrosos ni propietarios; se ignoran `postgresql.auto.conf`, `restore_command` y `archive_command` del respaldo). Ambos comprueban que la clave que se presenta **corresponde al destinatario registrado aquí** antes de tocar el respaldo.
6. **Rotación.** Se genera una clave nueva y se registra (paso 1 y 2) **antes** de retirar la anterior; los respaldos ya hechos solo los descifra la clave con que se cifraron, así que la anterior se conserva mientras exista un respaldo cifrado con ella (hasta que el ciclo de vida del almacén retire el último, 100 días como máximo para los respaldos y el archivo de cambios). Se agrega una fila a la tabla del apartado 3 por cada clave vigente.
7. **Si se pierde o se compromete** la clave privada: se genera una nueva de inmediato, se cambia `BABELHUB_AGE_DESTINATARIO` en la VM de datos, se toma un respaldo completo nuevo (`scripts/respaldar-ahora.sh`) y se registra el incidente en `progress/hallazgos.md`. Los respaldos anteriores a ese punto solo se pueden descifrar con la clave anterior; si está perdida, esos respaldos están perdidos.

## 3. Otras claves custodiadas

Las claves que otras features dejan fuera de la plataforma se registran aquí con la misma regla (solo datos públicos, dos copias, prueba periódica). `identidad-seguridad` T50 agrega su entrada para la clave de firma y su custodia.

| Clave | Para qué sirve | Titular | Ubicación de las copias | Última prueba |
| :--- | :--- | :--- | :--- | :--- |
| Clave de cifrado de los respaldos (`age`) | Descifrar los respaldos y el archivo de cambios | (apartado 1) | (apartado 1) | (apartado 1) |
