#!/usr/bin/env bash
# Comprueba el endurecimiento de VM-AUX (PLA-20, AM29, D19): la VM es el salto SSH hacia las demás y vigila los respaldos, así que si se
# compromete lo más peligroso es que haya en ella una llave privada, un agente reenviado o una credencial de más. Corre en VM-AUX (a diario,
# desde cron y envuelto por alertar.sh, y a mano tras cada cambio) y no cambia nada: solo lee y avisa.
#
#   scripts/vm-aux/verificar-endurecimiento.sh
#
# Una línea «ok …» o «ALERTA …» por comprobación; código 0 si todo cumple, 1 si alguna falla. Nunca imprime el contenido de un archivo ni un
# valor secreto: solo rutas y nombres de variables.
#
# Qué exige:
#   1. sshd: sin contraseñas, sin ingreso de root, SIN REENVÍO DE AGENTE ni de X11, pocos intentos; y, para el usuario del salto
#      (AUX_USUARIO_SALTO, por defecto «salto»): sin terminal, con un comando forzado y con destinos acotados (PermitOpen) solo a direcciones
#      privadas, puerto 22. El salto de la persona responsable es `ssh -J`, que solo abre un canal TCP: no necesita shell en VM-AUX.
#   2. Ninguna llave privada en VM-AUX (SSH, PuTTY ni age) bajo las carpetas que se revisan: la llave vive en el equipo de la persona
#      responsable. Las llaves del propio sshd (/etc/ssh) no se cuentan.
#   3. Ningún agente SSH corriendo ni `ForwardAgent yes` en ninguna configuración de cliente.
#   4. authorized_keys: ninguna entrada con `agent-forwarding`; las del usuario del salto, con `restrict`, `port-forwarding` y `permitopen`
#      a direcciones privadas.
#   5. El archivo de entorno (AUX_ENV) es de root y 0600 o 0640 (el usuario sin privilegios lo lee pero no lo puede cambiar; los scripts lo leen sin ejecutarlo) y solo tiene las variables propias de VM-AUX: su credencial de correo y las credenciales
#      mínimas del almacén (vigilante, sello, copia y cuota). Cualquier otra (de la base, de n8n, un escritor o un lector de respaldos, una
#      clave de age…) es una credencial de más y se rechaza: no existe en VM-AUX ninguna credencial que borre.
#   6. Los scripts que esta comprobación corre como root (ella misma, alertar.sh y comun.sh) son de root y no se pueden modificar: si
#      `babelhub-aux` pudiera editarlos, ganaría root en la siguiente corrida. Por eso esta comprobación tiene su propio archivo de cron
#      (infra/vm-aux/babelhub-aux-endurecimiento.cron): necesita leer sshd y las carpetas de todos, algo que un usuario sin privilegios no
#      puede (y una revisión parcial daría un «todo bien» falso).
#
# El cifrado del disco es del proveedor y no se ve desde dentro de la VM: lo verifica scripts/verificar-cifrado-de-disco.sh desde el equipo de
# la persona responsable.
#
# Variables (las pruebas las sustituyen): SSHD_T (por defecto «sshd -T»), PS, STAT (por defecto «stat»), AUX_USUARIO_SALTO, AUX_RUTAS_CLAVES
# (carpetas separadas por espacios), AUX_AUTORIZADAS y AUX_CONFIGS_SSH (patrones de archivos separados por espacios), AUX_ENV.
set -uo pipefail

