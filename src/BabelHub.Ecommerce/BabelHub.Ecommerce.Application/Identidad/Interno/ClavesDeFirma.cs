using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Identidad;

namespace BabelHub.Ecommerce.Application.Identidad.Interno;

/// <summary>
/// La rotación, la declaración de clave comprometida y la publicación de claves y revocaciones (T25; IDE-35, IDE-36, IDE-61, IDE-85; AC-IDE-32, AC-IDE-33). Solo el administrador central activo rota o declara; la rotación
/// automática cada 90 días corre con el actor del sistema. Cargar el juego toma su bloqueo (dos operaciones simultáneas se serializan), la privada nueva se genera cifrada dentro de la misma transacción y <b>cada cambio se
/// audita en ella</b> (AUD-04): si no se puede auditar, no se rota ni se declara. Declarar comprometida una clave la rechaza de inmediato (ya no se publica), rota si era la activa y <b>revoca todas las renovaciones opacas
/// de inmediato</b>; es idempotente, de modo que repetirla tras un fallo parcial vuelve a ordenar la revocación. Lo que se publica es solo el JWK público de la clave activa y de la anterior mientras duren sus credenciales
/// (nunca la privada ni una comprometida); las revocaciones son los <c>kid</c> comprometidos.
/// </summary>
public sealed class ClavesDeFirma(
    AlcanceDeLaAdministracion administracion,
    IAlmacenDeClavesDeFirma almacen,
    IGeneradorDeClavesDeFirma generador,
    IRepositorioDeRenovaciones renovaciones,
    IUnidadDeTrabajo unidad,
    IAuditoria auditoria,
    IReloj reloj) : IClavesDeFirma
{
    public const string AlgoritmoDeFirma = "ES256";

    public const string MotivoDeLaDeclaracion = "declaracion-de-administracion";

    private static ErrorDeAplicacion ClaveInexistente { get; } = new(CategoriaDeError.NoEncontrado, "clave-de-firma-no-encontrada");

    public async Task<Resultado<ClavePublicaDeFirma>> Rotar(Guid administradorId, CancellationToken cancelacion = default)
    {
        var (error, _) = await administracion.Exigir(administradorId, cancelacion).ConfigureAwait(false);
        if (error is not null)
        {
            return Resultado<ClavePublicaDeFirma>.Fallo(error);
        }

        return Resultado<ClavePublicaDeFirma>.Exito((await RotarAsync(cancelacion).ConfigureAwait(false))!);
    }

    /// <summary>Rota la clave sin un administrador y **sin periodo de gracia**: tras restaurar un respaldo, la clave restaurada se rechaza de inmediato y queda revocada (PLA-43, AC-PLA-43). Corre con el actor del sistema y audita igual.</summary>
    public async Task<ClavePublicaDeFirma> RotarParaLaRestauracion(CancellationToken cancelacion = default) => (await RotarAsync(cancelacion, soloSiToca: false, sinGracia: true).ConfigureAwait(false))!;

    /// <summary>La rotación periódica (IDE-36): si pasaron 90 días desde la última, rota y audita; devuelve la clave nueva o <see langword="null"/> si no tocaba. Corre con el actor del sistema.</summary>
    public async Task<ClavePublicaDeFirma?> RotarSiHanPasado90Dias(CancellationToken cancelacion = default)
    {
        var juego = await almacen.Cargar(paraActualizar: false, cancelacion).ConfigureAwait(false);
        return juego.DebeRotar(reloj.Ahora) ? await RotarAsync(cancelacion, soloSiToca: true).ConfigureAwait(false) : null;
    }

    public async Task<Resultado<ClavePublicaDeFirma>> DeclararComprometida(Guid administradorId, string kid, CancellationToken cancelacion = default)
    {
        var (error, _) = await administracion.Exigir(administradorId, cancelacion).ConfigureAwait(false);
        if (error is not null)
        {
            return Resultado<ClavePublicaDeFirma>.Fallo(error);
        }

        if (string.IsNullOrWhiteSpace(kid))
        {
            return Resultado<ClavePublicaDeFirma>.Fallo(FallosDeIdentidad.Validacion("kid", "obligatorio"));
        }

        return await unidad.Ejecutar(
            async ct =>
            {
                var juego = await almacen.Cargar(paraActualizar: true, ct).ConfigureAwait(false);
                var existente = juego.Claves.FirstOrDefault(c => c.Kid == kid);
                if (existente is null)
                {
                    return Resultado<ClavePublicaDeFirma>.Fallo(ClaveInexistente);
                }

                var ahora = reloj.Ahora;
                (ClaveDeFirma Clave, string Parametros)? nueva = existente.Estado == EstadoDeClaveDeFirma.Activa ? await generador.Generar(ahora, ct).ConfigureAwait(false) : null;
                var resultado = juego.DeclararComprometida(kid, nueva?.Clave, ahora)!;

                // La clave ya no se acepta en cuanto se guarda; las renovaciones opacas se revocan de inmediato en la misma transacción y la revocación se publica con el kid comprometido (IDE-61).
                await almacen.Guardar(juego, ct).ConfigureAwait(false);
                if (resultado.RevocarRenovaciones)
                {
                    await renovaciones.RevocarLasDeTodos(ahora, ct).ConfigureAwait(false);
                }

                await auditoria.Registrar(
                    new SolicitudDeRegistro(AccionesAuditables.ClaveDeFirmaDeclaradaComprometida)
                    {
                        ObjetoTipo = "clave-de-firma",
                        ObjetoId = kid,
                        Motivo = MotivoDeLaDeclaracion,
                        Detalle = new Dictionary<string, string?> { ["version"] = kid },
                    },
                    ct).ConfigureAwait(false);

                var activa = resultado.ClaveActiva;
                var parametros = nueva is { } generada && generada.Clave.Kid == activa.Kid
                    ? generada.Parametros
                    : (await almacen.ParametrosPublicos([activa.Kid], ct).ConfigureAwait(false)).GetValueOrDefault(activa.Kid, string.Empty);
                return Resultado<ClavePublicaDeFirma>.Exito(Modelo(activa, parametros));
            },
            cancelacion).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ClavePublicaDeFirma>> Publicadas(CancellationToken cancelacion = default)
    {
        var juego = await almacen.Cargar(paraActualizar: false, cancelacion).ConfigureAwait(false);
        var publicables = juego.Publicables(reloj.Ahora);
        var parametros = await almacen.ParametrosPublicos([.. publicables.Select(c => c.Kid)], cancelacion).ConfigureAwait(false);
        return [.. publicables.Select(c => Modelo(c, parametros.GetValueOrDefault(c.Kid, string.Empty)))];
    }

    public async Task<IReadOnlyList<string>> Revocadas(CancellationToken cancelacion = default)
    {
        var juego = await almacen.Cargar(paraActualizar: false, cancelacion).ConfigureAwait(false);
        return juego.Revocadas;
    }

    private async Task<ClavePublicaDeFirma?> RotarAsync(CancellationToken cancelacion, bool soloSiToca = false, bool sinGracia = false)
    {
        return await unidad.Ejecutar(
            async ct =>
            {
                var juego = await almacen.Cargar(paraActualizar: true, ct).ConfigureAwait(false);
                var ahora = reloj.Ahora;

                // Bajo el bloqueo se relee: si otra instancia ya rotó mientras tanto, la rotación periódica no rota otra vez.
                if (soloSiToca && !juego.DebeRotar(ahora))
                {
                    return null;
                }

                var anterior = juego.Activa.Kid;
                var (nueva, parametros) = await generador.Generar(ahora, ct).ConfigureAwait(false);
                if (sinGracia)
                {
                    juego.RotarRevocandoLaAnterior(nueva, ahora);
                }
                else
                {
                    juego.Rotar(nueva, ahora);
                }

                await almacen.Guardar(juego, ct).ConfigureAwait(false);
                await auditoria.Registrar(
                    new SolicitudDeRegistro(AccionesAuditables.ClaveDeFirmaRotada)
                    {
                        ObjetoTipo = "clave-de-firma",
                        ObjetoId = nueva.Kid,
                        Detalle = new Dictionary<string, string?> { ["versionAnterior"] = anterior, ["versionNueva"] = nueva.Kid },
                    },
                    ct).ConfigureAwait(false);
                return Modelo(nueva, parametros);
            },
            cancelacion).ConfigureAwait(false);
    }

    private static ClavePublicaDeFirma Modelo(ClaveDeFirma clave, string parametrosPublicos) => new(
        clave.Kid,
        AlgoritmoDeFirma,
        parametrosPublicos,
        clave.Estado switch
        {
            EstadoDeClaveDeFirma.Activa => "activa",
            EstadoDeClaveDeFirma.Anterior => "anterior",
            EstadoDeClaveDeFirma.Comprometida => "comprometida",
            _ => "retirada",
        });
}
