using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Catalogo;

namespace BabelHub.Ecommerce.Application.Catalogo.Interno;

/// <summary>Los topes de entrada del módulo (plan §4 y §6): longitudes, listas con tope, importes y páginas.</summary>
internal static class LimitesDelCatalogo
{
    public const int LargoMaximoDeTexto = Obra.LongitudMaximaDelTexto;

    public const int LargoMaximoDeCategoria = Obra.LongitudMaximaDeLaCategoria;

    public const int LargoMinimoDeLaBusqueda = 2;

    public const int LargoMaximoDeLaBusqueda = 100;

    public const int MaximoDeEdicionesPorObra = 2;

    public const int MaximoDeTerritorios = LicenciaDigital.MaximoDeTerritorios;

    /// <summary>El largo de un código de país tal como llega (antes de normalizarlo): dos letras con espacios.</summary>
    public const int LargoMaximoDeUnTerritorio = 10;

    public const int StockMinimo = 1;

    public const int StockMaximo = 5_000;

    public const int LargoMaximoDeLaReferencia = 80;

    public const int LargoMaximoDeLaNota = 500;

    public const int LargoMaximoDelMotivoDeRetiro = Edicion.LongitudMaximaDelMotivoDeRetiro;

    public const int LargoMaximoDelToken = 512;

    public const int TamanoMinimoDePagina = 1;

    public const int TamanoMaximoDePagina = 50;

    /// <summary>Con este tope el desplazamiento <c>(página − 1) × tamaño</c> cabe siempre en un entero.</summary>
    public const int PaginaMaxima = 1_000_000;
}

/// <summary>Reglas de forma compartidas por los validadores del módulo: cada error lleva el campo y una clave, nunca el valor recibido (SEG-01).</summary>
internal static class ReglasDeEntradaDelCatalogo
{
    public static void Texto(List<ErrorDeCampo> errores, string campo, string? valor, int maximo, bool obligatorio = true, bool permiteSaltos = false)
    {
        if (string.IsNullOrWhiteSpace(valor))
        {
            if (obligatorio)
            {
                errores.Add(new ErrorDeCampo(campo, ConstructorDeValidacion.Obligatorio));
            }

            return;
        }

        if (valor.Length > maximo)
        {
            errores.Add(new ErrorDeCampo(campo, ConstructorDeValidacion.DemasiadoLargo));
        }
        else if (TieneCaracteresDeControl(valor, permiteSaltos))
        {
            errores.Add(new ErrorDeCampo(campo, ConstructorDeValidacion.FormatoInvalido));
        }
    }

    /// <summary>
    /// La descripción opcional de la obra (CAT-01, CAT-25): la regla es la del Dominio (<see cref="DescripcionDeObra"/>: 600 puntos de código, solo el salto de línea como control). El error lleva el campo y la clave, nunca el texto.
    /// </summary>
    public static void Descripcion(List<ErrorDeCampo> errores, string? valor)
    {
        DescripcionDeObra.Normalizar(valor, out var motivo);
        if (motivo is { } rechazo)
        {
            errores.Add(new ErrorDeCampo("descripcion", rechazo == MotivoDeDescripcionInvalida.DemasiadoLarga ? ConstructorDeValidacion.DemasiadoLargo : ConstructorDeValidacion.FormatoInvalido));
        }
    }

    /// <summary>Texto libre (referencia, nota, motivo): además de lo anterior, no puede llevar un dato que SEG-10 prohíbe en los registros (una tarjeta, un token, una credencial).</summary>
    public static void TextoLibre(List<ErrorDeCampo> errores, string campo, string? valor, int maximo, bool obligatorio, bool permiteSaltos = false)
    {
        var antes = errores.Count;
        Texto(errores, campo, valor, maximo, obligatorio, permiteSaltos);
        if (errores.Count == antes && !string.IsNullOrWhiteSpace(valor) && RedactorDeDatosProhibidos.ContieneDatosProhibidos(valor))
        {
            errores.Add(new ErrorDeCampo(campo, ConstructorDeValidacion.FormatoInvalido));
        }
    }

