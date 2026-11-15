using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Domain.Fuentes;

namespace BabelHub.Inventario.Application.Fuentes.Interno;

/// <summary>Los límites de las consultas de lectura que fija el plan (§6): hasta 50 ediciones por consulta y páginas de 1 a 100.</summary>
internal static class LimitesDeConsulta
{
    public const int MaximoDeEdiciones = 50;

    public const int TamanoMinimoDePagina = 1;

    public const int TamanoMaximoDePagina = 100;

    /// <summary>Tope de la página: con él el desplazamiento <c>(página − 1) × tamaño</c> cabe siempre en un entero.</summary>
    public const int PaginaMaxima = 1_000_000;
}

/// <summary>Reglas de forma compartidas por los validadores del módulo: cada error lleva el campo y una clave, nunca el valor recibido (SEG-01).</summary>
internal static class ReglasDeEntrada
{
    public static void Codigo(List<ErrorDeCampo> errores, string campo, string? valor)
    {
        if (string.IsNullOrEmpty(valor))
        {
            errores.Add(new ErrorDeCampo(campo, ConstructorDeValidacion.Obligatorio));
        }
        else if (!Fuente.EsCodigoValido(valor))
        {
            errores.Add(new ErrorDeCampo(campo, ConstructorDeValidacion.FormatoInvalido));
        }
    }

    public static void DatosDeFuente(List<ErrorDeCampo> errores, string? nombre, string? region, string? contacto)
    {
        if (string.IsNullOrWhiteSpace(nombre))
        {
            errores.Add(new ErrorDeCampo("nombre", ConstructorDeValidacion.Obligatorio));
        }
        else if (!Fuente.EsNombreValido(nombre))
        {
            errores.Add(new ErrorDeCampo("nombre", nombre.Length > Fuente.LargoMaximoDelNombre ? ConstructorDeValidacion.DemasiadoLargo : ConstructorDeValidacion.FormatoInvalido));
        }

        if (string.IsNullOrWhiteSpace(region))
        {
            errores.Add(new ErrorDeCampo("region", ConstructorDeValidacion.Obligatorio));
        }
        else if (!Fuente.EsRegionValida(region) || !RegionesDeEntrega.Es(region))
        {
            errores.Add(new ErrorDeCampo("region", ConstructorDeValidacion.FormatoInvalido));
        }

        if (contacto is not null && !Fuente.EsContactoValido(contacto))
        {
            errores.Add(new ErrorDeCampo("contactoDeNotificacion", ConstructorDeValidacion.FormatoInvalido));
        }
    }

    public static void Ediciones(List<ErrorDeCampo> errores, string campo, IReadOnlyCollection<Guid>? ediciones, bool obligatorio)
    {
        if (ediciones is null || ediciones.Count == 0)
        {
            if (obligatorio)
            {
                errores.Add(new ErrorDeCampo(campo, ConstructorDeValidacion.Obligatorio));
            }

            return;
        }

        if (ediciones.Count > LimitesDeConsulta.MaximoDeEdiciones)
        {
            errores.Add(new ErrorDeCampo(campo, ConstructorDeValidacion.DemasiadoLargo));
        }
        else if (ediciones.Any(e => e == Guid.Empty))
        {
            errores.Add(new ErrorDeCampo(campo, ConstructorDeValidacion.FormatoInvalido));
        }
    }

    public static ResultadoDeValidacion Resultado(List<ErrorDeCampo> errores) => errores.Count == 0 ? ResultadoDeValidacion.Valido : ResultadoDeValidacion.Invalido(errores);
}

internal sealed class ValidadorDeRegistrarFuente : IValidador<RegistrarFuenteEntrada>
{
    public ResultadoDeValidacion Validar(RegistrarFuenteEntrada entrada)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var errores = new List<ErrorDeCampo>();
        ReglasDeEntrada.Codigo(errores, "codigo", entrada.Codigo);
        ReglasDeEntrada.DatosDeFuente(errores, entrada.Nombre, entrada.Region, entrada.ContactoDeNotificacion);
        return ReglasDeEntrada.Resultado(errores);
    }
}

internal sealed class ValidadorDeEditarFuente : IValidador<EditarFuenteEntrada>
{
    public ResultadoDeValidacion Validar(EditarFuenteEntrada entrada)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var errores = new List<ErrorDeCampo>();
        ReglasDeEntrada.Codigo(errores, "codigo", entrada.Codigo);
        ReglasDeEntrada.DatosDeFuente(errores, entrada.Nombre, entrada.Region, entrada.ContactoDeNotificacion);
        return ReglasDeEntrada.Resultado(errores);
    }
}

internal sealed class ValidadorDeDesignarRecepcion : IValidador<DesignarFuenteDeRecepcionEntrada>
{
    public ResultadoDeValidacion Validar(DesignarFuenteDeRecepcionEntrada entrada)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var errores = new List<ErrorDeCampo>();
        ReglasDeEntrada.Codigo(errores, "codigo", entrada.Codigo);
        return ReglasDeEntrada.Resultado(errores);
    }
}

