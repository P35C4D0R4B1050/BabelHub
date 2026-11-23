# Guía del primer despliegue en Oracle Cloud

Para una persona que **nunca** usó Docker, Oracle Cloud, máquinas virtuales ni Ubuntu sin pantalla. Cada paso trae el comando exacto, **qué debes ver** si salió bien, **qué hacer si falla** y **cómo deshacerlo**. No saltes pasos ni cambies el orden: cada etapa prepara la siguiente.

> **Léela entera una vez antes de empezar.** Dedícale varios días: la mayor parte del tiempo es esperar (la cuenta de Oracle, el DNS, la capacidad ARM). Nada de lo que hagas aquí te cuesta dinero si respetas el cupo gratuito (ver el paso 3), pero un error con secretos sí puede costarte caro. Ante la duda, **para y pregunta**.

## 0. Antes de empezar: el estado real de BabelHub (léelo primero)

Esta guía deja listo **todo lo que es infraestructura**. Pero hay cosas del código que hoy impiden que la tienda arranque en producción, y debes saberlas antes de gastar tiempo:

| # | Qué pasa | Por qué | Qué decidir |
| :--- | :--- | :--- | :--- |
| **1** | **El servicio E-commerce NO arranca en modo `Production`** y, con él, tampoco la réplica de gestión de VM-INVENTARIO. | `BABELHUB__Pagos__Pasarela=simulador` solo vale en `Development` y `Test`, y `culqi` aún no tiene adaptador (T37 y T38 de `ecommerce-pagos`, a la espera de las llaves de Culqi, B05): el arranque falla a propósito para que nunca se cobre «a ciegas». Se verificó con la imagen real el 2026-11-23. | Implementar T37/T38 de `ecommerce-pagos` con tus llaves de pruebas **antes** del primer arranque real de la tienda (y entonces `sk_live_…`, ver §13); o aceptar que la primera vez solo se despliega la **infraestructura** y el **Inventario**. Los pasos de VM-INVENTARIO y VM-VENTAS te dicen qué esperar. |
| **2** | PostgreSQL va **sin TLS** entre VM-VENTAS y VM-INVENTARIO. | La imagen no tiene TLS configurado todavía; la contraseña de la cuenta viaja por la red privada de Oracle (aislada de Internet). | Riesgo residual registrado (`progress/hallazgos.md`). Aceptable para pruebas; resolver antes de operar con clientes reales. |
| **3** | Los clientes de seguridad exigen **HTTPS entre servicios** en producción. | Es una regla de la aplicación (SEG-15). | Ya está resuelto aquí: `scripts/generar-secretos.sh` crea una **CA privada** y los certificados de cada VM, y los Compose abren el puerto 8443. No hay que hacer nada, solo entenderlo (reglas de red en el paso 3). |

Lo que sí **funciona hoy y se probó de verdad** (en tu equipo, con las imágenes reales y los mismos scripts): construir las tres imágenes, PostgreSQL con sus migraciones, el **Inventario en `Production`** respondiendo `/health/ready` por HTTPS con el certificado validado, la réplica de gestión y el E-commerce (estos dos en `Development`), Redis con TLS, n8n y los scripts de despliegue. Lo que **no** se pudo probar sin Oracle y sin dominio: Cloudflare, el túnel, el almacén de objetos, el cobro con Culqi, WhatsApp y la capacidad ARM real.

## 1. Qué necesito tener a mano

Marca cada uno antes de empezar:

**Cuentas y dinero**
- [ ] Una tarjeta de crédito o débito **internacional** (Oracle la pide para verificar la cuenta; no te cobra mientras te quedes en el cupo gratuito). Anota que Oracle hace un cargo temporal de verificación (~1 USD) y lo devuelve.
- [ ] Un correo que revises a diario y un teléfono para el SMS de verificación.
- [ ] La cuenta de **GitHub** dueña del repositorio.
- [ ] Una cuenta de **Cloudflare** (gratuita) y el **dominio comprado en Spaceship** con acceso a su panel.
- [ ] Una cuenta de **Gmail sencilla** (no institucional) con verificación en dos pasos y una **contraseña de aplicación** (Cuenta de Google → Seguridad → Contraseñas de aplicaciones). Es para los correos del sistema. Otra contraseña de aplicación (o la misma cuenta con otra contraseña) para los avisos del espacio reservado.
- [ ] Más adelante: llaves de Culqi (B05), tu API key de Anthropic (B06), un número de WhatsApp dedicado (B07). **No las necesitas para empezar.**

**En tu equipo**
- [ ] Windows 11 con **WSL** (Ubuntu) o una Linux/macOS. En Windows, abre la app «Ubuntu» (si no la tienes: PowerShell como administrador → `wsl --install -d Ubuntu`, reinicia). Todos los comandos marcados **[Tu equipo]** se ejecutan ahí.
- [ ] **Docker Desktop** (con la integración WSL activada) o Docker Engine en tu Linux.
- [ ] `git`, `openssl`, `curl`, `ssh`. En Ubuntu: `sudo apt update && sudo apt install -y git openssl curl openssh-client age`.
- [ ] El **repositorio clonado**: `git clone https://github.com/<propietario>/BabelHub.git ~/BabelHub && cd ~/BabelHub`.
- [ ] La **CLI de Oracle** (`oci`) para el almacén de objetos (paso 8): <https://docs.oracle.com/en-us/iaas/Content/API/SDKDocs/cliinstall.htm>.
- [ ] Un **gestor de contraseñas** o un cuaderno **fuera de línea** y **dos lugares distintos** para guardar la clave de respaldos (paso 9).

**Convenciones.** `[Tu equipo]` = tu WSL/Linux. `[VM-X]` = dentro de esa VM por SSH. Lo que va entre `<ángulos>` lo reemplazas tú. **Nunca pegues un secreto en un chat, un correo ni el repositorio.** Si un comando imprime algo que parece secreto, no lo copies a ningún lado.

## 2. El mapa

```
Internet ──► Cloudflare (DNS, TLS, WAF, Access) ──túnel saliente──► VM-BORDE (nginx + cloudflared) ──► VM-VENTAS ──► VM-INVENTARIO
                                                                       └ (segundo conector) ─────────────────► (E-commerce)   (Inventario + PostgreSQL)
                                  tu equipo ──SSH──► VM-AUX (salto) ──SSH──► las otras tres
```

| VM | Tipo en Oracle | IP privada (la que usaremos) | Qué corre |
| :--- | :--- | :--- | :--- |
| **VM-INVENTARIO** | ARM `VM.Standard.A1.Flex`, 1 OCPU, 6 GB | `10.0.0.20` | PostgreSQL, Inventario, réplica de gestión del E-commerce |
| **VM-VENTAS** | ARM `VM.Standard.A1.Flex`, 1 OCPU, 6 GB | `10.0.0.10` | E-commerce, Redis (caché y contadores), n8n, Evolution API, 2.º conector de cloudflared |
| **VM-BORDE** | AMD `VM.Standard.E2.1.Micro` | `10.0.0.30` | nginx y cloudflared |
| **VM-AUX** | AMD `VM.Standard.E2.1.Micro` | `10.0.0.40` | Salto SSH y vigilancia de respaldos |

**El orden de la guía** (cada etapa termina con una comprobación; no pases a la siguiente si falla):

1. Ensayo en tu equipo (paso 4) → 2. Publicar las imágenes (paso 5) → 3. Cuenta, red y las 4 VMs de Oracle (pasos 6–7) → 4. Almacén de objetos y secretos (pasos 8–9) → 5. **VM-INVENTARIO** (paso 11) → 6. **VM-VENTAS** (paso 12) → 7. Cloudflare y **VM-BORDE** (paso 13) → 8. **VM-AUX** y respaldos (paso 14) → 9. Después del despliegue (paso 15).

