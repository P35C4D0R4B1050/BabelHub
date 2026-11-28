using BabelHub.Ecommerce.Application.Devoluciones;
using BabelHub.Ecommerce.Application.Pagos;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Devoluciones;
using BabelHub.Ecommerce.UnitTests.Soporte;

namespace BabelHub.Ecommerce.UnitTests.Devoluciones;

/// <summary>La conciliación de reembolsos de devoluciones (T11; DEV-08, DEV-17, DEV-21, D08; AC-DEV-08, AC-DEV-16, AC-DEV-20): registra y procesa lo que faltó y pasa a reembolsada lo que Pagos ya completó, sin repetir pasos.</summary>
public sealed class ConciliacionDeReembolsosTests
{
    private static readonly DateTimeOffset Ahora = EntornoDeDevoluciones.AhoraInicial;

    /// <summary>Una devolución recibida con la pasarela caída (reembolso registrado y pendiente), vista por el sistema.</summary>
    private static async Task<(EntornoDeDevoluciones Sistema, Guid DevolucionId)> ConRecibidaPendiente()
    {
        var cliente = new EntornoDeDevoluciones();
        var linea = EntornoDeDevoluciones.Entregada(Ahora.AddDays(-2), 3, 50m);
        var pedido = cliente.SembrarPedido(linea);
        var solicitada = await cliente.SolicitarImpreso.Ejecutar(new SolicitarDevolucionDeImpresoEntrada(pedido.PedidoId, [new LineaPedidaParaDevolver(linea.LineaId, 2)], new TextoLibre("x"), "lima-central"));
        var admin = new EntornoDeDevoluciones(ActorDePrueba.DeAdministradorCentral()).ConLosDatosDe(cliente);
        await admin.Gestion.Aprobar(solicitada.Valor.DevolucionId);
        admin.Reembolsos.ProcesarResponde = EstadoDelReembolso.Pendiente;
        Assert.True((await admin.Recepcion.Ejecutar(solicitada.Valor.DevolucionId)).EsExito);
        return (new EntornoDeDevoluciones(ActorDePrueba.DelSistema()).ConLosDatosDe(admin), solicitada.Valor.DevolucionId);
    }

    private static Devolucion Guardada(EntornoDeDevoluciones entorno, Guid id) => entorno.Repositorio.Obtener(id, false).GetAwaiter().GetResult()!;

