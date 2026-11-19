using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Pedidos;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Envios;

namespace BabelHub.Ecommerce.Application.Envios.Interno;

/// <summary>
/// Pedir la guía al operador (T06; ENV-02, ENV-04, ENV-10, ENV-17, ENV-18): un <b>orquestador sin transacción</b> con la llamada al operador fuera de toda transacción. (1) Lee el envío sin bloqueo y exige pendiente de guía.
/// (2) Lee el contacto por <see cref="IContactoDeEntregaDelPedido"/> en nombre de quien pide, <b>antes de fijar nada</b>: un campo faltante o anonimizado se rechaza sin llamar al operador. (2b) Una transacción corta fija la
/// clave de solicitud y el arrendamiento. (3) Llama al operador con solo nombre, dirección y teléfono. (4) Una segunda transacción corta guarda el número y el operador y audita. Todo fallo manejado libera el arrendamiento
/// (conservando la clave) para que el reintento no espere los 90 s, y la falla tras crearse la guía se recupera con la misma clave sin duplicar (AC-ENV-15).
/// </summary>
internal sealed class SolicitarGuia(
    AutorizadorDeEnvios autorizador,
    LimitesDeEnvios limites,
    IRepositorioDeEnvios envios,
    IContactoDeEntregaDelPedido contactos,
    IOperadorLogistico operador,
    IUnidadDeTrabajo unidad,
    IAuditoria auditoria,
    IPublicadorDeAlertasTecnicas alertas,
    AvisosDeEnvios avisos,
    IReloj reloj) : ICasoDeUso<Guid, DetalleDeEnvio>
{
    public async Task<Resultado<DetalleDeEnvio>> Ejecutar(Guid envioId, CancellationToken cancelacion = default)
    {
        if (await autorizador.ExigirLogisticaOAdministracion(cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return FallosDeEnvios.Fallo<DetalleDeEnvio>(rechazo);
        }

        if (await limites.Operacion(cancelacion).ConfigureAwait(false) is { } excedido)
        {
            return FallosDeEnvios.Fallo<DetalleDeEnvio>(excedido);
        }

        try
        {
            // (1) Sin bloqueo: solo para saber el pedido y rechazar pronto lo que no se puede pedir.
            var inicial = await envios.Obtener(envioId, paraActualizar: false, cancelacion).ConfigureAwait(false);
            if (inicial is null)
            {
                return FallosDeEnvios.De<DetalleDeEnvio>(CategoriaDeError.NoEncontrado, ClavesDeEnvios.EnvioNoEncontrado);
            }

            if (inicial.Estado != EstadoDeEnvio.PendienteDeGuia)
            {
                return FallosDeEnvios.De<DetalleDeEnvio>(CategoriaDeError.Conflicto, ClavesDeEnvios.EstadoInvalido);
            }

            // (2) El contacto, antes de fijar clave ni arrendamiento y fuera de toda transacción (ENV-10).
            var lectura = await contactos.Leer(inicial.PedidoId, cancelacion).ConfigureAwait(false);
            switch (lectura.Estado)
            {
                case EstadoDeLecturaDelContacto.NoDisponible:
                    return FallosDeEnvios.NoDisponible<DetalleDeEnvio>();
                case EstadoDeLecturaDelContacto.Anonimizado:
                    return ContactoIncompleto("anonimizado");
                case EstadoDeLecturaDelContacto.Faltante:
                    return ContactoIncompleto(NombreDelCampo(lectura.CampoFaltante));
            }

            var destinatario = DestinatarioDe(lectura.Contacto!);
            if (!destinatario.EsValido)
            {
                return ContactoIncompleto(NombreDelCampo(destinatario.CampoFaltante!.Value));
            }

            // Lo que el aviso necesita, antes de fijar nada: sin Pedidos no se cambia el estado y se reintenta.
            var datosDelAviso = await avisos.Preparar(inicial.PedidoId, cancelacion).ConfigureAwait(false);
            if (!datosDelAviso.EsExito)
            {
                return FallosDeEnvios.Fallo<DetalleDeEnvio>(datosDelAviso.Error!);
            }

            // El tope de solicitudes de guía al operador (30 por minuto en total): se reserva antes de fijar nada.
            if (await limites.GuiaAlOperador(cancelacion).ConfigureAwait(false) is { } sobreCarga)
            {
                return FallosDeEnvios.Fallo<DetalleDeEnvio>(sobreCarga);
            }

            // (2b) Transacción corta: la clave y el arrendamiento.
            var tomada = await unidad.Ejecutar(
                async token =>
                {
                    var envio = await envios.Obtener(envioId, paraActualizar: true, token).ConfigureAwait(false);
                    if (envio is null)
                    {
                        return (Error: (Resultado<DetalleDeEnvio>?)FallosDeEnvios.De<DetalleDeEnvio>(CategoriaDeError.NoEncontrado, ClavesDeEnvios.EnvioNoEncontrado), Clave: (Guid?)null);
                    }

                    switch (envio.TomarSolicitudDeGuia(reloj.Ahora, Guid.NewGuid()))
                    {
                        case ResultadoDeSolicitudDeGuia.Tomada:
                            await envios.Guardar(envio, token).ConfigureAwait(false);
                            return (Error: (Resultado<DetalleDeEnvio>?)null, Clave: envio.ClaveDeSolicitud);
                        case ResultadoDeSolicitudDeGuia.EnCurso:
                            return (Error: FallosDeEnvios.De<DetalleDeEnvio>(CategoriaDeError.Conflicto, ClavesDeEnvios.GuiaEnCurso), Clave: (Guid?)null);
                        default:
                            return (Error: FallosDeEnvios.De<DetalleDeEnvio>(CategoriaDeError.Conflicto, ClavesDeEnvios.EstadoInvalido), Clave: (Guid?)null);
                    }
                },
                cancelacion).ConfigureAwait(false);
            if (tomada.Error is not null)
            {
                return tomada.Error;
            }

            var clave = tomada.Clave!.Value;

            // (3) Fuera de toda transacción: solo nombre, dirección y teléfono (ENV-17).
            ResultadoDelOperador respuesta;
            try
            {
                respuesta = await operador.CrearOObtenerGuia(clave, destinatario.Destinatario!, cancelacion).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancelacion.IsCancellationRequested)
            {
                await LiberarElArrendamiento(envioId).ConfigureAwait(false);
                throw;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Una excepción inesperada del adaptador es un operador no disponible: el texto nunca se propaga (ENV-04, ENV-17).
                respuesta = new ResultadoDelOperador.NoDisponible();
            }

            if (respuesta is not ResultadoDelOperador.GuiaCreada creada)
            {
                await LiberarElArrendamiento(envioId).ConfigureAwait(false);
                return FallosDeEnvios.De<DetalleDeEnvio>(CategoriaDeError.NoDisponible, ClavesDeEnvios.OperadorNoDisponible);
            }

            // (4) Transacción corta: el número, el operador y la auditoría. Sin poder auditar se revierte y el reintento recupera la misma guía.
            try
            {
                return await unidad.Ejecutar(
                    async token =>
                    {
                        var envio = await envios.Obtener(envioId, paraActualizar: true, token).ConfigureAwait(false);
                        if (envio is null)
                        {
                            return FallosDeEnvios.De<DetalleDeEnvio>(CategoriaDeError.NoEncontrado, ClavesDeEnvios.EnvioNoEncontrado);
                        }

                        var anterior = envio.Estado;
                        var registro = envio.RegistrarGuia(creada.Numero, operador.Nombre, reloj.Ahora);
                        switch (registro)
                        {
                            case ResultadoDeRegistroDeGuia.Registrada:
                                await auditoria.Registrar(
                                    AuditoriaDeEnvios.Cambio(envio, anterior, EstadoDeEnvio.GuiaGenerada),
                                    token).ConfigureAwait(false);
                                await avisos.Solicitar(envio, datosDelAviso.Valor, AvisosDeEnvios.EstadoGuiaGenerada, token).ConfigureAwait(false);
                                await envios.Guardar(envio, token).ConfigureAwait(false);
                                return Resultado<DetalleDeEnvio>.Exito(MapeoDeEnvios.Detalle(envio));

                            case ResultadoDeRegistroDeGuia.YaRegistrada:
                                // Otra solicitud ganó con el mismo número: éxito sin auditar de nuevo.
                                return Resultado<DetalleDeEnvio>.Exito(MapeoDeEnvios.Detalle(envio));

                            case ResultadoDeRegistroDeGuia.OtroNumero:
                            case ResultadoDeRegistroDeGuia.GuiaSinUso:
                                await alertas.Publicar(new AlertaTecnica(registro == ResultadoDeRegistroDeGuia.OtroNumero ? ClavesDeEnvios.AlertaGuiaRepetida : ClavesDeEnvios.AlertaGuiaSinUso, reloj.Ahora), token).ConfigureAwait(false);
                                return FallosDeEnvios.De<DetalleDeEnvio>(CategoriaDeError.Conflicto, ClavesDeEnvios.EstadoInvalido);

                            default:
                                return FallosDeEnvios.De<DetalleDeEnvio>(CategoriaDeError.Conflicto, ClavesDeEnvios.EstadoInvalido);
                        }
                    },
                    cancelacion).ConfigureAwait(false);
            }
            catch (GuiaRepetidaException)
            {
                // El operador devolvió el número de otro envío (ENV-04): alerta técnica sin el número y reintento; nunca se ve la guía de otro pedido.
                await LiberarElArrendamiento(envioId).ConfigureAwait(false);
                try
                {
                    await alertas.Publicar(new AlertaTecnica(ClavesDeEnvios.AlertaGuiaRepetida, reloj.Ahora), CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Una alerta que no se publica nunca interrumpe la operación.
                }

                return FallosDeEnvios.De<DetalleDeEnvio>(CategoriaDeError.NoDisponible, ClavesDeEnvios.OperadorNoDisponible);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // La guía ya existe en el operador: se libera el arrendamiento (la clave se conserva) para recuperarla al reintentar.
                await LiberarElArrendamiento(envioId).ConfigureAwait(false);
                return FallosDeEnvios.NoDisponible<DetalleDeEnvio>();
            }
        }
        catch (EnviosNoDisponiblesException)
        {
            return FallosDeEnvios.NoDisponible<DetalleDeEnvio>();
        }
    }

    private async Task LiberarElArrendamiento(Guid envioId)
    {
        try
        {
            await unidad.Ejecutar(
                async token =>
                {
                    var envio = await envios.Obtener(envioId, paraActualizar: true, token).ConfigureAwait(false);
                    if (envio is { Estado: EstadoDeEnvio.PendienteDeGuia })
                    {
                        envio.LiberarArrendamientoDeLaGuia();
                        await envios.Guardar(envio, token).ConfigureAwait(false);
                    }

                    return true;
                },
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (EnviosNoDisponiblesException)
        {
            // El arrendamiento vence solo a los 90 s: el reintento espera, pero nada queda a medias.
        }
    }

    private static Resultado<DetalleDeEnvio> ContactoIncompleto(string campo) =>
        FallosDeEnvios.De<DetalleDeEnvio>(CategoriaDeError.ReglaDeNegocio, ClavesDeEnvios.ContactoIncompleto, [new ErrorDeCampo(campo, "faltante")]);

    /// <summary>La región que Pedidos guarda aparte se entrega al operador como parte de la dirección (ENV-17 limita el contacto a nombre, dirección y teléfono).</summary>
    private static ResultadoDeValidacionDelDestinatario DestinatarioDe(ContactoDeEntrega contacto) =>
        DestinatarioParaGuia.Validar(contacto.Nombre, $"{contacto.Direccion}, {contacto.Region}", contacto.Telefono);

    private static string NombreDelCampo(CampoDelContactoDeEntrega? campo) => campo switch
    {
        CampoDelContactoDeEntrega.Nombre => "nombre",
        CampoDelContactoDeEntrega.Telefono => "telefono",
        CampoDelContactoDeEntrega.Direccion => "direccion",
        CampoDelContactoDeEntrega.Region => "region",
        _ => "contacto",
    };

    private static string NombreDelCampo(CampoDelDestinatario campo) => campo switch
    {
        CampoDelDestinatario.Nombre => "nombre",
        CampoDelDestinatario.Telefono => "telefono",
        _ => "direccion",
    };
}