*Por qué el almacén y los secretos van antes que las VMs (y no al final):* los secretos de VM-INVENTARIO y de VM-VENTAS incluyen las llaves del almacén de objetos; sin ellas el despliegue se niega a continuar a propósito.

*Contacto del comercio y versión de los textos legales (mínimo, `web-informacion-del-comercio`):* el `ecommerce.env` de VM-VENTAS y el `ecommerce-gestion.env` de VM-INVENTARIO llevan además `BABELHUB__Contacto__Telefono`, `…Correo` y `…Direccion` como `CAMBIAR` (no son secretos): escribe los reales entre comillas dobles antes de desplegar, y tras `desplegar inventario` fija la versión de los textos legales como indica `infra/README.md` (sección «Contacto del comercio y versión de los textos legales»).

## 3. Reglas de oro (para no perder dinero ni datos)

- **El cupo gratuito** (verifícalo en la consola al crear la cuenta; las cifras de esta guía vienen de fuentes secundarias del 2026-11-06): **2 VMs ARM con 2 OCPU y 12 GB en total, 2 VMs AMD micro, 200 GB de disco en total**. Crear una tercera VM ARM, una VM más grande, un balanceador o un volumen extra **sí cobra**. Crea exactamente lo de esta guía.
- **Presupuesto con alerta de 1 USD** desde el primer día (paso 6.3).
- **La región no se puede cambiar** después de crear la cuenta: elige **Chile (Santiago) `sa-santiago-1`** (la más cercana a Perú).
- **Nunca** ejecutes `docker compose down -v` en VM-INVENTARIO: **borra la base de datos**. `down` sin `-v` no.
- **Nunca** copies la carpeta `custodia/` a una VM. Contiene la clave que descifra todos los respaldos.
- **Nunca** hagas `git push --force`, ni subas un `.env`, una `.pem` ni una clave al repositorio (es **público**).

## 4. Paso 0 — Ensayo en tu equipo (30–60 minutos, gratis, sin riesgo)

Reproduce el despliegue con los mismos scripts y los mismos Compose, sin Oracle, sin dominio y con secretos de mentira. **Hazlo siempre antes de tocar Oracle.**

**[Tu equipo]**

```bash
cd ~/BabelHub
bash scripts/ensayo-local.sh
```

**Qué debes ver** (la primera vez tarda 10–20 minutos construyendo las imágenes; las siguientes, 2–3):

```
==> 1/6 Registro local y construcción de las tres imágenes ...
==> 3/6 VM-INVENTARIO: PostgreSQL, migraciones, Inventario y réplica de gestión
migracion: inventario al día (...)
migracion: ecommerce al día (14 pasos, 3 cuentas)
ok postgres
ok ecommerce-gestion
ok inventario
VM inventario desplegada y sana.
==> 4/6 VM-VENTAS: ...
ok redis-contadores   ok redis   ok ecommerce   ok n8n
VM ventas desplegada y sana.
==> 5/6 Humo: ...
    Inventario (https://inventario:8443): {"estado":"listo",...}  [HTTP 200]
    E-commerce (https://ecommerce:8443):  {"estado":"listo",...}  [HTTP 200]
```

Verás también una línea `Cannot load library libgssapi_krb5.so.2` al migrar: **es un aviso inofensivo** de .NET (busca autenticación Kerberos que no usamos).

**Si falla:** el mensaje nombra qué falta. Los fallos típicos: *Docker no está corriendo* (ábrelo), *puerto ocupado* (cierra lo que use 5055, 55432, 58080…), *poca memoria* (cierra programas; necesita ~6 GB libres).

**Cómo deshacer** (siempre que quieras repetirlo desde cero):

```bash
bash scripts/ensayo-local.sh --limpiar
```

Esto borra solo los contenedores, volúmenes y archivos del ensayo.

## 5. Paso 1 — Publicar las imágenes (GitHub Actions → GHCR)

Las VMs no construyen nada: **bajan imágenes ya construidas** del registro de contenedores de GitHub (GHCR). Un flujo (`.github/workflows/lanzamiento.yml`) las construye para ARM y x86, las revisa (secretos en las capas, usuario sin privilegios), las sube y te muestra el **digest** de cada una: una huella (`sha256:…`) que identifica *exactamente* ese contenido. Esa huella es lo que pegas en las VMs.

### 5.1 Preparación única en GitHub (10 minutos)

1. **Protege las etiquetas de versión.** GitHub → tu repositorio → *Settings → Rules → Rulesets → New ruleset → New tag ruleset*. *Target:* etiquetas `v*`. Reglas: *Restrict creations*, *Restrict updates*, *Restrict deletions*. *Bypass list:* solo tú. Así nadie más puede crear la versión que se publica.
2. **Crea el entorno `lanzamiento`.** *Settings → Environments → New environment* → nombre `lanzamiento` → *Required reviewers*: tú. Así cada publicación te pide aprobación (un botón verde) antes de construir.
3. Confirma que el flujo `ci` y el de `secretos` están **en verde** en `main` (pestaña *Actions*). El flujo de lanzamiento se niega a publicar si el commit no pasó ambos.

### 5.2 Lanzar

La persona responsable (tú, no un agente) etiqueta un commit de `main` **que ya esté subido a GitHub** y sube la etiqueta:

**[Tu equipo]**

```bash
cd ~/BabelHub
git checkout main && git pull
git tag -a v0.1.0 -m "primer despliegue"
git push origin v0.1.0
```

GitHub → pestaña *Actions* → «lanzamiento» → te pide **Review deployments** → *Approve*. Tarda 15–30 minutos (construye 6 imágenes: 3 × 2 arquitecturas).

**Qué debes ver:** los seis trabajos «Construir …» en verde y, al final, el resumen **«Digests para los Compose»** con tres líneas:

```
IMAGEN_ECOMMERCE=ghcr.io/<propietario>/babelhub-ecommerce@sha256:<64 caracteres>
IMAGEN_INVENTARIO=ghcr.io/<propietario>/babelhub-inventario@sha256:<64 caracteres>
IMAGEN_POSTGRES=ghcr.io/<propietario>/babelhub-postgres@sha256:<64 caracteres>
```

**Cópialas** a un archivo de texto tuyo (también quedan como artefacto «imagenes-publicadas» 90 días). Las usarás tal cual en los pasos 11–13.

**Si falla:** «El flujo ci.yml no terminó en verde» → espera o corrige; «El commit no está en la rama principal» → etiqueta un commit de `main`; «La etiqueta ya existe» → una versión publicada nunca se sobrescribe: crea `v0.1.1`; «Se encontraron secretos en la imagen» → **no publiques**, avisa (es un hallazgo de seguridad real).

### 5.3 Que las VMs puedan bajar las imágenes

Los paquetes de GHCR nacen **privados**. Haz públicas las tres imágenes (el código ya es público y las imágenes no llevan secretos: el flujo lo comprueba): GitHub → tu perfil u organización → *Packages* → cada paquete (`babelhub-ecommerce`, `babelhub-inventario`, `babelhub-postgres`) → *Package settings → Change visibility → Public*. **Es irreversible** para ese paquete: hazlo solo después de ver el flujo en verde.

Alternativa (si prefieres paquetes privados): crea un *Personal Access Token (classic)* con **solo** `read:packages`, y en cada VM `echo <TOKEN> | sudo docker login ghcr.io -u <usuario> --password-stdin`. Es un secreto más de la VM: guárdalo con permisos 600 y caduca el token a los 90 días.

### 5.4 Verificar de dónde vienen (recomendado)

La procedencia firmada demuestra qué commit y qué flujo construyó cada imagen:

