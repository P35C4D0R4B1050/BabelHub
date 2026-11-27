---
spec: identidad-recuperacion-del-segundo-factor
---

# Decisiones de implementación — Restablecimiento del segundo factor desde la consola

Lo que precisa el plan sin cambiar la spec (2026-11-27).

- **Nombre de la regla de la temporal:** el plan y la tarea T01 dicen `ContrasenaTemporal.Vencida`; en el código es `VigenciaDeLaContrasenaTemporal.Vencida` (Dominio), porque el primero se confundía con el valor de la contraseña.
- **Contraseña cambiada a mitad de un ingreso (revisión de seguridad del código, IDE-44):** además del corte de sesiones, `Ingresar` compara bajo el bloqueo el resumen de la contraseña que verificó. El titular que cambia su contraseña conservando su sesión no avanza el corte, y un ingreso en vuelo con la anterior ya no continúa.
- **Cancelaciones en la consola (RSF-14):** el modo solo deja subir la cancelación pedida por quien llama, y `AnfitrionDeConsola` (compartido con `cuenta-de-prueba` y `crear-administrador`) trata como falla también la de una dependencia que agota su plazo, sin traza.
- **Registro de la línea de órdenes:** `sudo` puede anotar la orden completa, con el correo, en el registro de autenticación de la VM. El sistema no lo escribe; la guía lo avisa.
