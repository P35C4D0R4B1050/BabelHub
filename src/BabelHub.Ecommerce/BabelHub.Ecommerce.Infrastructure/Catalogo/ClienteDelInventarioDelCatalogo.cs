using System.Globalization;
using BabelHub.Ecommerce.Application.Catalogo;
using BabelHub.Ecommerce.Application.Catalogo.Interno;
using BabelHub.Ecommerce.Application.Plataforma;

namespace BabelHub.Ecommerce.Infrastructure.Catalogo;

/// <summary>
/// El adaptador del Catálogo hacia el Inventario (T16, D01): <b>delega en <see cref="IClienteDelInventario"/> de <c>Plataforma</c></b>, que toma el token de la administración por dentro, usa la credencial de cada alcance, reenvía el origen, aplica
/// el plazo, los reintentos de lo idempotente y el cortacircuitos y nunca registra credenciales; este adaptador no abre un <c>HttpClient</c> ni toca el token. Traduce entre los modelos de ambos puertos y <b>valida las respuestas antes de usarlas</b> (SEG-13):
/// los identificadores devueltos son un subconjunto de los pedidos, las cantidades no son negativas, los textos tienen un largo acotado, y un 201 de ingreso debe coincidir con lo enviado. Un 429, un tiempo agotado, un cortacircuitos abierto o una respuesta
/// inválida son «sin respuesta» (nunca una excepción): el Catálogo usa su memoria y el alta no se revierte (CAT-12, CAT-20).
/// </summary>
public sealed class ClienteDelInventarioDelCatalogo(IClienteDelInventario cliente) : IInventarioDelCatalogo
{
    private const int MaximoDeFuentes = 100;

    private const int LargoMaximoDelCodigoDeFuente = 40;

    private const int LargoMaximoDelNombreDeFuente = 200;

    private const int LargoMaximoDeLaReferencia = 80;

    private const int LargoMaximoDeLaNota = 500;

    public async Task<bool> RegistrarEdicionImpresa(Guid edicionId, CancellationToken cancelacion = default)
    {
        var resultado = await cliente.RegistrarEdicion(new EdicionARegistrar(edicionId, "impreso"), cancelacion).ConfigureAwait(false);
        return resultado.Exitosa;
    }

    public async Task<RespuestaDeDisponibilidad?> ConsultarDisponibilidad(IReadOnlyCollection<Guid> edicionIds, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(edicionIds);
        var resultado = await cliente.ConsultarDisponibilidad(edicionIds.ToList(), cancelacion).ConfigureAwait(false);
        if (!resultado.Exitosa)
        {
            return null;
        }

        var valor = resultado.Valor;
        var ids = valor.Ediciones.Select(e => e.EdicionId).Concat(valor.NoRegistradas).ToList();
        var valida = ids.All(edicionIds.Contains)
            && ids.Distinct().Count() == ids.Count
            && valor.Ediciones.All(
                e => e.VendibleEnLaRed >= 0
                    && e.Fuentes.Count <= MaximoDeFuentes
                    && e.Fuentes.All(f => f.Vendible >= 0 && !string.IsNullOrWhiteSpace(f.CodigoDeFuente) && f.CodigoDeFuente.Length <= LargoMaximoDelCodigoDeFuente && f.Nombre is { Length: <= LargoMaximoDelNombreDeFuente }));
        if (!valida)
        {
            return null;
        }

        return new RespuestaDeDisponibilidad(
            valor.Ediciones
                .Select(e => new DisponibilidadDelInventario(e.EdicionId, e.VendibleEnLaRed, e.Fuentes.Select(f => new DisponibilidadPorFuente(f.CodigoDeFuente, f.Nombre, f.Vendible)).ToList(), e.ConsultadaEn))
                .ToList(),
            valor.NoRegistradas.ToList());
    }

    public async Task<RespuestaDeIngreso> RegistrarIngreso(SolicitudDeIngresoAlInventario solicitud, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(solicitud);

        // La clave de idempotencia es el nonce del token firmado: 32 hexadecimales, dentro de lo que admite el contrato (16 a 80 de [A-Za-z0-9_-]).
        var resultado = await cliente
            .RegistrarIngreso(
                new IngresoARegistrar(solicitud.EdicionId, null, solicitud.Cantidad, solicitud.Referencia, solicitud.Nota),
                solicitud.ClaveDeIdempotencia.ToString("N", CultureInfo.InvariantCulture),
                cancelacion)
            .ConfigureAwait(false);

        return resultado.Estado switch
        {
            EstadoDeLlamadaAlInventario.Exitosa => Validar(resultado.Valor, solicitud),
            EstadoDeLlamadaAlInventario.Rechazada => RespuestaDeIngreso.Rechazado(resultado.EstadoHttp ?? 0, resultado.Codigo),
            _ => RespuestaDeIngreso.SinRespuesta,
        };
    }

    public async Task<IReadOnlyList<IngresoDelInventario>?> ListarIngresos(Guid edicionId, int limite, CancellationToken cancelacion = default)
    {
        var resultado = await cliente.ListarIngresos(edicionId, Math.Clamp(limite, 1, 50), cancelacion).ConfigureAwait(false);
        if (!resultado.Exitosa)
        {
            return null;
        }

        // Solo ingresos de la edición pedida, con cantidades válidas y sin repetir (la conciliación depende de ello).
        var ingresos = resultado.Valor;
        if (ingresos.Count > Math.Clamp(limite, 1, 50)
            || ingresos.Any(i => i.EdicionId != edicionId || i.Cantidad < 1 || i.IngresoId == Guid.Empty || i.Referencia is { Length: > LargoMaximoDeLaReferencia } || i.Nota is { Length: > LargoMaximoDeLaNota } || string.IsNullOrWhiteSpace(i.CodigoDeFuente) || i.CodigoDeFuente.Length > LargoMaximoDelCodigoDeFuente)
            || ingresos.Select(i => i.IngresoId).Distinct().Count() != ingresos.Count)
        {
            return null;
        }

        return ingresos.Select(i => new IngresoDelInventario(i.IngresoId, i.EdicionId, i.CodigoDeFuente, i.Cantidad, i.Referencia, i.Nota, i.UsuarioId, i.Instante)).ToList();
    }

    /// <summary>Un 201 solo cuenta si es del ingreso pedido: el mismo <c>edicionId</c> y la misma cantidad; si no, el resultado es incierto (SEG-13).</summary>
    private static RespuestaDeIngreso Validar(IngresoRegistrado registrado, SolicitudDeIngresoAlInventario solicitud) =>
        registrado.IngresoId != Guid.Empty && registrado.EdicionId == solicitud.EdicionId && registrado.Cantidad == solicitud.Cantidad
            ? RespuestaDeIngreso.Creado(registrado.IngresoId)
            : RespuestaDeIngreso.SinRespuesta;
}
