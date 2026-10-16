# 2026-10-16 — Análisis inicial y cierre de brechas de las specs y docs

> Nota: `git` no está disponible en la terminal de esta sesión, por lo que no se pudo revisar `git log` ni `git status`, ni crear el commit. Lo registrado sale de los archivos del repositorio y de la salida de `SpecCheck`. Los cambios quedan **sin commit**.

## Qué se hizo

- Se recuperó el contexto leyendo `progress/`, `specs/` y `docs/`, y se auditaron las 19 specs y los documentos de `docs/` (informe en `progress/R3-analisis-inicial.md`). `prototype/ui-v2/` se trató solo como referencia de UI/UX.
- Se resolvieron los hallazgos con `spec-clarify` y preguntas cortas, en seis bloques (cada uno validado con `SpecCheck`, que termina sin errores: 19 specs, 24 avisos).
  1. **Núcleo de stock:** reserva y traslado (la reserva se consume al despachar y se recrea en el destino; el traslado de un pedido pagado no se rechaza y se cancela al anular el pedido), baja de físico a domicilio al pasar a en reparto, pedido no recogido a los 15 días, consulta previa de selección y tabla de rutas. Specs: `inventario-fuentes` (FUE-12 a 14), `inventario-seleccion-fuente` (SEL-02, 03, 07, 11, 20, 21), `inventario-traslados` (TRA-03, 05 a 07, 14 a 17), `ecommerce-pedidos` (PED-15, 16), `ecommerce-envios` (ENV-11, 12), `ecommerce-carrito-checkout` (CAR-09).
  2. **Legal y reclamos:** respuesta a reclamos por correo, campos legales de la hoja, plazo informado de 15 días, límite de 3 hojas por hora por origen y descarte sin borrado, plazos ARCO de 20 y 10 días hábiles. Specs: `ecommerce-libro-reclamaciones` (REC-03, 05, 07, 11 a 13), `identidad-seguridad` (IDE-12, 32), `transversal-notificaciones`.
  3. **Auditoría y seguridad transversal:** consulta unificada mezclada por la Web, alertas por servicio y por correo, acceso denegado como evento único y más eventos auditados. Specs: `transversal-auditoria` (AUD-02, 05, 08 a 10, 12 a 18), `transversal-seguridad` (SEG-04, 09).
  4. **Identidad y asistente:** credencial de lectura del cliente para el asistente, correo verificado para pagar, 3 cuentas nuevas por hora por origen, sesión web de 30 minutos de inactividad con tope absoluto, textos legales versionados e IP como dato personal. Specs: `asistente-compra-ia` (ASI-19), `identidad-seguridad` (IDE-29, 33, 34), `transversal-seguridad` (SEG-24, 25), `ecommerce-carrito-checkout` (CAR-21, 22), `web-base` (WEB-27 a 29).
  5. **Docs y redacción:** modelo C4 niveles 1 a 3 (`docs/04-modelo-c4/`), Ubuntu como sistema operativo, validación de archivos digitales (CAT-16), contraseña de 10 caracteres sin composición (IDE-15), clave de firma con rotación (IDE-35, 36), BIB-07 dividido (BIB-18) e identificadores y criterios reordenados en las 19 specs.
  6. **Pedidos, pagos y envío:** cancelación por el cliente (PED-17, 18), reembolso del impreso tras la recepción (DEV-08, 10), pago pendiente con reserva extendida a 30 minutos y aviso por todos los medios (SEL-22, PAG-13 a 15) y costo de envío fijo (CAR-23).
  7. **Segundo factor y legales menores:** segundo factor obligatorio para el personal y opcional para el cliente (IDE-37, 38), mayores de 18 años (IDE-39), meta de 48 horas para incidentes, cookies necesarias y plan de salida de WhatsApp.
- Se actualizaron los documentos asociados: marco legal, requisitos (RNF15, RC18), decisiones (AD16, AD22, AD30), amenazas, controles ASVS, autenticación, privacidad, API, operación segura, modelo de dominio, estados, modelo de datos, actores y glosario. `specs/TRACEABILITY.md` se regeneró.

