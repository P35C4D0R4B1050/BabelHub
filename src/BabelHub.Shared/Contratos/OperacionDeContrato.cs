namespace BabelHub.Shared.Contratos;

/// <summary>
/// Una operación de los contratos v1 entre servicios, como dato: nombre estable, método, ruta, alcance de la credencial, si lleva el token
/// del usuario o la clave de idempotencia, los DTOs de entrada y salida y los códigos de error que puede devolver. El servicio que la publica
/// la declara con <c>[OperacionDeServicio(nombre)]</c>; el que la consume la llama con el alcance indicado. El documento
/// <c>docs/03-diseno-de-software/08-contratos-entre-servicios-v1.md</c> la describe y una prueba lo verifica.
/// </summary>
/// <param name="Nombre">Nombre estable: <c>inventario.reservas.crear</c>.</param>
/// <param name="Metodo">Método HTTP.</param>
/// <param name="Ruta">Ruta con prefijo y versión; los parámetros van entre llaves.</param>
/// <param name="Alcance">Alcance de la credencial de servicio (<see cref="AlcancesDeCredencial"/>).</param>
/// <param name="RequiereTokenDeUsuario">Verdadero si, además de la credencial, lleva el token del usuario (nunca una cabecera de actor).</param>
/// <param name="RequiereClaveDeIdempotencia">Verdadero si exige la cabecera <c>Idempotency-Key</c>.</param>
/// <param name="CodigoDeExito">Código HTTP de la respuesta normal (otro código de éxito de una repetición idempotente se documenta aparte).</param>
/// <param name="Solicitud">DTO del cuerpo de la solicitud, si lo lleva.</param>
/// <param name="Respuesta">DTO del cuerpo de la respuesta, si lo lleva.</param>
/// <param name="Errores">Códigos de error estables de la operación (<c>INV-…</c> o <c>ECO-…</c>).</param>
public sealed record OperacionDeContrato(
    string Nombre,
    string Metodo,
    string Ruta,
    string Alcance,
    bool RequiereTokenDeUsuario,
    bool RequiereClaveDeIdempotencia,
    int CodigoDeExito,
    Type? Solicitud,
    Type? Respuesta,
    IReadOnlyList<string> Errores);
