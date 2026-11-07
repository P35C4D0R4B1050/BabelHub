# Atributos de calidad

Los atributos de calidad expresan **cómo** debe comportarse el sistema y se describen con escenarios (estímulo → respuesta → medida). Provienen de los requisitos no funcionales de [requisitos](02-requisitos.md) y se priorizan según el problema central: *mostrar siempre stock real*.

Cada uno se desarrolla con escenarios completos (fuente, estímulo, entorno, artefacto, respuesta y medida) en [escenarios de calidad](08-escenarios-de-calidad.md). Son **seis**. Cada uno agrupa varios requisitos no funcionales (RNF) con su medida, y cada medida se comprueba con una prueba concreta (ver §3). La versión anterior tenía catorce; se redujo para que cada atributo sea medible y verificable sin repetir pruebas (ver §5).

## 1. Atributos y escenarios

| ID | Atributo | Prioridad | Escenario de calidad | Medida | RNF | Justificación |
| :--- | :--- | :---: | :--- | :--- | :--- | :--- |
| **AC01** | **Consistencia del inventario** | Crítica | Dos clientes intentan comprar la última unidad de un título en la misma sucursal casi al mismo tiempo. El sistema concede una reserva y rechaza la otra, o la asigna a otra sucursal con stock. | 0 casos de stock vendible negativo en pruebas de concurrencia (100 reservas simultáneas por 1 unidad: solo una prospera). | RNF01 | Es el problema que da origen al proyecto. Vender stock inexistente destruye la confianza del cliente. |
| **AC02** | **Rendimiento y eficiencia** | Alta | Durante una feria del libro, decenas de clientes consultan la disponibilidad y agregan libros al carrito a la vez, en celulares con conexión variable, sobre VMs de 1 OCPU. | p95 < 500 ms (disponibilidad) con 50 usuarios concurrentes; checkout p95 < 3 s; LCP ≤ 2,5 s, INP ≤ 200 ms, CLS ≤ 0,1; memoria y CPU sostenidas < 70 % por VM. | RNF02, RNF12, RNF26 | La disponibilidad está en el camino crítico de la compra y los recursos gratuitos son limitados. |
| **AC03** | **Disponibilidad y recuperabilidad** | Alta | Un contenedor o una VM falla. Se reinicia solo; si Inventario no responde, el catálogo sigue consultable y el checkout informa que no puede validar stock en vez de cobrar a ciegas; si falla WhatsApp, la compra se completa. Si la VM de datos se pierde, se restaura desde el último respaldo en un entorno limpio. | ≥ 99 % mensual; recuperación automática < 2 minutos; RPO ≤ 24 h y RTO ≤ 4 h con restauración probada. | RNF03, RNF24 | Las compras ocurren a cualquier hora y no hay SLA ni alta disponibilidad real: se compensa con degradación controlada y recuperación probada. |
| **AC04** | **Seguridad y privacidad** | Alta | Un encargado intenta ver el inventario de otra sucursal; un usuario intenta descargar un libro digital que no compró; un programa intenta crear cuentas en masa, adivinar contraseñas o consultar sin límite al asistente; un cliente pide acceder a sus datos y cancelar su cuenta; un atacante intenta llegar a la base de datos desde Internet. En todos los casos el acceso se rechaza y se registra. | 100 % de accesos fuera de alcance rechazados y registrados; 0 descargas no autorizadas (enlaces de 5 minutos como máximo); límites de uso aplicados sin dejar sin servicio a clientes legítimos; 0 puertos de BD expuestos; 0 datos de tarjeta almacenados; solicitudes sobre datos personales atendidas. | RNF05–RNF08, RNF14–RNF23 | Se manejan datos personales, pagos y contenido con licencia de terceros; los flujos abiertos (reservas, descargas, asistente) son blanco de abuso. |
| **AC05** | **Mantenibilidad y evolucionabilidad** | Media | El equipo cambia la regla de selección de fuente o la pasarela de pago; la librería abre una sucursal nueva (hasta 20). El cambio se hace en un solo módulo y se valida con pruebas; la sucursal se registra como dato; el sistema se despliega igual en ARM64 y x86_64. | Cambio localizado en un módulo sin tocar el contrato entre sistemas; 0 líneas de código para una sucursal nueva; reglas de dependencia verificadas automáticamente; cobertura ≥ 85 % en Dominio y Aplicación; imágenes multi-arch. | RNF04, RNF09, RNF10, RNF11, RNF28 | Con un equipo pequeño y desarrollo guiado por especificaciones, el costo del cambio debe ser bajo y crecer debe ser configuración, no desarrollo. |
| **AC06** | **Usabilidad y accesibilidad** | Media | Una persona, en un celular o con solo teclado y lector de pantalla, busca un libro y completa una compra con recojo en sucursal, en un navegador reciente (Chrome, Firefox, Safari o Edge). | Máximo 5 pasos; estados claros en cada pantalla; ≥ 90 % completa la compra sin ayuda y SUS ≥ 68; 0 incumplimientos WCAG 2.2 AA críticos; recorrido exitoso en cada navegador soportado. | RNF13, RNF25, RNF27 | La demanda en provincias llega desde dispositivos y conexiones variables y la accesibilidad amplía el público (NN16). |

