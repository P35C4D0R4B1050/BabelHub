namespace BabelHub.Inventario.Domain.Traslados;

/// <summary>El rol con el que actúa quien opera un traslado, para decidir sus permisos (TRA-09). Cualquier otro rol o actor es <see cref="Otro"/>.</summary>
public enum RolDeTraslado
{
    AdministracionCentral,
    EncargadoDeSucursal,
    Logistica,
    Cliente,
    Otro,
}

/// <summary>Las operaciones que un actor puede intentar sobre un traslado.</summary>
public enum OperacionDeTraslado
{
    Solicitar,
    Aprobar,
    Rechazar,
    Cancelar,
    Despachar,
    Recibir,
    RegistrarGuia,
    Consultar,
}

/// <summary>Por qué se deniega una operación: el rol no la tiene o la sucursal no es la que corresponde (AUD-05).</summary>
public enum MotivoDeDenegacionDeTraslado
{
    FueraDeRol,
    FueraDeSucursal,
}

/// <summary>El resultado de decidir un permiso: permitido, o el motivo de la denegación (un único evento de acceso denegado).</summary>
public readonly record struct DecisionDePermiso
{
    private DecisionDePermiso(MotivoDeDenegacionDeTraslado? motivo) => Motivo = motivo;

    public bool Permitido => Motivo is null;

    public MotivoDeDenegacionDeTraslado? Motivo { get; }

    public static DecisionDePermiso Si => new(null);

    public static DecisionDePermiso No(MotivoDeDenegacionDeTraslado motivo) => new(motivo);
}

/// <summary>
/// La matriz de permisos de TRA-09, decidida <b>solo por rol y sucursal</b> (D08): aprobar, rechazar, cancelar y despachar los hace el origen, recibir el destino, la guía logística y la administración central todo lo permitido; logística y
/// cliente solo lo que les toca (logística registra la guía y consulta). El destino ajeno de un encargado al solicitar es una violación de alcance (TRA-20). Que un traslado sea vinculado, o su estado, son <b>reglas de negocio</b> (422 o 409 sin
/// evento de seguridad), no permisos: la administración puede intentar rechazar un vinculado y recibe el 422 del Dominio (TRA-16). Sin dependencias: se prueba como una tabla rol × operación con negativos.
/// </summary>
public static class PermisosDeTraslado
{
    /// <summary>Decide si <paramref name="rol"/>, con la sucursal <paramref name="sucursalDelActor"/> (nula si no tiene), puede hacer <paramref name="operacion"/> sobre un traslado de <paramref name="origenId"/> a <paramref name="destinoId"/>.</summary>
    public static DecisionDePermiso Evaluar(OperacionDeTraslado operacion, RolDeTraslado rol, Guid? sucursalDelActor, Guid origenId, Guid destinoId)
    {
        switch (rol)
        {
            case RolDeTraslado.AdministracionCentral:
                return DecisionDePermiso.Si;

            case RolDeTraslado.EncargadoDeSucursal:
                return Encargado(operacion, sucursalDelActor, origenId, destinoId);

            case RolDeTraslado.Logistica:
                return operacion is OperacionDeTraslado.RegistrarGuia or OperacionDeTraslado.Consultar
                    ? DecisionDePermiso.Si
                    : DecisionDePermiso.No(MotivoDeDenegacionDeTraslado.FueraDeRol);

            default:
                return DecisionDePermiso.No(MotivoDeDenegacionDeTraslado.FueraDeRol);
        }
    }

    /// <summary>
    /// Si el rol puede intentar la operación en alguna sucursal: es lo primero que se comprueba, antes de leer el traslado, para que un rol sin ningún permiso (cliente, logística fuera de la guía y la consulta, cualquier otro) reciba su rechazo
    /// sin saber si el traslado existe. La sucursal se decide después con <see cref="Evaluar"/>.
    /// </summary>
    public static bool RolPuedeIntentar(OperacionDeTraslado operacion, RolDeTraslado rol) =>
        rol switch
        {
            RolDeTraslado.AdministracionCentral => true,
            RolDeTraslado.EncargadoDeSucursal => operacion != OperacionDeTraslado.RegistrarGuia,
            RolDeTraslado.Logistica => operacion is OperacionDeTraslado.RegistrarGuia or OperacionDeTraslado.Consultar,
            _ => false,
        };

    private static DecisionDePermiso Encargado(OperacionDeTraslado operacion, Guid? sucursal, Guid origenId, Guid destinoId)
    {
        // La guía es de logística: el encargado no la registra, sea cual sea su sucursal.
        if (operacion == OperacionDeTraslado.RegistrarGuia)
        {
            return DecisionDePermiso.No(MotivoDeDenegacionDeTraslado.FueraDeRol);
        }

        // Un encargado sin sucursal no es un comodín: se decide por rol y sucursal, nunca por omisión.
        var propia = sucursal is { } s && s != Guid.Empty ? s : (Guid?)null;
        var permitido = operacion switch
        {
            OperacionDeTraslado.Solicitar => propia == destinoId,
            OperacionDeTraslado.Aprobar or OperacionDeTraslado.Rechazar or OperacionDeTraslado.Cancelar or OperacionDeTraslado.Despachar => propia == origenId,
            OperacionDeTraslado.Recibir => propia == destinoId,
            OperacionDeTraslado.Consultar => propia == origenId || propia == destinoId,
            _ => false,
        };

        return permitido ? DecisionDePermiso.Si : DecisionDePermiso.No(MotivoDeDenegacionDeTraslado.FueraDeSucursal);
    }
}

/// <summary>Por qué se rechaza una solicitud de traslado (TRA-20). La respuesta es uniforme y no lleva cifras de otras sucursales.</summary>
public enum MotivoDeRechazoDeSolicitud
{
    MismoOrigenYDestino,
    CantidadNoPositiva,
    EdicionNoImpresa,
    CantidadMayorQueElVendible,
}

/// <summary>
/// Las reglas de una solicitud de traslado (TRA-20): origen distinto de destino, cantidad positiva, edición impresa y cantidad no mayor que el vendible del origen. El destino ajeno de un encargado <b>no</b> es de aquí: es una violación de
/// alcance que decide <see cref="PermisosDeTraslado"/>. Con varias violaciones, el primer motivo es el que se informa.
/// </summary>
public static class ReglasDeSolicitud
{
    public static MotivoDeRechazoDeSolicitud? Evaluar(Guid origenId, Guid destinoId, int cantidad, bool edicionImpresa, int vendibleDelOrigen)
    {
        if (origenId == destinoId)
        {
            return MotivoDeRechazoDeSolicitud.MismoOrigenYDestino;
        }

        if (cantidad <= 0)
        {
            return MotivoDeRechazoDeSolicitud.CantidadNoPositiva;
        }

        if (!edicionImpresa)
        {
            return MotivoDeRechazoDeSolicitud.EdicionNoImpresa;
        }

        return cantidad > vendibleDelOrigen ? MotivoDeRechazoDeSolicitud.CantidadMayorQueElVendible : null;
    }
}
