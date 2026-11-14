using BabelHub.Ecommerce.Domain.Plataforma;

namespace BabelHub.Ecommerce.Application.Plataforma.Interno;

/// <summary>La cuenta que se suprime o anonimiza: su identificador opaco y qué se le hace. Nunca datos personales.</summary>
public sealed record SupresionDeCuenta(string IdentificadorOpaco, ClaseDeSupresion Clase);

/// <summary>Lo que comparten los dos casos de uso: validar la cuenta con el Dominio y traducir los fallos del registro.</summary>
internal static class EscrituraEnElRegistro
{
    public const string ClaveIdentificadorInvalido = "identificador-opaco-invalido";
    public const string ClaveRegistroNoDisponible = "registro-de-supresiones-no-disponible";
    public const string ClaveSupresionSinIntencion = "supresion-sin-intencion";

    /// <summary>Valida el identificador con la misma regla del Dominio (el interno aleatorio, nunca un dato personal).</summary>
    public static ErrorDeAplicacion? Validar(SupresionDeCuenta entrada)
    {
        try
        {
            _ = EntradaDeSupresion.Crear(entrada.IdentificadorOpaco, TipoDeSupresion.Cancelacion, FaseDeSupresion.Intencion, DateTimeOffset.UnixEpoch);
            return null;
        }
        catch (ArgumentException)
        {
            return new ErrorDeAplicacion(CategoriaDeError.Validacion, ClaveIdentificadorInvalido, [new ErrorDeCampo(nameof(SupresionDeCuenta.IdentificadorOpaco), ClaveIdentificadorInvalido)]);
        }
    }

    public static ErrorDeAplicacion NoDisponible() => new(CategoriaDeError.NoDisponible, ClaveRegistroNoDisponible);
}

/// <summary>
/// Agrega la entrada de <b>intención</b> al registro de supresiones, que Identidad debe lograr <b>antes</b> de completar la supresión
/// de la cuenta (PLA-37). Es idempotente: repetirla es éxito y devuelve la entrada original. Si el registro no responde, devuelve un
/// fallo «no disponible» y la supresión no se completa.
/// </summary>
public sealed class AgregarIntencionDeSupresion(IRegistroDeSupresiones registro, IReloj reloj) : ICasoDeUso<SupresionDeCuenta, EntradaDeSupresionDto>
{
    public async Task<Resultado<EntradaDeSupresionDto>> Ejecutar(SupresionDeCuenta entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);

        if (EscrituraEnElRegistro.Validar(entrada) is { } invalida)
        {
            return Resultado<EntradaDeSupresionDto>.Fallo(invalida);
        }

        try
        {
            var escrita = await registro.Agregar(entrada.IdentificadorOpaco, entrada.Clase, EtapaDeSupresion.Intencion, reloj.Ahora, cancelacion).ConfigureAwait(false);
            return Resultado<EntradaDeSupresionDto>.Exito(escrita);
        }
        catch (RegistroDeSupresionesNoDisponibleException)
        {
            return Resultado<EntradaDeSupresionDto>.Fallo(EscrituraEnElRegistro.NoDisponible());
        }
    }
}

/// <summary>
/// Agrega la entrada de <b>cierre</b>, con su propia fecha, al completar la supresión (PLA-37). Solo se cierra una supresión con
/// intención previa: sin ella se rechaza y no se escribe nada. Es idempotente. Si el registro no responde devuelve «no disponible»: la
/// supresión ya se hizo y el cierre se reintenta sin deshacerla (PLA-41, lo reintenta Identidad).
/// </summary>
public sealed class CerrarSupresion(IRegistroDeSupresiones registro, IReloj reloj) : ICasoDeUso<SupresionDeCuenta, EntradaDeSupresionDto>
{
    public async Task<Resultado<EntradaDeSupresionDto>> Ejecutar(SupresionDeCuenta entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);

        if (EscrituraEnElRegistro.Validar(entrada) is { } invalida)
        {
            return Resultado<EntradaDeSupresionDto>.Fallo(invalida);
        }

        try
        {
            var intencion = await registro.Obtener(entrada.IdentificadorOpaco, entrada.Clase, EtapaDeSupresion.Intencion, cancelacion).ConfigureAwait(false);
            if (intencion is null)
            {
                return Resultado<EntradaDeSupresionDto>.Fallo(new ErrorDeAplicacion(CategoriaDeError.ReglaDeNegocio, EscrituraEnElRegistro.ClaveSupresionSinIntencion));
            }

            var cierre = await registro.Agregar(entrada.IdentificadorOpaco, entrada.Clase, EtapaDeSupresion.Cierre, reloj.Ahora, cancelacion).ConfigureAwait(false);
            return Resultado<EntradaDeSupresionDto>.Exito(cierre);
        }
        catch (RegistroDeSupresionesNoDisponibleException)
        {
            return Resultado<EntradaDeSupresionDto>.Fallo(EscrituraEnElRegistro.NoDisponible());
        }
    }
}