    public static void Identificador(List<ErrorDeCampo> errores, string campo, Guid? valor, bool obligatorio = true)
    {
        if (valor is null)
        {
            if (obligatorio)
            {
                errores.Add(new ErrorDeCampo(campo, ConstructorDeValidacion.Obligatorio));
            }

            return;
        }

        if (valor == Guid.Empty)
        {
            errores.Add(new ErrorDeCampo(campo, ConstructorDeValidacion.FormatoInvalido));
        }
    }

    public static void Version(List<ErrorDeCampo> errores, long version)
    {
        if (version < 1)
        {
            errores.Add(new ErrorDeCampo("version", ConstructorDeValidacion.FueraDeRango));
        }
    }

    public static void Precio(List<ErrorDeCampo> errores, string campo, decimal precio)
    {
        if (precio < 0 || precio > Domain.Catalogo.Precio.Maximo)
        {
            errores.Add(new ErrorDeCampo(campo, ConstructorDeValidacion.FueraDeRango));
        }
        else if (decimal.Round(precio, 2) != precio)
        {
            errores.Add(new ErrorDeCampo(campo, ConstructorDeValidacion.FormatoInvalido));
        }
    }

    public static void Pagina(List<ErrorDeCampo> errores, int pagina, int tamano)
    {
        if (pagina is < 1 or > LimitesDelCatalogo.PaginaMaxima)
        {
            errores.Add(new ErrorDeCampo("pagina", ConstructorDeValidacion.FueraDeRango));
        }

        if (tamano is < LimitesDelCatalogo.TamanoMinimoDePagina or > LimitesDelCatalogo.TamanoMaximoDePagina)
        {
            errores.Add(new ErrorDeCampo("tamano", ConstructorDeValidacion.FueraDeRango));
        }
    }

    public static void Licencia(List<ErrorDeCampo> errores, string prefijo, LicenciaDeEdicionEntrada licencia)
    {
        // Solo la forma que podría romper el procesamiento: el contenido (titular obligatorio, territorios con código válido, vigencia según el tipo y posterior a hoy) lo decide
        // LicenciaDigital, cuyo rechazo es ECO-LICENCIA-INVALIDA (422) y no una validación de forma.
        if (!Enum.IsDefined(licencia.Tipo))
        {
            errores.Add(new ErrorDeCampo(prefijo + "tipo", ConstructorDeValidacion.FormatoInvalido));
        }

        if (!Enum.IsDefined(licencia.Formato))
        {
            errores.Add(new ErrorDeCampo(prefijo + "formato", ConstructorDeValidacion.FormatoInvalido));
        }

        if (licencia.Titular is { Length: > LimitesDelCatalogo.LargoMaximoDeTexto })
        {
            errores.Add(new ErrorDeCampo(prefijo + "titular", ConstructorDeValidacion.DemasiadoLargo));
        }
        else if (licencia.Titular is not null && TieneCaracteresDeControl(licencia.Titular, permiteSaltos: false))
        {
            errores.Add(new ErrorDeCampo(prefijo + "titular", ConstructorDeValidacion.FormatoInvalido));
        }

        if (licencia.Territorios is { Count: > LimitesDelCatalogo.MaximoDeTerritorios })
        {
            errores.Add(new ErrorDeCampo(prefijo + "territorios", ConstructorDeValidacion.DemasiadoLargo));
        }
        else if (licencia.Territorios is not null && licencia.Territorios.Any(t => t is null || t.Length > LimitesDelCatalogo.LargoMaximoDeUnTerritorio))
        {
            errores.Add(new ErrorDeCampo(prefijo + "territorios", ConstructorDeValidacion.FormatoInvalido));
        }
    }

