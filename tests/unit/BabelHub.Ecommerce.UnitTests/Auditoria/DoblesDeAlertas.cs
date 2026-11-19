using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Auditoria.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Auditoria;
using AlertaAgrupada = BabelHub.Ecommerce.Domain.Auditoria.AlertaDeSeguridad;

namespace BabelHub.Ecommerce.UnitTests.Auditoria;

/// <summary>Unidad de trabajo que ejecuta el trabajo tal cual y puede fallar a pedido (una base de datos que no responde).</summary>
internal sealed class UnidadDeTrabajoDeAuditoria : IUnidadDeTrabajo
{
    public int Ejecuciones { get; private set; }

    public Exception? Falla { get; set; }

    public async Task<T> Ejecutar<T>(Func<CancellationToken, Task<T>> trabajo, CancellationToken cancelacion = default)
    {
        Ejecuciones++;
        return Falla is not null ? throw Falla : await trabajo(cancelacion);
    }

    public Task EjecutarComo(EsquemaDeModulo esquema, Func<CancellationToken, Task> bloque, CancellationToken cancelacion = default) => bloque(cancelacion);
}

/// <summary>Almacén de alertas en memoria con la semántica del puerto (la última por servicio y motivo, el orden de la lista y la lectura desde un instante).</summary>
internal sealed class AlmacenDeAlertasEnMemoria : IAlmacenDeAlertas
{
    public List<AlertaAgrupada> Alertas { get; } = [];

    public Exception? Falla { get; set; }

    public int Guardados { get; private set; }

    public AlertaAgrupada? Buscar(string motivo, ServicioDeOrigen servicio = ServicioDeOrigen.Ecommerce) =>
        Alertas.Where(a => a.Motivo == motivo && a.Servicio == servicio).OrderByDescending(a => a.PrimeraOcurrencia).FirstOrDefault();

    public Task<AlertaAgrupada?> UltimaDelMotivoParaActualizar(ServicioDeOrigen servicio, string motivo, CancellationToken cancelacion = default) =>
        Falla is not null ? Task.FromException<AlertaAgrupada?>(Falla) : Task.FromResult(Buscar(motivo, servicio));

    public Task<AlertaAgrupada?> Obtener(string id, CancellationToken cancelacion = default) =>
        Falla is not null ? Task.FromException<AlertaAgrupada?>(Falla) : Task.FromResult(Alertas.FirstOrDefault(a => a.Id == id));

