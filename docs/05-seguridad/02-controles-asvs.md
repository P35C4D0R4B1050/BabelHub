# Lista de verificación de controles (ASVS 5.0)

Traduce el **OWASP Application Security Verification Standard 5.0** a controles concretos de BabelHub. El objetivo es el **nivel 1 completo** y un **subconjunto selectivo del nivel 2** en las áreas de mayor riesgo (autenticación, sesión, control de acceso, datos personales y archivos digitales). No hay certificación: el estándar se usa como criterio objetivo (RC16).

> Los controles se identifican como `CTL-NN` (el prefijo `SEG` pertenece a la spec de seguridad transversal). Se agrupan por capítulo del ASVS; antes de planificar una spec, se enlazan los números exactos de requisito del ASVS que le aplican y se verifican contra el texto del estándar.

**Estado:** *Especificado* (hay requisito en una spec), *Decisión* (lo fija un AD), *Pendiente* (falta definir en un plan o en infraestructura). **Nivel:** L1 siempre; L2 cuando se indica.

## 1. Codificación, validación y lógica de negocio

| ID | Control | Nivel | Dónde se especifica | Verificación | Estado |
| :--- | :--- | :---: | :--- | :--- | :---: |
| **CTL-01** | Toda entrada se valida en el servidor (tipo, longitud, formato, rango). | L1 | SEG-01 | Pruebas unitarias y de integración | Especificado |
| **CTL-02** | Toda salida se codifica según su contexto; el contenido de usuario nunca se interpreta como código. | L1 | SEG-02, ASI-12 | Pruebas E2E y revisión estática | Especificado |
| **CTL-03** | Las consultas a la base de datos usan parámetros; no se arma SQL con texto del usuario. | L1 | Plan de cada servicio | Revisión estática y pruebas | Pendiente |
| **CTL-04** | Los flujos de negocio sensibles (reservas, descargas, pagos) tienen límites y son idempotentes. | L2 | RF28, SEL-09, PAG-04, BIB-15 | Pruebas de concurrencia y carga | Especificado |

## 2. Frontend web

| ID | Control | Nivel | Dónde se especifica | Verificación | Estado |
| :--- | :--- | :---: | :--- | :--- | :---: |
| **CTL-05** | Política de contenido restrictiva, transporte seguro obligatorio, no adivinar el tipo de contenido y política de referencia limitada. | L1 | WEB-18, AD26 | Prueba E2E de cabeceras y escáner | Especificado |
| **CTL-06** | Los formularios llevan protección contra falsificación entre sitios. | L1 | WEB-05 | Prueba E2E | Especificado |
| **CTL-07** | Los scripts de terceros se limitan; el de la pasarela se permite solo en la página de pago. | L2 | AD26 | Revisión de la política | Decisión |

## 3. API y servicios web

| ID | Control | Nivel | Dónde se especifica | Verificación | Estado |
| :--- | :--- | :---: | :--- | :--- | :---: |
| **CTL-08** | Cada servicio valida la firma, la vigencia y el rol de la credencial en cada solicitud. | L1 | IDE-10, AD22 | Pruebas de integración | Especificado |
| **CTL-09** | Los errores usan un formato uniforme sin trazas ni datos internos. | L1 | SEG-03 | Pruebas de contrato | Especificado |
| **CTL-10** | Las rutas incluyen la versión del contrato (`/api/<servicio>/v1/...`). | L1 | RNF28 | Prueba de arquitectura | Especificado |
| **CTL-11** | Los esquemas aceptan solo los campos esperados (sin asignación masiva). | L2 | Plan de cada servicio | Pruebas de contrato | Pendiente |

## 4. Manejo de archivos

| ID | Control | Nivel | Dónde se especifica | Verificación | Estado |
| :--- | :--- | :---: | :--- | :--- | :---: |
| **CTL-12** | Los archivos digitales viven en un almacén privado y se entregan con URL firmada de corta duración. | L1 | BIB-04, BIB-14, AD19, AD20 | Pruebas de integración | Especificado |
| **CTL-13** | Los archivos de los usuarios (si los hubiera) no se ejecutan ni se sirven desde un origen con permisos. | L1 | Fuera de alcance hoy | — | No aplica |
| **CTL-14** | Las portadas se validan por tipo y tamaño antes de guardarse. | L1 | `ecommerce-catalogo` (plan) | Pruebas | Pendiente |

## 5. Autenticación

| ID | Control | Nivel | Dónde se especifica | Verificación | Estado |
| :--- | :--- | :---: | :--- | :--- | :---: |
| **CTL-15** | Política de contraseñas: mínimo 8 caracteres con mayúscula, minúscula, número y símbolo; fuera de listas comunes. | L1 | IDE-15, RNF15 | Pruebas unitarias | Especificado |
| **CTL-16** | Las contraseñas se almacenan con función de resumen robusta y sal; nunca en claro. | L1 | IDE-09, RNF18 | Inspección y pruebas | Especificado |
| **CTL-17** | Bloqueo temporal tras 5 fallos en 15 minutos; respuestas que no revelan si la cuenta existe. | L1 | IDE-03, SEG-06 | Pruebas de integración | Especificado |
| **CTL-18** | Recuperación con código de un solo uso, vigencia corta y límite de intentos; al restablecer se cierran todas las sesiones. | L1 | IDE-17 a IDE-19 | Pruebas de integración | Especificado |
| **CTL-19** | Segundo factor (TOTP) para el administrador central. | L2 | IDE-20, AD22 | Pruebas de integración | Especificado |
| **CTL-20** | Las claves y los secretos de servicio salen del entorno, no del código. | L1 | SEG-11, PAG-12 | Detección de secretos en CI | Especificado |

