using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Notificaciones;

namespace BabelHub.Ecommerce.Application.Notificaciones.Interno;

/// <summary>Qué respondió el servicio de envío (n8n) a un aviso (NOT-04, NOT-12): <b>aceptado</b> (200, no entregado), <b>duplicado</b> (ya lo había recibido) o fallido (espera agotada, 5xx, 4xx o sin firma válida).</summary>
public enum ResultadoDelEnvio
{
    Aceptado,
    Duplicado,
    Fallido,
}

/// <summary>
/// Lo que se le entrega al servicio de envío (T14): el identificador del aviso como clave de idempotencia, la plantilla con sus campos cerrados y el número de destino, que <b>solo viaja en la llamada firmada</b> y nunca se
/// guarda ni se imprime. El servicio arma el texto con su plantilla (AD16, NOT-10): aquí no hay texto.
/// </summary>
public sealed record SolicitudDeEnvio(Guid AvisoId, string Plantilla, int VersionDeLaPlantilla, IReadOnlyDictionary<string, string> Campos, NumeroDeDestino Numero)
{
    /// <summary>No imprime el número ni los campos.</summary>
    public override string ToString() => $"[solicitud de envío {AvisoId:N}]";
}

/// <summary>
/// El puerto del envío por WhatsApp (<c>N8nEnvioDeWhatsApp</c>, <c>SimuladoEnvioDeWhatsApp</c>, <c>NuloEnvioDeWhatsApp</c>; AD16): una llamada firmada de espera acotada. <b>No lanza</b> por una falla del servicio: la traduce a
/// <see cref="ResultadoDelEnvio.Fallido"/>, que consume un intento; solo un estado de entrega firmado marca el aviso como enviado.
/// </summary>
public interface IEnvioDeWhatsApp
{
    Task<ResultadoDelEnvio> Enviar(SolicitudDeEnvio solicitud, CancellationToken cancelacion = default);
}

/// <summary>El resultado de resolver el contacto de una sucursal en el Inventario (T16).</summary>
public enum EstadoDelContactoDeSucursal
{
    Encontrado,

    /// <summary>La sucursal existe pero no tiene un contacto de notificación registrado: error permanente que cuenta como un intento.</summary>
    SinContacto,

    /// <summary>El Inventario no conoce la sucursal (<c>INV-NO-ENCONTRADO</c>): error permanente que cuenta como un intento.</summary>
    NoEncontrada,

    /// <summary>El Inventario rechazó la credencial (<c>INV-NO-AUTENTICADO</c>): error permanente que cuenta como un intento.</summary>
    NoAutenticado,

    /// <summary>El Inventario no respondió (<c>INV-SERVICIO-NO-DISPONIBLE</c>, espera agotada): el aviso queda pendiente <b>sin consumir un intento</b>.</summary>
    NoDisponible,
}

/// <summary>El contacto registrado de una sucursal: su número, que solo se usa para enviar y no se guarda ni se imprime.</summary>
public sealed record ContactoDeSucursal(EstadoDelContactoDeSucursal Estado, string? Numero = null)
{
    public override string ToString() => $"[contacto de sucursal {Estado}]";
}

/// <summary>El cliente REST del contacto de notificación de una sucursal (<c>ClienteDeContactoDelInventario</c>, T16; NOT-09): el aviso de una sucursal guarda su código y el número se resuelve aquí, al enviar.</summary>
public interface IContactoDeSucursal
{
    Task<ContactoDeSucursal> Resolver(string codigoDeSucursal, CancellationToken cancelacion = default);
}

/// <summary>Qué pasó al reservar el cupo del tope por número de un aviso.</summary>
public enum EstadoDelCupo
{
    Reservado,

    /// <summary>Alguna de las dos políticas (5 por hora, 20 en 24 horas) no tiene cupo: el aviso se omite por tope.</summary>
    Excedido,

    /// <summary>El motor de límites no responde: falla cerrada, el aviso sigue pendiente sin consumir un intento (NOT-15).</summary>
    NoDisponible,
}

public sealed record ResultadoDelCupo(EstadoDelCupo Estado, IReadOnlyList<CupoDelTope> Cupos, string? PoliticaExcedida = null);

