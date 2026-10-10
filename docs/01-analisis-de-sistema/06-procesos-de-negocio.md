# Procesos de negocio

Resumen de los procesos principales de punta a punta, con los actores y las specs que los implementan. El detalle de comportamiento vive en cada spec; aquí se ve el recorrido completo.

## 1. Compra de libros impresos

```mermaid
flowchart LR
A["Cliente arma el carrito"] --> B["Inicia el checkout"]
B --> C{"¿Hay stock vendible en la red?"}
C -- No --> X["Informa falta de stock"]
C -- Sí --> D["Elige recojo o domicilio"]
D --> E["Inventario crea reservas de 15 min y elige la fuente"]
E --> F["Cliente paga con Yape o tarjeta"]
F --> G{"¿Pago aprobado?"}
G -- No --> H["Se liberan las reservas"]
G -- Sí --> I["Se confirman las reservas y se crea el pedido"]
I --> J{"¿La fuente es la sucursal de recojo?"}
J -- No --> K["Traslado entre sucursales"]
J -- Sí --> L["Preparación"]
K --> L
L --> M["Entrega o despacho"]
M --> N["Se descuenta el stock físico"]
```

**Actores:** cliente, Inventario, pasarela de pago, encargado de sucursal, logística. **Specs:** `ecommerce-carrito-checkout`, `inventario-seleccion-fuente`, `ecommerce-pagos`, `ecommerce-pedidos`, `inventario-traslados`, `ecommerce-envios`. **Puntos de control:** límite de una reserva activa por cliente y 5 por hora (RF28); una falla del aviso por WhatsApp nunca interrumpe la compra.

## 2. Compra y descarga de un libro digital

```mermaid
flowchart LR
A["Cliente agrega la edición digital"] --> B["Acepta política de reembolso digital"]
B --> C["Paga"]
C --> D{"¿Pago aprobado?"}
D -- No --> X["No hay acceso"]
D -- Sí --> E["Se concede el acceso en Mi biblioteca"]
E --> F["Pide descargar"]
F --> G{"¿Quedan descargas y es el titular?"}
G -- No --> H["Se rechaza y se registra"]
G -- Sí --> I["Enlace firmado de 5 min"]
I --> J["El navegador descarga del almacén"]
```

**Actores:** cliente, pasarela de pago, almacén de archivos. **Specs:** `ecommerce-carrito-checkout`, `ecommerce-pagos`, `ecommerce-biblioteca-digital`. **Puntos de control:** nunca hay reserva ni consulta al Inventario; el archivo no pasa por la Web; máximo 5 descargas por acceso y 10 solicitudes de enlace por hora.

## 3. Reposición mediante traslado

| Paso | Quién | Resultado |
| :---: | :--- | :--- |
| 1 | Encargado o administración | Solicita el traslado (estado solicitado). |
| 2 | Origen o administración | Aprueba o rechaza. |
| 3 | Origen | Despacha: el stock físico del origen baja. |
| 4 | Destino | Recibe: el stock físico del destino sube. |
| 5 | Sistema | Notifica a origen y destino en cada cambio de estado y audita la aprobación. |

**Spec:** `inventario-traslados`.

## 4. Ajuste de inventario

El encargado registra una merma, un producto dañado o una corrección de conteo con motivo; la merma y el daño nunca descuentan más que el stock vendible, y la corrección se expresa como el conteo real, que no puede quedar por debajo de lo reservado. El ajuste y su auditoría ocurren en la misma transacción. **Spec:** `inventario-ajustes`.

## 5. Devolución y reembolso

```mermaid
flowchart LR
A["Cliente solicita la devolución"] --> B["Se evalúa la solicitud"]
B --> C{"¿Aprobada?"}
C -- No --> X["Se rechaza con motivo"]
C -- Sí --> D["Reembolso por la pasarela"]
D --> E{"¿Impreso o digital?"}
E -- Impreso --> F["Reingreso de stock en la sucursal receptora"]
E -- Digital --> G["Se revoca el acceso; no hay stock"]
```

**Spec:** `ecommerce-devoluciones` (quién aprueba un impreso está pendiente de decisión). El libro digital ya descargado solo se reembolsa si el archivo es defectuoso.

## 6. Libro de Reclamaciones

Cualquier consumidor, con o sin sesión, registra una hoja; recibe una constancia imprimible y un número único; la administración la responde y avisa al consumidor; las hojas se conservan al menos dos años. **Spec:** `ecommerce-libro-reclamaciones`.

## 7. Registro y recuperación de la cuenta

| Proceso | Pasos |
| :--- | :--- |
| **Registro** | La persona ingresa nombre, correo y contraseña; recibe un código por correo y verifica el correo. El número de WhatsApp y el consentimiento para recibir avisos son opcionales y se registran aparte. |
| **Recuperación** | La persona pide recuperar el acceso; recibe un código de un solo uso en su correo verificado; define una nueva contraseña y se revocan sus sesiones anteriores. El personal la recupera con el administrador central. La respuesta es la misma exista o no la cuenta. |
| **Cancelación** | El cliente cancela su cuenta; se desactiva y se suprimen o anonimizan sus datos personales, salvo lo que una obligación legal exija. |

**Spec:** `identidad-seguridad`.

## 8. Asistente de compra

El cliente consulta; el asistente obtiene datos de los servicios, redacta la respuesta como texto y enlaza a las pantallas. Nunca ejecuta acciones ni consulta las bases de datos. Si el proveedor del modelo no responde, la compra continúa sin él. **Spec:** `asistente-compra-ia`.