**[Tu equipo]** (necesita la CLI `gh`)

```bash
gh attestation verify oci://ghcr.io/<propietario>/babelhub-inventario@sha256:<64> --repo <propietario>/BabelHub --source-ref refs/tags/v0.1.0
```

**Debes ver** `✓ Verification succeeded!`. Si dice que no encuentra atestación o que el origen no coincide, **no despliegues esa imagen**.

**Cómo deshacer:** una versión mala no se borra ni se sobrescribe; publicas `v0.1.1` y despliegas esa. Los paquetes se pueden borrar desde *Package settings → Delete this package*.

## 6. Paso 2 — La cuenta de Oracle Cloud

### 6.1 Crear la cuenta

1. Entra a <https://www.oracle.com/cloud/free/> → *Start for free*.
2. **Región de inicio (Home Region): Chile (Santiago) — `sa-santiago-1`.** No se puede cambiar. Si no aparece como opción, la más cercana disponible; anótala, porque cambia los nombres de la consola.
3. Verifica correo, teléfono y tarjeta. La cuenta tarda de minutos a horas en quedar activa.

### 6.2 Subir a «Pay As You Go» (PAYG)

Oracle recupera las VMs gratuitas con uso muy bajo y las cuentas gratuitas fallan más al pedir capacidad ARM. *Menú → Billing & Cost Management → Upgrade and Manage Payment → Upgrade to Pay As You Go.* **Subir a PAYG no amplía el cupo**: lo gratuito sigue gratis; solo lo que *superes* se cobra. Anota la fecha en que confirmaste las cifras del cupo en la consola (*Governance → Limits, Quotas and Usage*).

### 6.3 Presupuesto y alerta de 1 USD (hazlo ya)

*Billing & Cost Management → Budgets → Create Budget*: monto mensual **1 USD**, alerta al **100 %** del gasto *real* a tu correo. Si te llega un correo, **entra y busca qué recurso se pasó del cupo**.

**Cómo deshacer todo lo de Oracle:** *Compute → Instances → cada una → Terminate (marca «delete boot volume»)*, y luego la red (§7). Con eso no queda nada que cobre.

## 7. Paso 3 — Red privada y las cuatro VMs

### 7.1 La red (VCN)

*Menú → Networking → Virtual Cloud Networks → Start VCN Wizard → Create VCN with Internet Connectivity* → nombre `babelhub`, CIDR `10.0.0.0/16`, subred pública `10.0.0.0/24`.

Las VMs necesitan **salida** a Internet (bajar imágenes de Docker y conectarse a Cloudflare), pero **ningún puerto de entrada abierto a Internet** salvo SSH hacia VM-AUX desde tu IP. Edita la **Security List** de la subred pública así:

*Ingress rules* (borra todas las que traiga el asistente y deja **solo** estas; «Source CIDR» = quién puede entrar):

