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
I->>I: Selecciona la fuente de despacho
I-->>E: Fuente asignada
E->>P: Cobro con clave de idempotencia
P-->>E: Pago aprobado
E->>I: Confirma la reserva
opt La fuente no es la sucursal elegida
I->>I: Genera solicitud de traslado
end
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
