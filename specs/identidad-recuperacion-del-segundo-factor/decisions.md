---
spec: identidad-recuperacion-del-segundo-factor
---

# Decisiones locales — Restablecimiento del segundo factor desde la consola

| ID | Decisión | Motivo | Alternativa descartada | Enlace |
| :--- | :--- | :--- | :--- | :--- |
| D01 | Un modo de consola propio, `restablecer-segundo-factor`, sobre un contrato público de Identidad, y no una opción de `crear-administrador` (DD-140). | Cada modo hace una cosa; la regla vive en Dominio y Aplicación y se prueba sin base. | Ampliar `PrimerAdministradorDeLaInstalacion` (regla en Infraestructura y dos efectos bajo un mismo comando). | RF19 |
| D02 | Reutilizar la acción de auditoría `SegundoFactorRestablecidoPorAdministracion` con el actor «sistema», sin acción nueva. | AUD-16 ya la exige; el actor distingue la consola de la aplicación; así no cambia el catálogo ni `transversal-auditoria`. | Una acción nueva (reabrir la spec de auditoría sin información adicional). | RF20 |
| D03 | Cuenta como «puede ingresar» toda administración activa con contraseña definitiva o temporal vigente, tenga o no segundo factor (DD-141). | Es comprobable con los datos y coincide con lo que el ingreso permite; quien puede ingresar puede restablecer a la otra. | Exigir segundo factor activado (negaría la vía normal a una recién creada); una opción para forzar (convierte la guarda en un trámite). | RNF15 |
| D04 | Bloquear **todas** las cuentas de administración, también las inactivas, con un miembro nuevo del repositorio. | `ContarAdministradoresActivos` no bloquea una desactivada, que podría reactivarse mientras corre la orden (RSF-16). | Bloquear la tabla entera (detendría los ingresos); no bloquear las inactivas. | RNF15 |
| D05 | Anular los ingresos pendientes de la cuenta (`desafios_de_ingreso`) en la misma transacción. | El corte de sesiones no cubre un desafío: uno anterior podría completarse con el factor nuevo (RSF-08). | Confiar en que sin factor activo ya falla (revive al configurar el nuevo). | RNF15 |
| D06 | Un solo texto de correo para el restablecimiento por consola y desde la aplicación (DD-142). | El tope de la primera alerta de cada tipo (IDE-74) se cuenta por plantilla; dos plantillas lo partirían. | Plantilla nueva solo para la consola. | RNF15 |
| D07 | El ingreso crea su paso siguiente (y un secreto sin confirmar) bajo el bloqueo de la fila de la cuenta, y se rechaza si el corte de sesiones cambió desde que comprobó la contraseña. | Sin eso, un desafío calculado con datos anteriores puede escribirse después de la anulación y completarse con el factor nuevo (RSF-08). Protege igual a IDE-38 frente a esa carrera. | Filtrar los desafíos por su hora de creación (el de la contraseña temporal hereda la hora y avanza el corte; uno creado tarde pasa el filtro). | RNF15 |
| D08 | El caso de uso comprueba también el entorno y el modo, además de la consola. | El contrato se compone en el servidor en marcha, donde los trabajos de fondo usan el actor «sistema». | Registrar el contrato solo en la consola (dos composiciones distintas). | RNF15 |