| Origen | Protocolo / puerto destino | Para qué |
| :--- | :--- | :--- |
| `<tu IP pública>/32` (búscala en <https://ifconfig.me>) | TCP 22 | Tú → VM-AUX (después restringimos a ella en el sistema operativo) |
| `10.0.0.40/32` (VM-AUX) | TCP 22 | VM-AUX → las demás VMs |
| `10.0.0.30/32` (VM-BORDE) | TCP 8080 | nginx → E-commerce (VM-VENTAS) |
| `10.0.0.30/32` | TCP 8081 | nginx → réplica de gestión (VM-INVENTARIO) |
| `10.0.0.10/32` (VM-VENTAS) | TCP 8082 | E-commerce → Inventario (VM-INVENTARIO, por HTTPS) |
| `10.0.0.10/32` | TCP 5432 | E-commerce → PostgreSQL (VM-INVENTARIO) |
| `10.0.0.20/32` (VM-INVENTARIO) | TCP 8443 | Inventario → E-commerce (VM-VENTAS, por HTTPS) |
| `10.0.0.20/32` | TCP 6380 | Réplica de gestión → Redis de contadores (VM-VENTAS, TLS) |
| `10.0.0.0/24` | ICMP tipo 3 código 4 | Obligatoria para que no se corten conexiones grandes |

*Egress:* deja la regla `0.0.0.0/0` todos los protocolos (salida libre).

Aunque la lista de seguridad pone estas reglas a nivel de red, **cada VM tendrá su propio cortafuegos** (paso 8.1): doble barrera. **No abras nunca 80, 443 ni 5678 a `0.0.0.0/0`.**

> **Cambio respecto a `05-despliegue.md` §7:** el aviso de traslado del Inventario al E-commerce entra ahora por el puerto **8443 por HTTPS** (antes decía 8080) porque la aplicación exige HTTPS entre servicios. El 8080 de VM-VENTAS solo lo usa nginx.

### 7.2 Las llaves SSH (en tu equipo)

**[Tu equipo]**

```bash
ssh-keygen -t ed25519 -f ~/.ssh/babelhub_oracle -C "babelhub-oracle"
```

Te pide una **frase de paso**: ponla (protege la llave si te roban el equipo). **Qué debes ver:** dos archivos, `~/.ssh/babelhub_oracle` (**privada: jamás la copies, envíes ni subas**) y `~/.ssh/babelhub_oracle.pub` (pública: esta sí se pega en Oracle). `cat ~/.ssh/babelhub_oracle.pub` imprime una línea `ssh-ed25519 AAAA… babelhub-oracle`.

### 7.3 Crear las cuatro VMs

*Menú → Compute → Instances → Create instance* (repite cuatro veces):

| Campo | VM-INVENTARIO | VM-VENTAS | VM-BORDE | VM-AUX |
| :--- | :--- | :--- | :--- | :--- |
| Name | `vm-inventario` | `vm-ventas` | `vm-borde` | `vm-aux` |
| Image | Canonical **Ubuntu 24.04** (aarch64) | Ubuntu 24.04 (aarch64) | Ubuntu 24.04 (x86_64) | Ubuntu 24.04 (x86_64) |
| Shape | *Ampere* → `VM.Standard.A1.Flex`, **1 OCPU, 6 GB** | ídem | *AMD* → `VM.Standard.E2.1.Micro` | ídem |
| Subred | `babelhub` pública | ídem | ídem | ídem |
| IP pública | *Assign a public IPv4* | ídem | ídem | ídem |
| IP privada | *Show advanced options → Networking → Private IPv4 address*: `10.0.0.20` | `10.0.0.10` | `10.0.0.30` | `10.0.0.40` |
| SSH keys | *Paste public keys* → pega `babelhub_oracle.pub` | ídem | ídem | ídem |
| Boot volume | **47 GB** (el mínimo; no marques volumen extra) | ídem | ídem | ídem |

(Las VMs sin servicio público también llevan IP pública solo para que *salgan* a Internet; la lista de seguridad impide que alguien entre.)

**«Out of host capacity» / «Out of capacity for shape»** es muy común en ARM. No es un error tuyo. Pruebas: otro *Availability Domain* (AD-1, AD-2, AD-3) en el mismo formulario; reintentar cada pocos minutos, de madrugada o en fines de semana; subir a PAYG (§6.2). Puede tardar horas o días. Si prefieres automatizar los reintentos, la CLI `oci` (`oci compute instance launch …`) lo permite; no dejes una VM a medio crear.

**Qué debes ver:** las cuatro instancias en estado **Running** con su IP privada y su IP pública. Anota las **IP públicas** (solo para entrar por SSH).

**Cómo deshacer:** *Instance → More actions → Terminate* y marca «Permanently delete the attached boot volume».

### 7.4 Entrar por SSH

Edita `~/.ssh/config` en **[Tu equipo]** (créalo si no existe: `nano ~/.ssh/config`):

```
Host babelhub-aux
    HostName <IP pública de vm-aux>
    User ubuntu
    IdentityFile ~/.ssh/babelhub_oracle
    ForwardAgent no

Host babelhub-inventario
    HostName 10.0.0.20
    User ubuntu
    IdentityFile ~/.ssh/babelhub_oracle
    ProxyJump babelhub-aux
    ForwardAgent no

Host babelhub-ventas
    HostName 10.0.0.10
    User ubuntu
    IdentityFile ~/.ssh/babelhub_oracle
    ProxyJump babelhub-aux
    ForwardAgent no

Host babelhub-borde
    HostName 10.0.0.30
    User ubuntu
    IdentityFile ~/.ssh/babelhub_oracle
    ProxyJump babelhub-aux
    ForwardAgent no
```

```bash
chmod 600 ~/.ssh/config
ssh babelhub-aux      # primera vez: escribe «yes» a la huella; después pide tu frase de paso
```

**Qué debes ver:** `ubuntu@vm-aux:~$`. Sal con `exit`. Repite con `ssh babelhub-ventas` (pasa por VM-AUX sin que tengas que hacer nada). **Si no entra:** revisa que la IP pública sea la de la VM, la regla de entrada TCP 22 con *tu* IP actual (cambia si tu proveedor cambia tu IP) y que la llave pública sea la correcta.

## 8. Paso 4 — Preparar cada VM (Ubuntu, cortafuegos, Docker)

Hazlo **en las cuatro VMs** (`ssh babelhub-<nombre>`). Cada bloque se pega completo.

### 8.1 Actualizar, tiempo y cortafuegos

**[VM-X]**

```bash
sudo apt-get update && sudo apt-get -y full-upgrade
sudo apt-get install -y unattended-upgrades chrony ufw ca-certificates curl git
sudo dpkg-reconfigure -plow unattended-upgrades     # responde «Yes»: parches de seguridad automáticos
sudo systemctl enable --now chrony                  # reloj sincronizado: las credenciales duran 15 minutos
chronyc tracking | head -3                          # «Leap status: Normal»
```

**Cortafuegos del sistema (`ufw`).** Las imágenes de Ubuntu de Oracle traen reglas de `iptables` que bloquean casi todo; las reemplazamos por `ufw`:

```bash
sudo apt-get purge -y iptables-persistent netfilter-persistent 2>/dev/null || true
sudo ufw --force reset
sudo ufw default deny incoming
sudo ufw default allow outgoing
# SSH: en VM-AUX desde tu IP; en las demás solo desde VM-AUX.
sudo ufw allow from 10.0.0.40 to any port 22 proto tcp      # en VM-AUX: usa  sudo ufw allow from <tu IP>/32 to any port 22 proto tcp
```

Luego, **según la VM**, abre solo lo que le toca:

```bash
# VM-INVENTARIO (10.0.0.20)
sudo ufw allow from 10.0.0.30 to any port 8081 proto tcp   # nginx → réplica de gestión
sudo ufw allow from 10.0.0.10 to any port 8082 proto tcp   # E-commerce → Inventario (HTTPS)
sudo ufw allow from 10.0.0.10 to any port 5432 proto tcp   # E-commerce → PostgreSQL

# VM-VENTAS (10.0.0.10)
sudo ufw allow from 10.0.0.30 to any port 8080 proto tcp   # nginx → E-commerce
sudo ufw allow from 10.0.0.20 to any port 8443 proto tcp   # Inventario → E-commerce (HTTPS)
sudo ufw allow from 10.0.0.20 to any port 6380 proto tcp   # réplica de gestión → Redis de contadores (TLS)

# VM-BORDE y VM-AUX: nada más (solo el SSH de arriba)
```

```bash
sudo ufw --force enable
sudo ufw status numbered
```

**Qué debes ver:** `Status: active` y solo esas reglas. **Importante:** si en VM-AUX pones una IP equivocada para tu SSH, **te quedas fuera**; en ese caso *Instance → Console connection* de Oracle te da una consola de rescate. Docker publica sus puertos saltándose `ufw`; por eso los Compose publican cada puerto **solo en la IP privada** de la VM y la Security List de Oracle es la segunda barrera.

**Cómo deshacer:** `sudo ufw disable`.

### 8.2 Endurecer SSH y comprobar el disco

```bash
sudo tee /etc/ssh/sshd_config.d/60-babelhub.conf >/dev/null <<'EOF'
PermitRootLogin no
PasswordAuthentication no
KbdInteractiveAuthentication no
X11Forwarding no
AllowAgentForwarding no
EOF
sudo sshd -t && sudo systemctl reload ssh
swapon --show          # debe salir VACÍO (sin swap: los secretos en memoria no se escriben a disco). Si sale algo: sudo swapoff -a  y borra la línea «swap» de /etc/fstab
lsblk -f
```

**Qué debes ver:** `sshd -t` sin mensajes; `swapon --show` vacío. (El swap desactivado es obligatorio en el **entorno de restauración de ensayo**, PLA-39 y T05; en las VMs de producción es una buena práctica.) **No cierres tu sesión SSH actual hasta probar que puedes abrir otra** en una terminal nueva.

El cifrado en reposo de los discos de Oracle está siempre activo y no se ve desde dentro; en el paso 14 lo verificas con la CLI.

### 8.3 Instalar Docker

Solo en **VM-INVENTARIO, VM-VENTAS y VM-BORDE** (VM-AUX no usa Docker):

```bash
sudo install -m 0755 -d /etc/apt/keyrings
sudo curl -fsSL https://download.docker.com/linux/ubuntu/gpg -o /etc/apt/keyrings/docker.asc
echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/ubuntu $(. /etc/os-release && echo $VERSION_CODENAME) stable" | sudo tee /etc/apt/sources.list.d/docker.list >/dev/null
sudo apt-get update
sudo apt-get install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
sudo docker version --format '{{.Server.Version}} {{.Server.Arch}}'
sudo docker compose version
sudo docker run --rm hello-world | head -3
```

**Qué debes ver:** una versión de Docker 28 o más y `arm64` (en las ARM) o `amd64`; `Docker Compose version v2.…`; y `Hello from Docker!`. **No** te agregues al grupo `docker` (equivale a ser administrador): usa `sudo`.

### 8.4 Traer el código de los scripts

Los scripts y los Compose viven en el repositorio. Se clonan como **root** (los scripts de VM-INVENTARIO deben ser de root, lo comprueba `desplegar.sh`) y se fija la versión:

```bash
sudo git clone https://github.com/<propietario>/BabelHub.git /opt/babelhub
cd /opt/babelhub && sudo git checkout v0.1.0
sudo git log -1 --format='%H %s'        # debe ser el commit de tu etiqueta
```

(Repo público: no pide contraseña. Para actualizar: `cd /opt/babelhub && sudo git fetch --tags && sudo git checkout v0.1.1`.)

## 9. Paso 5 — El almacén de objetos de Oracle

Guarda los **respaldos cifrados**, los **libros digitales**, las **portadas** y el **registro de supresiones**. Cada tarea tiene su propia credencial con permisos mínimos (`infra/almacen/politicas.md`).

**[Tu equipo]** — configura la CLI una vez: `oci setup config` (te pide el OCID de tu usuario y tenancy, que están en *Perfil → Tenancy/User settings*, y crea una llave de API que subes en *User settings → API keys*). Comprueba con `oci iam region list`.

1. Datos que necesitas: el **espacio de nombres** (`oci os ns get`) y el **OCID del compartimento** (crea uno llamado `babelhub`: *Identity → Compartments*).
2. Mira lo que se va a hacer, **sin ejecutar nada**:

   ```bash
   cd ~/BabelHub
   bash scripts/almacen.sh plan --compartimento-id <ocid> --compartimento-nombre babelhub --espacio <espacio-de-nombres>
   ```

3. Aplica (crea los 9 contenedores privados, la retención con bloqueo y el ciclo de vida):

   ```bash
   bash scripts/almacen.sh aplicar --compartimento-id <ocid> --compartimento-nombre babelhub --espacio <espacio-de-nombres>
   ```

   > ⚠️ **La retención con bloqueo es irreversible**: Oracle la hace efectiva 15 días después. Durante ese plazo puedes corregir; después, nunca. `comprobar` te dice «bloqueo pendiente» o «bloqueado».
4. Las políticas de acceso: `bash scripts/almacen.sh politicas-iam --compartimento-nombre babelhub` imprime las sentencias; créalas en *Identity → Policies*. Cada credencial de `infra/almacen/credenciales.txt` es un **usuario de IAM** propio (por ejemplo `bh-respaldos-escritor`) dentro de un grupo con esa política; para cada usuario crea una **Customer Secret Key** (*Usuario → Customer secret keys → Generate*): te da una **clave de acceso** y una **clave secreta** (la secreta se muestra **una sola vez**: cópiala a tu gestor de contraseñas en el acto).
5. Comprueba:

   ```bash
   bash scripts/almacen.sh comprobar --espacio <espacio-de-nombres> --aceptar-bloqueo-pendiente
   ```

   **Debes ver** todos los contenedores «conformes». El endpoint S3 de todas las credenciales es `https://<espacio-de-nombres>.compat.objectstorage.sa-santiago-1.oraclecloud.com`.

Qué credencial va a qué archivo (§10): `respaldos-escritor` → `postgres.env`; `catalogo-archivos`, `biblioteca-lector` y `supresiones-registro` → `ecommerce.env` y `ecommerce-gestion.env`; `respaldos-vigilante`, `supresiones-sello`, `respaldos-copia-registro` y `cuota-lectura` → VM-AUX (paso 14); `respaldos-lector` → solo tu equipo (restaurar).

**Cómo deshacer:** los contenedores con bloqueo ya aplicado no se pueden vaciar antes de su fecha; por eso la regla de oro de no «probar» con esta etapa fuera de la guía.

## 10. Paso 6 — Los secretos (en tu equipo, nunca en el repositorio)

`scripts/generar-secretos.sh` crea **un archivo por servicio y por VM** con valores aleatorios (contraseñas, claves de firma, credenciales entre servicios, certificados de la red privada, el par de claves de cifrado de respaldos). Lo que solo puede dar un tercero (Gmail, Turnstile, almacén, túnel) queda como `CAMBIAR`.

**[Tu equipo]**

```bash
cd ~/BabelHub
bash scripts/generar-secretos.sh --destino ~/babelhub-secretos \
  --dominio <tu-dominio.com> \
  --ip-ventas 10.0.0.10 --ip-inventario 10.0.0.20 --ip-borde 10.0.0.30
```

**Qué debes ver:** «Listo. Se generaron los secretos de las tres VMs» y «Falta completar N valores «CAMBIAR»». Se crean `inventario/`, `ventas/`, `borde/`, `custodia/` y `PENDIENTES.txt`. Si la carpeta ya existe con archivos, el script **se niega** (no pisa secretos).

### 10.1 Custodia: lo más importante de toda la guía

`~/babelhub-secretos/custodia/` tiene **la clave privada de `age`** (descifra TODOS los respaldos, que contienen datos personales) y **la llave de la CA privada**. **Nunca van a una VM.**

1. Copia `custodia/age-clave-privada.txt` a **dos lugares distintos fuera de línea** (por ejemplo dos memorias USB guardadas en sitios diferentes). Sin esta clave, los respaldos son **irrecuperables**.
2. Llena `infra/respaldos/custodia.md` (titular, destinatario público `age1…`, dónde están las dos copias) y comprueba: `bash scripts/comprobar-custodia.sh --entorno ~/babelhub-secretos/inventario/postgres.env`.
3. En Windows/WSL los permisos `600` pueden **no aplicarse** si la carpeta está bajo `/mnt/c`: genera los secretos dentro del sistema de archivos de WSL (`~/…`), no en `C:\`. `desplegar.sh` los vuelve a comprobar en la VM.

### 10.2 Completar los `CAMBIAR`

```bash
cat ~/babelhub-secretos/PENDIENTES.txt          # la lista, por archivo
nano ~/babelhub-secretos/inventario/ecommerce-gestion.env
```

| Qué | De dónde sale |
| :--- | :--- |
| `…Smtp__Servidor`, `…Smtp__Cuenta`, `…Smtp__Remitente`, `…Smtp__Credencial` | Gmail: servidor `smtp.gmail.com`; cuenta y remitente tu Gmail; credencial = la **contraseña de aplicación** (16 letras; escríbela sin espacios). |
| `…Turnstile__ClaveDeSitio` y `…Secreto` | Cloudflare → *Turnstile → Add site* (hostnames: tu dominio y `gestion.<dominio>`); clave de sitio (`0x4…`, pública) y clave secreta. **Hazlo después de activar Cloudflare (paso 13)**; mientras tanto déjalos en `CAMBIAR` y no despliegues el E-commerce. |
| `…Almacen__Catalogo__*`, `…Biblioteca__*`, `…Supresiones__*` | §9: las claves de cada credencial; `Endpoint` = `https://<espacio>.compat.objectstorage.sa-santiago-1.oraclecloud.com`. |
| `…Web__OrigenDelAlmacen` (ventas) | El origen `https://<espacio>.compat.objectstorage.sa-santiago-1.oraclecloud.com` (igual al host del endpoint de `biblioteca-lector`). |
| `postgres.env`: `RCLONE_CONFIG_ALMACEN_ENDPOINT`, `…ACCESS_KEY_ID`, `…SECRET_ACCESS_KEY` | La credencial `respaldos-escritor` (§9). |
| `aviso.env` (inventario) | Un correo propio de esa VM para el aviso del espacio reservado: `AUX_SMTP_URL=smtps://smtp.gmail.com:465` (**`smtps://`**, con TLS), usuario, contraseña de aplicación, remitente y destinatario (tú). |
| `cloudflared.env` (ventas y borde) | El token del túnel (paso 13). |

Edita con `nano`, guarda con `Ctrl+O`, `Enter`, y sal con `Ctrl+X`. No dejes comillas ni espacios alrededor del `=`.

### 10.3 Copiar cada carpeta **solo** a su VM

```bash
cd ~/babelhub-secretos
scp -r inventario babelhub-inventario:/tmp/secretos-inventario
scp -r ventas     babelhub-ventas:/tmp/secretos-ventas
scp -r borde      babelhub-borde:/tmp/secretos-borde
```

**Nunca** copies la carpeta `~/babelhub-secretos` entera ni `custodia/`: `desplegar.sh` se niega a desplegar si ve una clave privada ajena en la carpeta de la VM. En cada VM, deja los secretos fuera del repositorio y solo legibles por root:

**[VM-X]** (cambia `inventario` por `ventas` o `borde`)

```bash
sudo mkdir -p /etc/babelhub
sudo rm -rf /etc/babelhub/secretos
sudo mv /tmp/secretos-inventario /etc/babelhub/secretos
sudo chown -R root:root /etc/babelhub/secretos
sudo chmod 700 /etc/babelhub/secretos && sudo chmod 600 /etc/babelhub/secretos/*
sudo ls -l /etc/babelhub/secretos
```

**Debes ver** todos los archivos `-rw-------  root root`.

**Cómo deshacer / rotar:** vuelve a generar en una carpeta **nueva** (el script nunca pisa) y repite; para rotar credenciales de una VM en marcha: `scripts/rotar-credenciales.sh` (ver su cabecera).

## 11. Paso 7 — Etapa 1: VM-INVENTARIO

Corre PostgreSQL, el Inventario y la réplica de gestión. Es la VM más crítica (guarda la base de datos).

**[VM-INVENTARIO]** — pega tus tres líneas de digest del paso 5 (con los valores reales):

```bash
cd /opt/babelhub
sudo env \
  BABELHUB_SECRETOS=/etc/babelhub/secretos \
  IP_PRIVADA=10.0.0.20 \
  IMAGEN_POSTGRES=ghcr.io/<propietario>/babelhub-postgres@sha256:<64> \
  IMAGEN_INVENTARIO=ghcr.io/<propietario>/babelhub-inventario@sha256:<64> \
  IMAGEN_ECOMMERCE=ghcr.io/<propietario>/babelhub-ecommerce@sha256:<64> \
  scripts/desplegar.sh inventario --sin ecommerce-gestion
```

(`--sin ecommerce-gestion` mientras exista el bloqueo #1 de §0: la réplica de gestión no arrancaría. Cuando lo resuelvas, repites **sin** `--sin`.)

**Qué debes ver, en este orden:**

1. Descarga de las imágenes (`Pulling…`).
2. `Migrando las bases (esquemas y roles)...` y líneas `paso: roles`, `paso: plataforma`… hasta `migracion: inventario al día (… pasos, … cuentas)`. (Con la réplica de gestión: también `migracion: ecommerce al día (14 pasos, 3 cuentas)`; si usas `--sin ecommerce-gestion` **la migración del E-commerce igual corre**, porque la hace el script.)
3. `ok postgres`, `ok inventario`, `ok tarea diaria de respaldo (/etc/cron.d/babelhub-respaldo)` y `VM inventario desplegada y sana.`

**Si falla:** el script **nombra** cada cosa que falta («falta la clave X en postgres.env», «aviso.env es de … debe ser de root», «CAMBIAR sin completar»). Corrige y vuelve a ejecutar: **es idempotente** (repetirlo no rompe nada). Si dice «no es una imagen publicada con su digest», copiaste mal una línea del paso 5.

### 11.1 Comprobaciones

```bash
sudo docker ps --format 'table {{.Names}}\t{{.Status}}'                                  # todos «(healthy)»
sudo docker logs --tail 20 babelhub-inventario-postgres-1                                # sin FATAL
sudo docker exec babelhub-inventario-postgres-1 pg_isready -d postgres                   # «accepting connections»
# El Inventario por HTTPS, validando el certificado con tu CA privada:
sudo docker run --rm --network babelhub-inventario_default -v /etc/babelhub/secretos/tls-ca.pem:/ca.pem:ro \
  curlimages/curl@sha256:3fb7e13e5d179a845c97d78204f3f1064d1907037f26a3ebf91710571151fd93 \
  -sS --cacert /ca.pem https://inventario:8443/health/ready
```

**Debes ver** `{"estado":"listo","componentes":[{"nombre":"base-de-datos","estado":"ok"}]}`. En PostgreSQL pueden verse avisos del **archivado de cambios** si el almacén aún no responde: revisa que `postgres.env` tenga las claves de `respaldos-escritor` y espera 5 minutos (`archive_timeout=300`).

**Primer respaldo y su comprobación** (hazlo una vez):

```bash
sudo docker exec babelhub-inventario-postgres-1 /opt/babelhub/respaldar.sh --bajo-demanda
```

Debe terminar sin error; en la consola de Oracle, el contenedor `respaldos-diarios` tendrá un objeto `AAAA-MM-DD/…tar.age`. **Un respaldo que nunca se restauró no es un respaldo**: el spike T01 (§15.4) lo comprueba.

**Cómo deshacer** (sin perder datos): `cd /opt/babelhub/infra && sudo env BABELHUB_SECRETOS=… IP_PRIVADA=… IMAGEN_…=… docker compose -f docker-compose.inventario.yml down` (**sin `-v`**).

## 12. Paso 8 — Etapa 2: VM-VENTAS

**[VM-VENTAS]**

```bash
cd /opt/babelhub
sudo env \
  BABELHUB_SECRETOS=/etc/babelhub/secretos \
  IP_PRIVADA=10.0.0.10 \
  IMAGEN_ECOMMERCE=ghcr.io/<propietario>/babelhub-ecommerce@sha256:<64> \
  scripts/desplegar.sh ventas --sin cloudflared,evolution-api
```

(`cloudflared` espera el túnel del paso 13; `evolution-api` espera el número de WhatsApp, §15.3.)

**Qué debes ver:** `ok redis`, `ok redis-contadores`, `ok n8n` y, **si resolviste el bloqueo #1**, `ok ecommerce`. Si el E-commerce no arranca por el bloqueo #1, verás su registro con `culqi … aún no tiene adaptador` o `simulador solo se acepta en Development y Test`: **es lo esperado hoy**; el resto de la VM sí queda sana y puedes validar Redis con TLS:

```bash
sudo docker run --rm --network babelhub-ventas_default -v /etc/babelhub/secretos/redis-contadores-ca.pem:/ca.pem:ro \
  redis:7-alpine@sha256:7f7d30b7fdd09467bac58198a1f166dd69789d348d4fe859cccb20e0cf287fbe \
  redis-cli --tls --cacert /ca.pem -h redis-contadores -p 6380 ping
```

(Debe responder `NOAUTH` o `NOPERM`: significa que el TLS funciona y exige credenciales.)

**Paneles de n8n y Evolution:** no están en Internet. Se alcanzan con un túnel SSH desde tu equipo: `ssh -L 5678:127.0.0.1:5678 babelhub-ventas` y abres <http://localhost:5678>.

## 13. Paso 9 — Etapa 3: Cloudflare, el dominio y VM-BORDE

### 13.1 Pasar el dominio de Spaceship a Cloudflare

1. Cloudflare → *Add a domain* → tu dominio → plan **Free** → *Continue*. Importará los registros DNS que ya tengas; revísalos.
2. Cloudflare te muestra **dos nameservers** (por ejemplo `xxx.ns.cloudflare.com` y `yyy.ns.cloudflare.com`).
3. **Spaceship** → *Domains* → tu dominio → *Nameservers* → *Custom nameservers* → pega los dos de Cloudflare → guardar. Si tienes **DNSSEC** activo en Spaceship, desactívalo primero (si no, el dominio deja de resolver).
4. Espera. Cloudflare te envía un correo cuando el dominio queda **Active** (de minutos a 24 horas). **No sigas hasta verlo activo.**

**Qué debes ver:** en Cloudflare, *Overview* del dominio con «Great news! Cloudflare is now protecting your site».

### 13.2 TLS y seguridad básica (certificado válido: requisito de Culqi)

En *SSL/TLS*: modo **Full (strict)**; *Edge Certificates*: **Always Use HTTPS = On**, **Minimum TLS Version = 1.2**, **Automatic HTTPS Rewrites = On**. El certificado del borde es **Universal SSL** (gratuito y válido) y se emite solo cuando el dominio está activo. *Security → Settings*: **Bot Fight Mode = On**. *Security → WAF → Managed rules*: activa el conjunto gratuito de Cloudflare. Culqi exige que la URL de sus avisos tenga un certificado válido: lo da este paso; compruébalo después con `curl -sI https://<tu-dominio>/` (sin `-k`) sin errores de certificado.

### 13.3 El túnel

*Zero Trust* (primera vez: elige el plan gratis) → *Networks → Tunnels → Create a tunnel → Cloudflared* → nombre `babelhub` → en «Install and run connectors» **no ejecutes lo que te muestra**: solo copia el **token** (la cadena larga después de `--token`). Ese token es un secreto: ponlo en `cloudflared.env` de **ventas y borde** (`TUNNEL_TOKEN=…`) y vuelve a copiar esos archivos a las VMs (§10.3).

*Public Hostnames* (agrega tres):

| Hostname | Servicio |
| :--- | :--- |
| `<tu-dominio.com>` | `HTTP` → `nginx:8080` |
| `www.<tu-dominio.com>` | `HTTP` → `nginx:8080` |
| `gestion.<tu-dominio.com>` | `HTTP` → `nginx:8080` |

Los tres apuntan a `nginx:8080`: nginx de VM-BORDE enruta por el nombre de host. En VM-VENTAS, el nombre `nginx` apunta directo al E-commerce (así el segundo conector sigue sirviendo la tienda si cae VM-BORDE; sin la gestión). Cloudflare crea solo los registros DNS (`CNAME`) de cada hostname.

### 13.4 Cloudflare Access para la gestión (obligatorio)

*Zero Trust → Access → Applications → Add an application → Self-hosted*: nombre `gestion`, dominio `gestion.<tu-dominio.com>`. *Policy*: acción **Allow**, regla *Emails* = los correos del personal autorizado. En el plan gratuito el método es el **código de un solo uso por correo**. Para exigir un segundo factor real, añade en *Settings → Authentication* un proveedor de identidad (Google o GitHub) con verificación en dos pasos y haz que la política lo **requiera**. Sin esto, **cualquiera podría llegar a la pantalla de ingreso del personal**.

### 13.5 Desplegar VM-BORDE y volver a VM-VENTAS

**[VM-BORDE]**

```bash
cd /opt/babelhub
sudo env BABELHUB_SECRETOS=/etc/babelhub/secretos IP_VENTAS=10.0.0.10 IP_INVENTARIO=10.0.0.20 scripts/desplegar.sh borde
```

**Debes ver** `ok nginx`, `ok cloudflared`, `VM borde desplegada y sana.` Y en el registro de cloudflared: `sudo docker logs babelhub-borde-cloudflared-1 2>&1 | grep -i "registered tunnel connection"` (cuatro líneas).

**[VM-VENTAS]** — ahora sí con el túnel (omite `--sin cloudflared`):

```bash
cd /opt/babelhub
sudo env BABELHUB_SECRETOS=/etc/babelhub/secretos IP_PRIVADA=10.0.0.10 IMAGEN_ECOMMERCE=ghcr.io/<propietario>/babelhub-ecommerce@sha256:<64> \
  scripts/desplegar.sh ventas --sin evolution-api
```

### 13.6 Humo de punta a punta

**[Tu equipo]**

```bash
curl -s  https://<tu-dominio.com>/health              # «ok» (lo responde nginx)
curl -sI https://<tu-dominio.com>/ | head -3          # «HTTP/2 200» si el E-commerce arrancó (bloqueo #1); sin errores de certificado
curl -sI https://gestion.<tu-dominio.com>/ | head -3  # redirige a la pantalla de Cloudflare Access (no a la app)
```

**Si falla:** `502` = nginx no alcanza al E-commerce (revisa `ufw` y la Security List, §7.1 y §8.1); error de certificado = el dominio aún no está activo en Cloudflare; `1033`/`530` = el túnel no está conectado (token mal copiado).

**Cómo deshacer:** `down` del Compose de VM-BORDE; en Cloudflare, borra el túnel y sus hostnames; para devolver el dominio a Spaceship, cambia los nameservers de vuelta.

## 14. Paso 10 — Etapa 4: VM-AUX y la vigilancia

VM-AUX vigila **desde fuera** de la VM de datos: si esa VM cae, nadie dentro puede avisarlo. Sus scripts (`scripts/vm-aux/`) **no guardan ninguna llave privada ni credencial de borrado**. Sigue **exactamente** `infra/vm-aux/README.md` (secciones «Instalación» y «Endurecimiento»). Resumen en orden:

1. **[VM-AUX]** `sudo git clone https://github.com/<propietario>/BabelHub.git /opt/babelhub && cd /opt/babelhub && sudo git checkout v0.1.0` (la carpeta `scripts/vm-aux` debe ser de **root**).
2. Usuario sin privilegios `babelhub-aux`; `/etc/babelhub-aux/aux.env` (de root, grupo `babelhub-aux`, `0640`) con las credenciales de `respaldos-vigilante`, `supresiones-sello`, `respaldos-copia-registro` y el correo propio (§10.2, §9).
3. `sudo cp infra/vm-aux/babelhub-aux.cron /etc/cron.d/babelhub-aux` y `sudo cp infra/vm-aux/babelhub-aux-endurecimiento.cron /etc/cron.d/`.
4. SSH: `infra/vm-aux/sshd-babelhub-aux.conf` → `/etc/ssh/sshd_config.d/50-babelhub-aux.conf`, usuario `salto` con su `authorized_keys` (la llave **pública**), `sudo sshd -t`, recargar. **Con una segunda terminal abierta** para no quedarte fuera.
5. Comprueba: `sudo /opt/babelhub/scripts/vm-aux/verificar-endurecimiento.sh` (debe terminar en 0) y `/opt/babelhub/scripts/vm-aux/comprobar-respaldos.sh` (en 0 cuando haya un respaldo de las últimas 24 horas).
6. **[Tu equipo]** Disco cifrado de las VMs de datos (T38): `bash scripts/verificar-cifrado-de-disco.sh --instancia <ocid de la VM> --compartimento <ocid> --dominio <AD>` para `vm-inventario` y `vm-ventas`. Debe decir que el volumen está `AVAILABLE` con clave de Oracle. Guarda la salida.
7. **[Tu equipo]** `bash scripts/respaldar-ahora.sh --salto salto@<IP pública de vm-aux> --destino ubuntu@10.0.0.20` prueba el respaldo bajo demanda por el salto.
8. Latido externo (`AUX_LATIDO_URL`): crea un monitor gratuito de «heartbeat» (por ejemplo en healthchecks.io) y pega su URL en `aux.env`.

## 15. Después del despliegue

### 15.1 Culqi (B05)

Con tus llaves de **pruebas** entras al panel de Culqi. Hoy el adaptador de Culqi **no existe** (bloqueo #1): primero se implementa T37/T38 de `ecommerce-pagos` en el repositorio (otra sesión de trabajo, con tus llaves de pruebas solo como variables de entorno locales). Cuando exista: `BABELHUB__Pagos__Pasarela=culqi` y `BABELHUB__Pagos__LlavePrivada=sk_live_…` **solo** en `ventas/ecommerce.env` y `inventario/ecommerce-gestion.env` (en producción la guarda **solo acepta `sk_live_`**; con `sk_test_` el arranque se niega: para probar con llaves de pruebas hace falta un entorno `Development`/`Test`, nunca la VM de producción). En el panel de Culqi registra el **webhook** `https://<tu-dominio.com>/api/ecommerce/v1/integraciones/pasarela/avisos` (certificado válido: §13.2) y contrasta a mano los reembolsos y cobros dudosos con el panel (`docs/05-seguridad/07-operacion-segura.md` §3.2.1). **Nunca** pegues una llave en el chat ni en el repositorio.

### 15.2 LLM del asistente (B06)

Tienes una API key de Anthropic. El asistente (ECO-12) está **diferido** y su variable de entorno aún no existe: `specs/asistente-compra-ia/decisions.md` D07 fija la compuerta (alta en `NombresDeSecretos` y en el redactor, `docs/05-seguridad/06-seguridad-del-asistente.md`, base legal de la transferencia fuera del país). **Hasta entonces no la escribas en ningún archivo.** Cuando exista: va **solo** en `ventas/ecommerce.env` de VM-VENTAS (nunca en `ecommerce-gestion.env` ni en otra VM), con permisos 600, y se rota si hay sospecha (Anthropic Console → API keys → revocar y crear).

### 15.3 WhatsApp (B07)

Necesitas un **número dedicado y descartable** (no el personal). Pasos (detalle en `infra/n8n/README.md`):

1. Crea la base técnica de Evolution **una vez** en VM-INVENTARIO: `sudo docker exec -i babelhub-inventario-postgres-1 psql -U postgres -c "CREATE ROLE evolution LOGIN PASSWORD '<la contraseña de DATABASE_CONNECTION_URI de ventas/evolution.env>'; CREATE DATABASE evolution OWNER evolution; REVOKE CONNECT ON DATABASE evolution FROM PUBLIC; GRANT CONNECT ON DATABASE evolution TO evolution;"`.
2. Despliega VM-VENTAS **sin** `--sin evolution-api`.
3. Por túnel SSH (`ssh -L 8081:127.0.0.1:8081 babelhub-ventas`) abre <http://localhost:8081> y empareja el número (QR).
4. Por otro túnel (`-L 5678:127.0.0.1:5678`) importa `infra/n8n/flujo-de-avisos.json` en n8n, crea la credencial `Evolution API (apikey)` (la de `evolution.env`) y activa el flujo.
5. **Obligatorio antes de activar avisos en producción:** la prueba de humo de `infra/n8n/README.md` («Importar y probar», paso 2).
6. Los avisos por WhatsApp exigen consentimiento (Ley N.° 29733).

### 15.4 Spikes opcionales T01–T05 (`specs/transversal-plataforma/tasks.md`)

Son mediciones que cierran decisiones de la spec. Ninguna bloquea el despliegue, pero **T01 es la única prueba de que tus respaldos sirven**. Registra cada resultado en `progress/ensayos/AAAA-MM-DD-<spike>.md` (fecha, qué hiciste, el número medido, la decisión) y avisa para actualizar `specs/transversal-plataforma/decisions.md`.

| Spike | Qué hacer (ejecutable) | Qué registrar |
| :--- | :--- | :--- |
| **T01** Restaurar una copia hecha en ARM | En tu equipo: baja el respaldo más reciente de `respaldos-diarios` con `respaldos-lector`; `bash scripts/descifrar-respaldo.sh --entrada <respaldo>.tar.age < <tu clave de age> \| bash scripts/extraer-respaldo.sh --destino ~/restauracion-ensayo`; arranca `postgres:17-alpine` (la imagen de `infra/postgres` con `--platform linux/arm64`, en emulación) montando esa carpeta y comprueba `select count(*) from …` de pedidos y reservas. Hazlo en un **volumen cifrado efímero**, con swap apagado, sin red hacia proveedores. | Duración total, contadores, si arrancó sin errores en ARM emulado, y la decisión D12. |
| **T02** Almacén de Oracle | `bash scripts/almacen.sh comprobar --espacio <ns>`; sube dos veces la misma clave con la credencial `supresiones-registro` y comprueba que la segunda **falla** (no sobrescribe); confirma si la retención es por contenedor y **cuándo** ejecuta el ciclo de vida (los objetos de prueba vencen a los días que dice `contenedores.txt`). | Error exacto al repetir, si la retención es por contenedor o por prefijo (decide D23), fecha en que corrió el ciclo de vida. |
| **T03** Tamaño y cuota | En VM-INVENTARIO: `sudo docker exec babelhub-inventario-postgres-1 psql -U postgres -c "select pg_size_pretty(sum(pg_database_size(datname))) from pg_database"`; en la consola de Oracle, *Object Storage → cuota* y el tamaño de cada contenedor. Estima 17 copias × (tamaño comprimido) + WAL + libros. | Tamaño de la base, tamaño por contenedor, cuota gratuita vigente y la proyección contra ella. |
| **T04** Memoria y CPU de VM-INVENTARIO | Con la VM bajo uso normal **y** un respaldo en curso (`respaldar.sh --bajo-demanda`): `free -m` (usa «available», sin caché), `sudo docker stats --no-stream`, `uptime`. Durante 15 minutos, cada minuto. | Memoria usada (sin caché recuperable), CPU, pico durante el respaldo; compara con el 70 % de RNF12 (~4,2 GB). |
| **T05** Volumen cifrado efímero | Una VM o distro de ensayo **aparte** con `swapon --show` vacío; crea un volumen LUKS (`cryptsetup`), restaura ahí, destrúyelo (`cryptsetup luksClose` y borra la llave) y comprueba que no quedan datos en `/tmp`, el historial de la shell ni los registros de Docker (`docker system prune -af --volumes`). | Qué quedó fuera del volumen (residual) y la constancia de destrucción. |

(T29 y T31, los scripts de restauración y de ensayo aislado, **aún no están hechos**: esos spikes se hacen a mano como arriba.)

## 16. Si algo sale mal: tabla de rescate

| Síntoma | Causa probable | Qué hacer |
| :--- | :--- | :--- |
| No entras por SSH | Tu IP cambió o la regla de la Security List | Actualiza la regla TCP 22; o usa *Instance → Console connection* de Oracle. |
| `desplegar.sh` dice «CAMBIAR sin completar» | Falta un dato externo | `cat PENDIENTES.txt`, complétalo, vuelve a copiar el archivo y repite. |
| `desplegar.sh` dice «clave privada que no usa esta VM» | Copiaste una carpeta de más | Borra ese archivo de `/etc/babelhub/secretos` y copia solo la carpeta de la VM. |
| Un contenedor sale «unhealthy» | Configuración o dependencia caída | `sudo docker logs --tail 50 <contenedor>`: el mensaje nombra la variable que falla (nunca su valor). |
| El Inventario responde 403 a las llamadas del E-commerce | Falta HTTPS o la credencial | Ambos lados usan `https://<IP>:8443` o `:8082` y el certificado de la CA privada; revisa `ca-bundle.pem` en la carpeta de secretos. |
| La IP pública de una VM cambió | Reinicio sin IP reservada | Solo afecta tu SSH (las VMs hablan por IP privada). |
| Te llega la alerta de 1 USD | Algo fuera del cupo | *Cost Analysis* → qué recurso; termínalo. |
| Quieres empezar de cero una VM | — | `down` (sin `-v` en VM-INVENTARIO si hay datos), borra `/opt/babelhub` y `/etc/babelhub`, vuelve al paso correspondiente. |
| Sospechas que se filtró un secreto | — | Rota ya: genera nuevos, copia, `desplegar.sh`; revoca en Oracle/Cloudflare/Gmail lo que corresponda; sigue `docs/05-seguridad/07-operacion-segura.md` §6. |

## 17. Dónde está cada pieza (para quien mantenga esto)

| Pieza | Archivo |
| :--- | :--- |
| Flujo de publicación | `.github/workflows/lanzamiento.yml` |
| Ensayo local | `scripts/ensayo-local.sh` |
| Generador de secretos | `scripts/generar-secretos.sh` y `infra/secretos-requeridos.txt` |
| Despliegue de una VM | `scripts/desplegar.sh` y `infra/docker-compose.<vm>.yml` |
| Almacén de objetos | `scripts/almacen.sh` e `infra/almacen/` |
| VM-AUX | `scripts/vm-aux/` e `infra/vm-aux/` |
| Respaldos y custodia | `infra/respaldos/`, `scripts/comprobar-custodia.sh` |
| Decisiones de esta guía | `progress/decisiones-delegadas.md` (DD-55 a DD-59) |