- **Barrido de coherencia de `docs/` y cierre de preguntas (misma fecha, segunda ronda).** Se alinearon con las specs el glosario, los mensajes y las tareas de UX, las pantallas, los procesos, los requisitos (RF06, RF09, RF11), los atributos de calidad, las tácticas, el estilo arquitectónico, los escenarios, el modelo de dominio y de datos, módulos y contratos, Clean Architecture, el mapeo IEEE 1016 y los controles ASVS. Con el «confirmo» del responsable se cerraron las preguntas abiertas de las 18 specs: las confirmaciones de valores quedaron resueltas y las decisiones técnicas pasaron a «Nota para el plan».
- Conflictos detectados y corregidos al confirmar: TRA-09 (la administración «puede todo») contra TRA-16; la propuesta de cubrir un traslado perdido con una merma en el origen, que descontaría dos veces el físico (queda fuera de la versión); la «tabla de carrito de visitante» sin requisito (CAR-24); ENV con un solo envío por pedido frente a pedidos de varias fuentes (ENV-01, ENV-07); y el aviso por correo frente a «solo WhatsApp» en NOT.
- Requisitos añadidos al confirmar: SEL-23, TRA-18, CAR-24, DEV-13 a DEV-15. `SpecCheck`: 19 specs, 0 preguntas abiertas, 6 avisos (RNF de infraestructura y CI sin spec).

- **Revisión con agentes y segunda ronda.** `spec-auditor` (AUD y SEG: «necesita otra vuelta») y `security-reviewer` (SEG e IDE: «cumple con observaciones») revisaron las specs transversales. Con ocho decisiones del responsable se aplicaron: origen desconocido único (SEG-23), cabeceras del cliente ignoradas (SEG-17), agrupación de límites excedidos (SEG-08, SEG-27), cierre de lo sensible si cae el almacén de contadores (SEG-26), huella de IP con clave (SEG-25), TOTP cifrado y de un uso por ventana con sesión parcial (IDE-40 a IDE-42), bloqueo del segundo factor por cuenta y origen (SEG-20), clave de firma con identificador y revocación en 5 minutos (IDE-35, IDE-36, AM25), contraseña de 10 a 128 caracteres, reaceptación de textos (IDE-43), revocación de renovaciones y aviso al cambiar la contraseña (IDE-44, IDE-45), renovación de 8 horas para el personal, y en auditoría los actores sin usuario, la IP en la depuración, más eventos y pantallas de auditoría y alertas (AUD-16, AUD-19 a AUD-21, WEB-30). `SpecCheck`: 19 specs, 6 avisos (RNF de infraestructura y CI).
- Se entregó al responsable el bloque de frontmatter para aprobar `transversal-auditoria` y `transversal-seguridad`; la aprobación no se escribió.

## Decisiones

- Las decisiones están en `progress/R3-analisis-inicial.md` (secciones 7.1 a 7.7), cada una enlazada a su requisito y documento.
- Cambios que revisan decisiones previas: el segundo factor deja de ser solo del administrador (AD22), la contraseña pasa de 8 caracteres con composición a 10 sin composición (R0, RNF15), comprar exige correo verificado (IDE-29) y la respuesta a un reclamo sale por correo y no por WhatsApp (RC18, AD30).
- Por el cambio de comportamiento, todas las specs siguen en `Draft`; ninguna se aprobó. Los valores y plazos legales marcados «a verificar» no se confirmaron contra el texto vigente.

## Pendiente / siguiente

- Revisar y aprobar las specs en el orden de `specs/README.md`. Ya no quedan preguntas abiertas; la aprobación (`aprobado_por`, `aprobado_en`, `estado: Approved`) la escribe la persona responsable, previa revisión opcional con `spec-auditor` y `security-reviewer`.
- Para el plan de cada spec: custodia de la clave de firma, almacenamiento de la sesión web y de los contadores de límites.
- Verificar con una persona experta las normas marcadas (Ley 29571, formato de la hoja, plazos de reclamos, ARCO, incidentes, menores y cookies) — ver B09.
- Revisar el render de los diagramas Mermaid de `docs/04-modelo-c4/`, que no se visualizaron en esta sesión.
- Crear el commit `docs:` cuando `git` esté disponible; no se hizo `push`.
