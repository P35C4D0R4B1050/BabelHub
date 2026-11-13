# R7 — Auditoría de cierre de la fase 2 (2026-11-12)

Seis revisores (`spec-auditor`, `arch-reviewer` y `security-reviewer`, tres sobre `identidad-seguridad` y tres sobre el conjunto de specs, docs y proceso). Ninguno encontró Bloqueantes. Las 19 specs aprobadas siguen en pie; los Importantes son huecos **entre specs** y de **diseño** que se resuelven al planificar o reabriendo specs concretas. Las decisiones de la persona responsable de esta auditoría están en la sección 3.

## 1. Corregido en esta sesión

| Qué | Dónde |
| :--- | :--- |
| Pruebas de la puerta en rojo (el fixture copiaba las specs ya aprobadas): ahora revierte a Draft en la copia temporal y vacía el registro | `tests/tools/BabelHub.SpecCheck.Tests/PuertaDeAprobacionTests.cs` |
| Inundación de alertas a la administración, cupo y tope de códigos, alerta crítica de cuenta, descarte a los 30 días | IDE-73, IDE-74, IDE-77, IDE-81, IDE-82, IDE-83 |
| Registro con un correo sin verificar (la cuenta nueva reemplaza a la anterior) | IDE-84, AC-IDE-63 |
| El Inventario obtiene la clave y las revocaciones de la réplica de gestión, sin depender de VM-VENTAS | IDE-85, AC-IDE-64, despliegue §6 |
| Cola de reintento de reclamos y pagos única (`correos_pendientes`), AM28, glosario, Enviados cada 7 días incluida la Papelera | docs/02, docs/03, docs/05, glosario |
| Recuento de specs, RPO de 15 minutos, copia consistente del clúster (AD03, despliegue, operación segura) | docs/02, docs/03, docs/05 |
| Proceso: README de `docs/07`, flujo SDD (aprueba la persona o el agente delegado), tope de rondas, bitácora y bloqueos | `docs/07-proceso-de-desarrollo`, `progress/` |

## 2. Pendiente (Importantes, con dueño y fase)

| # | Hallazgo | Acción | Reabre spec Approved | Fase |
| :---: | :--- | :--- | :--- | :--- |
| 1 | **Receptores de la cancelación de cuenta:** ninguna spec de pedidos, envíos, devoluciones, reclamaciones, pagos, biblioteca ni carrito responde «pendientes de la cuenta» (IDE-27) ni anonimiza el contacto de sus pedidos (IDE-47, IDE-70); faltan también la retención a 2 años de pedidos, dirección y registro de descargas, y el borrado de la biblioteca y las ayudas al cancelar. CTL-33 figura como «Especificado» sin estarlo | Añadir requisitos y criterios en cada spec; la persona decide la retención. Cambiar CTL-33 a «Pendiente» hasta entonces | Sí: PED, PAG, DEV, ENV, BIB, REC, CAR | Al planificar cada una |
| 2 | **Teléfono del destinatario:** ENV-02 lo envía al operador y CAR-08 solo pide dirección | Decidido: pedirlo en el checkout como dato de contacto de entrega, con su fila de privacidad y retención | Sí: CAR y ENV | Al planificar CAR y ENV |
| 3 | **web-base sin las pantallas de identidad nuevas** (aviso en la cuenta, confirmar o rechazar un cambio de correo, reconfirmar o desistir de la cancelación, mayoría de edad) y **Turnstile frente a la CSP** (WEB-18, CTL-07) | Ampliar WEB-18 y las pantallas; incluir Turnstile en la privacidad | Sí: web-base | Al reabrir web-base |
| 4 | **Controles de red y transporte sin requisito** (TLS interno, puertos, Cloudflare Access con MFA, hora con chrony): CTL-27, CTL-30, AM22 | Requisito con criterio en `transversal-plataforma` o tarea de infraestructura con prueba de escaneo | Sí (plataforma), o solo plan | Plan de plataforma |
| 5 | **SEG-26 frente a la réplica de gestión** | Decidido: la réplica solo atiende sesiones vigentes (hasta 8 h); ya reflejado en el despliegue §6. Ajustar AD11 y tácticas | No | Plan de despliegue |
| 6 | **Transacciones entre módulos frente a «un rol por esquema»** (IDE-47, IDE-70, AUD-04) | Fijar en AD18 y en el modelo de datos el mecanismo (propuesta: una conexión con pertenencia a los roles y `SET LOCAL ROLE` por módulo, o un rol de escritura con permisos revisados) | No | Plan de identidad |
| 7 | **Capacidades transversales sin módulo ni esquema dueño** (auditoría, alertas, avisos, correo y cuota, trabajos de fondo): la regla 13 no tiene dónde apoyarse | Declarar un núcleo compartido (`Plataforma`) con sus esquemas y roles, en módulos §1.1 y en la prueba de arquitectura | No | Plan de plataforma |
| 8 | **`correos_pendientes` en el esquema de Identidad** guarda correos de otros módulos; falta el contador de la cuota de IDE-66 | Mover la cola y el contador al núcleo compartido (hallazgo 7) | No | Plan de plataforma |
| 9 | **Credenciales de servicio:** IDE-30 dice que Identidad las emite y docs/05 que son secretos del entorno | Propuesta: secretos estáticos por alcance, rotados a mano; ajustar IDE-30 y módulos §4 | No | Plan de identidad |
| 10 | **Almacén de la sesión web:** `sesiones_web` y `renovaciones` no definen dónde vive el valor de la renovación que la sesión necesita | Definir columnas, cifrado en reposo y su invalidación (PLA-39, PLA-43) | No | Plan de identidad y web-base |
| 11 | **La prueba de arquitectura cubre menos de lo que `AGENTS.md` §3 dice** (no impide usar el Dominio o la Infraestructura de otro módulo ni mira la Api) | Ampliar la prueba, sin debilitarla; o la persona ajusta la redacción de `AGENTS.md` | No | Antes de implementar |
| 12 | **n8n y Evolution API** guardan números y mensajes sin retención ni purga al cancelar | Documentarlo en privacidad y fijar la retención en el plan | No | Plan de plataforma |
| 13 | **VM-AUX** (salto SSH, monitoreo, probable credencial de retiro de respaldos) falta en el modelo de amenazas | Añadir la amenaza y sus controles; credencial de retiro separada del salto SSH | No | Antes de operar con datos reales |

## 3. Decisiones de la persona responsable en esta auditoría

- Cuota de correo: 300 al día, cupos 15 / 15 / 25 / 45 %, tope de 3 avisos por cuenta, 24 correos al día por motivo y servicio, 10 códigos al día por origen, cancelación diferida descartada a los 30 días (`progress/decisiones-previas-al-plan.md`).
- Reclamos y pagos que no salen por cuota se reintentan solos desde la cola del E-commerce.
- La réplica de gestión solo atiende sesiones vigentes si cae VM-VENTAS; sirve las claves y las revocaciones al Inventario.
- El teléfono de entrega se pide en el checkout.
- La cuenta sin verificar se reemplaza cuando el dueño real se registra.

## 4. Menores

Registrados en `progress/hallazgos.md` (redundancias entre specs, glosario, trazabilidad AM/CTL, inventario de privacidad, RNF02 y RNF03 sin dueño, tareas de fondo y máquinas de estado, Redis en el Inventario, nombres de proyectos en `docs/03`, y los restos de formato de `docs/03`).