    public static void Edicion(List<ErrorDeCampo> errores, string prefijo, NuevaEdicionEntrada? edicion)
    {
        if (edicion is null)
        {
            errores.Add(new ErrorDeCampo(prefijo.TrimEnd('.'), ConstructorDeValidacion.Obligatorio));
            return;
        }

        if (!Enum.IsDefined(edicion.Formato))
        {
            errores.Add(new ErrorDeCampo(prefijo + "formato", ConstructorDeValidacion.FormatoInvalido));
            return;
        }

        if (string.IsNullOrWhiteSpace(edicion.Isbn))
        {
            errores.Add(new ErrorDeCampo(prefijo + "isbn", ConstructorDeValidacion.Obligatorio));
        }
        else if (!Isbn.TryCrear(edicion.Isbn, out _))
        {
            // CAT-17: el ISBN debe tener 10 o 13 dígitos con un dígito de control válido; el rechazo es de formato.
            errores.Add(new ErrorDeCampo(prefijo + "isbn", ConstructorDeValidacion.FormatoInvalido));
        }

        Precio(errores, prefijo + "precio", edicion.Precio);

        if (edicion.Formato == FormatoDeEdicion.Digital)
        {
            if (edicion.Licencia is null)
            {
                errores.Add(new ErrorDeCampo(prefijo + "licencia", ConstructorDeValidacion.Obligatorio));
            }
            else
            {
                Licencia(errores, prefijo + "licencia.", edicion.Licencia);
            }

            Identificador(errores, prefijo + "archivoRef", edicion.ArchivoRef);
            if (edicion.StockInicial is not null)
            {
                // Una edición digital nunca tiene stock (regla 12).
                errores.Add(new ErrorDeCampo(prefijo + "stockInicial", ConstructorDeValidacion.FormatoInvalido));
            }
        }
        else
        {
            if (edicion.Licencia is not null)
            {
                errores.Add(new ErrorDeCampo(prefijo + "licencia", ConstructorDeValidacion.FormatoInvalido));
            }

            if (edicion.ArchivoRef is not null)
            {
                errores.Add(new ErrorDeCampo(prefijo + "archivoRef", ConstructorDeValidacion.FormatoInvalido));
            }

            StockInicial(errores, prefijo + "stockInicial", edicion.StockInicial);
        }
    }

    /// <summary>El stock inicial es opcional: ausente es «sin stock inicial»; si viene, un entero de 1 a 5 000 y cualquier otro valor, incluido 0, se rechaza sin crear nada (CAT-23).</summary>
    public static void StockInicial(List<ErrorDeCampo> errores, string campo, int? stock)
    {
        if (stock is < LimitesDelCatalogo.StockMinimo or > LimitesDelCatalogo.StockMaximo)
        {
            errores.Add(new ErrorDeCampo(campo, ConstructorDeValidacion.FueraDeRango));
        }
    }

    internal static bool TieneCaracteresDeControl(string valor, bool permiteSaltos) =>
        valor.Any(c => char.IsControl(c) && !(permiteSaltos && c is '\n' or '\r' or '\t'));
}

internal sealed class ValidadorDeRegistrarObra : IValidador<RegistrarObraEntrada>
{
    public ResultadoDeValidacion Validar(RegistrarObraEntrada entrada)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var errores = new List<ErrorDeCampo>();
        ReglasDeEntradaDelCatalogo.Texto(errores, "titulo", entrada.Titulo, LimitesDelCatalogo.LargoMaximoDeTexto);
        ReglasDeEntradaDelCatalogo.Texto(errores, "autor", entrada.Autor, LimitesDelCatalogo.LargoMaximoDeTexto);
        ReglasDeEntradaDelCatalogo.Texto(errores, "editorial", entrada.Editorial, LimitesDelCatalogo.LargoMaximoDeTexto);
        ReglasDeEntradaDelCatalogo.Texto(errores, "categoria", entrada.Categoria, LimitesDelCatalogo.LargoMaximoDeCategoria);
        ReglasDeEntradaDelCatalogo.Descripcion(errores, entrada.Descripcion);
        ReglasDeEntradaDelCatalogo.Identificador(errores, "portadaRef", entrada.PortadaRef);

