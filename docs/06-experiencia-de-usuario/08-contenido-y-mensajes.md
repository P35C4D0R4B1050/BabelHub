# Contenido y mensajes

Guía de redacción de los textos de la interfaz: tono, estructura de los mensajes y catálogo de mensajes de seguridad y de error. El objetivo es que cada mensaje diga qué pasó, por qué y qué hacer, **sin revelar información sensible** (véase [seguridad](../05-seguridad/)).

## 1. Voz y tono

| Aspecto | Regla |
| :--- | :--- |
| Idioma | Español del Perú (es-PE); fechas en formato día/mes/año y hora de Lima; importes en soles (`S/ 1 234,50`). |
| Tono | Cálido, directo y sobrio, acorde con la identidad «Babel cromático»; sin infantilismos ni jerga técnica. |
| Tratamiento | Tuteo respetuoso («tu pedido», «puedes»). |
| Longitud | Frases cortas; la acción en el verbo («Reintentar», «Ver mi biblioteca»). |
| Términos | Los del [glosario](../glossary.md): *recojo en sucursal*, *biblioteca*, *traslado*. Sin mezclar sinónimos. |
| Humor | Solo en estados vacíos o de espera, nunca en errores ni en seguridad. |

## 2. Estructura de un mensaje de error

> **Qué pasó** + **por qué (si ayuda)** + **qué hacer**.

| Parte | Ejemplo |
| :--- | :--- |
| Qué pasó | «No pudimos validar el stock.» |
| Por qué | «El inventario no responde en este momento.» |
| Qué hacer | «Intenta de nuevo en unos minutos. No se te ha cobrado nada.» |

Reglas: sin códigos internos visibles (el `code` estable y el identificador de seguimiento van en un detalle plegable «Soporte»); sin culpar a la persona; sin trazas; el foco pasa al mensaje o al primer campo con error y se anuncia con `role="alert"`.

## 3. Mensajes de seguridad

Estos mensajes **no deben revelar** si una cuenta existe, qué dato falló ni detalles internos (SEG-06, SEG-07, IDE-03, IDE-17).

| Situación | Mensaje | Qué evita |
| :--- | :--- | :--- |
| Credenciales incorrectas | «El correo o la contraseña no coinciden.» | Revelar cuál falló o si la cuenta existe. |
| Ingreso bloqueado por límite | «Hiciste varios intentos. Espera 15 minutos o recupera tu acceso.» (con la hora de reintento) | Distinguir cuentas existentes. |
| Recuperación solicitada | «Si el correo está registrado y verificado, te enviamos un código. Revisa tu bandeja.» | Confirmar la existencia de la cuenta. |
| Código inválido, vencido o usado | «El código no es válido o venció. Puedes pedir uno nuevo.» | Indicar cuál de las tres causas fue. |
| Límite general de uso | «Estás yendo muy rápido. Vuelve a intentarlo en 30 segundos.» | Acusar o dar pistas internas. |
| Reserva con límite | «Ya tienes un pedido en pago. Termínalo o cancélalo para iniciar otro.» / «Llegaste al máximo de reservas por hora. Podrás intentarlo de nuevo a las 15:42.» | Mensajes ambiguos. |
| Descarga con límite | «Usaste las 5 descargas de este libro. Si perdiste el archivo, pide ayuda y lo revisamos.» | Dejar sin salida. |
| Sin permiso | «Tu cuenta no tiene acceso a esta sección.» + salidas permitidas | Revelar que el recurso existe. |
| Segundo factor | «Escribe el código de tu aplicación de autenticación.» | Detallar el motivo del fallo. |
| Sesión vencida o inactiva | «Tu sesión venció por inactividad. Inicia sesión para continuar; guardamos lo que escribiste.» | Perder datos. |
| Correo sin verificar al pagar | «Verifica tu correo para pagar. Te enviamos un código; tu carrito sigue guardado.» | Perder el carrito. |
| Registros de cuentas con límite | «Se hicieron varios registros desde esta conexión. Inténtalo de nuevo a las 15:42.» | Acusar a la persona. |
| Reclamos con límite | «Se enviaron varias hojas desde esta conexión. Podrás enviar otra a las 15:42.» | Acusar a la persona. |
| Cancelación no permitida | «Este pedido ya está en preparación o incluye un libro digital. Si ya lo recibiste, puedes pedir una devolución.» | Dejar sin salida. |
| Segundo factor incorrecto o bloqueado | «El código no es válido. Si perdiste tu dispositivo, usa un código de respaldo.» / «Hiciste varios intentos. Podrás volver a probar a las 15:42.» | Indicar si la contraseña era correcta. |
| Contraseña temporal | «Define tu contraseña para continuar. La temporal vence a las 15:42.» | Mostrar la temporal. |
| Cambio de correo inusual | «Por seguridad, confirma este cambio desde un dispositivo que ya usaste. Mientras tanto, no cambiamos nada.» | Alertar a quien intenta el cambio de qué canales avisamos. |
| Nueva versión de los términos | «Actualizamos nuestros términos. Acéptalos para pagar; puedes seguir viendo tu cuenta.» | Bloquear toda la cuenta. |
| Cancelación de cuenta aplazada | «Cancelaremos tu cuenta cuando cierres: pedido ..., reclamo ... Hasta entonces no podrás hacer compras nuevas.» | Negar el derecho. |
| Devolución digital rechazada | «Ya descargaste este libro, por eso no podemos reembolsarlo. Si el archivo no abre, repórtalo y lo revisamos.» | Dejar sin salida. |
| Registro sin mayoría de edad | «BabelHub es solo para mayores de 18 años.» | Pedir datos innecesarios. |

