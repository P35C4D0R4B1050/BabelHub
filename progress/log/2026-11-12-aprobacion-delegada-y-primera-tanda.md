# 2026-11-12 — Aprobación delegada y primera tanda de specs

> Punto de partida para un chat nuevo: lee primero esta entrada, luego `progress/blockers.md` y `progress/hallazgos.md`.

## Estado al cerrar la sesión

- **Specs:** 19 de 20 en `Approved`; solo `identidad-seguridad` está en `Draft` (86 requisitos, 65 criterios). `SpecCheck`: 20 specs, 0 avisos; build, pruebas de arquitectura y pruebas de la puerta en verde.
- **Fase 2 del plan de trabajo** (`docs/07-proceso-de-desarrollo/01-flujo-sdd.md` §6): en curso. Falta que la persona responsable apruebe `identidad-seguridad`; después empiezan los planes y tareas (`spec-plan`, `spec-tasks`) de la fase 3.
- **Commits de la sesión:** `d629b3b` (puerta de aprobación delegada y su validación), `7e8bf50` (presentación) y `6be0e24` (specs aprobadas, identidad reescrita, documentos alineados). Sin `push`. `.claude/launch.json` quedó sin commitear (apunta a un script ignorado por git).

## Qué se hizo

- **Presentación y resumen:** `docs/BabelHub_Exposicion.pptx` actualizado a dos servicios, cuatro VMs gratuitas, seis atributos de calidad, 31 decisiones y 20 specs. El resumen para la exposición y el guion viven fuera del repositorio.
- **Puerta de aprobación delegada:** gravedad Bloqueante, Importante y Menor; criterios G1 a G10; máximo de rondas; firma «Claude (delegado por Diego Amiquero)». Documento `04-aprobacion-delegada.md`, skill `spec-approve`, registro `progress/aprobaciones.md` con huella SHA-256 del contenido y registro de Menores en `progress/hallazgos.md`. `tools/SpecCheck` valida cada fila de aprobación y `tests/tools/BabelHub.SpecCheck.Tests` (8 pruebas) cubre la puerta.
- **Revisión de specs:** `transversal-auditoria` (AUD-01 a AUD-41), `transversal-plataforma` (PLA-01 a PLA-43) e `identidad-seguridad` (IDE-01 a IDE-86) se reescribieron con respuestas de la persona responsable y se revisaron con `spec-auditor`, `arch-reviewer` y `security-reviewer`. Auditoría y plataforma quedaron **Approved** (huellas en `progress/aprobaciones.md`). Identidad se dejó en Draft por decisión de la persona responsable tras las rondas 4 a 11 (cada ronda reabría un hueco menor en la cuota de correo).
- **Documentos alineados:** autenticación, operación segura, despliegue (copia consistente del clúster; en producción solo se restaura el clúster completo), modelo de datos (`fecha_confirmacion` en `reservas`, segundo factor del personal), privacidad, amenazas, controles ASVS, presupuesto de recursos y glosario.

## Decisiones

