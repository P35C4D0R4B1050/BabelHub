# Autenticación y autorización

Describe cómo se identifica a las personas y a los servicios, cómo viaja la identidad y cómo se decide qué puede hacer cada uno. Desarrolla AD22 y los requisitos RNF05 y RNF15; el comportamiento exacto está en `identidad-seguridad` (IDE) y `transversal-seguridad` (SEG).

## 1. Modelo

```mermaid
sequenceDiagram
participant N as Navegador
participant W as Web (servidor)
participant I as Identidad
participant S as Servicio de negocio
N->>W: Formulario de ingreso (TLS)
W->>I: Credenciales
I-->>W: Credencial de acceso (15 min) y renovación (7 días)
W-->>N: Cookie de sesión (HttpOnly, Secure, SameSite)
N->>W: Solicitud con cookie
W->>S: Solicitud con la credencial de acceso (solo servidor)
S->>S: Verifica firma, vigencia, rol y alcance
S-->>W: Respuesta
W-->>N: Página
```

La credencial **nunca llega al navegador** (AGENTS.md, regla 9). El navegador solo guarda una cookie de sesión opaca. Los servicios validan la firma y la vigencia por sí mismos, sin consultar a Identidad en cada solicitud (IDE-10).

## 2. Credenciales de las personas

| Elemento | Decisión | Fuente |
| :--- | :--- | :--- |
| Credencial de acceso | Firmada, de **15 minutos**, con rol y sucursal. | AD22, IDE-02 |
| Renovación | De **7 días**, revocable; se invalida al cerrar sesión, al desactivar al usuario y al restablecer la contraseña. | AD22, IDE-08, IDE-14, IDE-18 |
| Contraseña | Mínimo 8 caracteres con mayúscula, minúscula, número y símbolo; fuera de listas comunes; resumen robusto con sal. | IDE-09, IDE-15, RNF15 |
| Bloqueo | 5 fallos en 15 minutos bloquean temporalmente el ingreso; la respuesta no distingue si la cuenta existe. | IDE-03, SEG-06 |
| Segundo factor | TOTP obligatorio para el administrador central; los demás roles no lo tienen en esta etapa. | IDE-20 |
| Verificación del número | Código de un solo uso por WhatsApp al registrarse; el número es obligatorio. | IDE-01, IDE-16 |
| Recuperación | Código de un solo uso y vigencia corta enviado al número verificado; al restablecer se cierran todas las sesiones. | IDE-17 a IDE-19 |

**Riesgo conocido:** el canal de WhatsApp (Evolution API) no es oficial y un número puede reasignarse; por eso la recuperación es la amenaza AM03 con riesgo residual medio. Contingencia pendiente de decisión: recuperación asistida por la administración con verificación de identidad.

## 3. Identidades de servicio

| Caso | Mecanismo |
| :--- | :--- |
| E-commerce → Inventario | Se reenvía la credencial del usuario para acciones del usuario; para tareas de fondo (expiración de reservas, reintentos) se usa una credencial de servicio de mínimo privilegio. |
| Asistente → servicios | Credencial de servicio de solo lectura; sin acceso a las bases de datos. |
| Notificador ↔ servicios | Secreto compartido o firma verificada en ambos sentidos. |
| Pasarela → E-commerce (aviso) | Firma o secreto del aviso; el estado se confirma además consultando a la pasarela. |

Las credenciales de servicio viven en el entorno de cada contenedor (SEG-11), nunca en el repositorio, y se rotan al cambiar de integrante del equipo o ante sospecha de filtración.

## 4. Autorización

1. **Rol:** viene en la credencial. Define qué funciones existen para el usuario (menús, rutas, casos de uso).
2. **Alcance de sucursal:** viene en la credencial del encargado. Cada caso de uso de la **Aplicación** compara la sucursal del usuario con la del recurso. Los controladores solo verifican el rol, nunca el alcance.
3. **Propiedad:** un cliente solo accede a lo propio (pedidos, biblioteca, cuenta). La comprobación usa el identificador de la credencial, no un identificador recibido en la solicitud.
4. **Denegación:** todo acceso fuera de alcance se rechaza, se registra como evento de seguridad (AUD-10) y suma al umbral de alerta (SEG-09).
5. **Por defecto se niega:** una ruta o una función sin política explícita no está disponible.

### Matriz de verificación

| Escenario | Resultado esperado | Prueba |
| :--- | :--- | :--- |
| Encargado de Ayacucho pide el inventario de Cusco | Rechazo y registro de acceso denegado. | AC-AUD-04 |
| Cliente A pide la biblioteca del cliente B | Rechazo. | AC-BIB-10 |
| Cliente consulta pedidos | Solo ve los suyos. | PED-07 |
| Encargado abre una pantalla de administración | Pantalla de sin permiso. | AC-WEB-02 |
| Administrador ingresa sin segundo factor | No recibe credencial. | AC-IDE-18 |

## 5. Cierre de sesión y cambios de rol

Cerrar sesión invalida la renovación. Desactivar a un usuario impide su ingreso y su renovación (IDE-08). Cambiar el rol hace que las credenciales vigentes dejen de servir en la siguiente renovación (como máximo 15 minutos de desfase); esa ventana se acepta porque una credencial más corta aumentaría la carga sobre Identidad.

## 6. Decisiones pendientes

- Mecanismo de rotación de la clave de firma y su custodia (CTL-29).
- Contingencia de recuperación cuando el canal de WhatsApp falla (AM23).
- Si se extiende el segundo factor al encargado de sucursal (hoy fuera de alcance).
