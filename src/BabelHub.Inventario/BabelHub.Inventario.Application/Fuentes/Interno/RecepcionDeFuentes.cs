using BabelHub.Inventario.Application.Auditoria;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Domain.Fuentes;

namespace BabelHub.Inventario.Application.Fuentes.Interno;

/// <summary>
/// Cambia la fuente de recepción (FUE-19, FUE-20): solo la administración central. Toma el bloqueo de recepción (después de autorizar), lee todas las fuentes y deja
/// <b>exactamente una</b> con la marca: el Dominio decide el conjunto resultante (<see cref="ReglaDeRecepcion.Designar"/>) y el repositorio quita la marca anterior antes
/// de poner la nueva; el índice único parcial de la tabla impide dos aunque se salte la aplicación. La auditoría (la fuente anterior y la nueva) va en la misma
/// transacción. Designar la que ya lo es no cambia ni audita nada. Un encargado recibe el rechazo con su evento y la recepción no se mueve.
/// </summary>
internal sealed class DesignarFuenteDeRecepcion(
    AutorizadorDeFuentes autorizador,
    IValidador<DesignarFuenteDeRecepcionEntrada> validador,
    IBloqueosAsesores bloqueos,
    IRepositorioDeFuentes fuentes,
    IAuditoria auditoria) : ICasoDeUso<DesignarFuenteDeRecepcionEntrada, RecepcionDesignada>
{
    public async Task<Resultado<RecepcionDesignada>> Ejecutar(DesignarFuenteDeRecepcionEntrada entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);

        if (await autorizador.ExigirAdministracionCentral(cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return FallosDeFuentes.Fallo<RecepcionDesignada>(rechazo);
        }

        var validacion = validador.Validar(entrada);
        if (!validacion.EsValido)
        {
            return FallosDeFuentes.Validacion<RecepcionDesignada>(validacion);
        }

        try
        {
            await bloqueos.Recepcion(cancelacion).ConfigureAwait(false);
        }
        catch (EsperaDeBloqueoAgotadaException)
        {
            return FallosDeFuentes.BloqueoOcupado<RecepcionDesignada>();
        }

        // Se lee bajo el bloqueo: dos administradores simultáneos se serializan y el segundo ve la recepción que dejó el primero.
        var todas = await fuentes.Listar(cancelacion).ConfigureAwait(false);
        var elegida = todas.FirstOrDefault(f => string.Equals(f.Codigo, entrada.Codigo, StringComparison.Ordinal));
        if (elegida is null)
        {
            return FallosDeFuentes.NoEncontrada<RecepcionDesignada>();
        }

        var designacion = ReglaDeRecepcion.Designar(todas, elegida.Id);
        if (designacion.Resultado != ResultadoDeDesignacion.Cambiada || designacion.Nueva is not { } nueva)
        {
            return Resultado<RecepcionDesignada>.Exito(new RecepcionDesignada(elegida.Codigo, Cambio: false));
        }

        await fuentes.CambiarRecepcion(designacion.Anterior?.Id, nueva.Id, cancelacion).ConfigureAwait(false);

        var detalle = new Dictionary<string, string?> { ["fuenteNuevaId"] = FallosDeFuentes.Identificador(nueva.Id) };
        if (designacion.Anterior is { } anterior)
        {
            detalle["fuenteAnteriorId"] = FallosDeFuentes.Identificador(anterior.Id);
        }

        await auditoria.Registrar(
            new SolicitudDeRegistro(AccionesAuditables.RecepcionDesignada)
            {
                ObjetoTipo = "fuente",
                ObjetoId = FallosDeFuentes.Identificador(nueva.Id),
                SucursalOrigen = nueva.Id,
                Detalle = detalle,
            },
            cancelacion).ConfigureAwait(false);

        return Resultado<RecepcionDesignada>.Exito(new RecepcionDesignada(nueva.Codigo, Cambio: true));
    }
}