internal sealed class ValidadorDeRegistrarRuta : IValidador<RegistrarRutaEntrada>
{
    public ResultadoDeValidacion Validar(RegistrarRutaEntrada entrada)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var errores = new List<ErrorDeCampo>();
        ReglasDeEntrada.Codigo(errores, "codigoDeOrigen", entrada.CodigoDeOrigen);
        ReglasDeEntrada.Codigo(errores, "codigoDeDestino", entrada.CodigoDeDestino);
        return ReglasDeEntrada.Resultado(errores);
    }
}

internal sealed class ValidadorDeConsultarPlazo : IValidador<ConsultarPlazoDeRutaEntrada>
{
    public ResultadoDeValidacion Validar(ConsultarPlazoDeRutaEntrada entrada)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var errores = new List<ErrorDeCampo>();
        ReglasDeEntrada.Codigo(errores, "codigoDeOrigen", entrada.CodigoDeOrigen);
        ReglasDeEntrada.Codigo(errores, "codigoDeDestino", entrada.CodigoDeDestino);
        return ReglasDeEntrada.Resultado(errores);
    }
}

internal sealed class ValidadorDeRegistrarFicha : IValidador<RegistrarFichaDeEdicionEntrada>
{
    public ResultadoDeValidacion Validar(RegistrarFichaDeEdicionEntrada entrada)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var errores = new List<ErrorDeCampo>();
        if (entrada.EdicionId == Guid.Empty)
        {
            errores.Add(new ErrorDeCampo("edicionId", ConstructorDeValidacion.Obligatorio));
        }

        if (!Enum.IsDefined(entrada.Formato))
        {
            errores.Add(new ErrorDeCampo("formato", ConstructorDeValidacion.FormatoInvalido));
        }

        return ReglasDeEntrada.Resultado(errores);
    }
}

internal sealed class ValidadorDeConsultarDisponibilidad : IValidador<ConsultarDisponibilidadEntrada>
{
    public ResultadoDeValidacion Validar(ConsultarDisponibilidadEntrada entrada)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var errores = new List<ErrorDeCampo>();
        ReglasDeEntrada.Ediciones(errores, "edicionIds", entrada.EdicionIds, obligatorio: true);
        return ReglasDeEntrada.Resultado(errores);
    }
}

internal sealed class ValidadorDeConsultarFuente : IValidador<ConsultarFuenteEntrada>
{
    public ResultadoDeValidacion Validar(ConsultarFuenteEntrada entrada)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var errores = new List<ErrorDeCampo>();
        ReglasDeEntrada.Codigo(errores, "codigo", entrada.Codigo);
        return ReglasDeEntrada.Resultado(errores);
    }
}

internal sealed class ValidadorDeConsultarExistencias : IValidador<ConsultarExistenciasDeSucursalEntrada>
{
    public ResultadoDeValidacion Validar(ConsultarExistenciasDeSucursalEntrada entrada)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var errores = new List<ErrorDeCampo>();
        ReglasDeEntrada.Codigo(errores, "codigoDeFuente", entrada.CodigoDeFuente);
        ReglasDeEntrada.Ediciones(errores, "edicionId", entrada.EdicionIds, obligatorio: false);

        if (entrada.Estado is { } estado && !Enum.IsDefined(estado))
        {
            errores.Add(new ErrorDeCampo("estado", ConstructorDeValidacion.FormatoInvalido));
        }

        if (entrada.Pagina is < 1 or > LimitesDeConsulta.PaginaMaxima)
        {
            errores.Add(new ErrorDeCampo("pagina", ConstructorDeValidacion.FueraDeRango));
        }

        if (entrada.Tamano is < LimitesDeConsulta.TamanoMinimoDePagina or > LimitesDeConsulta.TamanoMaximoDePagina)
        {
            errores.Add(new ErrorDeCampo("tamano", ConstructorDeValidacion.FueraDeRango));
        }

        return ReglasDeEntrada.Resultado(errores);
    }
}

internal sealed class ValidadorDeConsultarUnidadesVendidas : IValidador<ConsultarUnidadesVendidasEntrada>
{
    public ResultadoDeValidacion Validar(ConsultarUnidadesVendidasEntrada entrada)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var errores = new List<ErrorDeCampo>();
        if (entrada.CodigoDeFuente is not null)
        {
            ReglasDeEntrada.Codigo(errores, "codigoDeFuente", entrada.CodigoDeFuente);
        }

        return ReglasDeEntrada.Resultado(errores);
    }
}

internal sealed class ValidadorDeConsultarContacto : IValidador<ConsultarContactoDeNotificacionEntrada>
{
    public ResultadoDeValidacion Validar(ConsultarContactoDeNotificacionEntrada entrada)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var errores = new List<ErrorDeCampo>();
        ReglasDeEntrada.Codigo(errores, "codigoDeSucursal", entrada.CodigoDeSucursal);
        return ReglasDeEntrada.Resultado(errores);
    }
}