    public Task Guardar(AlertaAgrupada alerta, CancellationToken cancelacion = default)
    {
        if (Falla is not null)
        {
            return Task.FromException(Falla);
        }

        Guardados++;
        var indice = Alertas.FindIndex(a => a.Id == alerta.Id);
        if (indice >= 0)
        {
            Alertas[indice] = alerta;
        }
        else
        {
            Alertas.Add(alerta);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AlertaAgrupada>> ListarDeSeguridad(PosicionDeAlerta? despuesDe, int maximo, CancellationToken cancelacion = default)
    {
        IEnumerable<AlertaAgrupada> consulta = Alertas.Where(a => a.Clase == ClaseDeAlerta.Seguridad)
            .OrderByDescending(a => a.UltimaOcurrencia).ThenBy(a => a.Id, StringComparer.Ordinal);
        if (despuesDe is not null)
        {
            consulta = consulta.Where(a => a.UltimaOcurrencia < despuesDe.UltimaOcurrencia
                || (a.UltimaOcurrencia == despuesDe.UltimaOcurrencia && string.CompareOrdinal(a.Id, despuesDe.Id) > 0));
        }

        return Task.FromResult<IReadOnlyList<AlertaAgrupada>>([.. consulta.Take(maximo)]);
    }

    public Task<IReadOnlyList<AlertaAgrupada>> Desde(DateTimeOffset desde, int maximo, CancellationToken cancelacion = default) =>
        Falla is not null
            ? Task.FromException<IReadOnlyList<AlertaAgrupada>>(Falla)
            : Task.FromResult<IReadOnlyList<AlertaAgrupada>>([.. Alertas.Where(a => a.UltimaOcurrencia > desde).OrderBy(a => a.UltimaOcurrencia).Take(maximo)]);
}

/// <summary>Acumulador de límites en memoria: una ventana por inicio, con el dominio real (tope de filas, «otros» y cierre).</summary>
internal sealed class AcumuladorDeLimitesEnMemoria : IAcumuladorDeLimites
{
    public Dictionary<DateTimeOffset, AcumuladoDeLimite> Ventanas { get; } = [];

    public Exception? Falla { get; set; }

    public Task<ResultadoDeRegistroDeRechazo> Registrar(string huella, string limite, DateTimeOffset instante, string? direccionCompleta, CancellationToken cancelacion = default)
    {
        if (Falla is not null)
        {
            return Task.FromException<ResultadoDeRegistroDeRechazo>(Falla);
        }

        var ventana = VentanaDeLimite.Que(instante);
        if (!Ventanas.TryGetValue(ventana.Inicio, out var acumulado))
        {
            Ventanas[ventana.Inicio] = acumulado = new AcumuladoDeLimite(ventana);
        }

        return Task.FromResult(acumulado.Registrar(huella, limite, instante, direccionCompleta));
    }
}

internal sealed class ConteoLocalFalso : IConteoLocalDeEventos
{
    public int Accesos { get; set; }

    public int RechazosDeIngresos { get; set; }

    public List<(string Accion, DateTimeOffset Desde, DateTimeOffset Hasta)> Consultas { get; } = [];

    public Task<int> ContarRegistros(AccionAuditable accion, DateTimeOffset desde, DateTimeOffset hasta, CancellationToken cancelacion = default)
    {
        Consultas.Add((accion.Identificador, desde, hasta));
        return Task.FromResult(Accesos);
    }

    public Task<int> ContarRechazosDeLimite(string? limite, DateTimeOffset desde, DateTimeOffset hasta, CancellationToken cancelacion = default)
    {
        Consultas.Add(("limite:" + limite, desde, hasta));
        return Task.FromResult(RechazosDeIngresos);
    }
}

/// <summary>La fuente de los registros aislados de Identidad: solo el conteo importa en estas pruebas; puede fallar o tardar a pedido.</summary>
internal sealed class FuenteAisladaFalsa : IFuenteDeRegistrosAislados
{
    public int IngresosFallidos { get; set; }

    public Exception? Falla { get; set; }

    public bool Cuelga { get; set; }

    public List<SolicitudDeConteo> Conteos { get; } = [];

    public PaginaDeRegistros Pagina { get; set; } = new([], null);

    public List<ConsultaDeAuditoria> Consultas { get; } = [];

    public Task<PaginaDeRegistros> Consultar(ConsultaDeAuditoria consulta, CancellationToken cancelacion = default)
    {
        Consultas.Add(consulta);
        return Falla is not null ? Task.FromException<PaginaDeRegistros>(Falla) : Task.FromResult(Pagina);
    }

    public async Task<int> Contar(SolicitudDeConteo solicitud, CancellationToken cancelacion = default)
    {
        Conteos.Add(solicitud);
        if (Cuelga)
        {
            await Task.Delay(Timeout.Infinite, cancelacion);
        }

        return Falla is not null ? throw Falla : IngresosFallidos;
    }
}

/// <summary>Las alertas con correo pendiente sobre el mismo almacén en memoria, con los intentos y el próximo intento de cada una.</summary>
internal sealed class AlmacenDeCorreoEnMemoria(AlmacenDeAlertasEnMemoria alertas) : IAlmacenDeCorreoDeAlertas
{
    public Dictionary<string, (int Intentos, DateTimeOffset Proximo)> Intentos { get; } = [];

    public Task<IReadOnlyList<AlertaConCorreoPendiente>> ConCorreoPendiente(DateTimeOffset ahora, int maximo, CancellationToken cancelacion = default) =>
        Task.FromResult<IReadOnlyList<AlertaConCorreoPendiente>>(
        [
            .. alertas.Alertas
                .Where(a => !a.EnviadaPorCorreo && (!Intentos.TryGetValue(a.Id, out var i) || i.Proximo <= ahora))
                .OrderBy(a => a.PrimeraOcurrencia)
                .Take(maximo)
                .Select(a => new AlertaConCorreoPendiente(a, Intentos.TryGetValue(a.Id, out var i) ? i.Intentos : 0)),
        ]);

    public Task RegistrarIntentoFallido(string alertaId, int intentos, DateTimeOffset proximoIntento, CancellationToken cancelacion = default)
    {
        Intentos[alertaId] = (intentos, proximoIntento);
        return Task.CompletedTask;
    }
}

internal sealed class AlertasYaEnviadasEnMemoria : IAlertasYaEnviadas
{
    public HashSet<string> Ids { get; } = [];

    public Task<bool> Contiene(string alertaId, CancellationToken cancelacion = default) => Task.FromResult(Ids.Contains(alertaId));

    public Task Marcar(string alertaId, CancellationToken cancelacion = default)
    {
        Ids.Add(alertaId);
        return Task.CompletedTask;
    }
}

internal sealed class MarcaDeLecturaEnMemoria : IMarcaDeLecturaDeAlertas
{
    public DateTimeOffset? Valor { get; set; }

    public Task<DateTimeOffset?> Obtener(CancellationToken cancelacion = default) => Task.FromResult(Valor);

    public Task Guardar(DateTimeOffset marca, CancellationToken cancelacion = default)
    {
        Valor = marca;
        return Task.CompletedTask;
    }
}

internal sealed class DestinatariosFalsos : IDestinatariosDeAlertas
{
    public List<DestinatarioDeAlerta> Cuentas { get; } = [new(Guid.NewGuid()), new(Guid.NewGuid())];

    public Task<IReadOnlyList<DestinatarioDeAlerta>> Activos(CancellationToken cancelacion = default) => Task.FromResult<IReadOnlyList<DestinatarioDeAlerta>>([.. Cuentas]);

    public List<DestinatarioDeAlerta> Logistica { get; } = [];

    public bool LogisticaFalla { get; set; }

    public Task<IReadOnlyList<DestinatarioDeAlerta>> DeLogistica(CancellationToken cancelacion = default) =>
        LogisticaFalla ? throw new InvalidOperationException("No se pudo leer a logística.") : Task.FromResult<IReadOnlyList<DestinatarioDeAlerta>>([.. Logistica]);
}

/// <summary>
/// El puerto de correo con el tope de la fuente única (IDE-82): 24 correos al día de calendario en hora de Lima por motivo y servicio; el resto se suprime. Puede caerse.
/// </summary>
internal sealed class EnvioDeCorreoFalso(IReloj reloj) : IEnvioDeCorreoDeAlertas
{
    public const int TopeDiario = 24;

    private readonly Dictionary<(DateOnly Dia, string Servicio, string Motivo), int> _porDia = [];

    public List<(CorreoDeAlerta Correo, int Destinatarios)> Enviados { get; } = [];

    public Exception? Falla { get; set; }

    public int Intentos { get; private set; }

    public Task<ResultadoDeEnvioDeAlerta> Enviar(CorreoDeAlerta correo, IReadOnlyList<DestinatarioDeAlerta> destinatarios, CancellationToken cancelacion = default)
    {
        Intentos++;
        if (Falla is not null)
        {
            return Task.FromException<ResultadoDeEnvioDeAlerta>(Falla);
        }

        var clave = (reloj.HoyEnLima, correo.Servicio.ToString(), correo.Motivo.Valor);
        _porDia[clave] = _porDia.GetValueOrDefault(clave) + 1;
        if (_porDia[clave] > TopeDiario)
        {
            return Task.FromResult(ResultadoDeEnvioDeAlerta.Suprimido);
        }

        Enviados.Add((correo, destinatarios.Count));
        return Task.FromResult(ResultadoDeEnvioDeAlerta.Enviado);
    }
}
