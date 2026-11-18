using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Application.Traslados;
using BabelHub.Inventario.Application.Traslados.Interno;
using BabelHub.Inventario.Domain.Traslados;
using BabelHub.Inventario.UnitTests.Soporte;
using BabelHub.Shared.Contratos;

namespace BabelHub.Inventario.UnitTests.Traslados;

/// <summary>T18, T20 y T21: la entrega del buzón al E-commerce, la revisión de plazos de 48 horas y la depuración, sin base de datos ni red.</summary>
public sealed class SalidaDeTrasladosTests
{
    private static (EntornoDeTraslados Entorno, Guid Edicion) Preparar()
    {
        var entorno = new EntornoDeTraslados(ActorDePrueba.DelSistema());
        var edicion = entorno.Impresa();
        entorno.Poner(entorno.Cusco, edicion, 10, 0);
        return (entorno, edicion);
    }

    /// <summary>Los mensajes de un cambio de estado de un traslado, creados en el instante dado (como los dejaría la operación).</summary>
    private static void Dejar(EntornoDeTraslados entorno, Traslado traslado, DateTimeOffset creados) =>
        entorno.Buzon.Insertar([.. ConversionDeTraslados.Mensajes(traslado, "cusco", "ayacucho", creados)]).GetAwaiter().GetResult();

    private static Traslado Vinculado(Guid edicion, EntornoDeTraslados entorno, EstadoDeTraslado estado)
    {
        var t = Traslado.VincularAUnPedido(Guid.NewGuid(), edicion, entorno.Cusco, entorno.Ayacucho, 2, Guid.NewGuid(), EstimacionDeLlegada.Calcular(new DateOnly(2026, 11, 14), 2), null, EntornoDeTraslados.Ahora.AddDays(-1));
        t.AsignarNumero(7);
        switch (estado)
        {
            case EstadoDeTraslado.EnTransito: t.Aprobar(EntornoDeTraslados.Ahora); t.Despachar(EntornoDeTraslados.Ahora); break;
            case EstadoDeTraslado.Recibido: t.Aprobar(EntornoDeTraslados.Ahora); t.Despachar(EntornoDeTraslados.Ahora); t.Recibir(EntornoDeTraslados.Ahora); break;
        }

        return t;
    }

    // ---------- T18: entrega ----------