/// <summary>
/// El tope de avisos por número de cliente (T09; NOT-14, NOT-15, NOT-16): dos políticas «por número» sobre <see cref="IControlDeLimites"/> (5 por hora y 20 en 24 horas, deslizantes) con la <b>huella del número E.164</b> como
/// sujeto. El cupo se reserva <b>una sola vez por aviso</b> (la reserva es idempotente por el identificador del aviso: un reintento tras una caída recupera la misma) y se <b>devuelve</b> si el aviso termina cancelado u omitido.
/// Los contactos de sucursal están exentos. El motor falla cerrado: sin él, no se envía.
/// </summary>
public sealed class TopeDeAvisosPorNumero(IControlDeLimites limites, IHuellasDeNumeros huellas)
{
    public static readonly IReadOnlyList<ClaveDePolitica> Politicas = [PoliticasDeLimite.AvisosPorNumeroPorHora, PoliticasDeLimite.AvisosPorNumeroEn24Horas];

    public async Task<ResultadoDelCupo> Reservar(Aviso aviso, NumeroDeDestino numero, CancellationToken cancelacion = default)
    {
        var huella = huellas.ParaElTope(numero);
        var clave = ClaveDelAviso(aviso.Id);
        var reservados = new List<CupoDelTope>();
        foreach (var politica in Politicas)
        {
            var resultado = await limites.Reservar(politica, huellaDelLlamador: huella, claveDelLlamador: clave, cancelacion: cancelacion).ConfigureAwait(false);
            if (resultado is { EsPermitido: true, Reserva: { } reserva })
            {
                reservados.Add(new CupoDelTope(reserva.Politica, reserva.Sujeto, reserva.Identificador, reserva.ReservadaEn));
                continue;
            }

            await Devolver(aviso.Id, reservados, cancelacion).ConfigureAwait(false);
            return resultado.Estado == EstadoDeLimite.Excedido
                ? new ResultadoDelCupo(EstadoDelCupo.Excedido, [], politica.Valor)
                : new ResultadoDelCupo(EstadoDelCupo.NoDisponible, []);
        }

        return new ResultadoDelCupo(EstadoDelCupo.Reservado, reservados);
    }

    /// <summary>Devuelve los cupos de un aviso que terminó cancelado u omitido; un fallo del motor no interrumpe nada (el cupo expira con su ventana).</summary>
    public async Task Devolver(Guid avisoId, IReadOnlyList<CupoDelTope> cupos, CancellationToken cancelacion = default)
    {
        foreach (var cupo in cupos)
        {
            try
            {
                await limites.Liberar(new ReservaDeLimite(cupo.Politica, cupo.Sujeto, cupo.Identificador, cupo.ReservadoEn, ClaveDelAviso(avisoId)), cancelacion).ConfigureAwait(false);
            }
            catch (Exception) when (!cancelacion.IsCancellationRequested)
            {
                // El cupo expira con su ventana: devolverlo es una cortesía, no una condición.
            }
        }
    }

    private static string ClaveDelAviso(Guid avisoId) => avisoId.ToString("N");
}

