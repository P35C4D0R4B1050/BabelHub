using BabelHub.Ecommerce.Domain.Biblioteca;

namespace BabelHub.Ecommerce.Application.Biblioteca.Interno;

/// <summary>La base no pudo atender a tiempo (conexión caída, <c>lock_timeout</c>): se responde «servicio no disponible» sin dejar nada a medias.</summary>
internal sealed class BibliotecaNoDisponibleException : Exception
{
    public BibliotecaNoDisponibleException()
    {
    }

    public BibliotecaNoDisponibleException(string message)
        : base(message)
    {
    }

    public BibliotecaNoDisponibleException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>Un cambio sobre un acceso y el acceso que quedó (para auditar su edición y su cuenta); <c>null</c> si no había fila que cambiar.</summary>
internal sealed record CambioDeAcceso<TResultado>(TResultado Resultado, Acceso? Acceso);

/// <summary>Lo que el repositorio devuelve al intentar contar una descarga (la sentencia condicional del plan §1, D14).</summary>
internal enum ResultadoDelConteoAtomico
{
    /// <summary>La sentencia afectó una fila: la descarga quedó contada.</summary>
    Contada,

    /// <summary>No afectó filas: el acceso no existe, es de otra cuenta, no está vigente o no tiene cupo; hay que releer para clasificar.</summary>
    NoAfecto,
}

internal enum ResultadoDeAgregarAyuda
{
    Agregada,
    YaHayAbierta,
    AccesoInexistente,
}

internal enum ResultadoDeRegistrarClave
{
    Registrada,

    /// <summary>Ya hay una clave vigente igual: es una repetición (BIB-05).</summary>
    RepeticionVigente,

    /// <summary>Una violación de la clave foránea (23503): el acceso ya no existe (BIB-28).</summary>
    AccesoInexistente,
}

/// <summary>
/// El repositorio de los accesos. Lo implementa la Infraestructura bajo <c>EjecutarComo(biblioteca)</c> con <b>una sola sentencia condicional por operación</b> (plan §5); lo que lee sin transacción lo lee sin bloqueo. Ningún método
/// guarda la URL firmada ni la clave de objeto del archivo.
/// </summary>
internal interface IRepositorioDeAccesos
{
    /// <summary>Lee sin bloqueo; con <paramref name="cuentaId"/>, filtrado por la cuenta (un acceso ajeno es lo mismo que uno inexistente).</summary>
    Task<Acceso?> Obtener(Guid accesoId, Guid? cuentaId, CancellationToken cancelacion = default);

    Task<IReadOnlyList<Acceso>> ListarDeLaCuenta(Guid cuentaId, int salto, int tomar, CancellationToken cancelacion = default);

    Task<int> ContarDeLaCuenta(Guid cuentaId, CancellationToken cancelacion = default);

    /// <summary>Las ediciones, de las pedidas, de las que la cuenta tiene un acceso vigente o suspendido (CAR-20, D10).</summary>
    Task<IReadOnlyList<Guid>> EdicionesPoseidas(Guid cuentaId, IReadOnlyCollection<Guid> edicionIds, CancellationToken cancelacion = default);

    /// <summary>El acceso de la compra (por pedido y edición) de la cuenta.</summary>
    Task<Acceso?> ObtenerDeLaCompra(Guid cuentaId, Guid pedidoId, Guid edicionId, CancellationToken cancelacion = default);

    /// <summary><c>INSERT … ON CONFLICT (cuenta_id, edicion_id) DO NOTHING</c> y, si ya existía, la reactivación condicionada a «revocado de otro pedido» (BIB-01, BIB-19, BIB-20). Participa en la transacción del llamador.</summary>
    Task<ResultadoDeConcesion> Conceder(Guid cuentaId, Guid edicionId, Guid pedidoId, DateTimeOffset ahora, CancellationToken cancelacion = default);

    /// <summary><c>UPDATE … WHERE pedido_id AND edicion_id AND estado &lt;&gt; 'revocado'</c> (BIB-24, D16).</summary>
    Task<CambioDeAcceso<ResultadoDeRevocacion>> Revocar(Guid pedidoId, Guid edicionId, CausaDeRevocacion causa, DateTimeOffset ahora, CancellationToken cancelacion = default);

    Task<CambioDeAcceso<ResultadoDeRevocacionSinEnlace>> RevocarSiNoHayEnlace(Guid cuentaId, Guid pedidoId, Guid edicionId, DateTimeOffset ahora, CancellationToken cancelacion = default);

    Task<CambioDeAcceso<ResultadoDeSuspension>> Suspender(Guid cuentaId, Guid accesoId, DateTimeOffset ahora, CancellationToken cancelacion = default);

    Task<CambioDeAcceso<ResultadoDeReanudacion>> Reanudar(Guid cuentaId, Guid accesoId, CancellationToken cancelacion = default);

    /// <summary><c>UPDATE … SET descargas_usadas + 1, enlaces_entregados + 1 WHERE id AND cuenta_id AND estado = 'vigente' AND descargas_usadas &lt; 5</c> (BIB-17, D14).</summary>
    Task<ResultadoDelConteoAtomico> ConsumirCupo(Guid accesoId, Guid cuentaId, CancellationToken cancelacion = default);