    [Fact]
    [Trait("Criterio", "AC-DEV-20")]
    public async Task Si_Pagos_informa_el_reembolso_completado_por_el_reintento_de_la_administracion_la_linea_pasa_a_reembolsada_como_sistema_sin_registrar_otro()
    {
        var (sistema, id) = await ConRecibidaPendiente();
        var registrados = sistema.Reembolsos.Registrados.Count;
        var linea = Guardada(sistema, id).Lineas[0];
        sistema.Reembolsos.PorClaveNatural[$"dev:{id:D}:{linea.LineaDePedidoId:D}"] = EstadoDelReembolso.Reembolsado;

        var resultado = await sistema.Conciliacion.Ejecutar(10);

        Assert.Equal(1, resultado.Valor);
        var despues = Guardada(sistema, id).Lineas[0];
        Assert.Equal(EstadoDeLineaDeDevolucion.Reembolsada, despues.Estado);
        Assert.Equal(ResponsableDelCambio.Sistema, despues.Cambios[^1].Responsable);
        Assert.Equal(registrados, sistema.Reembolsos.Registrados.Count);
        Assert.Contains(sistema.Auditoria.Solicitudes, s => s.Detalle.GetValueOrDefault("estadoNuevo") == "reembolsada");
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-08")]
    public async Task Si_el_reembolso_sigue_pendiente_se_vuelve_a_procesar_y_con_la_confirmacion_pasa_a_reembolsada()
    {
        var (sistema, id) = await ConRecibidaPendiente();
        var linea = Guardada(sistema, id).Lineas[0];
        sistema.Reembolsos.PorClaveNatural[$"dev:{id:D}:{linea.LineaDePedidoId:D}"] = EstadoDelReembolso.Pendiente;
        var procesados = sistema.Reembolsos.Procesados;

        var aun = await sistema.Conciliacion.Ejecutar(10);
        sistema.Reembolsos.ProcesarResponde = EstadoDelReembolso.Reembolsado;
        var despues = await sistema.Conciliacion.Ejecutar(10);

        Assert.Equal(1, aun.Valor);
        Assert.Equal(1, despues.Valor);
        Assert.Equal(procesados + 2, sistema.Reembolsos.Procesados);
        Assert.Equal(EstadoDeLineaDeDevolucion.Reembolsada, Guardada(sistema, id).Lineas[0].Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-16")]
    public async Task Una_linea_ya_reembolsada_no_se_toca_y_repetir_la_conciliacion_no_repite_nada()
    {
        var (sistema, id) = await ConRecibidaPendiente();
        var linea = Guardada(sistema, id).Lineas[0];
        sistema.Reembolsos.PorClaveNatural[$"dev:{id:D}:{linea.LineaDePedidoId:D}"] = EstadoDelReembolso.Reembolsado;
        await sistema.Conciliacion.Ejecutar(10);
        var auditorias = sistema.Auditoria.Solicitudes.Count;
        var procesados = sistema.Reembolsos.Procesados;

        var repetida = await sistema.Conciliacion.Ejecutar(10);

        Assert.Equal(0, repetida.Valor);
        Assert.Equal(auditorias, sistema.Auditoria.Solicitudes.Count);
        Assert.Equal(procesados, sistema.Reembolsos.Procesados);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-08")]
    public async Task Una_linea_recibida_sin_reembolso_registrado_se_registra_con_la_clave_natural_y_se_procesa()
    {
        var cliente = new EntornoDeDevoluciones();
        var linea = EntornoDeDevoluciones.Entregada(Ahora.AddDays(-2), 3, 50m);
        var pedido = cliente.SembrarPedido(linea);
        var solicitada = await cliente.SolicitarImpreso.Ejecutar(new SolicitarDevolucionDeImpresoEntrada(pedido.PedidoId, [new LineaPedidaParaDevolver(linea.LineaId, 1)], new TextoLibre("x"), "lima-central"));
        var id = solicitada.Valor.DevolucionId;
        var sistema = new EntornoDeDevoluciones(ActorDePrueba.DelSistema()).ConLosDatosDe(cliente);
        // Recibida sin que se haya registrado el reembolso (una falla entre pasos): se arma directamente sobre el almacén.
        var devolucion = Guardada(sistema, id);
        devolucion.Aprobar(Ahora, ResponsableDelCambio.Personal(Guid.NewGuid()));
        devolucion.IniciarReingreso(Ahora);
        devolucion.ConfirmarReingreso(Ahora);
        devolucion.Recibir(Ahora, ResponsableDelCambio.Personal(Guid.NewGuid()));
        await sistema.Repositorio.Guardar(devolucion);

        var resultado = await sistema.Conciliacion.Ejecutar(10);

        Assert.Equal(1, resultado.Valor);
        Assert.Equal($"dev:{id:D}:{linea.LineaId:D}", Assert.Single(sistema.Reembolsos.Registrados).ClaveNatural);
        Assert.Equal(50m, sistema.Reembolsos.Registrados[0].Lineas[0].Monto);
        Assert.Equal(EstadoDeLineaDeDevolucion.Reembolsada, Guardada(sistema, id).Lineas[0].Estado);
        Assert.Equal(0, sistema.Reembolsos.ProcesosDentroDeLaTransaccion);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-20")]
    public async Task Con_Pagos_caido_no_se_cambia_nada_y_una_falla_de_una_linea_no_detiene_la_corrida()
    {
        var (sistema, id) = await ConRecibidaPendiente();
        sistema.Reembolsos.ConsultaFalla = true;

        var resultado = await sistema.Conciliacion.Ejecutar(10);

        Assert.Equal(0, resultado.Valor);
        Assert.Equal(EstadoDeLineaDeDevolucion.Recibida, Guardada(sistema, id).Lineas[0].Estado);
    }

    [Theory]
    [Trait("Criterio", "AC-DEV-08")]
    [InlineData("cliente")]
    [InlineData("anonimo")]
    [InlineData("integracion")]
    public async Task Un_actor_que_no_es_el_sistema_no_concilia_y_cada_intento_deja_un_evento(string quien)
    {
        var (sistema, _) = await ConRecibidaPendiente();
        var actor = quien switch
        {
            "cliente" => ActorDePrueba.DeCliente(),
            "encargado" => ActorDePrueba.DeEncargadoDe(Guid.NewGuid()),
            "integracion" => ActorDePrueba.DeIntegracion("n8n"),
            _ => ActorDePrueba.Anonimo(),
        };
        var otro = new EntornoDeDevoluciones(actor).ConLosDatosDe(sistema);

        var resultado = await otro.Conciliacion.Ejecutar(10);

        Assert.True(resultado.Error!.Categoria is CategoriaDeError.Prohibido or CategoriaDeError.NoAutenticado);
        Assert.Single(otro.Denegaciones.Eventos);
        Assert.Equal(EstadoDeLineaDeDevolucion.Recibida, otro.Repositorio.Todas.Single().Lineas[0].Estado);
    }
}