/// <summary>
/// El envío de los avisos pendientes (T08, T09; NOT-03, NOT-04, NOT-06, NOT-08, NOT-11 a NOT-16; AC-NOT-02, AC-NOT-03, AC-NOT-05, AC-NOT-12, AC-NOT-14 a AC-NOT-16). Toma un lote con <c>FOR UPDATE SKIP LOCKED</c> y un
/// arrendamiento corto, y por cada aviso comprueba, <b>en este orden</b>: (1) la edad máxima de 24 horas, (2) el contacto vigente (cliente: consentimiento, cuenta y número por Identidad; sucursal: el contacto por REST),
/// (3) el número no desactivado en ninguna versión de la clave, (4) el cupo del tope por número (solo clientes) y (5) que el aviso siga en curso (si lo cancelaron entre tanto, no sale). Solo entonces lo entrega al servicio
/// de envío; <b>solo el estado de entrega firmado lo marca enviado</b>. Un fallo ajeno (Identidad, el Inventario, el motor de límites) deja el aviso pendiente <b>sin consumir un intento</b>; ningún aviso interrumpe a los demás.
/// </summary>
public sealed class EnviarAvisosPendientes(
    IRepositorioDeAvisos avisos,
    IUnidadDeTrabajo unidad,
    IContactoDeAvisosDelCliente clientes,
    IContactoDeSucursal sucursales,
    IHuellasDeNumeros huellas,
    IRepositorioDeNumerosDesactivados desactivados,
    IEnvioDeWhatsApp envio,
    TopeDeAvisosPorNumero tope,
    IPublicadorDeEventosDeSeguridad eventos,
    IAlertas alertas,
    IReloj reloj)
{
    public const int LoteMaximo = 25;

    /// <summary>Cuánto tiene un aviso para que el servicio de envío responda antes de que otro trabajo pueda recogerlo.</summary>
    public static readonly TimeSpan Arrendamiento = TimeSpan.FromMinutes(2);

    public const string MotivoDeCuentaCancelada = "cuenta-cancelada";

    public const string MotivoDeNumeroDesactivado = "numero-desactivado";

    public const string MotivoDeSinNumero = "sin-numero";

    public const string MotivoDeNumeroInvalido = "numero-invalido";

    /// <summary>Procesa un lote y devuelve cuántos avisos tomó.</summary>
    public async Task<int> Ejecutar(int maximo = LoteMaximo, CancellationToken cancelacion = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximo, 1);
        var ahora = reloj.Ahora;
        var tomados = await unidad.Ejecutar(
            async ct =>
            {
                var candidatos = await avisos.TomarParaEnviar(ahora, maximo, ct).ConfigureAwait(false);
                var lista = new List<Aviso>();
                foreach (var aviso in candidatos)
                {
                    var cupos = aviso.Cupos;
                    if (aviso.VencerSiCorresponde(ahora))
                    {
                        // Venció por edad (24 horas): fallido por vencido, sin campos; el cupo de un aviso que no salió se devuelve.
                        await avisos.Guardar(aviso, ct).ConfigureAwait(false);
                        await tope.Devolver(aviso.Id, cupos, ct).ConfigureAwait(false);
                        continue;
                    }

                    if (aviso.IniciarEnvio(ahora, ahora + Arrendamiento))
                    {
                        await avisos.Guardar(aviso, ct).ConfigureAwait(false);
                        lista.Add(aviso);
                    }
                }

                return lista;
            },
            cancelacion).ConfigureAwait(false);

        foreach (var aviso in tomados)
        {
            cancelacion.ThrowIfCancellationRequested();
            await Procesar(aviso, cancelacion).ConfigureAwait(false);
        }

        return tomados.Count;
    }

    private async Task Procesar(Aviso aviso, CancellationToken cancelacion)
    {
        try
        {
            await Decidir(aviso, cancelacion).ConfigureAwait(false);
        }
        catch (Exception) when (!cancelacion.IsCancellationRequested)
        {
            // Un fallo ajeno (Identidad, el Inventario, el motor de límites o la base) no consume un intento ni se traga: alerta técnica y, si se puede, el aviso se aplaza 5 minutos.
            await AlertarSinInterrumpir(cancelacion).ConfigureAwait(false);
            try
            {
                if (aviso.AplazarSinConsumir(reloj.Ahora))
                {
                    await avisos.GuardarSiNoEsFinal(aviso, cancelacion).ConfigureAwait(false);
                }
            }
            catch (Exception) when (!cancelacion.IsCancellationRequested)
            {
                // La base tampoco responde: el arrendamiento corto lo devuelve al siguiente ciclo.
            }
        }
    }

    private async Task Decidir(Aviso aviso, CancellationToken cancelacion)
    {
        var ahora = reloj.Ahora;
        var numero = await ResolverElDestino(aviso, ahora, cancelacion).ConfigureAwait(false);
        if (numero is null)
        {
            return;
        }

        // (3) Un número desactivado a pedido de su titular no recibe avisos, escrito de cualquier forma y con cualquier versión de la clave (NOT-13).
        if (await desactivados.ExisteAlguna(huellas.Vigentes(numero.Value), cancelacion).ConfigureAwait(false))
        {
            await Finalizar(aviso, a => a.Cancelar(MotivoDeNumeroDesactivado, ahora), cancelacion).ConfigureAwait(false);
            return;
        }

        // (4) El tope por número solo cuenta a clientes; un contacto de sucursal está exento (NOT-16). Se reserva una vez por aviso aunque se reintente.
        if (aviso.Tipo == TipoDeDestinatario.Cliente && !aviso.CupoReservado)
        {
            var cupo = await tope.Reservar(aviso, numero.Value, cancelacion).ConfigureAwait(false);
            switch (cupo.Estado)
            {
                case EstadoDelCupo.NoDisponible:
                    await Aplazar(aviso, ahora, cancelacion).ConfigureAwait(false);
                    return;
                case EstadoDelCupo.Excedido:
                    await OmitirPorTope(aviso, cupo.PoliticaExcedida!, ahora, cancelacion).ConfigureAwait(false);
                    return;
                default:
                    aviso.ReservarLosCupos(cupo.Cupos);
                    break;
            }
        }

        // (5) Un aviso cancelado entre tanto no sale: la escritura condicional lo detecta (y devuelve el cupo recién reservado, que el cancelador no vio).
        if (!await avisos.GuardarSiNoEsFinal(aviso, cancelacion).ConfigureAwait(false))
        {
            await tope.Devolver(aviso.Id, aviso.Cupos, cancelacion).ConfigureAwait(false);
            return;
        }

        var resultado = await Entregar(aviso, numero.Value, cancelacion).ConfigureAwait(false);
        ahora = reloj.Ahora;
        _ = resultado switch
        {
            ResultadoDelEnvio.Aceptado => aviso.EsperarLaEntrega(ahora),
            ResultadoDelEnvio.Duplicado => aviso.EsperarElEstadoDeEntrega(ahora),
            _ => aviso.FalloElIntento(ahora),
        };
        await avisos.GuardarSiNoEsFinal(aviso, cancelacion).ConfigureAwait(false);
    }

    /// <summary>(2) El contacto vigente: el número al que se envía, o <see langword="null"/> si el aviso ya quedó resuelto (omitido, cancelado, aplazado o con un intento fallido).</summary>
    private async Task<NumeroDeDestino?> ResolverElDestino(Aviso aviso, DateTimeOffset ahora, CancellationToken cancelacion)
    {
        if (aviso.Tipo == TipoDeDestinatario.Cliente)
        {
            if (!Guid.TryParse(aviso.DestinatarioRef, out var cuenta))
            {
                await Finalizar(aviso, a => a.Cancelar(MotivoDeCuentaCancelada, ahora), cancelacion).ConfigureAwait(false);
                return null;
            }

            var contacto = await clientes.Consultar(cuenta, cancelacion).ConfigureAwait(false);
            if (contacto is null or { CuentaCanceladaODesactivada: true })
            {
                await Finalizar(aviso, a => a.Cancelar(MotivoDeCuentaCancelada, ahora), cancelacion).ConfigureAwait(false);
                return null;
            }

            if (!contacto.Consiente)
            {
                await Finalizar(aviso, a => a.OmitirSinConsentimiento(SolicitarAviso.MotivoDeOmisionSinConsentimiento, ahora), cancelacion).ConfigureAwait(false);
                return null;
            }

            if (string.IsNullOrWhiteSpace(contacto.NumeroE164))
            {
                await Finalizar(aviso, a => a.OmitirSinConsentimiento(MotivoDeSinNumero, ahora), cancelacion).ConfigureAwait(false);
                return null;
            }

            if (!NumeroDeDestino.TryNormalizar(contacto.NumeroE164, out var delCliente))
            {
                await Finalizar(aviso, a => a.OmitirSinConsentimiento(MotivoDeNumeroInvalido, ahora), cancelacion).ConfigureAwait(false);
                return null;
            }

            return delCliente;
        }

        var sucursal = await sucursales.Resolver(aviso.DestinatarioRef, cancelacion).ConfigureAwait(false);
        switch (sucursal.Estado)
        {
            case EstadoDelContactoDeSucursal.NoDisponible:
                await Aplazar(aviso, ahora, cancelacion).ConfigureAwait(false);
                return null;
            case EstadoDelContactoDeSucursal.Encontrado when NumeroDeDestino.TryNormalizar(sucursal.Numero, out var delContacto):
                return delContacto;
            default:
                // Sin contacto registrado, sucursal desconocida, credencial rechazada o un número que no es E.164: errores permanentes que cuentan como un intento (NOT-04).
                aviso.FalloElIntento(ahora);
                await avisos.GuardarSiNoEsFinal(aviso, cancelacion).ConfigureAwait(false);
                return null;
        }
    }

    private async Task<ResultadoDelEnvio> Entregar(Aviso aviso, NumeroDeDestino numero, CancellationToken cancelacion)
    {
        try
        {
            return await envio.Enviar(new SolicitudDeEnvio(aviso.Id, aviso.Plantilla, aviso.VersionDeLaPlantilla, aviso.Campos, numero), cancelacion).ConfigureAwait(false);
        }
        catch (Exception) when (!cancelacion.IsCancellationRequested)
        {
            // Un adaptador que lanza se trata como un envío fallido: consume un intento y el aviso se reintenta con su espera creciente.
            return ResultadoDelEnvio.Fallido;
        }
    }

    private async Task Aplazar(Aviso aviso, DateTimeOffset ahora, CancellationToken cancelacion)
    {
        if (aviso.AplazarSinConsumir(ahora))
        {
            await avisos.GuardarSiNoEsFinal(aviso, cancelacion).ConfigureAwait(false);
        }
    }

    /// <summary>Lleva el aviso a un estado final y, si había reservado cupo, lo devuelve; si otro (la cancelación de Identidad) ya lo había terminado, no se toca.</summary>
    private async Task Finalizar(Aviso aviso, Func<Aviso, bool> transicion, CancellationToken cancelacion)
    {
        var cupos = aviso.Cupos;
        if (transicion(aviso) && await avisos.GuardarSiNoEsFinal(aviso, cancelacion).ConfigureAwait(false))
        {
            await tope.Devolver(aviso.Id, cupos, cancelacion).ConfigureAwait(false);
        }
    }

    private async Task OmitirPorTope(Aviso aviso, string politica, DateTimeOffset ahora, CancellationToken cancelacion)
    {
        if (!aviso.OmitirPorTope(ahora) || !await avisos.GuardarSiNoEsFinal(aviso, cancelacion).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            // El evento de seguridad (SEG-08, NOT-14) lleva la política y la cuenta (un identificador opaco), nunca el número; Auditoría lo agrupa en ventanas de 5 minutos.
            await eventos.Publicar(new EventoDeSeguridad(TipoDeEventoDeSeguridad.LimiteExcedido, ahora, limite: politica, usuario: aviso.DestinatarioRef), cancelacion).ConfigureAwait(false);
        }
        catch (Exception) when (!cancelacion.IsCancellationRequested)
        {
            // Publicarlo es una cortesía de seguridad: no deshace la omisión ni interrumpe a los demás avisos.
        }
    }

    private async Task AlertarSinInterrumpir(CancellationToken cancelacion)
    {
        try
        {
            await alertas.PublicarTecnica(new CodigoDeAlerta(SolicitarAviso.MotivoDeFallaPropia), cancelacion).ConfigureAwait(false);
        }
        catch (Exception) when (!cancelacion.IsCancellationRequested)
        {
            // Sin a dónde mandar la alerta, el trabajo sigue con el siguiente aviso.
        }
    }
}

