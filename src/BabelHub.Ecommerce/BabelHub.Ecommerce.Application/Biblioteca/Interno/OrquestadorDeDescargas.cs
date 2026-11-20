using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using BabelHub.Ecommerce.Application.Catalogo;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Biblioteca;

namespace BabelHub.Ecommerce.Application.Biblioteca.Interno;

/// <summary>El nombre del archivo que ve el navegador: derivado solo del título de Catálogo, sin caracteres de control, comillas ni separadores de ruta y sin ningún dato del cliente (plan §1).</summary>
internal static class NombreDeArchivoDeDescarga
{
    private const int LargoMaximoDelTitulo = 80;

    public static string Para(string? titulo, FormatoDeArchivoDigital formato)
    {
        var limpio = new StringBuilder();
        foreach (var c in titulo ?? string.Empty)
        {
            if (char.IsLetterOrDigit(c) || c is '-' or '_' or '.' or '(' or ')')
            {
                limpio.Append(c);
            }
            else if (char.IsWhiteSpace(c) && limpio.Length > 0 && limpio[^1] != ' ')
            {
                limpio.Append(' ');
            }
        }

        var texto = limpio.ToString().Trim().Trim('.');
        if (texto.Length > LargoMaximoDelTitulo)
        {
            texto = texto[..LargoMaximoDelTitulo].TrimEnd();
        }

        if (texto.Length == 0)
        {
            texto = "libro";
        }

        return texto + (formato == FormatoDeArchivoDigital.Pdf ? ".pdf" : ".epub");
    }
}

/// <summary>Traduce el resultado del almacén de contadores a un fallo del módulo (SEG-07, SEG-26): el límite excedido lleva el tiempo de espera, el motivo y el instante de nueva disponibilidad; el almacén caído falla cerrado.</summary>
internal static class LimiteDeDescargas
{
    public static ErrorDeAplicacion? Traducir(ResultadoDeLimite resultado, ClaveDePolitica politica) => resultado.Estado switch
    {
        EstadoDeLimite.Permitido or EstadoDeLimite.RepeticionCompletada => null,
        EstadoDeLimite.Excedido => new ErrorDeAplicacion(
            CategoriaDeError.Limite,
            ClavesDeBiblioteca.LimiteExcedido,
            [
                new ErrorDeCampo(ClavesDeBiblioteca.EsperaEnSegundos, (resultado.EsperaEnSegundos ?? 1).ToString(CultureInfo.InvariantCulture)),
                new ErrorDeCampo(ClavesDeBiblioteca.Motivo, resultado.Motivo ?? politica.Valor),
                new ErrorDeCampo(ClavesDeBiblioteca.DisponibleDesde, (resultado.DisponibleDesde ?? DateTimeOffset.UtcNow.AddSeconds(resultado.EsperaEnSegundos ?? 1)).ToString("O", CultureInfo.InvariantCulture)),
            ]),
        EstadoDeLimite.RepeticionEnCurso or EstadoDeLimite.ClaveEnConflicto => new ErrorDeAplicacion(CategoriaDeError.Conflicto, ClavesDeBiblioteca.Conflicto),
        _ => new ErrorDeAplicacion(CategoriaDeError.NoDisponible, ClavesDeBiblioteca.ServicioNoDisponible),
    };
}