    [Fact]
    [Trait("Criterio", "AC-TRA-08")]
    public async Task UnCambioDeEstadoSeEntregaALasDosSucursalesConCamposTipados_YQuedaEnviado()
    {
        var (entorno, edicion) = Preparar();
        var traslado = entorno.Reposicion(edicion, entorno.Cusco, entorno.Ayacucho, 4);
        entorno.Como(RolesDeUsuario.AdministradorCentral);
        await entorno.Aprobar().Ejecutar(new AprobarTrasladoEntrada(traslado.Id));
        entorno.Actor.Establecer(ActorActual.DelSistema());

        var resultado = await entorno.Entrega().Ejecutar(new EntregarMensajesSalientesEntrada());

        Assert.Equal((2, 2, 0, 0), (resultado.Valor.Reclamados, resultado.Valor.Entregados, resultado.Valor.Reprogramados, resultado.Valor.Fallidos));
        Assert.Equal(["ayacucho", "cusco"], entorno.Envio.Avisos.Select(a => a.CodigoDeSucursal).Order().ToArray());
        var alOrigen = entorno.Envio.Avisos.Single(a => a.CodigoDeSucursal == "cusco");
        Assert.Equal(($"traslado:{traslado.Id:D}:aprobado", traslado.Numero, "aprobado", "origen", 4), (alOrigen.EventoId, alOrigen.NumeroDeTraslado, alOrigen.Estado, alOrigen.RolDeLaSucursal, alOrigen.Cantidad));
        Assert.All(entorno.Buzon.Mensajes, m => Assert.Equal("enviado", entorno.Cola.De(m).Valor));
        Assert.Empty(entorno.Alertas.Tecnicas);
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-09")]
    public async Task ElEstadoDelTrasladoDeUnPedidoSeEnviaAlEcommerceConSuClaveDeIdempotencia_YLaSucursalDeDestinoRecibeElAviso()
    {
        var (entorno, edicion) = Preparar();
        var traslado = Vinculado(edicion, entorno, EstadoDeTraslado.Recibido);
        Dejar(entorno, traslado, EntornoDeTraslados.Ahora);

        var resultado = await entorno.Entrega().Ejecutar(new EntregarMensajesSalientesEntrada());

        Assert.Equal(3, resultado.Valor.Entregados);
        var estado = Assert.Single(entorno.Envio.Estados);
        Assert.Equal(($"traslado:{traslado.Id:D}:recibido", traslado.Id, edicion, 2, "recibido"), (estado.EventoId, estado.TrasladoId, estado.EdicionId, estado.Cantidad, estado.Estado));
        Assert.Equal((traslado.PedidoId!.Value, (DateOnly?)new DateOnly(2026, 11, 16), false), (estado.PedidoId, estado.FechaEstimadaDeLlegada, estado.EstimadaPorConfirmar));
        Assert.Contains(entorno.Envio.Avisos, a => a.CodigoDeSucursal == "ayacucho" && a.RolDeLaSucursal == "destino");
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-09")]
    public async Task UnTrasladoConRutaSinDefinirSeInformaPorConfirmar()
    {
        var (entorno, edicion) = Preparar();
        var t = Traslado.VincularAUnPedido(Guid.NewGuid(), edicion, entorno.Cusco, entorno.Ayacucho, 1, Guid.NewGuid(), EstimacionDeLlegada.Calcular(new DateOnly(2026, 11, 14), null), null, EntornoDeTraslados.Ahora);
        t.AsignarNumero(3);
        t.Aprobar(EntornoDeTraslados.Ahora);
        t.Despachar(EntornoDeTraslados.Ahora);
        Dejar(entorno, t, EntornoDeTraslados.Ahora);

        await entorno.Entrega().Ejecutar(new EntregarMensajesSalientesEntrada());

        var estado = Assert.Single(entorno.Envio.Estados);
        Assert.Equal(((DateOnly?)null, true, "en-transito"), (estado.FechaEstimadaDeLlegada, estado.EstimadaPorConfirmar, estado.Estado));
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-09")]
    public async Task LosEstadosDeUnMismoTrasladoSeEntreganEnOrden_ElRecibidoEsperaAQueElEnTransitoSeEntregue()
    {
        var (entorno, edicion) = Preparar();
        var enTransito = Vinculado(edicion, entorno, EstadoDeTraslado.EnTransito);
        var recibido = Traslado.Rehidratar(enTransito.Id, 7, edicion, entorno.Cusco, entorno.Ayacucho, 2, EstadoDeTraslado.Recibido, null, null, enTransito.PedidoId, false, enTransito.FechaEstimada, false, null, null, enTransito.SolicitadoEn, EntornoDeTraslados.Ahora, EntornoDeTraslados.Ahora, EntornoDeTraslados.Ahora.AddMinutes(1), EntornoDeTraslados.Ahora.AddMinutes(1), null, 3);
        Dejar(entorno, enTransito, EntornoDeTraslados.Ahora);
        Dejar(entorno, recibido, EntornoDeTraslados.Ahora.AddMinutes(1));
        entorno.Base.Reloj.Ahora = EntornoDeTraslados.Ahora.AddMinutes(2);
        entorno.Envio.Respuesta = ResultadoDeEnvio.Reintentable;

        await entorno.Entrega().Ejecutar(new EntregarMensajesSalientesEntrada());

        Assert.Equal(["en-transito"], entorno.Envio.Estados.Select(e => e.Estado).ToArray());

        entorno.Envio.Respuesta = ResultadoDeEnvio.Entregado;
        entorno.Base.Reloj.Ahora = EntornoDeTraslados.Ahora.AddMinutes(5);
        await entorno.Entrega().Ejecutar(new EntregarMensajesSalientesEntrada());
        Assert.Equal(["en-transito", "en-transito"], entorno.Envio.Estados.Select(e => e.Estado).ToArray());
        await entorno.Entrega().Ejecutar(new EntregarMensajesSalientesEntrada());

        Assert.Equal(["en-transito", "en-transito", "recibido"], entorno.Envio.Estados.Select(e => e.Estado).ToArray());
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-08")]
    public async Task ElEnvioSeHaceFueraDeTodaTransaccion_ElReclamoYElRegistroSonTransaccionesCortas()
    {
        var (entorno, edicion) = Preparar();
        Dejar(entorno, entorno.Reposicion(edicion, entorno.Cusco, entorno.Ayacucho, 1), EntornoDeTraslados.Ahora);

        await entorno.Entrega().Ejecutar(new EntregarMensajesSalientesEntrada());

        Assert.False(entorno.Envio.EnvioDentroDeUnaTransaccion);
        Assert.Equal(1 + 2, entorno.Unidad.Transacciones);
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-08")]
    public async Task UnEcommerceCaidoDejaElCambioAplicadoYElMensajePendiente_QueSeEntregaDespues()
    {
        var (entorno, edicion) = Preparar();
        var traslado = entorno.Reposicion(edicion, entorno.Cusco, entorno.Ayacucho, 4);
        entorno.Como(RolesDeUsuario.AdministradorCentral);
        var aprobado = await entorno.Aprobar().Ejecutar(new AprobarTrasladoEntrada(traslado.Id));
        entorno.Actor.Establecer(ActorActual.DelSistema());
        entorno.Envio.Respuesta = ResultadoDeEnvio.Reintentable;

        var caido = await entorno.Entrega().Ejecutar(new EntregarMensajesSalientesEntrada());
        entorno.Base.Reloj.Ahora = EntornoDeTraslados.Ahora.AddMinutes(2);
        entorno.Envio.Respuesta = ResultadoDeEnvio.Entregado;
        var recuperado = await entorno.Entrega().Ejecutar(new EntregarMensajesSalientesEntrada());

        Assert.True(aprobado.EsExito);
        Assert.Equal(EstadoDeTraslado.Aprobado, entorno.Traslados.Almacenado(traslado.Id).Estado);
        Assert.Equal((2, 0), (caido.Valor.Reprogramados, caido.Valor.Entregados));
        Assert.Equal((2, 2), (recuperado.Valor.Reclamados, recuperado.Valor.Entregados));
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-08")]
    public async Task UnAdaptadorQueLanzaNuncaInterrumpeLaEntrega_SeReintenta()
    {
        var (entorno, edicion) = Preparar();
        Dejar(entorno, entorno.Reposicion(edicion, entorno.Cusco, entorno.Ayacucho, 1), EntornoDeTraslados.Ahora);
        entorno.Envio.Lanza = new InvalidOperationException("red caída");

        var resultado = await entorno.Entrega().Ejecutar(new EntregarMensajesSalientesEntrada());

        Assert.True(resultado.EsExito);
        Assert.Equal(2, resultado.Valor.Reprogramados);
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-08")]
    public async Task LosReintentosSonDe1_5_15_30_60MinutosYLuegoCadaHora()
    {
        var (entorno, edicion) = Preparar();
        var traslado = entorno.Reposicion(edicion, entorno.Cusco, entorno.Ayacucho, 1);
        Dejar(entorno, traslado, EntornoDeTraslados.Ahora);
        var mensaje = entorno.Buzon.Mensajes.First(m => m.SucursalCodigo == "cusco");
        entorno.Envio.Respuesta = ResultadoDeEnvio.Reintentable;
        var esperas = new List<double>();
        var ahora = EntornoDeTraslados.Ahora;

        for (var intento = 1; intento <= 7; intento++)
        {
            entorno.Base.Reloj.Ahora = ahora;
            await entorno.Entrega().Ejecutar(new EntregarMensajesSalientesEntrada());
            var estado = entorno.Cola.De(mensaje);
            esperas.Add((estado.ProximaFecha - ahora).TotalMinutes);
            Assert.Equal(intento, estado.Intentos);
            ahora = estado.ProximaFecha;
        }

        Assert.Equal([1d, 5, 15, 30, 60, 60, 60], esperas);
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-08")]
    public async Task UnMensajeNoSeReclamaAntesDeSuFecha_NiMientrasDuraSuArrendamiento()
    {
        var (entorno, edicion) = Preparar();
        Dejar(entorno, entorno.Reposicion(edicion, entorno.Cusco, entorno.Ayacucho, 1), EntornoDeTraslados.Ahora);
        entorno.Envio.Respuesta = ResultadoDeEnvio.Reintentable;
        await entorno.Entrega().Ejecutar(new EntregarMensajesSalientesEntrada());

        var antes = await entorno.Entrega().Ejecutar(new EntregarMensajesSalientesEntrada());
        var arrendado = await entorno.Cola.Reclamar(10, EntornoDeTraslados.Ahora.AddMinutes(2), TimeSpan.FromMinutes(5));
        var concurrente = await entorno.Cola.Reclamar(10, EntornoDeTraslados.Ahora.AddMinutes(3), TimeSpan.FromMinutes(5));
        var vencido = await entorno.Cola.Reclamar(10, EntornoDeTraslados.Ahora.AddMinutes(8), TimeSpan.FromMinutes(5));

        Assert.Equal(0, antes.Valor.Reclamados);
        Assert.Equal((2, 0, 2), (arrendado.Count, concurrente.Count, vencido.Count));
    }

    [Theory]
    [Trait("Criterio", "AC-TRA-08")]
    [InlineData("permanente")]
    [InlineData("credencial")]
    public async Task UnErrorPermanenteOUnaCredencialRechazadaDejanFallidoConAlerta_SinReintentarCadaHora(string motivo)
    {
        var (entorno, edicion) = Preparar();
        Dejar(entorno, entorno.Reposicion(edicion, entorno.Cusco, entorno.Ayacucho, 1), EntornoDeTraslados.Ahora);
        entorno.Envio.Respuesta = motivo == "permanente" ? ResultadoDeEnvio.Permanente : ResultadoDeEnvio.CredencialRechazada;

        var resultado = await entorno.Entrega().Ejecutar(new EntregarMensajesSalientesEntrada());
        entorno.Base.Reloj.Ahora = EntornoDeTraslados.Ahora.AddHours(3);
        var despues = await entorno.Entrega().Ejecutar(new EntregarMensajesSalientesEntrada());

        // Una credencial rechazada corta la corrida: solo el primero queda fallido y el resto sigue pendiente; un error permanente falla cada mensaje.
        var fallidos = motivo == "credencial" ? 1 : 2;
        Assert.Equal(fallidos, resultado.Valor.Fallidos);
        Assert.All(entorno.Buzon.Mensajes.Where(m => entorno.Cola.De(m).Valor == "fallido"), m => Assert.Equal(motivo, entorno.Cola.De(m).MotivoDeFallo));
        Assert.Equal(2, entorno.Buzon.Mensajes.Count(m => entorno.Cola.De(m).Valor == "fallido"));
        Assert.Equal(2, entorno.Alertas.Tecnicas.Count);
        Assert.Equal(motivo == "credencial" ? 1 : 0, despues.Valor.Reclamados);
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-08")]
    public async Task UnAvisoDeMasDe24HorasQuedaFallidoConAlerta()
    {
        var (entorno, edicion) = Preparar();
        Dejar(entorno, entorno.Reposicion(edicion, entorno.Cusco, entorno.Ayacucho, 1), EntornoDeTraslados.Ahora);
        entorno.Base.Reloj.Ahora = EntornoDeTraslados.Ahora.AddHours(25);
        entorno.Envio.Respuesta = ResultadoDeEnvio.Reintentable;

        var resultado = await entorno.Entrega().Ejecutar(new EntregarMensajesSalientesEntrada());

        Assert.Equal(2, resultado.Valor.Fallidos);
        Assert.All(entorno.Buzon.Mensajes, m => Assert.Equal("vencido", entorno.Cola.De(m).MotivoDeFallo));
        Assert.Equal(2, entorno.Alertas.Tecnicas.Count);
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-09")]
    public async Task ElEstadoDelPedidoSigueCadaHoraYAlertaUnaSolaVezALas24Horas()
    {
        var (entorno, edicion) = Preparar();
        var traslado = Vinculado(edicion, entorno, EstadoDeTraslado.EnTransito);
        Dejar(entorno, traslado, EntornoDeTraslados.Ahora);
        var estadoDelPedido = entorno.Buzon.Mensajes.Single(m => m.Tipo == "estado-de-pedido");
        entorno.Envio.Respuesta = ResultadoDeEnvio.Reintentable;

        entorno.Base.Reloj.Ahora = EntornoDeTraslados.Ahora.AddHours(25);
        await entorno.Entrega().Ejecutar(new EntregarMensajesSalientesEntrada());
        var primera = entorno.Alertas.Tecnicas.Count;
        var estado = entorno.Cola.De(estadoDelPedido);
        entorno.Base.Reloj.Ahora = estado.ProximaFecha;
        await entorno.Entrega().Ejecutar(new EntregarMensajesSalientesEntrada());

        Assert.Equal(("pendiente", true), (entorno.Cola.De(estadoDelPedido).Valor, entorno.Cola.De(estadoDelPedido).Alertado));
        Assert.Equal(3, primera);
        Assert.Equal(2, entorno.Envio.Estados.Count);
        Assert.Equal(3, entorno.Alertas.Tecnicas.Count);
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-08")]
    public async Task UnaCargaInvalidaEsUnErrorPermanente_YUnaAlertaQueFallaNoInterrumpe()
    {
        var (entorno, _) = Preparar();
        await entorno.Buzon.Insertar([new MensajeSaliente(Guid.NewGuid(), "aviso", $"traslado:{Guid.NewGuid():D}:aprobado", "cusco", "{\"estado\":1}", EntornoDeTraslados.Ahora)]);
        entorno.Alertas.Falla = true;

        var resultado = await entorno.Entrega().Ejecutar(new EntregarMensajesSalientesEntrada());

        Assert.True(resultado.EsExito);
        Assert.Equal(1, resultado.Valor.Fallidos);
        Assert.Empty(entorno.Envio.Avisos);
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-08")]
    public async Task SoloElActorSistemaEntregaMensajes_SinActorNoSeToca()
    {
        var (entorno, edicion) = Preparar();
        Dejar(entorno, entorno.Reposicion(edicion, entorno.Cusco, entorno.Ayacucho, 1), EntornoDeTraslados.Ahora);
        entorno.Como(RolesDeUsuario.AdministradorCentral);
        var comoUsuario = await entorno.Entrega().Ejecutar(new EntregarMensajesSalientesEntrada());
        entorno.Actor.Establecer(null);
        var sinActor = await entorno.Entrega().Ejecutar(new EntregarMensajesSalientesEntrada());

        Assert.Equal(CategoriaDeError.Prohibido, comoUsuario.Error!.Categoria);
        Assert.Equal(CategoriaDeError.NoAutenticado, sinActor.Error!.Categoria);
        Assert.Equal(0, entorno.Cola.Reclamos);
        Assert.Empty(entorno.Envio.Avisos);
    }

    [Fact]
    public void LasAlertasDeLaSalidaUsanSoloMotivosDelCatalogoCerrado()
    {
        var catalogo = BabelHub.Inventario.Application.Auditoria.Interno.CatalogoDeAlertasDeAuditoria.Crear();

        Assert.True(catalogo.Contiene(EntregarMensajesSalientes.MotivoDeAlerta));
        Assert.True(catalogo.Contiene(RevisarTrasladosSinAprobar.MotivoDeAlerta));
    }

    // ---------- T20: revisión de plazos ----------

    [Fact]
    [Trait("Criterio", "AC-TRA-21")]
    public async Task UnVinculadoSolicitadoHace49HorasAlertaYUnoDe47No_YCadaTrasladoAvisaUnaSolaVez()
    {
        var (entorno, edicion) = Preparar();
        var ahora = entorno.Base.Reloj.Ahora;
        var hace49 = Traslado.VincularAUnPedido(Guid.NewGuid(), edicion, entorno.Cusco, entorno.Ayacucho, 1, Guid.NewGuid(), EstimacionDeLlegada.Calcular(new DateOnly(2026, 11, 14), 2), null, ahora.AddHours(-49));
        var hace47 = Traslado.VincularAUnPedido(Guid.NewGuid(), edicion, entorno.Cusco, entorno.Ayacucho, 1, Guid.NewGuid(), EstimacionDeLlegada.Calcular(new DateOnly(2026, 11, 14), 2), null, ahora.AddHours(-47));
        entorno.Traslados.Sembrar(hace49);
        entorno.Traslados.Sembrar(hace47);

        var primera = await entorno.Revision().Ejecutar(new RevisarTrasladosSinAprobarEntrada());
        var repetida = await entorno.Revision().Ejecutar(new RevisarTrasladosSinAprobarEntrada());
        entorno.Base.Reloj.Ahora = ahora.AddHours(2);
        var despues = await entorno.Revision().Ejecutar(new RevisarTrasladosSinAprobarEntrada());

        Assert.Equal((1, 0, 1), (primera.Valor.Avisados, repetida.Valor.Avisados, despues.Valor.Avisados));
        Assert.Equal(["traslado-vinculado-sin-aprobar", "traslado-vinculado-sin-aprobar"], entorno.Alertas.Tecnicas.ToArray());
        Assert.NotNull(entorno.Traslados.Almacenado(hace49.Id).AlertaDeDemoraEn);
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-21")]
    public async Task UnVinculadoAprobadoOUnaReposicionNoAlertan()
    {
        var (entorno, edicion) = Preparar();
        entorno.Base.Reloj.Ahora = EntornoDeTraslados.Ahora.AddDays(5);
        var aprobado = Traslado.VincularAUnPedido(Guid.NewGuid(), edicion, entorno.Cusco, entorno.Ayacucho, 1, Guid.NewGuid(), EstimacionDeLlegada.Calcular(new DateOnly(2026, 11, 14), 2), null, EntornoDeTraslados.Ahora.AddDays(-3));
        aprobado.Aprobar(EntornoDeTraslados.Ahora);
        entorno.Traslados.Sembrar(aprobado);
        entorno.Traslados.Sembrar(Traslado.SolicitarReposicion(Guid.NewGuid(), edicion, entorno.Cusco, entorno.Ayacucho, 1, null, Guid.NewGuid(), EntornoDeTraslados.Ahora.AddDays(-3)));

        var resultado = await entorno.Revision().Ejecutar(new RevisarTrasladosSinAprobarEntrada());

        Assert.Equal(0, resultado.Valor.Avisados);
        Assert.Empty(entorno.Alertas.Tecnicas);
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-21")]
    public async Task UnTrasladoAprobadoEnParaleloNoSeAvisa_LaMarcaEsCondicional()
    {
        var (entorno, edicion) = Preparar();
        var vencido = Traslado.VincularAUnPedido(Guid.NewGuid(), edicion, entorno.Cusco, entorno.Ayacucho, 1, Guid.NewGuid(), EstimacionDeLlegada.Calcular(new DateOnly(2026, 11, 14), 2), null, EntornoDeTraslados.Ahora.AddHours(-60));
        entorno.Traslados.Sembrar(vencido);
        var aprobado = entorno.Traslados.Almacenado(vencido.Id);
        aprobado.Aprobar(EntornoDeTraslados.Ahora);
        entorno.Traslados.Sembrar(aprobado);

        var marcado = await entorno.Traslados.MarcarAlertaDeDemora(vencido.Id, EntornoDeTraslados.Ahora);

        Assert.False(marcado);
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-21")]
    public async Task SoloElActorSistemaRevisaLosPlazos()
    {
        var (entorno, _) = Preparar();
        entorno.Como(RolesDeUsuario.AdministradorCentral);

        var resultado = await entorno.Revision().Ejecutar(new RevisarTrasladosSinAprobarEntrada());

        Assert.Equal(CategoriaDeError.Prohibido, resultado.Error!.Categoria);
    }

    // ---------- T21: depuración ----------

    [Fact]
    public async Task ALos2AnosSeAnonimizaElSolicitanteYSeVacianLosTextos_ConservandoElMovimiento_YRepetirNoCambiaNada()
    {
        var (entorno, edicion) = Preparar();
        var ahora = entorno.Base.Reloj.Ahora;
        var viejo = entorno.Cerrado(edicion, ahora.AddYears(-2).AddDays(-1));
        var reciente = entorno.Cerrado(edicion, ahora.AddYears(-2).AddDays(1));

        var primera = await entorno.Depuracion().Ejecutar(new DepurarTrasladosEntrada());
        var repetida = await entorno.Depuracion().Ejecutar(new DepurarTrasladosEntrada());

        var anonimo = entorno.Traslados.Almacenado(viejo.Id);
        Assert.Equal((1, 0), (primera.Valor.Anonimizados, repetida.Valor.Anonimizados));
        Assert.Equal(((Guid?)null, (string?)null, (string?)null, (string?)null, EstadoDeTraslado.Recibido, 1, viejo.Numero), (anonimo.SolicitadoPor, anonimo.Motivo, anonimo.MotivoDeCierre, anonimo.Guia, anonimo.Estado, anonimo.Cantidad, anonimo.Numero));
        var conserva = entorno.Traslados.Almacenado(reciente.Id);
        Assert.Equal(("motivo viejo", "CA-1111"), (conserva.Motivo, conserva.Guia));
        Assert.NotNull(conserva.SolicitadoPor);
    }

    [Fact]
    public async Task ALos30DiasSePurganLosMensajesEnviadosOFallidos_NuncaLosPendientes()
    {
        var (entorno, edicion) = Preparar();
        var ahora = entorno.Base.Reloj.Ahora;
        var traslado = entorno.Reposicion(edicion, entorno.Cusco, entorno.Ayacucho, 1);
        var viejos = ConversionDeTraslados.Mensajes(traslado, "cusco", "ayacucho", ahora.AddDays(-31));
        await entorno.Buzon.Insertar([.. viejos]);
        var pendienteViejo = new MensajeSaliente(Guid.NewGuid(), "aviso", $"traslado:{traslado.Id:D}:cancelado", "cusco", "{}", ahora.AddDays(-40));
        var recienteEnviado = new MensajeSaliente(Guid.NewGuid(), "aviso", $"traslado:{traslado.Id:D}:recibido", "cusco", "{}", ahora.AddDays(-5));
        await entorno.Buzon.Insertar([pendienteViejo, recienteEnviado]);
        await entorno.Cola.MarcarEnviado(viejos.First().Id, ahora.AddDays(-31));
        await entorno.Cola.MarcarFallido(viejos.Last().Id, MotivoDeFalloDeMensaje.Permanente, 1, ahora.AddDays(-31));
        await entorno.Cola.MarcarEnviado(recienteEnviado.Id, ahora.AddDays(-5));

        var resultado = await entorno.Depuracion().Ejecutar(new DepurarTrasladosEntrada());

        Assert.Equal(2, resultado.Valor.MensajesPurgados);
        Assert.Equal(new[] { pendienteViejo.Id, recienteEnviado.Id }.Order().ToArray(), entorno.Buzon.Mensajes.Select(m => m.Id).Order().ToArray());
    }

    [Fact]
    public async Task SoloElActorSistemaDepura()
    {
        var (entorno, _) = Preparar();
        entorno.Como(RolesDeUsuario.PersonalDeLogistica);
        var comoLogistica = await entorno.Depuracion().Ejecutar(new DepurarTrasladosEntrada());

        Assert.Equal(CategoriaDeError.Prohibido, comoLogistica.Error!.Categoria);
        Assert.Equal(0, entorno.Traslados.Anonimizaciones);
    }
}
