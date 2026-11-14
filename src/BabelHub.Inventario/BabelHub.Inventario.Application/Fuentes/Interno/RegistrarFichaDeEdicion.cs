using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Domain.Fuentes;

namespace BabelHub.Inventario.Application.Fuentes.Interno;

/// <summary>
/// Registra o actualiza la ficha de una edición (FUE-07, D03): solo el formato, por <c>edicionId</c>, idempotente, también para ediciones digitales (el Catálogo las
/// registra todas). Solo la administración central (el Catálogo la escribe con el token de una administradora; ningún trabajo de fondo la escribe). Pasar a digital una
/// edición que tiene stock (físico o reservado en alguna fuente) es <c>INV-FORMATO-EN-CONFLICTO</c>; la clave compuesta de la tabla es la última defensa si el stock
/// aparece entre la lectura y la escritura, y el repositorio lo devuelve como resultado. Registrar la ficha no se audita: el plan solo audita fuentes y rutas (FUE-14, FUE-16).
/// </summary>
internal sealed class RegistrarFichaDeEdicion(
    AutorizadorDeFuentes autorizador,
    IValidador<RegistrarFichaDeEdicionEntrada> validador,
    IRepositorioDeFichas fichas,
    IRepositorioDeExistencias existencias) : ICasoDeUso<RegistrarFichaDeEdicionEntrada, FichaDeEdicionGuardada>
{
    public async Task<Resultado<FichaDeEdicionGuardada>> Ejecutar(RegistrarFichaDeEdicionEntrada entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);

        if (await autorizador.ExigirAdministracionCentral(cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return FallosDeFuentes.Fallo<FichaDeEdicionGuardada>(rechazo);
        }

        var validacion = validador.Validar(entrada);
        if (!validacion.EsValido)
        {
            return FallosDeFuentes.Validacion<FichaDeEdicionGuardada>(validacion);
        }

        var formato = entrada.Formato == FormatoDeEdicionRegistrado.Impreso ? FormatoDeEdicion.Impreso : FormatoDeEdicion.Digital;
        var actual = await fichas.PorId(entrada.EdicionId, cancelacion).ConfigureAwait(false);

        // Solo hay que mirar el stock cuando el formato cambia: si es el mismo o la ficha es nueva, no hay conflicto posible.
        var tieneStock = actual is not null && actual.Formato != formato
            && await existencias.HayExistencias(entrada.EdicionId, cancelacion).ConfigureAwait(false);

        var (resultado, ficha) = FichaDeEdicion.Actualizar(actual, entrada.EdicionId, formato, tieneStock);
        switch (resultado)
        {
            case ResultadoDeFicha.FormatoEnConflicto:
                return FallosDeFuentes.De<FichaDeEdicionGuardada>(CategoriaDeError.Conflicto, ClavesDeFuentes.FormatoEnConflicto);

            case ResultadoDeFicha.SinCambios:
                return Resultado<FichaDeEdicionGuardada>.Exito(Guardada(ficha, seEscribio: false));
        }

        if (await fichas.Guardar(ficha, cancelacion).ConfigureAwait(false) == ResultadoDeGuardadoDeFicha.FormatoEnConflicto)
        {
            return FallosDeFuentes.De<FichaDeEdicionGuardada>(CategoriaDeError.Conflicto, ClavesDeFuentes.FormatoEnConflicto);
        }

        return Resultado<FichaDeEdicionGuardada>.Exito(Guardada(ficha, seEscribio: true));
    }

    private static FichaDeEdicionGuardada Guardada(FichaDeEdicion ficha, bool seEscribio) =>
        new(ficha.EdicionId, ficha.Formato == FormatoDeEdicion.Impreso ? FormatoDeEdicionRegistrado.Impreso : FormatoDeEdicionRegistrado.Digital, seEscribio);
}
