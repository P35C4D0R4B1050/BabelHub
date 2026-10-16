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

## 3. Variantes del flujo principal

| Situación | Qué ocurre |
| :--- | :--- |
| Correo sin verificar | No se inicia el pago ni se reserva; se invita a verificar el correo y el carrito se conserva (CAR-21). |
| La fuente no es la sucursal de recojo | La consulta previa de selección informa el traslado y su plazo antes de pagar (CAR-09, SEL-20); el traslado se crea al confirmarse el pago (SEL-03) y la reserva viaja con las unidades. |
| Pago pendiente de confirmación | La reserva se extiende hasta 30 minutos y se avisa por pantalla, correo y WhatsApp con consentimiento; si el pago llega con la reserva vencida, se reembolsa (PAG-13 a PAG-15). |
| Entrega | En recojo, el físico baja al entregar (SEL-11); a domicilio, al pasar el envío a en reparto (SEL-21). |
| Pedido no recogido | A los 15 días en listo para recojo se liberan las reservas y la administración decide el reembolso (PED-15, PED-16). |
| Cancelación del cliente | Con todas las líneas impresas aún confirmadas, se anula, se libera, se cancela el traslado y se reembolsa (PED-17). |
