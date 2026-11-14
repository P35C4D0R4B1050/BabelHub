using BabelHub.Ecommerce.Domain.Auditoria;
using TipoDeActorDeDominio = BabelHub.Ecommerce.Domain.Auditoria.TipoDeActor;
using TipoDeActorDePlataforma = BabelHub.Ecommerce.Application.Plataforma.TipoDeActor;

namespace BabelHub.Ecommerce.Application.Auditoria.Interno;

/// <summary>El paso del registro del Dominio al modelo propio del contrato (el Dominio de <c>Auditoria</c> no sale del módulo, regla 13).</summary>
public static class ConversionDeRegistros
{
    public static RegistroAuditado AModeloDeContrato(this RegistroDeAuditoria registro)
    {
        ArgumentNullException.ThrowIfNull(registro);
        return new RegistroAuditado(
            registro.Id,
            registro.Servicio switch
            {
                ServicioDeOrigen.Ecommerce => ServicioAuditado.Ecommerce,
                ServicioDeOrigen.Inventario => ServicioAuditado.Inventario,
                _ => throw new ArgumentException("El servicio no es válido.", nameof(registro)),
            },
            registro.Tipo switch
            {
                TipoDeRegistro.Auditoria => TipoDeRegistroAuditado.Auditoria,
                TipoDeRegistro.Seguridad => TipoDeRegistroAuditado.Seguridad,
                TipoDeRegistro.Ambos => TipoDeRegistroAuditado.Ambos,
                _ => throw new ArgumentException("El tipo no es válido.", nameof(registro)),
            },
            registro.Actor.Tipo switch
            {
                TipoDeActorDeDominio.Usuario => TipoDeActorDePlataforma.Usuario,
                TipoDeActorDeDominio.Sistema => TipoDeActorDePlataforma.Sistema,
                TipoDeActorDeDominio.Integracion => TipoDeActorDePlataforma.Integracion,
                TipoDeActorDeDominio.Anonimo => TipoDeActorDePlataforma.Anonimo,
                _ => throw new ArgumentException("El tipo de actor no es válido.", nameof(registro)),
            },
            registro.Actor.Id,
            registro.Actor.Rol,
            registro.InstanteUtc,
            registro.Accion,
            registro.Resultado switch
            {
                ResultadoDeOperacion.Exitoso => ResultadoAuditado.Exitoso,
                ResultadoDeOperacion.Rechazado => ResultadoAuditado.Rechazado,
                ResultadoDeOperacion.Fallido => ResultadoAuditado.Fallido,
                _ => throw new ArgumentException("El resultado no es válido.", nameof(registro)),
            })
        {
            ObjetoTipo = registro.ObjetoTipo,
            ObjetoId = registro.ObjetoId,
            SucursalOrigen = registro.SucursalOrigen,
            SucursalDestino = registro.SucursalDestino,
            Motivo = registro.Motivo,
            Origen = registro.Origen,
            Ruta = registro.Ruta,
            Detalle = registro.Detalle.Texto,
            ClavesPersonales = [.. registro.Detalle.ClavesPersonales],
            Truncado = registro.Truncado,
            Correlacion = registro.Correlacion,
            AnonimizadoEn = registro.AnonimizadoEn,
        };
    }
}
