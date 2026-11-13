namespace BabelHub.Inventario.Application.Plataforma.Interno;

/// <summary>
/// Decorador de un caso de uso que valida la entrada en el servidor antes de procesarla (SEG-01): si no cumple, devuelve un
/// fallo de validación con los campos inválidos y el caso de uso no se ejecuta. Es <c>public</c> solo para que la
/// composición de dependencias de la Infraestructura lo registre (D25).
/// </summary>
public sealed class ValidacionDeEntrada<TEntrada, TSalida>(ICasoDeUso<TEntrada, TSalida> interno, IValidador<TEntrada> validador)
    : ICasoDeUso<TEntrada, TSalida>
{
    public const string ClaveDeValidacion = "validacion-fallida";

    public Task<Resultado<TSalida>> Ejecutar(TEntrada entrada, CancellationToken cancelacion = default)
    {
        var validacion = validador.Validar(entrada);

        return validacion.EsValido
            ? interno.Ejecutar(entrada, cancelacion)
            : Task.FromResult(Resultado<TSalida>.Fallo(new ErrorDeAplicacion(CategoriaDeError.Validacion, ClaveDeValidacion, validacion.Errores)));
    }
}