AUX_ENV="${AUX_ENV:-/etc/babelhub-aux/aux.env}"
SSHD_T="${SSHD_T:-sshd -T}"
PS="${PS:-ps}"
STAT="${STAT:-stat}"
USUARIO_SALTO="${AUX_USUARIO_SALTO:-salto}"
USUARIO_ADMIN="${AUX_USUARIO_ADMIN:-admin}"
PAM_SSHD="${AUX_PAM_SSHD:-/etc/pam.d/sshd}"
SCRIPTS_DE_VM_AUX="${AUX_SCRIPTS:-/opt/babelhub/scripts/vm-aux}"
RUTAS_CLAVES="${AUX_RUTAS_CLAVES:-/home /root /etc/babelhub-aux /var/lib/babelhub-aux /opt/babelhub /srv /var/backups /tmp /var/tmp /dev/shm}"
AUTORIZADAS="${AUX_AUTORIZADAS:-/home/*/.ssh/authorized_keys /root/.ssh/authorized_keys /etc/ssh/authorized_keys/*}"
CONFIGS_SSH="${AUX_CONFIGS_SSH:-/etc/ssh/ssh_config /etc/ssh/ssh_config.d/*.conf /home/*/.ssh/config /root/.ssh/config}"
fallos=0

ok() { echo "ok $*"; }
alerta() { echo "ALERTA $*"; fallos=$((fallos + 1)); }

# Una dirección privada con puerto 22 (10/8, 172.16/12, 192.168/16).
destino_privado() {
  [[ "$1" =~ ^(10\.[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}|172\.(1[6-9]|2[0-9]|3[01])\.[0-9]{1,3}\.[0-9]{1,3}|192\.168\.[0-9]{1,3}\.[0-9]{1,3}):22$ ]]
}

# ---------- 1. sshd ----------
[[ "$USUARIO_SALTO" =~ ^[a-z_][a-z0-9_-]*$ && "$USUARIO_ADMIN" =~ ^[a-z_][a-z0-9_-]*$ ]] || { echo "ALERTA los nombres de usuario de AUX_USUARIO_SALTO y AUX_USUARIO_ADMIN no son válidos"; exit 1; }
read -r -a COMANDO_SSHD <<<"$SSHD_T"
sshd_de() { "${COMANDO_SSHD[@]}" -C "user=$1,host=vm-aux,addr=127.0.0.1" 2>/dev/null; }
if ! efectivo="$(sshd_de "$USUARIO_SALTO")" || [[ -z "$efectivo" ]]; then
  alerta "no se pudo leer la configuración efectiva de sshd"
  efectivo=""