        if (entrada.Ediciones is null || entrada.Ediciones.Count == 0)
        {
            // CAT-04: toda obra tiene al menos una edición.
            errores.Add(new ErrorDeCampo("ediciones", ConstructorDeValidacion.Obligatorio));
        }
        else if (entrada.Ediciones.Count > LimitesDelCatalogo.MaximoDeEdicionesPorObra)
        {
            errores.Add(new ErrorDeCampo("ediciones", ConstructorDeValidacion.DemasiadoLargo));
        }
        else
        {
            for (var i = 0; i < entrada.Ediciones.Count; i++)
            {
                ReglasDeEntradaDelCatalogo.Edicion(errores, $"ediciones[{i}].", entrada.Ediciones[i]);
            }
        }

        return errores.Count == 0 ? ResultadoDeValidacion.Valido : ResultadoDeValidacion.Invalido(errores);
    }
}

internal sealed class ValidadorDeEditarObra : IValidador<EditarObraEntrada>
{
    public ResultadoDeValidacion Validar(EditarObraEntrada entrada)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var errores = new List<ErrorDeCampo>();
        ReglasDeEntradaDelCatalogo.Identificador(errores, "obraId", entrada.ObraId);
        ReglasDeEntradaDelCatalogo.Version(errores, entrada.Version);
        ReglasDeEntradaDelCatalogo.Texto(errores, "titulo", entrada.Titulo, LimitesDelCatalogo.LargoMaximoDeTexto);
        ReglasDeEntradaDelCatalogo.Texto(errores, "autor", entrada.Autor, LimitesDelCatalogo.LargoMaximoDeTexto);
        ReglasDeEntradaDelCatalogo.Texto(errores, "editorial", entrada.Editorial, LimitesDelCatalogo.LargoMaximoDeTexto);
        ReglasDeEntradaDelCatalogo.Texto(errores, "categoria", entrada.Categoria, LimitesDelCatalogo.LargoMaximoDeCategoria);
        ReglasDeEntradaDelCatalogo.Descripcion(errores, entrada.Descripcion);
        ReglasDeEntradaDelCatalogo.Identificador(errores, "portadaRef", entrada.PortadaRef, obligatorio: false);
        return errores.Count == 0 ? ResultadoDeValidacion.Valido : ResultadoDeValidacion.Invalido(errores);
    }
}

internal sealed class ValidadorDeDarDeBajaObra : IValidador<DarDeBajaObraEntrada>
{
    public ResultadoDeValidacion Validar(DarDeBajaObraEntrada entrada)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var errores = new List<ErrorDeCampo>();
        ReglasDeEntradaDelCatalogo.Identificador(errores, "obraId", entrada.ObraId);
        ReglasDeEntradaDelCatalogo.Version(errores, entrada.Version);
        return errores.Count == 0 ? ResultadoDeValidacion.Valido : ResultadoDeValidacion.Invalido(errores);
    }
}

internal sealed class ValidadorDeRegistrarEdicion : IValidador<RegistrarEdicionEntrada>
{
    public ResultadoDeValidacion Validar(RegistrarEdicionEntrada entrada)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var errores = new List<ErrorDeCampo>();
        ReglasDeEntradaDelCatalogo.Identificador(errores, "obraId", entrada.ObraId);
        ReglasDeEntradaDelCatalogo.Version(errores, entrada.Version);
        ReglasDeEntradaDelCatalogo.Edicion(errores, "edicion.", entrada.Edicion);
        return errores.Count == 0 ? ResultadoDeValidacion.Valido : ResultadoDeValidacion.Invalido(errores);
    }
}