## 6. Gestión de sesión y credenciales

| ID | Control | Nivel | Dónde se especifica | Verificación | Estado |
| :--- | :--- | :---: | :--- | :--- | :---: |
| **CTL-21** | La cookie de sesión es `HttpOnly`, `Secure` y `SameSite`; la credencial no llega al navegador. | L1 | WEB-04, WEB-19 | Prueba E2E | Especificado |
| **CTL-22** | La credencial de acceso dura 15 minutos y la renovación 7 días, revocable al cerrar sesión o desactivar al usuario. | L1 | IDE-02, IDE-08, IDE-14, AD22 | Pruebas de integración | Decisión |
| **CTL-23** | Cambiar el rol invalida las credenciales vigentes en la siguiente renovación. | L2 | `identidad-seguridad` §6 | Prueba de integración | Especificado |

## 7. Autorización

| ID | Control | Nivel | Dónde se especifica | Verificación | Estado |
| :--- | :--- | :---: | :--- | :--- | :---: |
| **CTL-24** | El alcance por sucursal se comprueba en la Aplicación de cada caso de uso, no solo en el controlador. | L1 | RNF05, AUD-05 | Pruebas unitarias y de integración | Especificado |
| **CTL-25** | Un recurso ajeno responde igual que uno inexistente cuando revelar su existencia sería una fuga. | L2 | Plan de cada servicio | Pruebas de integración | Pendiente |
| **CTL-26** | Las funciones administrativas exigen rol y segundo factor. | L1 | IDE-20, WEB-02 | Pruebas de integración | Especificado |

## 8. Criptografía y comunicación segura

| ID | Control | Nivel | Dónde se especifica | Verificación | Estado |
| :--- | :--- | :---: | :--- | :--- | :---: |
| **CTL-27** | TLS 1.2 o superior hacia el exterior; HSTS. | L1 | RNF06, RNF18, AD13 | Escáner de configuración | Decisión |
| **CTL-28** | Respaldos y volúmenes cifrados. | L2 | RNF18, AD27 | Inspección | Pendiente |
| **CTL-29** | La firma de las credenciales usa una clave que rota y se guarda fuera del repositorio. | L2 | AD22 | Revisión | Pendiente |

## 9. Configuración y protección de datos

| ID | Control | Nivel | Dónde se especifica | Verificación | Estado |
| :--- | :--- | :---: | :--- | :--- | :---: |
| **CTL-30** | Las bases de datos no tienen puertos expuestos a Internet; los paneles de n8n y Evolution API tampoco. | L1 | RNF06, AM22 | Escaneo de puertos | Decisión |
| **CTL-31** | Contenedores sin privilegios, con límites de memoria y `healthcheck`. | L2 | RNF19, convenciones | Revisión de Compose | Pendiente |
| **CTL-32** | No se guardan datos de tarjeta ni se registran datos sensibles. | L1 | PAG-02, SEG-10, NOT-07 | Revisión y pruebas | Especificado |
| **CTL-33** | Los datos personales tienen inventario, base legal, retención y borrado o anonimización. | L2 | RNF23, AUD-11, IDE-22 | Inspección documental | Especificado |

## 10. Código seguro, registro y manejo de errores

| ID | Control | Nivel | Dónde se especifica | Verificación | Estado |
| :--- | :--- | :---: | :--- | :--- | :---: |
| **CTL-34** | Las dependencias tienen versión fija y se escanean; el repositorio genera un inventario de componentes (SBOM). | L1 | RNF19 | CI | Pendiente |
| **CTL-35** | Se registran ingresos fallidos, accesos denegados, límites excedidos y cambios de rol, sin datos sensibles. | L1 | AUD-10, SEG-08, SEG-10 | Pruebas | Especificado |
| **CTL-36** | Ante un error inesperado el sistema niega, no deja efectos parciales y no filtra detalles. | L1 | SEG-04, AD25 | Pruebas | Especificado |
| **CTL-37** | Hay alertas ante umbrales de accesos denegados o ingresos fallidos. | L2 | SEG-09 | Pruebas | Especificado |
| **CTL-38** | Las integraciones validan firma o secreto, formato de respuesta y tiempos de espera. | L1 | SEG-12 a SEG-14 | Pruebas de integración | Especificado |

## 11. Resumen

| Estado | Cantidad |
| :--- | :---: |
| Especificado | 25 |
| Decisión | 4 |
| Pendiente | 8 |
| No aplica | 1 |

Los controles *Pendiente* se resuelven en los planes de las specs afectadas o en las tareas de infraestructura y CI. Ninguno bloquea la aprobación de una spec; sí bloquea el paso de esa spec a **Done**.