fi
valor() { awk -v k="$1" 'tolower($1) == k { sub(/^[^ ]+ +/, ""); print; exit }' <<<"$efectivo"; }
exigir() { # clave valor-esperado
  local actual
  actual="$(valor "$1")"
  if [[ "$actual" == "$2" ]]; then ok "sshd $1 $2"; else alerta "sshd $1 es «${actual:-sin definir}» y debe ser «$2»"; fi
}
if [[ -n "$efectivo" ]]; then
  exigir passwordauthentication no
  exigir kbdinteractiveauthentication no
  exigir permitrootlogin no
  exigir allowagentforwarding no
  exigir x11forwarding no
  exigir permitemptypasswords no
  exigir pubkeyauthentication yes
  exigir permituserenvironment no
  maximo="$(valor maxauthtries)"
  if [[ "$maximo" =~ ^[0-9]+$ && "$maximo" -le 3 ]]; then ok "sshd maxauthtries $maximo"; else alerta "sshd maxauthtries es «${maximo:-sin definir}» y debe ser 3 o menos"; fi
  # El usuario del salto: solo canales TCP hacia las otras VMs.
  exigir usepam yes
  exigir permittty no
  # El salto solo abre canales TCP: ni sockets Unix de la VM (docker.sock…), ni túneles, ni puertos abiertos al exterior.
  for clave in allowstreamlocalforwarding gatewayports; do exigir "$clave" no; done
  tunel="$(valor permittunnel)"
  if [[ "$tunel" == no || "$tunel" == false ]]; then ok "sshd permittunnel no"; else alerta "sshd permittunnel es «${tunel:-sin definir}» y debe ser «no»"; fi
  forzado="$(valor forcecommand)"
  if [[ -n "$forzado" && "$forzado" != none ]]; then ok "sshd forcecommand del salto definido"; else alerta "sshd forcecommand sin definir: el usuario «$USUARIO_SALTO» tendría shell"; fi
  tcp="$(valor allowtcpforwarding)"
  if [[ "$tcp" == yes || "$tcp" == local ]]; then ok "sshd allowtcpforwarding $tcp"; else alerta "sshd allowtcpforwarding es «${tcp:-sin definir}»: el salto de ProxyJump necesita yes o local"; fi
  permitidos="$(valor permitopen)"
  if [[ -z "$permitidos" || "$permitidos" == any || "$permitidos" == none ]]; then
    alerta "sshd permitopen es «${permitidos:-sin definir}»: debe acotar el salto a las direcciones privadas de las demás VMs"
  else
    malos=0
    for destino in $permitidos; do
      destino_privado "$destino" || { alerta "sshd permitopen admite «$destino»: solo direcciones privadas, puerto 22"; malos=1; }
    done
    [[ $malos -eq 0 ]] && ok "sshd permitopen solo a direcciones privadas: $permitidos"
  fi
  usuarios="$(valor allowusers)"
  if [[ -n "$usuarios" ]]; then ok "sshd allowusers definido"; else alerta "sshd allowusers sin definir: debe listar los usuarios con acceso"; fi

  # Un `Match User` de otro usuario no debe reabrir el reenvío de agente ni el ingreso de root: se evalúa también el administrador y root.
  for otro in "$USUARIO_ADMIN" root; do
    adicional="$(sshd_de "$otro" || true)"
    [[ -n "$adicional" ]] || { alerta "no se pudo leer la configuración efectiva de sshd para «$otro»"; continue; }
    for clave in allowagentforwarding x11forwarding passwordauthentication; do
      actual="$(awk -v k="$clave" 'tolower($1) == k { print $2; exit }' <<<"$adicional")"
      [[ "$actual" == no ]] || alerta "sshd $clave para «$otro» es «${actual:-sin definir}» y debe ser «no»"
    done
  done
fi

# La alerta de ingreso SSH necesita PAM en sshd y su línea: si faltan, el aviso no sale y nadie lo nota.
if [[ -f "$PAM_SSHD" ]] && grep -Eq '^[[:space:]]*session[[:space:]]+(optional|required|requisite|sufficient)[[:space:]]+pam_exec\.so[[:space:]].*avisar-ingreso-ssh\.sh' "$PAM_SSHD"; then
  ok "la alerta de ingreso SSH está instalada en PAM"
else
  alerta "la alerta de ingreso SSH no está instalada en $PAM_SSHD (session optional pam_exec.so quiet …/avisar-ingreso-ssh.sh)"
fi

# ---------- 2. Ninguna llave privada ----------
llaves=""
read -r -a carpetas <<<"$RUTAS_CLAVES"
for carpeta in "${carpetas[@]}"; do
  [[ -d "$carpeta" ]] || continue
  # Los corchetes evitan que este mismo archivo (si está bajo una carpeta revisada) coincida con sus propios patrones.
  encontradas="$(grep -rIl -e '-----BEGIN [A-Z0-9 ]*PRIVATE KEY[-]----' -e 'PuTTY-User-Key-Fil[e]' -e 'AGE-SECRET-KEY[-]' "$carpeta" 2>/dev/null || true)"
  [[ -n "$encontradas" ]] && llaves+="$encontradas"$'\n'
done
if [[ -z "${llaves//$'\n'/}" ]]; then
  ok "ninguna llave privada en VM-AUX"
else
  while IFS= read -r ruta; do
    [[ -n "$ruta" ]] && alerta "hay una llave privada en VM-AUX: $ruta"
  done <<<"$llaves"
fi

# ---------- 3. Sin agente ----------
if "$PS" -eo comm= 2>/dev/null | grep -qx 'ssh-agent'; then
  alerta "hay un ssh-agent corriendo en VM-AUX"
else
  ok "ningún agente SSH corriendo"