/// <summary>
/// La solicitud de un aviso que llega del Inventario (T10; NOT-01, NOT-09, AC-NOT-01, AC-NOT-08): un cambio de estado de un traslado para el contacto de una sucursal. Valida el formato del evento
/// (<c>traslado:&lt;id&gt;:&lt;estado&gt;</c>), que la plantilla sea de traslado y los campos del catálogo cerrado; la existencia de la sucursal <b>no</b> se comprueba aquí, sino al enviar, al resolver su contacto, para no
/// crear una dependencia circular en la entrega del Inventario. Después es una solicitud como cualquier otra: una sola vez por evento, nunca lanza.
/// </summary>
public sealed class RecibirSolicitudDelInventario(INotificador notificador) : IRecepcionDeAvisos
{
    public const string PrefijoDelEvento = "traslado:";

    public Task<ResultadoDeLaSolicitudDeAviso> Recibir(SolicitudDeAvisoDelInventario solicitud, CancellationToken cancelacion = default)
    {
        if (solicitud is null
            || solicitud.EventoId is not { } evento
            || !evento.StartsWith(PrefijoDelEvento, StringComparison.Ordinal)
            || !CatalogoDePlantillas.EsDeTraslado(solicitud.Plantilla))
        {
            return Task.FromResult(ResultadoDeLaSolicitudDeAviso.Rechazado);
        }

        return notificador.Solicitar(new SolicitudDeAviso(evento, DestinatarioDelAviso.Sucursal, solicitud.CodigoDeSucursal, solicitud.Plantilla, solicitud.Campos), cancelacion);
    }
}

