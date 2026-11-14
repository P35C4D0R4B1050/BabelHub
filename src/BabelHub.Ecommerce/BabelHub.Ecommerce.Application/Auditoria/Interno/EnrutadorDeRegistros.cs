using BabelHub.Ecommerce.Domain.Auditoria;

namespace BabelHub.Ecommerce.Application.Auditoria.Interno;

/// <summary>
/// Elige el almacén de cada registro por la marca «aislada» de su acción (D17): los de Identidad van a <see cref="IAlmacenDeRegistrosAislados"/>
/// (<c>identidad.registros</c>, que Identidad escribe en la misma transacción) y los demás al almacén propio del módulo.
/// </summary>
public sealed class EnrutadorDeRegistros(IAlmacenDeRegistros propio, IAlmacenDeRegistrosAislados aislado) : IAlmacenDeRegistros
{
    public Task Insertar(RegistroDeAuditoria registro, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(registro);
        return registro.Aislada
            ? aislado.Insertar(registro.AModeloDeContrato(), cancelacion)
            : propio.Insertar(registro, cancelacion);
    }
}

/// <summary>
/// El enrutador de los eventos de seguridad que no conceden nada (AUD-12): igual que <see cref="EnrutadorDeRegistros"/>, pero los de Identidad se
/// escriben con <see cref="IAlmacenDeRegistrosAislados.InsertarAislandoElFallo"/> y los demás con un almacén que no deja abortada la transacción de
/// quien llama (<see cref="IAlmacenDeEventosDeSeguridad"/>).
/// </summary>
public sealed class EnrutadorDeEventosDeSeguridad(IAlmacenDeEventosDeSeguridad propio, IAlmacenDeRegistrosAislados aislado) : IAlmacenDeEventosDeSeguridad
{
    public Task Insertar(RegistroDeAuditoria registro, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(registro);
        return registro.Aislada
            ? aislado.InsertarAislandoElFallo(registro.AModeloDeContrato(), cancelacion)
            : propio.Insertar(registro, cancelacion);
    }
}