- **Respaldos:** Oracle Object Storage de la misma cuenta, sin copia en otro proveedor por ahora (riesgo B13 aceptado; copia externa más adelante). Clave de cifrado fuera de la VM con copia fuera de línea; retención 7 diarios, 4 semanales y 3 mensuales; RPO de 15 minutos con archivado de WAL.
- **Correo:** MailKit con una cuenta de Gmail sencilla, no institucional (B08). **Pagos:** Culqi (B05). **LLM:** OpenCode Go o Claude, a confirmar al implementar (B06).
- **Registro de supresiones** (PLA-37 a PLA-41, IDE-75): intención y cierre; al restaurar solo se reaplican los cierres. Al restaurar en producción se rotan la clave de firma y las credenciales de servicio y se invalidan sesiones y renovaciones (PLA-43).
- **Cancelación de cuenta diferida** (IDE-27, IDE-68, IDE-70, IDE-71): aviso por correo, reconfirmación con contraseña y segundo factor en 7 días, desistimiento, falla cerrada y revalidación de pendientes al ejecutar. Clave de firma comprometida: la declara el administrador central y la caché de 5 minutos rechaza toda credencial si no puede renovarse (IDE-61).
- **Auditoría:** AUD-04 cancela solo lo que concede o cambia un acceso; rechazos, vencimientos y solicitudes siguen AUD-12; el «registro técnico» es la tabla `alertas`. La dirección completa va en los eventos de límite, con un acumulado aparte que la borra al escribir el evento (AUD-28, AUD-41).
- **Cuota de correo** (IDE-66, IDE-73, IDE-74, IDE-77 a IDE-80, IDE-82 y IDE-83): la spec fija la protección; los valores iniciales los decidió la persona el 2026-11-12 y están en `progress/decisiones-previas-al-plan.md` (300 correos al día; cupos 15, 15, 25 y 45 %; tope de 3 avisos por cuenta; 24 correos al día por motivo y servicio; 10 códigos al día por origen; cancelación diferida descartada a los 30 días).

## Pendiente / siguiente

1. La persona responsable aprueba `identidad-seguridad` (o pide otra ronda). Al aprobar: calcular la huella con `dotnet run --project tools/SpecCheck -- --huella identidad-seguridad spec`, agregar la fila a `progress/aprobaciones.md` antes de tocar el estado y regenerar la matriz.
2. Fase 3: planes y tareas de `transversal-auditoria`, `transversal-seguridad`, `identidad-seguridad`, `transversal-notificaciones` y `web-base` con `spec-plan` y `spec-tasks`, usando la misma puerta delegada.
3. En esos planes, resolver lo registrado en `progress/hallazgos.md` (caché de claves, mapa de credenciales, herramienta de archivado, cadena de hashes del registro de supresiones); los valores de la cuota de correo, la cola `correos_pendientes` y el almacén del registro de supresiones ya están decididos (`progress/decisiones-previas-al-plan.md`).
4. Reabrir cuando se planifique cada una: `transversal-auditoria` (referencias obsoletas a IDE-69 e IDE-73 en AUD-37), `inventario-seleccion-fuente` (requisito receptor de IDE-72), `ecommerce-carrito-checkout`, `ecommerce-pagos`, `ecommerce-pedidos`, `ecommerce-envios`, `ecommerce-devoluciones` y `ecommerce-libro-reclamaciones` (pendientes de la cuenta y anonimizar el contacto de sus pedidos: IDE-27, IDE-47, IDE-70), `transversal-seguridad` (SEG-25) y `web-base` (WEB-30 y las pantallas de los flujos nuevos de identidad). Detalle en `progress/hallazgos.md`.
5. Cuando la persona lo tenga: claves de Culqi, del proveedor de LLM y de la cuenta de Gmail, solo como variables de entorno locales (B05, B06, B08).

## Cierre de la fase 2 (misma fecha, segunda parte)

- Auditoría final de seis revisores y verificación posterior del texto de identidad: informe en `progress/R7-auditoria-de-cierre-fase-2.md` (13 Importantes de diseño y de cobertura entre specs, ninguno Bloqueante, con acción, dueño y fase).
- Decisiones de la persona en esta parte: valores de la cuota de correo, réplica de gestión solo con sesiones vigentes, claves del Inventario servidas por la réplica, teléfono de entrega en el checkout y reemplazo de la cuenta sin verificar (`progress/decisiones-previas-al-plan.md`).
- `identidad-seguridad` quedó en **Draft** con 86 requisitos y 65 criterios. Las últimas correcciones (IDE-84 limitado a clientes, IDE-86, alerta crítica por tipo) **no tuvieron una revisión final de los tres agentes**: cada verificación dejaba un hueco menor en la cuota de correo y la persona decidió aprobarla ella.
- También se corrigió el fixture de las pruebas de la puerta, que fallaban desde las aprobaciones del día.