/// <summary>
/// El registro del estado de entrega firmado (T11; NOT-06, NOT-12, AC-NOT-05, AC-NOT-11): <b>solo un estado firmado</b> (la Api verificó la firma y la marca de tiempo) de un conjunto cerrado mueve el aviso. «Entregado» lo marca
/// enviado; «fallido» cuenta como un intento fallido con reintento (el aviso solo termina fallido al agotarlos). Un estado tardío <b>no revierte</b> un estado final y repetirlo responde lo mismo (idempotente). Un aviso que no existe
/// se responde <c>NoEncontrado</c> sin más detalle.
/// </summary>
public sealed class RegistrarEstadoDeEntrega(IRepositorioDeAvisos avisos, IUnidadDeTrabajo unidad, IReloj reloj) : IRegistroDeEstadoDeEntrega
{
    public async Task<Resultado<ConfirmacionDeNotificaciones>> Registrar(Guid avisoId, EstadoDeEntrega estado, DateTimeOffset marcaDeTiempo, CancellationToken cancelacion = default)
    {
        if (avisoId == Guid.Empty || !Enum.IsDefined(estado))
        {
            return Resultado<ConfirmacionDeNotificaciones>.Fallo(new ErrorDeAplicacion(CategoriaDeError.Validacion, "validacion-invalida", [new ErrorDeCampo("estado", "invalido")]));
        }

        return await unidad.Ejecutar(
            async ct =>
            {
                var aviso = await avisos.PorId(avisoId, paraActualizar: true, ct).ConfigureAwait(false);
                if (aviso is null)
                {
                    return Resultado<ConfirmacionDeNotificaciones>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoEncontrado, "aviso-no-encontrado"));
                }

                // Un estado final no cambia nunca: el estado tardío se acepta sin efecto.
                if (!aviso.EsFinal)
                {
                    var ahora = reloj.Ahora;
                    if (estado == EstadoDeEntrega.Entregado)
                    {
                        aviso.MarcarEnviado(ahora);
                    }
                    else if (aviso.Estado == EstadoDelAviso.Enviando)
                    {
                        aviso.FalloElIntento(ahora);
                    }

                    await avisos.Guardar(aviso, ct).ConfigureAwait(false);
                }

                return Resultado<ConfirmacionDeNotificaciones>.Exito(new ConfirmacionDeNotificaciones());
            },
            cancelacion).ConfigureAwait(false);
    }
}

