# Guía del spike T38: verificar el adaptador de Culqi con tus llaves de integración

Para la **persona responsable**, cuando Culqi te dé las llaves de **integración**. Cuánto lleva: unos 20 minutos. El adaptador (`CulqiPasarelaDePago`, T37) está hecho y probado contra un Culqi **simulado**; esto lo prueba contra el Culqi **de verdad** en su ambiente de integración, donde **no se mueve dinero real**. Lo que dice la documentación pública y lo que no está en [`decisiones-de-implementacion.md`](decisiones-de-implementacion.md) (sección «T37»).

**Regla de oro:** las llaves **nunca** se pegan en el chat, en el repositorio, en un archivo `.env` del repositorio ni en una captura. Solo viven en las variables de tu sesión de PowerShell, que se pierden al cerrarla.

## 1. Lo que necesitas

- Las dos llaves de **integración** del CulqiPanel (integración → *Desarrollo / API Keys*): la pública `pk_test_…` y la privada `sk_test_…`. Las de producción (`pk_live_…`, `sk_live_…`) **no** se usan aquí: la prueba se niega a correr con ellas.
- .NET 10 y el repositorio compilado (`dotnet build BabelHub.sln`). No hace falta Docker ni la tienda levantada.

## 2. Poner las llaves solo en tu sesión

Abre PowerShell en la carpeta del repositorio y ejecuta (te las pide sin mostrarlas):

```powershell
$env:BABELHUB_CULQI_PK_TEST = Read-Host "Llave publica de integracion (pk_test_...)"
$privada = Read-Host "Llave privada de integracion (sk_test_...)" -AsSecureString
$env:BABELHUB_CULQI_SK_TEST = [System.Net.NetworkCredential]::new("", $privada).Password
```

## 3. Correr la prueba de humo

```powershell
dotnet test tests/integration/BabelHub.IntegrationTests --filter "FullyQualifiedName~CulqiRealTests" --logger "console;verbosity=detailed"
```

Sin las variables, las cuatro pruebas se **omiten** (así corre el resto del proyecto). Con ellas, hacen cargos de S/ 10, S/ 10 y S/ 5 (más reembolsos) **solo en integración**, con las tarjetas y el celular de prueba que publica Culqi (`4111 1111 1111 1111`, `4000 0400 0000 0008`, `4000 0200 0000 0000`, Yape `900000001` con cualquier código de 6 dígitos). Las líneas que empiezan con `OBSERVACIÓN` son lo que no se sabía.

| Prueba | Debe ocurrir | Si falla, significa |
| :--- | :--- | :--- |
| `Tarjeta_aprobada_…` | El cobro se aprueba; la consulta por la referencia lo encuentra; un reembolso de S/ 4, repetirlo con la misma marca no pide otro, el resto de S/ 6 se aprueba y uno de más se rechaza. | Cambia la forma del cargo, la ventana de la consulta (`creation_date_from/to`) o el `PATCH` de metadatos de la devolución (DD-95, DD-97). |
| `Tarjeta_sin_fondos_y_tarjeta_robada_…` | Se rechazan con `fondos_insuficientes` y `tarjeta_rechazada`. | Otro `decline_code` o forma del error `402`. |
| `Yape_se_cobra_…` | El cobro con Yape se aprueba; el reembolso se **observa** (no se afirma nada). | La línea `OBSERVACIÓN` responde la pregunta de DD-98: ¿se reembolsa Yape por la API? |
| `Un_token_que_ya_se_uso_…` | El segundo cobro con el mismo token no se aprueba. | El token no es de un solo uso: revisar el riesgo de doble cobro. |

Si algo falla, **no cambies nada a mano**: copia el nombre de la prueba y el mensaje (sin llaves ni tokens) a la sesión de trabajo para ajustar el adaptador.

## 4. Lo que solo se ve en el CulqiPanel (integración)

1. **Cargos y devoluciones:** en el panel de integración deben verse los cargos del paso 3 con la referencia en los metadatos (`referencia`) y las devoluciones con `reembolso_id`.
2. **Avisos (webhooks) y su firma:** en *Eventos* abre el formulario de un webhook y fíjate si ofrece **autenticación o secreto**, qué cabeceras envía y si reintenta. **No registres todavía** la dirección de tu tienda: hoy la entrada de avisos está deshabilitada a propósito (DD-95) porque la documentación pública no describe ninguna firma, y un aviso sin firma no se acepta. Anota lo que ves; con un esquema HMAC documentado se habilita después.

## 5. Dejar el resultado

Crea `progress/log/AAAA-MM-DD-spike-culqi.md` con: la fecha, qué pruebas pasaron, las líneas `OBSERVACIÓN`, lo del paso 4 y cualquier diferencia con la documentación. **Sin llaves, tokens, números de tarjeta ni correos reales.** Cierra la sesión de PowerShell (o `Remove-Item Env:BABELHUB_CULQI_PK_TEST, Env:BABELHUB_CULQI_SK_TEST`). Con eso T38 se marca hecha y la verificación de `ecommerce-pagos` puede cerrar su reserva.

## 6. Después: pasar la tienda a `Production`

Solo con el spike en verde y las llaves de **producción** (`sk_live_…`), sigue `docs/07-proceso-de-desarrollo/08-guia-primer-despliegue.md` §15.1, fase 2 (tres líneas en `ecommerce.env` y `ecommerce-gestion.env`). La aplicación se niega a arrancar si el entorno y la llave no son coherentes: `sk_test_` solo en `Development` y `Test`, `sk_live_` solo en `Production`, y `Staging` nunca lleva llave.

## 7. Lo que queda pendiente aunque el spike pase (DD-99)

- **La pantalla de pago todavía pide pegar el «identificador del medio de pago»**: falta integrar el formulario de Culqi (Custom Checkout) que lo genera en el navegador. Sin eso, un cliente real no puede pagar con Culqi; es una tarea aparte, con su CSP (`BABELHUB__Web__OrigenesDeLaPasarela`) y la llave pública `pk_`.
- **No hay forma de ensayar `sk_test_` en una VM** (a propósito: PAG-34). Si lo quieres, es una decisión tuya (DD-99).
