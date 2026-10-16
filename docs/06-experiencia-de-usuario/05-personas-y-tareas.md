# Personas y tareas

Perfiles de uso construidos a partir de los actores y las necesidades del negocio. Son **hipótesis de trabajo** (no resultados de una investigación formal) que se validan con las pruebas de usabilidad. Orientan decisiones de diseño y definen las tareas que se miden.

## 1. Personas

| Persona | Contexto | Necesidad principal | Dispositivo y conexión | Riesgo de UX |
| :--- | :--- | :--- | :--- | :--- |
| **Rosa, lectora de provincia** (cliente) | Ayacucho; compra libros que no encuentra en su ciudad. | Saber qué hay realmente y cuándo llega (NN01, NN03). | Celular, datos móviles variables | Perder la compra por una conexión lenta o por desconfiar del pago. |
| **Mateo, estudiante** (cliente) | Universitario; compra textos impresos y digitales. | Precio, disponibilidad y descarga inmediata (NN13). | Celular y laptop | Abandonar si el checkout tiene demasiados pasos. |
| **Carmen, encargada de sucursal** | Atiende mostrador y prepara pedidos de recojo. | Ver y ajustar su stock exacto sin errores (NN06). | PC en tienda, a veces tablet | Errores de conteo; interrupciones frecuentes. |
| **Julio, personal de logística** | Gestiona traslados y envíos. | Seguir traslados y generar guías (NN07, NN11). | PC | Perder trazabilidad entre sucursales. |
| **Elena, administradora central** | Gestiona catálogo, usuarios y auditoría. | Control y visibilidad nacional (NN08, NN09). | PC | Operaciones sensibles sin confirmación clara. |
| **Visitante** | Llega al catálogo sin cuenta. | Explorar y decidir si registrarse. | Celular | Barreras de registro antes de ver valor. |
| **Persona con discapacidad visual o motriz** | Usa lector de pantalla, teclado o ampliación. | Completar una compra sin barreras (NN16). | Cualquiera, con tecnología de apoyo | Controles inalcanzables, foco perdido, contraste insuficiente. |

## 2. Tareas principales y metas

| ID | Tarea | Persona | Meta de éxito | Métrica |
| :--- | :--- | :--- | :--- | :--- |
| **T01** | Buscar un libro y ver su disponibilidad por sucursal. | Rosa | ≥ 90 % sin ayuda | Éxito de tarea, tiempo |
| **T02** | Comprar un impreso con recojo en sucursal y pagar con Yape. | Rosa | ≥ 90 % sin ayuda; ≤ 5 pasos | Éxito, pasos, errores |
| **T03** | Comprar y descargar un libro digital. | Mateo | ≥ 90 % sin ayuda; ≤ 4 pasos | Éxito, pasos |
| **T04** | Recuperar el acceso a la cuenta. | Mateo | ≥ 90 % sin ayuda | Éxito, errores |
| **T05** | Registrar una merma y consultar el stock de su sucursal. | Carmen | ≥ 90 % sin ayuda | Éxito, errores |
| **T06** | Marcar un pedido de recojo como listo y entregado. | Carmen | ≥ 90 % sin ayuda | Éxito, tiempo |
| **T07** | Solicitar y seguir un traslado. | Julio | ≥ 90 % sin ayuda | Éxito |
| **T08** | Publicar una edición digital con su licencia. | Elena | ≥ 90 % sin ayuda | Éxito, errores |
| **T09** | Registrar una hoja en el Libro de Reclamaciones. | Visitante | ≥ 90 % sin ayuda | Éxito |
| **T10** | Completar T02 con solo teclado y lector de pantalla. | Persona con discapacidad | Sin bloqueos críticos | Lista de verificación |
| **T11** | Cancelar un pedido de impresos antes de su preparación. | Rosa | ≥ 90 % sin ayuda | Éxito, errores |
| **T12** | Configurar el segundo factor al primer ingreso y entrar con un código. | Carmen | ≥ 90 % sin ayuda | Éxito, errores |
| **T13** | Registrar el plazo de una ruta entre dos sucursales. | Elena | ≥ 90 % sin ayuda | Éxito |

Las metas globales vienen de RNF27: ≥ 90 % de éxito sin ayuda, SUS ≥ 68 y menos de un error por tarea en promedio.

## 3. Escenarios de contexto

| Escenario | Consecuencia de diseño |
| :--- | :--- |
| Conexión móvil que se corta durante el pago | El estado de pago queda pendiente de confirmación y se consulta después; no se cobra de nuevo (PAG-07). La reserva se extiende hasta 30 minutos y se avisa por pantalla, correo y, con consentimiento, WhatsApp (PAG-13, PAG-14). |
| Sesión inactiva en un equipo compartido de sucursal | La sesión del personal vence a los 30 minutos sin uso y a las 8 horas como máximo (WEB-27); lo escrito se conserva (WEB-25). |
| Mostrador con interrupciones | Los formularios de la Sucursal conservan lo escrito y confirman las acciones irreversibles. |
| Campaña con muchas visitas | Mensajes claros ante límites de uso y ante la reserva que expira. |
| Cliente sin WhatsApp | Se puede comprar y seguir el pedido desde la cuenta. |
| Uso con una sola mano | Objetivos táctiles de al menos 24×24 píxeles (WCAG 2.5.8) y acciones principales al alcance del pulgar. |
