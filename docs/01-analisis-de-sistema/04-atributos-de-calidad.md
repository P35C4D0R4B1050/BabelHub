# Atributos de calidad

Los atributos de calidad expresan **cómo** debe comportarse el sistema y se describen con escenarios (estímulo → respuesta → medida). Provienen de los requisitos no funcionales de [requisitos](02-requisitos.md) y se priorizan según el problema central: *mostrar siempre stock real*.

## 1. Atributos y escenarios

| ID | Atributo | Prioridad | Escenario de calidad | Medida | RNF | Justificación |
| :--- | :--- | :---: | :--- | :--- | :--- | :--- |
| **AC01** | **Consistencia de inventario** | Crítica | Dos clientes intentan comprar la última unidad de un título en la misma sucursal casi al mismo tiempo. El sistema concede una reserva y rechaza la otra, o la asigna a otra sucursal con stock. | 0 casos de stock vendible negativo en pruebas de concurrencia. | RNF01 | Es el problema que da origen al proyecto. Vender stock inexistente destruye la confianza del cliente. |
| **AC02** | **Rendimiento** | Alta | Durante una feria del libro, decenas de clientes consultan la disponibilidad y agregan libros al carrito a la vez. Las consultas se atienden desde caché o base de datos sin degradarse. | p95 < 500 ms (disponibilidad) con 50 usuarios concurrentes; checkout p95 < 3 s. | RNF02 | La consulta de disponibilidad está en el camino crítico de la compra y cada VM tiene solo 1 OCPU. |
| **AC03** | **Disponibilidad** | Alta | Un contenedor falla. Se reinicia solo; si Inventario no responde, el catálogo sigue consultable y el checkout informa que no puede validar stock en vez de cobrar a ciegas. Si el envío de WhatsApp falla, la compra se completa y el aviso se reintenta. Los libros digitales se siguen vendiendo y descargando, porque no dependen del Inventario. | ≥ 99 % mensual; recuperación < 2 minutos. | RNF03 | Las compras ocurren a cualquier hora, pero no hay SLA contratado; se compensa con reinicio automático y degradación controlada. |
| **AC04** | **Escalabilidad** | Media | La librería abre nuevas sucursales hasta llegar a 20. Cada una se registra como una nueva fuente. | 0 líneas de código modificadas; rendimiento dentro de AC02. | RNF04 | El patrón MSI está pensado para 1–20 fuentes; crecer debe ser configuración, no desarrollo. |
| **AC05** | **Seguridad** | Alta | Un encargado intenta ver el inventario de otra sucursal, o un atacante intenta llegar a la base de datos desde Internet. En ambos casos el acceso se rechaza y se registra. | 100 % de accesos fuera de alcance rechazados; 0 puertos de BD expuestos; 0 datos de tarjeta almacenados. | RNF05–RNF08 | Se manejan datos personales y pagos, y el aislamiento por sucursal es un requisito del negocio. |
| **AC06** | **Mantenibilidad y testabilidad** | Media | El equipo cambia la regla de selección de fuente (de cercanía a menor costo de traslado) o cambia la pasarela de pago. El cambio se hace en una sola capa y se valida con pruebas unitarias. | Cambio localizado; contrato REST sin modificaciones. | RNF09, RNF10 | Con un equipo pequeño y desarrollo guiado por especificaciones, el costo del cambio debe ser bajo. |
| **AC07** | **Portabilidad y eficiencia de recursos** | Media | El sistema se despliega en una VM ARM64 de Oracle y en una máquina x86_64 de desarrollo con la misma configuración, usando menos del 70 % de la memoria. | Despliegue con un comando por VM; imágenes multi-arch; memoria < 70 %. | RNF11, RNF12 | Los recursos gratuitos son limitados (2 OCPU y 12 GB en ARM) y mezclan arquitecturas. |
| **AC08** | **Usabilidad** | Media | Un cliente en un celular busca un libro y completa una compra con recojo en sucursal. | Máximo 5 pasos; estados claros en cada pantalla. | RNF13 | La demanda en provincias llega mayormente desde dispositivos móviles y conexiones variables. |
| **AC09** | **Protección de contenido digital** | Alta | Un usuario intenta descargar un libro digital que no compró, o reutiliza un enlace que le pasó otra persona o que ya venció. El sistema lo rechaza y lo registra. Un titular legítimo descarga su libro sin fricción hasta el límite de descargas. | 0 descargas no autorizadas; enlaces de 5 minutos como máximo; límite de descargas por compra. | RNF14 | La librería vende contenido con licencia de terceros: una fuga de archivos puede generar responsabilidad por derechos de autor y poner en riesgo los contratos. |

## 2. Priorización

Los atributos se ordenan por su impacto en el problema central y por el costo de equivocarse:

1. **AC01 Consistencia** (crítica): condiciona el diseño del servicio de Inventario y sus reservas.
2. **AC05 Seguridad**, **AC09 Protección de contenido digital**, **AC02 Rendimiento** y **AC03 Disponibilidad** (alta): determinan la estructura de despliegue y la separación de la base de datos.
3. **AC04, AC06, AC07 y AC08** (media): se atienden con decisiones de bajo costo (fuentes como datos, Clean Architecture, imágenes multi-arch, interfaz responsiva).

**Tensión principal:** AC03 (disponibilidad) y AC07 (eficiencia) chocan con la realidad de recursos gratuitos sin alta disponibilidad real. Se acepta un objetivo de ≥ 99 % sin redundancia, apoyado en reinicio automático y respaldos, y se documenta como riesgo en [drivers arquitectónicos](../02-arquitectura-de-software/01-drivers-arquitectonicos.md).