internal sealed class ValidadorDeEditarEdicion : IValidador<EditarEdicionEntrada>
{
    public ResultadoDeValidacion Validar(EditarEdicionEntrada entrada)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var errores = new List<ErrorDeCampo>();
        ReglasDeEntradaDelCatalogo.Identificador(errores, "obraId", entrada.ObraId);
        ReglasDeEntradaDelCatalogo.Identificador(errores, "edicionId", entrada.EdicionId);
        ReglasDeEntradaDelCatalogo.Version(errores, entrada.Version);
        if (entrada.Precio is { } precio)
        {
            ReglasDeEntradaDelCatalogo.Precio(errores, "precio", precio);
        }

        if (entrada.Licencia is not null)
        {
            ReglasDeEntradaDelCatalogo.Licencia(errores, "licencia.", entrada.Licencia);
        }

        ReglasDeEntradaDelCatalogo.Identificador(errores, "archivoRef", entrada.ArchivoRef, obligatorio: false);
        return errores.Count == 0 ? ResultadoDeValidacion.Valido : ResultadoDeValidacion.Invalido(errores);
    }
}

internal sealed class ValidadorDeDarDeBajaEdicion : IValidador<DarDeBajaEdicionEntrada>
{
    public ResultadoDeValidacion Validar(DarDeBajaEdicionEntrada entrada)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var errores = new List<ErrorDeCampo>();
        ReglasDeEntradaDelCatalogo.Identificador(errores, "obraId", entrada.ObraId);
        ReglasDeEntradaDelCatalogo.Identificador(errores, "edicionId", entrada.EdicionId);
        ReglasDeEntradaDelCatalogo.Version(errores, entrada.Version);
        return errores.Count == 0 ? ResultadoDeValidacion.Valido : ResultadoDeValidacion.Invalido(errores);
    }
}

internal sealed class ValidadorDeRetirarEdicionDigital : IValidador<RetirarEdicionDigitalEntrada>
{
    public ResultadoDeValidacion Validar(RetirarEdicionDigitalEntrada entrada)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var errores = new List<ErrorDeCampo>();
        ReglasDeEntradaDelCatalogo.Identificador(errores, "obraId", entrada.ObraId);
        ReglasDeEntradaDelCatalogo.Identificador(errores, "edicionId", entrada.EdicionId);
        ReglasDeEntradaDelCatalogo.TextoLibre(errores, "motivo", entrada.Motivo, LimitesDelCatalogo.LargoMaximoDelMotivoDeRetiro, obligatorio: true);
        return errores.Count == 0 ? ResultadoDeValidacion.Valido : ResultadoDeValidacion.Invalido(errores);
    }
}

internal sealed class ValidadorDeEmitirClaveDeIngreso : IValidador<EmitirClaveDeIngresoEntrada>
{
    public ResultadoDeValidacion Validar(EmitirClaveDeIngresoEntrada entrada)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var errores = new List<ErrorDeCampo>();
        ReglasDeEntradaDelCatalogo.Identificador(errores, "edicionId", entrada.EdicionId);
        return errores.Count == 0 ? ResultadoDeValidacion.Valido : ResultadoDeValidacion.Invalido(errores);
    }
}

internal sealed class ValidadorDeRegistrarIngreso : IValidador<RegistrarIngresoEntrada>
{
    public ResultadoDeValidacion Validar(RegistrarIngresoEntrada entrada)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var errores = new List<ErrorDeCampo>();
        ReglasDeEntradaDelCatalogo.Identificador(errores, "edicionId", entrada.EdicionId);

        // CAT-23: un entero de 1 a 5 000; cualquier otro valor se rechaza sin crear ni cambiar nada.
        if (entrada.Cantidad is < LimitesDelCatalogo.StockMinimo or > LimitesDelCatalogo.StockMaximo)
        {
            errores.Add(new ErrorDeCampo("cantidad", ConstructorDeValidacion.FueraDeRango));
        }

