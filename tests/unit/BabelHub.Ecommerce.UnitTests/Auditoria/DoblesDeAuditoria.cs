using BabelHub.Ecommerce.Application.Auditoria.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Auditoria;

namespace BabelHub.Ecommerce.UnitTests.Auditoria;

/// <summary>Almacén de registros en memoria; puede fallar a pedido, como una base de datos que no responde.</summary>
internal sealed class AlmacenDeRegistrosEnMemoria : IAlmacenDeRegistros
{
    public List<RegistroDeAuditoria> Registros { get; } = [];

    public Exception? Falla { get; set; }

    public Task Insertar(RegistroDeAuditoria registro, CancellationToken cancelacion = default)
    {
        if (Falla is not null)
        {
            return Task.FromException(Falla);
        }

        Registros.Add(registro);
        return Task.CompletedTask;
    }
}

/// <summary>Publicador de alertas técnicas que guarda lo publicado; puede fallar a pedido.</summary>
internal sealed class AlertasTecnicasEspia : IPublicadorDeAlertasTecnicas
{
    public List<AlertaTecnica> Alertas { get; } = [];

    public Exception? Falla { get; set; }

    public Task Publicar(AlertaTecnica alerta, CancellationToken cancelacion = default)
    {
        if (Falla is not null)
        {
            return Task.FromException(Falla);
        }

        Alertas.Add(alerta);
        return Task.CompletedTask;
    }
}