## 2. Clasificación por ISO/IEC 25010:2023

Los atributos se agrupan por las características del modelo de calidad del producto de ISO/IEC 25010:2023 (el nombre exacto de cada característica debe confirmarse contra el estándar).

| Característica | Atributos |
| :--- | :--- |
| Adecuación funcional | AC01 |
| Eficiencia de desempeño | AC02 |
| Fiabilidad | AC03 |
| Seguridad | AC04 |
| Mantenibilidad y flexibilidad | AC05 |
| Capacidad de interacción y compatibilidad | AC06 |
| Seguridad funcional (*safety*) | No aplica |

## 3. Cómo se comprueba cada atributo

| Atributo | Prueba que lo verifica | Herramienta | Cuándo |
| :--- | :--- | :--- | :--- |
| **AC01** | Solicitudes simultáneas sobre la última unidad y control de que el `CHECK` de la base impide el vendible negativo. | xUnit con Testcontainers | Cada cambio en Inventario |
| **AC02** | Carga sobre disponibilidad y checkout con 50 usuarios; Lighthouse sobre inicio, catálogo y detalle; medición de memoria y CPU por VM. | k6, Lighthouse, `docker stats` | Antes de cada despliegue mayor |
| **AC03** | Apagar el contenedor de Inventario y comprobar la degradación; restaurar un respaldo en un entorno limpio y medir el tiempo. | Prueba de caos manual y guion de restauración | Antes de cada despliegue mayor |
| **AC04** | Acceso fuera de alcance (403), descarga ajena, límites de uso, inyección y cabeceras; revisión de dependencias e imágenes. | xUnit, ZAP baseline, Trivy, CodeQL | Cada cambio y antes de cada despliegue mayor |
| **AC05** | Pruebas de arquitectura sobre referencias y módulos; cobertura; creación de una sucursal sin cambios de código. | `tests/architecture`, cobertura de `dotnet test` | Cada cambio |
| **AC06** | Recorrido de compra E2E en los navegadores soportados; revisión automática de accesibilidad y lista manual de teclado y lector de pantalla; prueba con personas. | Playwright con axe, SUS | Cada pantalla nueva y cada iteración |

## 4. Priorización

Los atributos se ordenan por su impacto en el problema central y por el costo de equivocarse:

1. **AC01 Consistencia** (crítica): condiciona el diseño del sistema de Inventario y sus reservas.
2. **AC04 Seguridad y privacidad**, **AC02 Rendimiento y eficiencia** y **AC03 Disponibilidad y recuperabilidad** (alta): determinan la estructura de despliegue y la separación de la base de datos.
3. **AC05 Mantenibilidad y evolucionabilidad** y **AC06 Usabilidad y accesibilidad** (media): se atienden con decisiones de bajo costo (fuentes como datos, módulos con límites verificados, interfaz responsiva).

**Tensión principal:** AC03 (disponibilidad) y AC02 (eficiencia) chocan con la realidad de recursos gratuitos sin alta disponibilidad real. Se acepta un objetivo de ≥ 99 % sin redundancia, apoyado en reinicio automático, degradación controlada y respaldos, y se documenta como riesgo en [drivers arquitectónicos](../02-arquitectura-de-software/01-drivers-arquitectonicos.md).

## 5. Equivalencias con la versión anterior (catorce atributos)

| Antes | Ahora |
| :--- | :--- |
| AC01 Consistencia | **AC01** |
| AC02 Rendimiento; AC07 Eficiencia de recursos | **AC02** |
| AC03 Disponibilidad; AC12 Recuperabilidad | **AC03** |
| AC05 Seguridad; AC09 Protección de contenido digital; AC11 Privacidad; AC13 Resistencia al abuso | **AC04** |
| AC04 Escalabilidad; AC06 Mantenibilidad; AC07 Portabilidad | **AC05** |
| AC08 Usabilidad; AC10 Accesibilidad; AC14 Compatibilidad de navegadores | **AC06** |

Los requisitos no funcionales (RNF) no cambian: solo cambia su agrupación. Las specs y los documentos citan los identificadores nuevos.
