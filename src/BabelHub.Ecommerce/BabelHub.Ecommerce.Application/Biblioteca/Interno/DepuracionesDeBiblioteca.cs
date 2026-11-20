using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Biblioteca;

namespace BabelHub.Ecommerce.Application.Biblioteca.Interno;

/// <summary>
/// La depuración diaria del registro de descargas (T11; BIB-26, D09): a los <b>dos años por día de Lima</b> quita del registro la referencia al acceso, el usuario y la hora exacta (queda la edición y el día), <b>sin tocar el acceso</b> del cliente. Corre como el actor
/// «sistema» una sola transacción con su auditoría (conteo, sin datos personales): <b>si no puede auditar, no hace nada ese día</b>. Es idempotente (solo toca lo no depurado) y no audita una corrida sin nada que depurar. Devuelve cuántos registros depuró.
/// </summary>
internal sealed class DepurarRegistrosDeDescargas(IActorActual actores, IRepositorioDeDescargas descargas, IAuditoria auditoria, IUnidadDeTrabajo unidad, IReloj reloj)
{
    public async Task<Resultado<int>> Ejecutar(CancellationToken cancelacion = default)
    {
        if (actores.Actual is not { Tipo: TipoDeActor.Sistema })
        {
            return FallosDeBiblioteca.De<int>(CategoriaDeError.Prohibido, ClavesDeBiblioteca.Prohibido);
        }

        var limite = PoliticaDeRetencion.UltimoDiaVencido(reloj.HoyEnLima);
        try
        {
            var cantidad = await unidad.Ejecutar(
                async token =>
                {
                    var depurados = await descargas.DepurarRegistros(limite, token).ConfigureAwait(false);
                    if (depurados > 0)
                    {
                        await auditoria.Registrar(AuditoriaDeDepuracion.De(AccionesAuditables.DescargasDeBibliotecaDepuradas, depurados), token).ConfigureAwait(false);
                    }

                    return depurados;
                },
                cancelacion).ConfigureAwait(false);
            return Resultado<int>.Exito(cantidad);
        }
        catch (Exception ex) when (ex is BibliotecaNoDisponibleException or AuditoriaNoDisponibleException)
        {
            return FallosDeBiblioteca.NoDisponible<int>();
        }
    }
}

/// <summary>La depuración diaria de las solicitudes de ayuda (T11; BIB-29): elimina las de <b>dos años o más</b> de cualquier estado, con su auditoría en la misma transacción (sin datos personales); sin auditar, no hace nada ese día.</summary>
internal sealed class DepurarAyudas(IActorActual actores, IRepositorioDeDescargas descargas, IAuditoria auditoria, IUnidadDeTrabajo unidad, IReloj reloj)
{
    public async Task<Resultado<int>> Ejecutar(CancellationToken cancelacion = default)
    {
        if (actores.Actual is not { Tipo: TipoDeActor.Sistema })
        {
            return FallosDeBiblioteca.De<int>(CategoriaDeError.Prohibido, ClavesDeBiblioteca.Prohibido);
        }

        var limite = PoliticaDeRetencion.UltimoDiaVencido(reloj.HoyEnLima);
        try
        {
            var cantidad = await unidad.Ejecutar(
                async token =>
                {
                    var eliminadas = await descargas.DepurarAyudas(limite, token).ConfigureAwait(false);
                    if (eliminadas > 0)
                    {
                        await auditoria.Registrar(AuditoriaDeDepuracion.De(AccionesAuditables.AyudasDeBibliotecaDepuradas, eliminadas), token).ConfigureAwait(false);
                    }

                    return eliminadas;
                },
                cancelacion).ConfigureAwait(false);
            return Resultado<int>.Exito(cantidad);
        }
        catch (Exception ex) when (ex is BibliotecaNoDisponibleException or AuditoriaNoDisponibleException)
        {
            return FallosDeBiblioteca.NoDisponible<int>();
        }
    }
}

/// <summary>La purga horaria de las claves de descarga (T11, D02): quita las que vencieron hace más de una hora; no audita (una clave es un resumen sin datos personales y ya no sirve para reconstruir ningún enlace vigente).</summary>
internal sealed class PurgarClavesDeDescarga(IActorActual actores, IRepositorioDeDescargas descargas, IReloj reloj)
{
    public async Task<Resultado<int>> Ejecutar(CancellationToken cancelacion = default)
    {
        if (actores.Actual is not { Tipo: TipoDeActor.Sistema })
        {
            return FallosDeBiblioteca.De<int>(CategoriaDeError.Prohibido, ClavesDeBiblioteca.Prohibido);
        }

        try
        {
            return Resultado<int>.Exito(await descargas.PurgarClaves(reloj.Ahora - PoliticaDeDescargas.RetencionDeLaClaveVencida, cancelacion).ConfigureAwait(false));
        }
        catch (BibliotecaNoDisponibleException)
        {
            return FallosDeBiblioteca.NoDisponible<int>();
        }
    }
}

internal static class AuditoriaDeDepuracion
{
    /// <summary>La entrada de una depuración: solo el conteo (sin cuenta, acceso ni libro) y, por el contexto, el actor «sistema».</summary>
    public static SolicitudDeRegistro De(AccionAuditable accion, int cantidad) =>
        new(accion) { Detalle = new Dictionary<string, string?> { ["cantidad"] = cantidad.ToString(System.Globalization.CultureInfo.InvariantCulture) } };
}
