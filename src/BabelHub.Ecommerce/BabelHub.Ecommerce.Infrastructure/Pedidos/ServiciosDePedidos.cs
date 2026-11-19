using System.Security.Cryptography;
using System.Text;
using BabelHub.Ecommerce.Application.Pedidos.Interno;
using BabelHub.Ecommerce.Domain.Pedidos;

namespace BabelHub.Ecommerce.Infrastructure.Pedidos;

/// <summary>El número de pedido de 10 caracteres sobre un generador <b>criptográfico</b> (D06): <c>RandomNumberGenerator.GetInt32</c> elige cada carácter del alfabeto sin sesgo; el formato es del Dominio.</summary>
public sealed class AleatorioGeneradorDeNumerosDePedido : IGeneradorDeNumerosDePedido
{
    public NumeroDePedido Siguiente() => NumeroDePedido.Generar(RandomNumberGenerator.GetInt32);
}

/// <summary>Los identificadores de las operaciones (entregas y anulaciones): aleatorios y sin patrón (el <c>Guid</c> de .NET sale de un generador criptográfico).</summary>
public sealed class GeneradorDeIdentificadoresDePedidos : IGeneradorDeIdentificadoresDePedidos
{
    public Guid Nuevo() => Guid.NewGuid();
}

/// <summary>
/// La equivalencia entre el código de una sucursal y su identificador (D11 de <c>ecommerce-pedidos</c>; D15 de <c>docs/03/08</c>, propuesta a confirmar por Identidad): el identificador es un <c>Guid</c> <b>derivado del código</b> (SHA-256 de un prefijo fijo y el código en minúsculas), así que el mismo código da siempre el mismo
/// identificador y no hace falta una tabla ni una llamada. <b>Falla cerrado</b>: un código vacío o con forma inválida no se resuelve, y un encargado cuyo token trae otro identificador de sucursal no coincide y se rechaza. La derivación es una propuesta: si Identidad fija otra equivalencia, solo se cambia esta clase.
/// </summary>
public sealed class CodigoResolucionDeSucursal : IResolucionDeSucursal
{
    private const string Prefijo = "babelhub:sucursal:";

    public Task<Guid?> IdentificadorDe(string codigoDeSucursal, CancellationToken cancelacion = default) => Task.FromResult(Derivar(codigoDeSucursal));

    /// <summary>El identificador de una sucursal a partir de su código (<c>null</c> si el código no tiene la forma de un código de sucursal).</summary>
    public static Guid? Derivar(string? codigoDeSucursal)
    {
        var codigo = codigoDeSucursal?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(codigo) || codigo.Length > 40 || !codigo.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'))
        {
            return null;
        }

        var huella = SHA256.HashData(Encoding.UTF8.GetBytes(Prefijo + codigo));
        return new Guid(huella.AsSpan(0, 16));
    }
}