fi
reenvio=0
for patron in $CONFIGS_SSH; do
  for archivo in $patron; do
    [[ -f "$archivo" ]] || continue
    if grep -Eiq '^[[:space:]]*ForwardAgent[[:space:]]+(yes|true)' "$archivo"; then
      alerta "ForwardAgent activo en $archivo"
      reenvio=1
    fi
  done
done
[[ $reenvio -eq 0 ]] && ok "ninguna configuración de cliente reenvía el agente"

# ---------- 4. authorized_keys ----------
claves_mal=0
for patron in $AUTORIZADAS; do
  for archivo in $patron; do
    [[ -f "$archivo" ]] || continue
    # Solo el usuario del salto: su carpeta es .../<usuario>/.ssh/authorized_keys o el nombre del archivo.
    del_salto=0
    [[ "$archivo" == */"$USUARIO_SALTO"/* || "$(basename "$archivo")" == "$USUARIO_SALTO" ]] && del_salto=1
    numero=0
    while IFS= read -r linea || [[ -n "$linea" ]]; do
      numero=$((numero + 1))
      [[ -z "${linea// /}" || "$linea" == \#* ]] && continue
      # Solo el campo de opciones (lo que va antes del tipo de llave): un comentario de la llave no cuenta, y `no-agent-forwarding` no es `agent-forwarding`.
      opciones=""
      if [[ "$linea" != ssh-* && "$linea" != ecdsa-* && "$linea" != sk-* ]]; then opciones="${linea%% ssh-*}"; opciones="${opciones%% ecdsa-*}"; opciones="${opciones%% sk-*}"; fi
      sin_comillas="$(sed 's/"[^"]*"//g' <<<"$opciones")"
      fichas=",${sin_comillas},"
      if [[ "$fichas" == *,agent-forwarding,* ]]; then
        alerta "$archivo línea $numero: una llave con agent-forwarding"
        claves_mal=1
      fi
      if [[ $del_salto -eq 1 ]]; then
        if [[ "$fichas" != *,restrict,* ]]; then
          alerta "$archivo línea $numero: la llave del salto no lleva «restrict»"
          claves_mal=1
        fi
        if [[ "$fichas" != *,port-forwarding,* ]]; then
          alerta "$archivo línea $numero: la llave del salto no lleva «port-forwarding» (sin él no puede saltar)"
          claves_mal=1
        fi
        if [[ "$opciones" == *permitopen=* ]]; then
          resto="$opciones"
          while [[ "$resto" =~ permitopen=\"([^\"]*)\"(.*)$ ]]; do
            grupo="${BASH_REMATCH[1]}"
            resto="${BASH_REMATCH[2]}"
            for destino in ${grupo//,/ }; do
              destino_privado "$destino" || { alerta "$archivo línea $numero: permitopen admite «$destino»"; claves_mal=1; }
            done
          done
        else
          alerta "$archivo línea $numero: la llave del salto no acota los destinos con permitopen"
          claves_mal=1
        fi
      fi
    done <"$archivo"
  done
done
[[ $claves_mal -eq 0 ]] && ok "las llaves autorizadas no reenvían agente y las del salto están acotadas"

# ---------- 5. Credenciales propias y mínimas ----------
if [[ ! -f "$AUX_ENV" ]]; then
  alerta "falta $AUX_ENV (el archivo de entorno de VM-AUX)"
else
  # Lo lee el usuario sin privilegios pero lo carga también la tarea de root: debe ser de root y no escribible por el grupo ni por otros
  # (0600, 0640, 0400 o 0440); si `babelhub-aux` pudiera cambiarlo, podría inyectar lo que root lea.
  lectura="$("$STAT" -c '%U %a' "$AUX_ENV" 2>/dev/null || echo '? ?')"
  dueno="${lectura% *}"
  modo="${lectura#* }"
  if [[ "$dueno" == root && "$modo" =~ ^[0-7]?[46][04]0$ ]]; then
    ok "$AUX_ENV es de root con permisos $modo"
  else
    alerta "$AUX_ENV es de $dueno con permisos $modo: debe ser de root y 0600 o 0640 (el usuario sin privilegios lo lee pero no lo cambia)"
  fi
  ajenas=0
  presentes=""
  while IFS= read -r linea || [[ -n "$linea" ]]; do
    [[ -z "${linea// /}" || "$linea" == \#* ]] && continue
    nombre="${linea%%=*}"
    nombre="${nombre#export }"
    presentes+=" $nombre "
    if [[ ! "$nombre" =~ ^(AUX_[A-Z0-9_]+|RCLONE_CONFIG_(VIGILANTE|SELLO|COPIA|CUOTA)_[A-Z0-9_]+)$ ]]; then
      alerta "$AUX_ENV tiene una variable ajena a VM-AUX: $nombre (credencial de más)"
      ajenas=1
    fi
  done <"$AUX_ENV"
  [[ $ajenas -eq 0 ]] && ok "$AUX_ENV solo tiene credenciales propias de VM-AUX"
  for requerida in AUX_SMTP_URL AUX_SMTP_USUARIO AUX_SMTP_CLAVE AUX_CORREO_DE AUX_CORREO_PARA; do
    [[ "$presentes" == *" $requerida "* ]] || alerta "falta $requerida: VM-AUX necesita su credencial de correo propia"
  done
  if grep -Eq '^(export )?AUX_SMTP_URL=["'\'']?smtp://' "$AUX_ENV"; then
    alerta "AUX_SMTP_URL usa smtp:// sin TLS implícito: usa smtps:// (el correo exige TLS)"
  fi
fi

# ---------- 6. Lo que corre como root no lo puede cambiar quien no es root ----------
# Esta comprobación corre como root (necesita leer sshd y las carpetas de todos): si el usuario sin privilegios `babelhub-aux` pudiera editar
# estos scripts, ganaría root en la siguiente corrida. Deben ser de root y sin escritura para el grupo ni para otros.
scripts_mal=0
for nombre in verificar-endurecimiento.sh alertar.sh comun.sh; do
  archivo="$SCRIPTS_DE_VM_AUX/$nombre"
  if [[ ! -f "$archivo" ]]; then
    alerta "falta $archivo"
    scripts_mal=1
    continue
  fi
  lectura="$("$STAT" -c '%U %a' "$archivo" 2>/dev/null || echo '? ?')"
  dueno="${lectura% *}"
  modo="${lectura#* }"
  if [[ "$dueno" != root ]] || [[ ! "$modo" =~ ^[0-7]?[0-7][0145][0145]$ ]]; then
    alerta "$archivo es de $dueno con permisos $modo: debe ser de root y sin escritura para otros"
    scripts_mal=1
  fi
done
# Y también sus carpetas (y las de arriba): quien pueda escribir en la carpeta puede renombrar un script por otro.
carpeta="$SCRIPTS_DE_VM_AUX"
for _ in 1 2 3 4; do
  [[ -d "$carpeta" && "$carpeta" != / ]] || break
  lectura="$("$STAT" -c '%U %a' "$carpeta" 2>/dev/null || echo '? ?')"
  dueno="${lectura% *}"
  modo="${lectura#* }"
  if [[ "$dueno" != root ]] || [[ ! "$modo" =~ ^[0-7]?[0-7][0145][0145]$ ]]; then
    alerta "la carpeta $carpeta es de $dueno con permisos $modo: debe ser de root y sin escritura para otros"
    scripts_mal=1
  fi
  carpeta="$(dirname "$carpeta")"
done
[[ $scripts_mal -eq 0 ]] && ok "los scripts que corren como root, y sus carpetas, son de root y no se pueden modificar"

if [[ $fallos -gt 0 ]]; then
  echo "VM-AUX: $fallos comprobación(es) del endurecimiento fallaron."
  exit 1
fi
echo "VM-AUX: el endurecimiento cumple."