/// <summary>Los nombres que la administración ve y filtra de los estados de un aviso (NOT-06).</summary>
public static class EstadosDeAviso
{
    public static string Nombre(EstadoDelAviso estado) => estado switch
    {
        EstadoDelAviso.Pendiente => "pendiente",
        EstadoDelAviso.Enviando => "enviando",
        EstadoDelAviso.Enviado => "enviado",
        EstadoDelAviso.Fallido => "fallido",
        EstadoDelAviso.Cancelado => "cancelado",
        EstadoDelAviso.OmitidoSinConsentimiento => "omitido-sin-consentimiento",
        EstadoDelAviso.OmitidoPorTope => "omitido-por-tope",
        _ => throw new ArgumentOutOfRangeException(nameof(estado)),
    };

    public static bool TryObtener(string? nombre, out EstadoDelAviso estado)
    {
        foreach (var candidato in Enum.GetValues<EstadoDelAviso>())
        {
            if (string.Equals(Nombre(candidato), nombre, StringComparison.Ordinal))
            {
                estado = candidato;
                return true;
            }
        }

        estado = default;
        return false;
    }
}

/// <summary>Una página de avisos almacenados con el total que cumple el filtro.</summary>
public sealed record PaginaDeAvisosAlmacenados(IReadOnlyList<Aviso> Avisos, int Total);