        ReglasDeEntradaDelCatalogo.TextoLibre(errores, "referencia", entrada.Referencia, LimitesDelCatalogo.LargoMaximoDeLaReferencia, obligatorio: false);
        ReglasDeEntradaDelCatalogo.TextoLibre(errores, "nota", entrada.Nota, LimitesDelCatalogo.LargoMaximoDeLaNota, obligatorio: false, permiteSaltos: true);
        ReglasDeEntradaDelCatalogo.Texto(errores, "token", entrada.Token, LimitesDelCatalogo.LargoMaximoDelToken);
        return errores.Count == 0 ? ResultadoDeValidacion.Valido : ResultadoDeValidacion.Invalido(errores);
    }
}

internal sealed class ValidadorDeReintentarIngreso : IValidador<ReintentarIngresoEntrada>
{
    public ResultadoDeValidacion Validar(ReintentarIngresoEntrada entrada)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var errores = new List<ErrorDeCampo>();
        ReglasDeEntradaDelCatalogo.Identificador(errores, "ingresoId", entrada.IngresoSolicitadoId);
        return errores.Count == 0 ? ResultadoDeValidacion.Valido : ResultadoDeValidacion.Invalido(errores);
    }
}

internal sealed class ValidadorDeConciliarIngreso : IValidador<ConciliarIngresoEntrada>
{
    public ResultadoDeValidacion Validar(ConciliarIngresoEntrada entrada)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var errores = new List<ErrorDeCampo>();
        ReglasDeEntradaDelCatalogo.Identificador(errores, "ingresoId", entrada.IngresoSolicitadoId);
        if (!Enum.IsDefined(entrada.Resolucion))
        {
            errores.Add(new ErrorDeCampo("resolucion", ConstructorDeValidacion.FormatoInvalido));
        }

        return errores.Count == 0 ? ResultadoDeValidacion.Valido : ResultadoDeValidacion.Invalido(errores);
    }
}

internal sealed class ValidadorDeListarIngresos : IValidador<ListarIngresosEntrada>
{
    public ResultadoDeValidacion Validar(ListarIngresosEntrada entrada)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var errores = new List<ErrorDeCampo>();
        ReglasDeEntradaDelCatalogo.Identificador(errores, "edicionId", entrada.EdicionId);
        return errores.Count == 0 ? ResultadoDeValidacion.Valido : ResultadoDeValidacion.Invalido(errores);
    }
}

internal sealed class ValidadorDeSubirArchivo : IValidador<SubirArchivoEntrada>
{
    public ResultadoDeValidacion Validar(SubirArchivoEntrada entrada)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var errores = new List<ErrorDeCampo>();
        if (!Enum.IsDefined(entrada.Finalidad))
        {
            errores.Add(new ErrorDeCampo("finalidad", ConstructorDeValidacion.FormatoInvalido));
        }

        if (entrada.Contenido is null)
        {
            errores.Add(new ErrorDeCampo("archivo", ConstructorDeValidacion.Obligatorio));
        }

        return errores.Count == 0 ? ResultadoDeValidacion.Valido : ResultadoDeValidacion.Invalido(errores);
    }
}

internal sealed class ValidadorDeBusquedaDeObras : IValidador<BusquedaDeObras>
{
    public ResultadoDeValidacion Validar(BusquedaDeObras entrada)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var errores = new List<ErrorDeCampo>();

        if (!string.IsNullOrEmpty(entrada.Texto))
        {
            // «q»: de 2 a 100 caracteres (los comodines se escapan en la consulta, D10).
            if (entrada.Texto.Trim().Length < LimitesDelCatalogo.LargoMinimoDeLaBusqueda)
            {
                errores.Add(new ErrorDeCampo("q", ConstructorDeValidacion.DemasiadoCorto));
            }
            else if (entrada.Texto.Length > LimitesDelCatalogo.LargoMaximoDeLaBusqueda)
            {
                errores.Add(new ErrorDeCampo("q", ConstructorDeValidacion.DemasiadoLargo));
            }
            else if (ReglasDeEntradaDelCatalogo.TieneCaracteresDeControl(entrada.Texto, permiteSaltos: false))
            {
                // Un carácter NUL o de control llegaría al parámetro de PostgreSQL y daría un 500 en lugar de un 400 (SEG-01, SEG-03).
                errores.Add(new ErrorDeCampo("q", ConstructorDeValidacion.FormatoInvalido));
            }
        }

