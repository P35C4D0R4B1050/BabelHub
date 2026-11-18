using BabelHub.Inventario.Application.Traslados;
using BabelHub.Inventario.Application.Traslados.Interno;

namespace BabelHub.Inventario.UnitTests.Traslados;

/// <summary>El caso de uso de registrar la guía de un traslado (T09).</summary>
internal sealed partial class EntornoDeTraslados
{
    public RegistrarGuiaDeTraslado Guia() => new(Ejecutor, new ValidadorDeRegistrarGuiaDeTraslado());
}