/// <summary>
/// <c>IDescargasDeLaBiblioteca.SolicitarEnlace</c> (T06; BIB-03 a BIB-07, BIB-10, BIB-13, BIB-15, BIB-18): el <b>orquestador sin transacción</b> con pasos cortos y ninguna llamada de red dentro de una transacción (D05).
/// Orden: rol → clave bien formada → <b>límite</b> (10 por cliente y hora; la clave del llamador = cuenta + acceso + clave de un solo uso, así que repetir esa combinación no cuenta y la misma clave contra dos accesos cuenta dos veces; falla cerrada) →
/// textos legales pendientes → acceso filtrado por la cuenta (ajeno o inexistente: el mismo <c>ECO-NO-ENCONTRADO</c> y un acceso denegado) → estado vigente → <b>clave vigente: el mismo enlace sin contar, también con el cupo agotado</b> → cupo →
/// el archivo existe en el almacén (<c>HEAD</c> fuera de transacción) → <see cref="ConsumirDescarga"/>. La URL nunca se guarda ni se registra (SEG-10).
/// </summary>
internal sealed class OrquestadorDeDescargas(
    AutorizadorDeBiblioteca autorizador,
    IControlDeLimites limites,
    IRepositorioDeAccesos accesos,
    IRepositorioDeDescargas descargas,
    IReferenciaDeArchivosDelCatalogo referencias,
    IConsultaDeCatalogo catalogo,
    IEntregaDeArchivos entrega,
    ConsumirDescarga consumir,
    IPublicadorDeAlertasTecnicas alertas,
    IReloj reloj) : IDescargasDeLaBiblioteca
{
    public async Task<Resultado<EnlaceDeDescarga>> SolicitarEnlace(Guid accesoId, string claveDeIdempotencia, CancellationToken cancelacion = default)
    {
        // (0) Rol, clave bien formada y límite, antes de cualquier lectura (D15).
        var cliente = await autorizador.ExigirCliente(cancelacion).ConfigureAwait(false);
        if (!cliente.EsExito)
        {
            return FallosDeBiblioteca.Fallo<EnlaceDeDescarga>(cliente.Error!);
        }

        var cuenta = cliente.Valor;
        var clave = ClaveDeDescarga.Crear(claveDeIdempotencia);
        if (clave is null)
        {
            return FallosDeBiblioteca.Validacion<EnlaceDeDescarga>();
        }

        var rechazoDeLimite = await ReservarElLimite(cuenta, accesoId, clave, cancelacion).ConfigureAwait(false);
        if (rechazoDeLimite is not null)
        {
            return FallosDeBiblioteca.Fallo<EnlaceDeDescarga>(rechazoDeLimite);
        }

        if (await autorizador.ExigirTextosAlDia(cuenta, cancelacion).ConfigureAwait(false) is { } pendiente)
        {
            return FallosDeBiblioteca.Fallo<EnlaceDeDescarga>(pendiente);
        }

        // (1) El acceso, sin bloqueo y filtrado por la cuenta del actor.
        Acceso? acceso;
        try
        {
            acceso = accesoId == Guid.Empty ? null : await accesos.Obtener(accesoId, cuenta, cancelacion).ConfigureAwait(false);
        }
        catch (BibliotecaNoDisponibleException)
        {
            return FallosDeBiblioteca.NoDisponible<EnlaceDeDescarga>();
        }

        if (acceso is null)
        {
            return FallosDeBiblioteca.Fallo<EnlaceDeDescarga>(await autorizador.NoEncontradoYDenegado(cancelacion).ConfigureAwait(false));
        }

        var ahora = AlSegundo(reloj.Ahora);
        return await Entregar(acceso, clave, ahora, cancelacion).ConfigureAwait(false);
    }

    private async Task<Resultado<EnlaceDeDescarga>> Entregar(Acceso acceso, ClaveDeDescarga clave, DateTimeOffset ahora, CancellationToken cancelacion)
    {
        if (acceso.Estado != EstadoDeAcceso.Vigente)
        {
            return FallosDeBiblioteca.De<EnlaceDeDescarga>(CategoriaDeError.Conflicto, ClavesDeBiblioteca.AccesoNoDisponible);
        }

        // (2) El archivo y su nombre, de Catálogo (también de una edición retirada o con la licencia vencida: el acceso se conserva, BIB-09).
        var referencia = await referencias.ObtenerDeEdicion(acceso.EdicionId, cancelacion).ConfigureAwait(false);
        if (!referencia.EsExito)
        {
            return referencia.Error!.Categoria == CategoriaDeError.NoEncontrado
                ? await ArchivoFaltante(ahora, cancelacion).ConfigureAwait(false)
                : FallosDeBiblioteca.NoDisponible<EnlaceDeDescarga>();
        }

        var nombre = await NombreDelArchivo(acceso.EdicionId, referencia.Valor.Formato, cancelacion).ConfigureAwait(false);

        // (3) Una clave con un enlace vigente: el mismo enlace sin contar, también con el cupo agotado (BIB-05, D15).
        ClaveRegistrada? registrada;
        try
        {
            registrada = await descargas.ObtenerClave(acceso.Id, clave, cancelacion).ConfigureAwait(false);
        }
        catch (BibliotecaNoDisponibleException)
        {
            return FallosDeBiblioteca.NoDisponible<EnlaceDeDescarga>();
        }

        if (registrada is not null && registrada.EstaVigente(ahora))
        {
            return Reconstruir(registrada, referencia.Valor.ClaveEnAlmacen, nombre);
        }

        // (4) El cupo (BIB-06) y (5) la existencia del archivo, fuera de toda transacción (BIB-13, D05).
        if (acceso.DescargasUsadas >= PoliticaDeDescargas.MaximoDeDescargas)
        {
            return FallosDeBiblioteca.De<EnlaceDeDescarga>(CategoriaDeError.Conflicto, ClavesDeBiblioteca.LimiteDeDescargas);
        }

        switch (await entrega.ExisteArchivo(referencia.Valor.ClaveEnAlmacen, cancelacion).ConfigureAwait(false))
        {
            case ExistenciaDelArchivo.NoExiste:
                return await ArchivoFaltante(ahora, cancelacion).ConfigureAwait(false);
            case ExistenciaDelArchivo.NoDisponible:
                return FallosDeBiblioteca.NoDisponible<EnlaceDeDescarga>();
        }

        // (6) La transacción corta; fuera de ella se clasifica lo que no afectó filas.
        SalidaDeConsumo salida;
        try
        {
            salida = await consumir.Ejecutar(
                new EntradaDeConsumo(acceso, clave, referencia.Valor.ClaveEnAlmacen, nombre, ahora, DiaDeLima(ahora)),
                cancelacion).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is BibliotecaNoDisponibleException or InvalidOperationException)
        {
            // La base caída o la firma imposible: la transacción ya se revirtió, no se consumió nada.
            return FallosDeBiblioteca.NoDisponible<EnlaceDeDescarga>();
        }

        return salida switch
        {
            SalidaDeConsumo.Entregado entregado => Resultado<EnlaceDeDescarga>.Exito(entregado.Enlace),
            SalidaDeConsumo.AccesoInexistente => await NoEncontrado(cancelacion).ConfigureAwait(false),
            _ => await Reclasificar(acceso, clave, referencia.Valor.ClaveEnAlmacen, nombre, ahora, cancelacion).ConfigureAwait(false),
        };
    }

    /// <summary>
    /// Tras revertir: inexistente o ajeno → revocado o suspendido → clave vigente (el mismo enlace sin contar, también sin cupo) → sin cupo. Una relectura que ya no distingue nada (el acceso volvió a estar disponible) responde «no disponible» y se reintenta.
    /// </summary>
    private async Task<Resultado<EnlaceDeDescarga>> Reclasificar(Acceso leido, ClaveDeDescarga clave, string claveEnAlmacen, string nombre, DateTimeOffset ahora, CancellationToken cancelacion)
    {
        Acceso? actual;
        ClaveRegistrada? registrada;
        try
        {
            actual = await accesos.Obtener(leido.Id, leido.CuentaId, cancelacion).ConfigureAwait(false);
            registrada = actual is null ? null : await descargas.ObtenerClave(leido.Id, clave, cancelacion).ConfigureAwait(false);
        }
        catch (BibliotecaNoDisponibleException)
        {
            return FallosDeBiblioteca.NoDisponible<EnlaceDeDescarga>();
        }

        if (actual is null)
        {
            return await NoEncontrado(cancelacion).ConfigureAwait(false);
        }

        if (actual.Estado != EstadoDeAcceso.Vigente)
        {
            return FallosDeBiblioteca.De<EnlaceDeDescarga>(CategoriaDeError.Conflicto, ClavesDeBiblioteca.AccesoNoDisponible);
        }

        if (registrada is not null && registrada.EstaVigente(ahora))
        {
            return Reconstruir(registrada, claveEnAlmacen, nombre);
        }

        return actual.DescargasUsadas >= PoliticaDeDescargas.MaximoDeDescargas
            ? FallosDeBiblioteca.De<EnlaceDeDescarga>(CategoriaDeError.Conflicto, ClavesDeBiblioteca.LimiteDeDescargas)
            : FallosDeBiblioteca.NoDisponible<EnlaceDeDescarga>();
    }

    private Resultado<EnlaceDeDescarga> Reconstruir(ClaveRegistrada registrada, string claveEnAlmacen, string nombre)
    {
        try
        {
            var vigencia = (int)(registrada.VenceEn - registrada.FirmadoEn).TotalSeconds;
            return Resultado<EnlaceDeDescarga>.Exito(new EnlaceDeDescarga(entrega.FirmarEnlace(claveEnAlmacen, nombre, registrada.FirmadoEn, vigencia), registrada.VenceEn));
        }
        catch (InvalidOperationException)
        {
            return FallosDeBiblioteca.NoDisponible<EnlaceDeDescarga>();
        }
    }

    private async Task<ErrorDeAplicacion?> ReservarElLimite(Guid cuenta, Guid accesoId, ClaveDeDescarga clave, CancellationToken cancelacion)
    {
        // La clave del llamador une cuenta, acceso y clave de un solo uso (SEG-22): la repetición de esa combinación no cuenta; una misma clave contra dos accesos cuenta dos veces, de modo que no sirve para esquivar el tope.
        var claveDelLlamador = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{cuenta:N}|{accesoId:N}|{clave.Hexadecimal}")));
        ResultadoDeLimite resultado;
        try
        {
            resultado = await limites.Reservar(PoliticasDeLimite.EnlacesDeDescargaPorCuenta, cuenta.ToString("N"), claveDelLlamador, cancelacion: cancelacion).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ErrorDeAplicacion(CategoriaDeError.NoDisponible, ClavesDeBiblioteca.ServicioNoDisponible);
        }

        return LimiteDeDescargas.Traducir(resultado, PoliticasDeLimite.EnlacesDeDescargaPorCuenta);
    }

    private async Task<Resultado<EnlaceDeDescarga>> ArchivoFaltante(DateTimeOffset ahora, CancellationToken cancelacion)
    {
        try
        {
            // Una alerta técnica sin datos del cliente; si no se puede publicar, la respuesta al cliente no cambia (regla 10).
            await alertas.Publicar(new AlertaTecnica(ClavesDeBiblioteca.AlertaArchivoFaltante, ahora), cancelacion).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
        }

        return FallosDeBiblioteca.De<EnlaceDeDescarga>(CategoriaDeError.NoDisponible, ClavesDeBiblioteca.ArchivoNoDisponible);
    }

    private async Task<Resultado<EnlaceDeDescarga>> NoEncontrado(CancellationToken cancelacion) =>
        FallosDeBiblioteca.Fallo<EnlaceDeDescarga>(await autorizador.NoEncontradoYDenegado(cancelacion).ConfigureAwait(false));

    private async Task<string> NombreDelArchivo(Guid edicionId, FormatoDeArchivoDigital formato, CancellationToken cancelacion)
    {
        try
        {
            var ediciones = await catalogo.ObtenerEdiciones([edicionId], cancelacion).ConfigureAwait(false);
            return NombreDeArchivoDeDescarga.Para(ediciones.EsExito ? ediciones.Valor.FirstOrDefault()?.Titulo : null, formato);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return NombreDeArchivoDeDescarga.Para(null, formato);
        }
    }

    /// <summary>El instante de firma al segundo: la firma SigV4 trabaja en segundos y la clave guarda el mismo valor, de modo que reconstruir da la misma URL.</summary>
    private static DateTimeOffset AlSegundo(DateTimeOffset instante) => new(instante.UtcTicks - (instante.UtcTicks % TimeSpan.TicksPerSecond), TimeSpan.Zero);

    private DateOnly DiaDeLima(DateTimeOffset instante) => DateOnly.FromDateTime(instante.ToOffset(TimeSpan.FromHours(-5)).DateTime);
}
