# Escenarios y flujos

```mermaid
sequenceDiagram
autonumber
actor C as Cliente
participant E as E-commerce
participant I as Inventario
participant P as Culqi

C->>E: Confirma el pedido
E->>I: Consulta disponibilidad real
I-->>E: Stock vendible por sucursal
alt Sin stock vendible
E-->>C: No disponible actualmente
else Hay stock
E->>I: Reserva temporal con expiración
I->>I: Aparta cada línea en la sucursal elegida
I-->>E: Reserva por sucursal
E->>P: Cobro con clave de idempotencia
P-->>E: Pago aprobado
E->>I: Confirma la reserva
E-->>C: Pedido confirmado y notificado
end
```

## 2. Flujo secundario: compra y descarga de un libro digital

El libro digital no pasa por el Inventario: no hay stock ni fuente que seleccionar.

```mermaid
sequenceDiagram
autonumber
actor C as Cliente
participant W as Web
participant E as E-commerce
participant P as Culqi
participant A as Almacén de archivos

C->>W: Compra un libro digital
W->>E: Crea el pedido, sin entrega
E->>E: Verifica licencia vigente y territorio
E->>P: Cobro con clave de idempotencia
P-->>E: Pago aprobado
E->>E: Concede acceso a la biblioteca
E-->>W: Pedido confirmado
C->>W: Pulsa Descargar
W->>E: Solicita la descarga
E->>E: Verifica titular y límite de descargas
E->>A: Firma una URL de pocos minutos
E-->>W: URL firmada
W-->>C: Redirige a la URL
C->>A: Descarga el archivo
```

En un pedido mixto, el cobro es único: las líneas impresas siguen el flujo principal y las digitales conceden acceso al aprobarse el pago.

## 3. Variantes del flujo principal

| Situación | Qué ocurre |
| :--- | :--- |
| Correo sin verificar | No se inicia el pago ni se reserva; se invita a verificar el correo y el carrito se conserva (CAR-21). |
| La sucursal elegida no tiene stock | La línea no se reasigna: el cliente vuelve a la revisión del carrito, que muestra la sucursal sin stock (CAR-27, SEL-43). No se generan traslados por una compra (venta por sucursal, 2026-11-27). |
| Pago pendiente de confirmación | La reserva se extiende hasta 30 minutos y se avisa por pantalla, correo y WhatsApp con consentimiento; si el pago llega con la reserva vencida, se reembolsa (PAG-13 a PAG-15). |
| Entrega | En recojo, el físico baja al entregar (SEL-11); a domicilio, al pasar el envío a en reparto (SEL-21). |
| Pedido no recogido | A los 15 días en listo para recojo se liberan las reservas y la administración decide el reembolso (PED-15, PED-16). |
| Cancelación del cliente | Con todas las líneas impresas aún confirmadas, se anula, se libera y se reembolsa, incluidos los envíos (PED-17). |

## 4. Flujo de apoyo: ingreso de mercadería

La administración central recibe la importación en Lima y registra cada lote desde el Catálogo. Solo entra a la fuente de recepción; las demás sucursales reciben stock por traslado o devolución.

```mermaid
sequenceDiagram
autonumber
actor A as Administración central
participant E as E-commerce (Catálogo)
participant I as Inventario (Ajustes)

A->>E: Registra un ingreso de 30 unidades con la guía
E->>I: Pide el ingreso en la fuente de recepción
I->>I: Suma al físico y audita en la misma transacción
I-->>E: Ingreso registrado
E-->>A: Lote en el historial y el título deja de figurar como agotado
```

Si el Inventario no responde al dar de alta una edición con stock inicial, la edición queda creada con stock 0 y se indica que el ingreso debe registrarse después (CAT-20). Un título agotado no se da de baja (CAT-21).

## 5. Escenarios de degradación

| Situación | Qué ocurre |
| :--- | :--- |
| Cae el contenedor de Inventario | El catálogo sigue mostrando la última disponibilidad y el checkout informa que no puede validar el stock, sin cobrar (CAR-06, CAT-12). |
| Cae la VM de ventas | No hay tienda, avisos ni acceso público a la gestión. **Desde el 2026-11-25 (dos VMs, DD-100 a DD-102):** la entrada vive en esa VM; la réplica sigue sirviendo claves y revocaciones al Inventario. |
| Cae la VM del Inventario | Cae todo el sistema (la base está ahí); se restaura desde el respaldo y el archivado de WAL. |

El detalle y las mitigaciones están en [despliegue](05-despliegue.md) §6.