    /// <summary><c>UPDATE … SET descargas_usadas - n WHERE descargas_usadas &gt;= n AND estado &lt;&gt; 'revocado'</c>; devuelve el acceso resultante o <c>null</c> si no afectó filas.</summary>
    Task<Acceso?> Restituir(Guid accesoId, int cantidad, CancellationToken cancelacion = default);

    /// <summary>Elimina los accesos de la cuenta (y, en cascada, sus claves, registros y ayudas); devuelve cuántos (BIB-25).</summary>
    Task<int> EliminarDeLaCuenta(Guid cuentaId, CancellationToken cancelacion = default);
}

/// <summary>El registro de descargas, las claves de un solo uso y las ayudas (tablas propias de Biblioteca, separadas de la auditoría).</summary>
internal interface IRepositorioDeDescargas
{
    Task Registrar(RegistroDeDescarga registro, CancellationToken cancelacion = default);

    /// <summary>La clave registrada del acceso, vigente o no (para reconstruir el mismo enlace o decidir que venció).</summary>
    Task<ClaveRegistrada?> ObtenerClave(Guid accesoId, ClaveDeDescarga clave, CancellationToken cancelacion = default);

    /// <summary><c>INSERT … ON CONFLICT (acceso_id, clave_hash) DO UPDATE … WHERE vence_en &lt;= ahora</c>: si ya hay una vigente no afecta filas (D02, D24 de <c>transversal-seguridad</c>).</summary>
    Task<ResultadoDeRegistrarClave> RegistrarClave(ClaveRegistrada clave, DateTimeOffset ahora, CancellationToken cancelacion = default);

    /// <summary>Quita las claves vencidas antes de <paramref name="antes"/>; devuelve cuántas.</summary>
    Task<int> PurgarClaves(DateTimeOffset antes, CancellationToken cancelacion = default);

    /// <summary>Pone en nulo el acceso, el usuario y la hora de los registros de <paramref name="diaLimite"/> o antes; devuelve cuántos.</summary>
    Task<int> DepurarRegistros(DateOnly diaLimite, CancellationToken cancelacion = default);

    Task<SolicitudDeAyuda?> ObtenerAyudaAbierta(Guid accesoId, CancellationToken cancelacion = default);

    Task<SolicitudDeAyuda?> ObtenerAyuda(Guid ayudaId, CancellationToken cancelacion = default);

    /// <summary>Inserta la ayuda: «ya hay abierta» si el único parcial lo impide; «acceso inexistente» si la cuenta se canceló entre la lectura y la inserción (clave foránea, 23503; BIB-28).</summary>
    Task<ResultadoDeAgregarAyuda> AgregarAyuda(SolicitudDeAyuda ayuda, CancellationToken cancelacion = default);

    /// <summary>Cierra la ayuda solo si está abierta; devuelve falso si no afectó filas.</summary>
    Task<bool> CerrarAyuda(Guid ayudaId, DateTimeOffset ahora, CancellationToken cancelacion = default);

    Task<(IReadOnlyList<SolicitudDeAyuda> Ayudas, int Total)> ListarAyudas(EstadoDeAyuda? estado, int salto, int tomar, CancellationToken cancelacion = default);

    /// <summary>Los accesos de la lista que tienen una ayuda abierta.</summary>
    Task<IReadOnlyList<Guid>> AccesosConAyudaAbierta(IReadOnlyCollection<Guid> accesoIds, CancellationToken cancelacion = default);

    /// <summary>Elimina las ayudas de dos años o más (por día de Lima de su creación) de cualquier estado; devuelve cuántas.</summary>
    Task<int> DepurarAyudas(DateOnly diaLimite, CancellationToken cancelacion = default);
}

/// <summary>Lo que el almacén respondió a la comprobación de que el archivo existe (BIB-13).</summary>
internal enum ExistenciaDelArchivo
{
    Existe,
    NoExiste,

    /// <summary>El almacén no respondió a tiempo o respondió un error del servidor: no se consume nada y no se ofrece reportar.</summary>
    NoDisponible,
}

/// <summary>
/// El almacén de objetos visto desde Biblioteca (AD19, RNF14): comprobar que el archivo existe y firmar el enlace de descarga <b>sin red</b> y de forma determinista con el instante recibido (D02). La Infraestructura la implementa con una credencial de
/// <b>solo lectura</b>; la URL nunca se guarda ni se registra (SEG-10).
/// </summary>
internal interface IEntregaDeArchivos
{
    Task<ExistenciaDelArchivo> ExisteArchivo(string claveEnAlmacen, CancellationToken cancelacion = default);

    /// <summary>Firma el enlace con <paramref name="firmadoEn"/> y una caducidad de <paramref name="vigenciaEnSegundos"/> (300 como máximo); el mismo instante produce la misma URL.</summary>
    Uri FirmarEnlace(string claveEnAlmacen, string nombreDeArchivo, DateTimeOffset firmadoEn, int vigenciaEnSegundos);
}
