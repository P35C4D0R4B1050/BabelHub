using BabelHub.Inventario.Application.Auditoria;
using BabelHub.Inventario.Application.Plataforma;

namespace BabelHub.Inventario.Application.Reservas.Interno;

internal sealed class ValidadorDeProcesarBajaDeTitular : IValidador<ProcesarBajaDeTitularEntrada>
{
    public ResultadoDeValidacion Validar(ProcesarBajaDeTitularEntrada entrada)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        return entrada.TitularId == Guid.Empty
            ? ResultadoDeValidacion.Invalido([new ErrorDeCampo("titularId", ConstructorDeValidacion.Obligatorio)])
            : ResultadoDeValidacion.Valido;
    }
}

/// <summary>Las bajas de titular recibidas por minuto: más de 20 solo generan una alerta técnica (plan §6, AM31) y nunca rechazan una baja, que es una obligación legal y que Identidad reintenta hasta que se acepta (IDE-64).</summary>
internal sealed class VolumenDeBajasDeTitular(IReloj reloj)
{
    public const string Tipo = "bajas-de-titular";

    public const string Motivo = "bajas-de-titular-anomalas";

    public const int UmbralPorMinuto = 20;

    private readonly VolumenDeOperaciones _volumen = new(reloj, UmbralPorMinuto, TimeSpan.FromMinutes(1));

    /// <summary>Cuenta una baja recibida y devuelve <see langword="true"/> la primera vez que el minuto supera el umbral.</summary>
    public bool RegistrarYDebeAvisar() => _volumen.RegistrarYDebeAvisar(Tipo);
}

/// <summary>
/// La baja de un titular de cuenta cancelada (SEL-34, SEL-35, D09), con la credencial de servicio <c>baja-de-titular</c> y sin token de usuario. Una sola transacción que toma el <b>bloqueo del cliente</b> (el mismo de
/// reservar, primero en la jerarquía de D04), registra la baja en <c>bajas_recibidas</c> (<c>ON CONFLICT DO NOTHING</c>), sustituye <c>cliente_id</c> de las cabeceras por el <b>identificador anónimo estable</b> y anula la
/// <c>huella_origen</c>, sustituye el sujeto en las claves de los eventos de límite (sin cambiar el conteo: el del origen vive en la huella de los eventos) y audita <b>solo si la baja es nueva</b>, con el identificador
/// anónimo. No toca líneas, stock ni unidades vendidas. Repetirla vuelve a aplicar las sustituciones (normalmente cero filas) y no escribe una segunda auditoría; un titular sin reservas termina sin error; si no se puede
/// auditar, nada se guarda (AUD-04). Reservar con un cliente dado de baja se rechaza (SEL-35, en <c>ReservarUnidades</c>). El alcance se comprueba antes de leer o escribir nada; más de 20 bajas por minuto solo avisan.
/// </summary>
internal sealed class ProcesarBajaDeTitular(
    AutorizadorDeReservas autorizador,
    IValidador<ProcesarBajaDeTitularEntrada> validador,
    IBloqueosAsesores bloqueos,
    IRepositorioDeReservas reservas,
    ISustitutorDeSujetoDeLimites limites,
    IGeneradorDeIdentificadorAnonimo generador,
    IAuditoriaDeBajasRecibidas auditoria,
    VolumenDeBajasDeTitular volumen,
    IAlertas alertas,
    IReloj reloj) : ICasoDeUso<ProcesarBajaDeTitularEntrada, BajaDeTitularProcesada>
{
    public async Task<Resultado<BajaDeTitularProcesada>> Ejecutar(ProcesarBajaDeTitularEntrada entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);

        if (await autorizador.ExigirIntegracion(AlcanceDeServicio.BajaDeTitular, cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return FallosDeReservas.Fallo<BajaDeTitularProcesada>(rechazo);
        }

        var validacion = validador.Validar(entrada);
        if (!validacion.EsValido)
        {
            return FallosDeReservas.Validacion<BajaDeTitularProcesada>(validacion);
        }

        // Un volumen anómalo solo avisa: la baja se procesa siempre (la 21 también).
        if (volumen.RegistrarYDebeAvisar())
        {
            await AlertasDeVolumenDeReservas.Publicar(alertas, VolumenDeBajasDeTitular.Motivo, cancelacion).ConfigureAwait(false);
        }

        await bloqueos.Cliente(entrada.TitularId, cancelacion).ConfigureAwait(false);

        var anonimo = generador.Generar(entrada.TitularId);
        var esNueva = await reservas.RegistrarBaja(entrada.TitularId, reloj.Ahora, cancelacion).ConfigureAwait(false);

        // También en la repetición: repara una sustitución que hubiera quedado a medias y normalmente no cambia ninguna fila.
        await reservas.AnonimizarCliente(entrada.TitularId, anonimo, cancelacion).ConfigureAwait(false);
        await limites.Sustituir(entrada.TitularId.ToString("D"), anonimo.ToString("D"), cancelacion).ConfigureAwait(false);

        var aviso = BajaDeTitularRecibida.Validar([BajaDeTitularRecibida.CampoDelIdentificador], anonimo.ToString("D"));
        if (!aviso.EsExito)
        {
            // El identificador anónimo siempre es un GUID no vacío en minúsculas; si no lo fuera, es un error de programación y la transacción se revierte.
            throw new InvalidOperationException("El identificador anónimo de la baja no tiene la forma de un GUID canónico.");
        }

        await auditoria.Registrar(aviso.Valor, esNueva, cancelacion).ConfigureAwait(false);

        return Resultado<BajaDeTitularProcesada>.Exito(new BajaDeTitularProcesada(EraRepeticion: !esNueva));
    }
}

/// <summary>
/// <see cref="IBajasRecibidas"/>: las bajas que <c>Auditoría</c> aún no consumió, para llenar <c>por_anonimizar</c> (AUD-19, AUD-32). <c>Reservas</c> es dueño de <c>bajas_recibidas</c>; Auditoría no lee su esquema.
/// Los dos métodos corren con el actor de quien los llama (la depuración corre como sistema).
/// </summary>
internal sealed class BajasRecibidasDeReservas(IRepositorioDeReservas reservas, IReloj reloj) : IBajasRecibidas
{
    public Task<IReadOnlyList<Guid>> Pendientes(int maximo, CancellationToken cancelacion = default) => reservas.BajasPendientesDeAuditoria(maximo, cancelacion);

    public Task MarcarEntregadas(IReadOnlyCollection<Guid> identificadores, CancellationToken cancelacion = default) => reservas.MarcarBajasEntregadas(identificadores, reloj.Ahora, cancelacion);
}