        ReglasDeEntradaDelCatalogo.Texto(errores, "autor", entrada.Autor, LimitesDelCatalogo.LargoMaximoDeTexto, obligatorio: false);
        ReglasDeEntradaDelCatalogo.Texto(errores, "editorial", entrada.Editorial, LimitesDelCatalogo.LargoMaximoDeTexto, obligatorio: false);
        ReglasDeEntradaDelCatalogo.Texto(errores, "categoria", entrada.Categoria, LimitesDelCatalogo.LargoMaximoDeCategoria, obligatorio: false);

        if (entrada.Formato is { } formato && !Enum.IsDefined(formato))
        {
            errores.Add(new ErrorDeCampo("formato", ConstructorDeValidacion.FormatoInvalido));
        }

        if (entrada.PrecioMinimo is { } minimo)
        {
            ReglasDeEntradaDelCatalogo.Precio(errores, "precioMin", minimo);
        }

        if (entrada.PrecioMaximo is { } maximo)
        {
            ReglasDeEntradaDelCatalogo.Precio(errores, "precioMax", maximo);
        }

        if (entrada.PrecioMinimo is { } desde && entrada.PrecioMaximo is { } hasta && desde > hasta)
        {
            errores.Add(new ErrorDeCampo("precioMax", ConstructorDeValidacion.FueraDeRango));
        }

        ReglasDeEntradaDelCatalogo.Pagina(errores, entrada.Pagina, entrada.Tamano);
        return errores.Count == 0 ? ResultadoDeValidacion.Valido : ResultadoDeValidacion.Invalido(errores);
    }
}

internal sealed class ValidadorDeListarObrasDeAdministracion : IValidador<ListarObrasDeAdministracionEntrada>
{
    public ResultadoDeValidacion Validar(ListarObrasDeAdministracionEntrada entrada)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var errores = new List<ErrorDeCampo>();
        ReglasDeEntradaDelCatalogo.Texto(errores, "q", entrada.Texto, LimitesDelCatalogo.LargoMaximoDeLaBusqueda, obligatorio: false);
        ReglasDeEntradaDelCatalogo.Pagina(errores, entrada.Pagina, entrada.Tamano);
        return errores.Count == 0 ? ResultadoDeValidacion.Valido : ResultadoDeValidacion.Invalido(errores);
    }
}

/// <summary>Los identificadores de varias ediciones (hasta 50, sin vacíos ni repetidos: un lote con el mismo identificador dos veces se rechaza): lo comparten <c>ConsultarDisponibilidad</c> y <c>ObtenerEdiciones</c>.</summary>
internal static class ValidadorDeEdicionesPorIdentificador
{
    public const int Maximo = 50;

    public static ResultadoDeValidacion Validar(IReadOnlyCollection<Guid>? ids)
    {
        var errores = new List<ErrorDeCampo>();
        if (ids is null || ids.Count == 0)
        {
            errores.Add(new ErrorDeCampo("edicionIds", ConstructorDeValidacion.Obligatorio));
        }
        else if (ids.Count > Maximo)
        {
            errores.Add(new ErrorDeCampo("edicionIds", ConstructorDeValidacion.DemasiadoLargo));
        }
        else if (ids.Any(i => i == Guid.Empty) || ids.Distinct().Count() != ids.Count)
        {
            errores.Add(new ErrorDeCampo("edicionIds", ConstructorDeValidacion.FormatoInvalido));
        }

        return errores.Count == 0 ? ResultadoDeValidacion.Valido : ResultadoDeValidacion.Invalido(errores);
    }
}
