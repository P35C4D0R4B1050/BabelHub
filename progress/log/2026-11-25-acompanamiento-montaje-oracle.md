# 2026-11-25 — Acompañamiento del montaje en Oracle (sin cambios de código)

Sesión de guía en vivo: la persona responsable hace cada paso en su cuenta; el agente no tiene acceso a Oracle ni a Cloudflare.

## Hecho

- Cuenta en *Pay As You Go*, VCN `babelhub`, reglas de entrada de la guía §7.1, presupuesto y llave SSH.
- `vm-inventario` (10.0.0.20) y `vm-ventas` (10.0.0.10): ARM de 1 OCPU y 6 GB, Ubuntu 24.04, actualizadas, con `chrony`, sin swap, SSH endurecido, cortafuegos de Oracle comprobado y Docker 29.10.1 con Compose v5.8.0.

## Problemas y cómo se resolvieron

| Problema | Resolución |
| :--- | :--- |
| El formulario de la VM no ofrecía red ni subred | Faltaba la VCN: crearla antes (guía §7.1). |
| Las reglas parecían de solo lectura | Se estaba en el resumen del asistente; se editan desde la pestaña *Seguridad* de la VCN. |
| Al limpiar las reglas se borró la de salida | Se volvió a crear. |
| *Connection timed out* al probar la llave: se usó la IP pública de casa | Usar la IP pública de la VM. |
| Más tarde, *Connection timed out* | Cambió la IP de casa; se actualizó la regla del puerto 22. |
| Sin capacidad para `VM.Standard.E2.1.Micro` | Reintentos en Cloud Shell. Sin `vm-borde` ni `vm-aux`. |
| La CLI rechazó un volumen de 47 GB | Mínimo 50 GB por CLI: no indicar el tamaño. |
| El bucle parecía detenido | La CLI reintenta sola los errores 500: `--no-retry`. |
| La guía §8.1 sustituye `iptables` por `ufw` | No se aplicó: se conserva el cortafuegos de Oracle (enlaces en la guía). |

## Pendiente (decisiones de la persona responsable, sin implementar)

- Dos VMs: nginx y cloudflared dentro de VM-VENTAS (respaldo previsto en B11, aquí por falta de capacidad) y VM-AUX aplazada (sin salto, avisos de respaldo ni sellos mientras tanto).
- Respaldos, opción B **provisional**: Cloudflare R2, copias completas como hoy y WAL comprimidos, WAL retenido 7 días; medir antes de darlo por definitivo. Requiere cambios de código, scripts, `transversal-plataforma` (PLA-18, PLA-38, D23) y documentación, en una rama que se fusione a `main` antes de etiquetar.
- La guía 08 **no** describe todavía la topología de dos VMs ni R2.
- No ejecutar `scripts/almacen.sh aplicar` hasta corregir el tamaño del archivo de cambios.

## No comprobado

- El tamaño real de las copias y del WAL.
- Nginx dentro de VM-VENTAS (en especial el acceso de un contenedor a un puerto publicado en la IP privada de la propia VM).
- El despliegue de la aplicación: no se llegó a esa etapa.