/// <summary>
/// La consulta administrativa de avisos (T21; NOT-06, AC-NOT-05): <b>solo la administración central</b> (otro actor, <c>Prohibido</c> con la denegación registrada). Muestra el estado, los intentos y las fechas de cada aviso,
/// incluidos los fallidos, <b>sin número ni campos</b>; <c>pagina</c> desde 1 y <c>tamano</c> de 1 a 100.
/// </summary>
public sealed class ConsultarAvisos(IActorActual actor, IDenegacionDeAcceso denegaciones, IRepositorioDeAvisos avisos) : IConsultaDeAvisos
{
    public const int TamanoMaximoDePagina = 100;

    public async Task<Resultado<PaginaDeAvisos>> Listar(string? estado, int pagina, int tamano, CancellationToken cancelacion = default)
    {
        if (await AdministracionCentral.Comprobar(actor, denegaciones, cancelacion).ConfigureAwait(false) is { } prohibido)
        {
            return Resultado<PaginaDeAvisos>.Fallo(prohibido);
        }

        EstadoDelAviso? filtro = null;
        if (!string.IsNullOrEmpty(estado))
        {
            if (!EstadosDeAviso.TryObtener(estado, out var conocido))
            {
                return Resultado<PaginaDeAvisos>.Fallo(Validacion("estado"));
            }

            filtro = conocido;
        }

        if (pagina < 1 || tamano is < 1 or > TamanoMaximoDePagina)
        {
            return Resultado<PaginaDeAvisos>.Fallo(Validacion(pagina < 1 ? "pagina" : "tamano"));
        }

        var almacenados = await avisos.Listar(filtro, pagina, tamano, cancelacion).ConfigureAwait(false);
        var vista = almacenados.Avisos
            .Select(a => new AvisoConsultado(
                a.Id,
                a.EventoId,
                a.Tipo == TipoDeDestinatario.Cliente ? DestinatarioDelAviso.Cliente : DestinatarioDelAviso.Sucursal,
                a.Plantilla,
                EstadosDeAviso.Nombre(a.Estado),
                a.Intentos,
                a.Creado,
                a.FechaFinal,
                a.MotivoDeCierre))
            .ToList();
        return Resultado<PaginaDeAvisos>.Exito(new PaginaDeAvisos(vista, pagina, tamano, almacenados.Total));
    }

    private static ErrorDeAplicacion Validacion(string campo) => new(CategoriaDeError.Validacion, "validacion-invalida", [new ErrorDeCampo(campo, "invalido")]);
}

/// <summary>La depuración de los avisos terminados (T12; AUD-31): los enviados, fallidos, cancelados y omitidos se purgan a los <b>90 días</b> de su estado final. Si falla, lanza para que el trabajo deje su alerta técnica.</summary>
public sealed class PurgarAvisosFinalizados(IRepositorioDeAvisos avisos, IReloj reloj)
{
    public static readonly TimeSpan Retencion = TimeSpan.FromDays(90);

    public Task<int> Ejecutar(CancellationToken cancelacion = default) => avisos.PurgarFinalesAnterioresA(reloj.Ahora - Retencion, cancelacion);
}

/// <summary>La comprobación de «solo la administración central» de las operaciones administrativas de Notificaciones: el rol exacto de un usuario autenticado, con la denegación registrada si no.</summary>
internal static class AdministracionCentral
{
    public const string Rol = "administrador-central";

    public static async Task<ErrorDeAplicacion?> Comprobar(IActorActual actor, IDenegacionDeAcceso denegaciones, CancellationToken cancelacion)
    {
        if (actor.Actual is { Tipo: TipoDeActor.Usuario, UsuarioId: not null, Rol: Rol })
        {
            return null;
        }

        try
        {
            await denegaciones.Registrar(new DenegacionDeAcceso("fuera-de-rol", ruta: null), cancelacion).ConfigureAwait(false);
        }
        catch (Exception) when (!cancelacion.IsCancellationRequested)
        {
            // IDenegacionDeAcceso nunca interrumpe y deja su alerta técnica si falla.
        }

        return new ErrorDeAplicacion(CategoriaDeError.Prohibido, "acceso-denegado");
    }
}
