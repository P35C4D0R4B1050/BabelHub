# RestauracionCheck

Comprueba una restauración en el ensayo (PLA-36 y PLA-40; AC-PLA-11 y AC-PLA-40). **Solo lectura.** No es un servicio: ningún servicio lo referencia y él no referencia ningún proyecto de los servicios (lo vigila `tests/architecture`). Corre en la máquina del ensayo, después de restaurar el clúster y arrancar los dos servicios en modo `Ensayo` (`scripts/restaurar.sh` y el ensayo aislado, T29 y T31).

```bash
export RESTAURACION_CHECK_ECOMMERCE='Host=127.0.0.1;Port=5432;Database=ecommerce;Username=restauracion-check;Password=…'
export RESTAURACION_CHECK_INVENTARIO='Host=127.0.0.1;Port=5432;Database=inventario;Username=restauracion-check;Password=…'
dotnet run --project tools/RestauracionCheck -- --instante 2026-11-14T02:30:00Z \
  --salud-ecommerce http://127.0.0.1:8080/health/ready --salud-inventario http://127.0.0.1:8082/health/ready
```

(Las cadenas de conexión también pueden ir con `--bases <ecommerce> <inventario>`, pero así viajarían en los argumentos del proceso: se prefieren las variables.) `--instante` es el instante del respaldo restaurado, en UTC.

## Qué comprueba

| Comprobación | Qué exige |
| :--- | :--- |
| `salud-ecommerce`, `salud-inventario` | Las dos direcciones responden **200** (PLA-36). |
| `rol-*` | La conexión es del rol `restauracion-check` y ese rol **no es superusuario ni tiene atributos de administración, no puede escribir en ninguna tabla y no puede crear objetos**. Un rol con permisos de más hace fallar la comprobación. |
| `mismo-instante` | Las dos bases son del **mismo clúster** (mismo identificador de sistema), la misma línea de tiempo y el mismo arranque, y ninguna sigue en recuperación (PLA-36: «al mismo instante de referencia»). Una copia por base, o dos clústeres restaurados por separado, fallan. |
| `pedidos-con-reservas` | Todo pedido **no anulado** con líneas **impresas** referencia reservas que existen en el Inventario, **en cualquier estado**, por (pedido, edición) (PLA-40). No se exigen reservas a las líneas digitales ni a los pedidos anulados. |
| `reservas-con-pedido` | Toda reserva **confirmada** cuya fecha de confirmación es **10 minutos o más** anterior al instante de referencia referencia un pedido que existe (PLA-40). Una confirmada hace menos de 10 minutos todavía puede no tener su pedido. |

Cada conexión es de solo lectura (`default_transaction_read_only=on`, transacción `READ ONLY` y `REPEATABLE READ`: una vista única de los datos). El informe tiene una línea `ok …` o `FALLA …` por comprobación y **solo contadores** (`contador lineas_impresas_sin_reserva=0`…): nunca identificadores, datos personales ni cadenas de conexión; una excepción nunca repite su texto. Códigos de salida: **0** todo cumple, **1** alguna comprobación falla, **2** uso inválido.

## El rol `restauracion-check`

Existe **solo en las bases restauradas del ensayo, nunca en producción** (plan §6, D24). Se crea tras restaurar, como administrador de ese clúster aislado:

```sql
CREATE ROLE "restauracion-check" LOGIN PASSWORD '<generada en el ensayo, no se guarda>';
-- En la base ecommerce:
GRANT CONNECT ON DATABASE ecommerce TO "restauracion-check";
GRANT USAGE ON SCHEMA catalogo, pedidos TO "restauracion-check";
GRANT SELECT ON ALL TABLES IN SCHEMA catalogo, pedidos TO "restauracion-check";
-- En la base inventario:
GRANT CONNECT ON DATABASE inventario TO "restauracion-check";
GRANT USAGE ON SCHEMA seleccion TO "restauracion-check";
GRANT SELECT ON ALL TABLES IN SCHEMA seleccion TO "restauracion-check";
```

## Nombres de tablas

Por defecto siguen el modelo lógico de `docs/03-diseno-de-software/05-modelo-de-datos.md`: `pedidos.pedidos` (`id`, `estado`), `pedidos.pedido_lineas` (`pedido_id`, `edicion_id`), `catalogo.ediciones` (`id`, `formato` = `impreso` o `digital`) en el E-commerce y `seleccion.reservas` (`pedido_id`, `edicion_id`, `estado`, `fecha_confirmacion`) en el Inventario. Los planes de pedidos y de selección y reservas fijan los nombres definitivos: si difieren, se ajustan con `--tabla-pedidos`, `--tabla-lineas`, `--tabla-ediciones` y `--tabla-reservas` (solo identificadores simples, citados) y, si cambia una **columna** o un valor de estado, en `Ejecutor.cs`. Si una tabla no existe, la comprobación falla con el código de error de PostgreSQL (`42P01`) y sin datos.