## 4. Catálogo de mensajes por estado

| Estado | Título | Texto | Acción |
| :--- | :--- | :--- | :--- |
| Cargando | — | (esqueleto, sin texto) | — |
| Vacío (carrito) | «Tu carrito está vacío» | «Explora el catálogo y encuentra tu próxima lectura.» | Ir al catálogo |
| Vacío (búsqueda) | «No encontramos resultados» | «Revisa la ortografía o prueba con menos filtros.» | Limpiar filtros |
| Error genérico | «Algo salió mal» | «No pudimos completar la acción. Intenta de nuevo.» | Reintentar |
| Servicio no disponible | «No podemos mostrar esto ahora» | Nombra la parte afectada. | Reintentar |
| Agotado | «Agotado» (con icono) | «Disponible en otras sucursales: …» | Ver sucursales |
| Reserva por vencer | «Te quedan 2 minutos» | «Tus libros están apartados hasta las 15:42.» | Pagar ahora |
| Pago pendiente | «Estamos confirmando tu pago» | «No vuelvas a pagar. Mantenemos tus libros apartados hasta las 15:42 y te avisaremos por correo (y por WhatsApp, si lo activaste) cuando se confirme.» | Ver mi pedido |
| Carrito fusionado con ajustes | «Unimos tu carrito. Ajustamos la cantidad de ... al máximo disponible y quitamos ... porque ya lo tienes.» | Perder el carrito en silencio. |
| Reserva cambió | «Cambió la disponibilidad de ... Revisa tu carrito: tu libro vendrá de ... y llegará en ... días.» | Perder el carrito. |
| Pago aprobado tarde | «Tu pago llegó cuando tus libros ya no estaban apartados» | «Te devolvimos el dinero automáticamente. Puedes volver a intentar la compra.» | Volver al carrito |
| Traslado sin plazo | «Plazo por confirmar» | «Tu libro vendrá de otra sucursal; te avisaremos la fecha.» | Continuar |
| Pedido no recogido | «No recogiste tu pedido a tiempo» | «Liberamos tus libros. Escríbenos y revisamos tu caso.» | Ayuda |

## 5. Contenido legal y de privacidad

- Los términos, la política de privacidad (que informa las cookies necesarias) y la de reembolso digital usan lenguaje claro y resumen al inicio lo esencial. Cada uno muestra su versión y fecha de vigencia.
- La constancia del Libro de Reclamaciones indica el plazo de respuesta (15 días calendario) y que la respuesta llega por correo.
- El consentimiento de WhatsApp se explica en una frase («Te avisaremos del estado de tu pedido; no enviamos publicidad») y se ofrece como opción.
- El Libro de Reclamaciones se enlaza siempre desde el pie y se explica qué es un reclamo y qué una queja.

## 6. Revisión de textos

La skill `ui-review` incluye una pasada de contenido: tono, claridad, términos del glosario, mensajes de seguridad que no filtran información y ausencia de texto en imágenes. Los textos viven en recursos de localización, no incrustados en el código de las vistas, para poder revisarlos en un solo lugar.
