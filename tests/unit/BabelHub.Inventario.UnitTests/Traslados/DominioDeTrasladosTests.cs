using BabelHub.Inventario.Domain.Traslados;

namespace BabelHub.Inventario.UnitTests.Traslados;

/// <summary>T01 a T03: la máquina de estados, los permisos por rol y sucursal y las reglas de solicitud de los traslados, sin base de datos.</summary>
public sealed class DominioDeTrasladosTests
{
    private static readonly Guid Edicion = Guid.NewGuid();
    private static readonly Guid Lima = Guid.NewGuid();
    private static readonly Guid Cusco = Guid.NewGuid();
    private static readonly Guid Ayacucho = Guid.NewGuid();
    private static readonly DateTimeOffset Ahora = new(2026, 11, 17, 10, 0, 0, TimeSpan.Zero);

    private static Traslado Reposicion(int cantidad = 4) => Traslado.SolicitarReposicion(Guid.NewGuid(), Edicion, Cusco, Ayacucho, cantidad, "reposición", Guid.NewGuid(), Ahora);

    private static Traslado Vinculado() => Traslado.VincularAUnPedido(Guid.NewGuid(), Edicion, Cusco, Ayacucho, 1, Guid.NewGuid(), EstimacionDeLlegada.Calcular(DateOnly.FromDateTime(Ahora.UtcDateTime), 2), null, Ahora);

    // ---------- T01: la máquina de estados ----------

    [Fact]
    [Trait("Criterio", "AC-TRA-01")]
    public void UnaSolicitudNaceSolicitada_ConSuVersionEnCero_YSinPedido()
    {
        var traslado = Reposicion();

        Assert.Equal((EstadoDeTraslado.Solicitado, 0L, false, 0L), (traslado.Estado, traslado.Version, traslado.EsVinculado, traslado.Numero));
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-06")]
    public void ElCicloCompleto_SolicitadoAprobadoEnTransitoRecibido_SubeLaVersionEnCadaCambio()
    {
        var traslado = Reposicion();

        traslado.Aprobar(Ahora.AddHours(1));
        traslado.Despachar(Ahora.AddHours(2));
        traslado.Recibir(Ahora.AddHours(30));

        Assert.Equal((EstadoDeTraslado.Recibido, 3L), (traslado.Estado, traslado.Version));
        Assert.Equal((Ahora.AddHours(1), Ahora.AddHours(2), Ahora.AddHours(30), Ahora.AddHours(30)), (traslado.AprobadoEn, traslado.DespachadoEn, traslado.RecibidoEn, traslado.CerradoEn));
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-02")]
    public void DespacharSinAprobarSeRechazaSinCambiarNada()
    {
        var traslado = Reposicion();

        Assert.Throws<TransicionDeTrasladoInvalidaException>(() => traslado.Despachar(Ahora));

        Assert.Equal((EstadoDeTraslado.Solicitado, 0L, (DateTimeOffset?)null), (traslado.Estado, traslado.Version, traslado.DespachadoEn));
    }

    public static TheoryData<EstadoDeTraslado, string> TransicionesImposibles() =>
        new()
        {
            { EstadoDeTraslado.Solicitado, "despachar" }, { EstadoDeTraslado.Solicitado, "recibir" },
            { EstadoDeTraslado.Aprobado, "aprobar" }, { EstadoDeTraslado.Aprobado, "rechazar" }, { EstadoDeTraslado.Aprobado, "recibir" },
            { EstadoDeTraslado.EnTransito, "aprobar" }, { EstadoDeTraslado.EnTransito, "rechazar" }, { EstadoDeTraslado.EnTransito, "cancelar" }, { EstadoDeTraslado.EnTransito, "despachar" },
            { EstadoDeTraslado.Recibido, "aprobar" }, { EstadoDeTraslado.Recibido, "despachar" }, { EstadoDeTraslado.Recibido, "recibir" }, { EstadoDeTraslado.Recibido, "cancelar" },
            { EstadoDeTraslado.Rechazado, "aprobar" }, { EstadoDeTraslado.Rechazado, "cancelar" }, { EstadoDeTraslado.Rechazado, "despachar" },
            { EstadoDeTraslado.Cancelado, "aprobar" }, { EstadoDeTraslado.Cancelado, "despachar" }, { EstadoDeTraslado.Cancelado, "cancelar" }, { EstadoDeTraslado.Cancelado, "rechazar" },
        };

    private static Traslado EnEstado(EstadoDeTraslado estado)
    {
        var t = Reposicion();
        switch (estado)
        {
            case EstadoDeTraslado.Aprobado: t.Aprobar(Ahora); break;
            case EstadoDeTraslado.EnTransito: t.Aprobar(Ahora); t.Despachar(Ahora); break;
            case EstadoDeTraslado.Recibido: t.Aprobar(Ahora); t.Despachar(Ahora); t.Recibir(Ahora); break;
            case EstadoDeTraslado.Rechazado: t.Rechazar("sin stock", Ahora); break;
            case EstadoDeTraslado.Cancelado: t.Cancelar("error de pedido", Ahora); break;
        }

        return t;
    }

    [Theory]
    [Trait("Criterio", "AC-TRA-02")]
    [MemberData(nameof(TransicionesImposibles))]
    public void CualquierOtraTransicionSeRechaza_YElTrasladoNoCambia(EstadoDeTraslado estado, string operacion)
    {
        var traslado = EnEstado(estado);
        var version = traslado.Version;

        Assert.Throws<TransicionDeTrasladoInvalidaException>(() =>
        {
            switch (operacion)
            {
                case "aprobar": traslado.Aprobar(Ahora); break;
                case "rechazar": traslado.Rechazar("x", Ahora); break;
                case "cancelar": traslado.Cancelar("x", Ahora); break;
                case "despachar": traslado.Despachar(Ahora); break;
                default: traslado.Recibir(Ahora); break;
            }
        });

        Assert.Equal((estado, version), (traslado.Estado, traslado.Version));
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-14")]
    public void UnTrasladoVinculadoNoSeRechazaNiSeCancelaAMano_ElDeReposicionSi()
    {
        var vinculado = Vinculado();

        Assert.Throws<TrasladoVinculadoNoSeRechazaException>(() => vinculado.Rechazar("sin stock", Ahora));
        Assert.Throws<TrasladoVinculadoNoSeRechazaException>(() => vinculado.Cancelar("error", Ahora));
        Assert.Equal((EstadoDeTraslado.Solicitado, 0L), (vinculado.Estado, vinculado.Version));

        var reposicion = Reposicion();
        reposicion.Rechazar("sin stock", Ahora);
        Assert.Equal(("sin stock", EstadoDeTraslado.Rechazado), (reposicion.MotivoDeCierre, reposicion.Estado));
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-14")]
    public void ElDominioNoPermiteReHidratarUnVinculadoRechazado()
    {
        Assert.Throws<ArgumentException>(() => Traslado.Rehidratar(Guid.NewGuid(), 1, Edicion, Cusco, Ayacucho, 1, EstadoDeTraslado.Rechazado, null, null, Guid.NewGuid(), false, null, true, null, null, Ahora, null, null, null, null, null, 0));
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-17")]
    public void ElOrigenCancelaUnoSolicitadoOAprobadoDeReposicionConMotivo()
    {
        var solicitado = Reposicion();
        var aprobado = EnEstado(EstadoDeTraslado.Aprobado);

        solicitado.Cancelar("error de pedido", Ahora.AddHours(1));
        aprobado.Cancelar("error de pedido", Ahora.AddHours(1));

        Assert.All(new[] { solicitado, aprobado }, t => Assert.Equal((EstadoDeTraslado.Cancelado, "error de pedido", (DateTimeOffset?)Ahora.AddHours(1)), (t.Estado, t.MotivoDeCierre, t.CerradoEn)));
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-15")]
    public void LaAnulacionDelPedidoCancelaUnoSolicitadoOAprobado_ConElMotivoPedidoAnulado()
    {
        var solicitado = Vinculado();
        var aprobado = Vinculado();
        aprobado.Aprobar(Ahora);

        var a = solicitado.AnularPorPedido(Ahora.AddHours(1));
        var b = aprobado.AnularPorPedido(Ahora.AddHours(1));

        Assert.Equal((EfectoDeLaAnulacionDelPedido.Cancelado, EfectoDeLaAnulacionDelPedido.Cancelado), (a, b));
        Assert.All(new[] { solicitado, aprobado }, t => Assert.Equal((EstadoDeTraslado.Cancelado, Traslado.MotivoPedidoAnulado), (t.Estado, t.MotivoDeCierre)));
        Assert.Throws<TransicionDeTrasladoInvalidaException>(() => aprobado.Despachar(Ahora));
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-18")]
    public void LaAnulacionDelPedidoMarcaUnoEnTransito_SinCancelarlo_YRepetirlaNoCambiaNada()
    {
        var traslado = Vinculado();
        traslado.Aprobar(Ahora);
        traslado.Despachar(Ahora);

        var primera = traslado.AnularPorPedido(Ahora);
        var version = traslado.Version;
        var repetida = traslado.AnularPorPedido(Ahora);

        Assert.Equal((EfectoDeLaAnulacionDelPedido.MarcadoDePedidoAnulado, EfectoDeLaAnulacionDelPedido.SinCambios), (primera, repetida));
        Assert.Equal((EstadoDeTraslado.EnTransito, true, version), (traslado.Estado, traslado.PedidoAnulado, traslado.Version));
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-18")]
    public void LaAnulacionDelPedidoNoCambiaUnoYaRecibidoOCancelado_NiFalla()
    {
        var recibido = Vinculado();
        recibido.Aprobar(Ahora);
        recibido.Despachar(Ahora);
        recibido.Recibir(Ahora);
        var cancelado = Vinculado();
        cancelado.AnularPorPedido(Ahora);

        Assert.Equal(EfectoDeLaAnulacionDelPedido.SinCambios, recibido.AnularPorPedido(Ahora));
        Assert.Equal(EfectoDeLaAnulacionDelPedido.SinCambios, cancelado.AnularPorPedido(Ahora));
        Assert.Equal(EstadoDeTraslado.Recibido, recibido.Estado);
        Assert.Throws<InvalidOperationException>(() => Reposicion().AnularPorPedido(Ahora));
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-10")]
    public void LaGuiaSeRegistraEnUnoAprobadoOEnTransito_SinCambiarElEstado_YIndicaSiReemplazaOtra()
    {
        var traslado = EnEstado(EstadoDeTraslado.EnTransito);

        var primera = traslado.RegistrarGuia("CA-771204");
        var igual = traslado.RegistrarGuia("CA-771204");
        var otra = traslado.RegistrarGuia("CA-999");

        Assert.Equal((false, false, true, EstadoDeTraslado.EnTransito, "CA-999"), (primera, igual, otra, traslado.Estado, traslado.Guia));
        Assert.Throws<TransicionDeTrasladoInvalidaException>(() => Reposicion().RegistrarGuia("CA-1"));
        Assert.Throws<TransicionDeTrasladoInvalidaException>(() => EnEstado(EstadoDeTraslado.Recibido).RegistrarGuia("CA-1"));
        Assert.Throws<ArgumentException>(() => traslado.RegistrarGuia(new string('G', 41)));
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-11")]
    public void ElToStringNoImprimeLosTextosLibres_YElDominioRechazaDatosImposibles()
    {
        var traslado = Traslado.SolicitarReposicion(Guid.NewGuid(), Edicion, Cusco, Ayacucho, 1, "MOTIVO-CENTINELA", Guid.NewGuid(), Ahora);
        traslado.Aprobar(Ahora);
        traslado.Despachar(Ahora);
        traslado.RegistrarGuia("GUIA-CENTINELA");

        Assert.DoesNotContain("CENTINELA", traslado.ToString(), StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => Traslado.SolicitarReposicion(Guid.NewGuid(), Edicion, Cusco, Cusco, 1, null, Guid.NewGuid(), Ahora));
        Assert.Throws<ArgumentOutOfRangeException>(() => Traslado.SolicitarReposicion(Guid.NewGuid(), Edicion, Cusco, Ayacucho, 0, null, Guid.NewGuid(), Ahora));
        Assert.Throws<ArgumentException>(() => Traslado.SolicitarReposicion(Guid.NewGuid(), Edicion, Cusco, Ayacucho, 1, new string('m', 201), Guid.NewGuid(), Ahora));
        Assert.Throws<ArgumentException>(() => Traslado.SolicitarReposicion(Guid.NewGuid(), Edicion, Cusco, Ayacucho, 1, null, Guid.Empty, Ahora));
    }

    // ---------- T02: permisos ----------

    private static DecisionDePermiso Permiso(OperacionDeTraslado operacion, RolDeTraslado rol, Guid? sucursal) => PermisosDeTraslado.Evaluar(operacion, rol, sucursal, Cusco, Ayacucho);

    [Theory]
    [Trait("Criterio", "AC-TRA-03")]
    [InlineData(OperacionDeTraslado.Aprobar)]
    [InlineData(OperacionDeTraslado.Rechazar)]
    [InlineData(OperacionDeTraslado.Cancelar)]
    [InlineData(OperacionDeTraslado.Despachar)]
    public void AprobarRechazarCancelarYDespacharSonDelOrigen_ElDestinoYOtrasSucursalesSonUnaViolacionDeAlcance(OperacionDeTraslado operacion)
    {
        Assert.True(Permiso(operacion, RolDeTraslado.EncargadoDeSucursal, Cusco).Permitido);
        Assert.Equal(MotivoDeDenegacionDeTraslado.FueraDeSucursal, Permiso(operacion, RolDeTraslado.EncargadoDeSucursal, Ayacucho).Motivo);
        Assert.Equal(MotivoDeDenegacionDeTraslado.FueraDeSucursal, Permiso(operacion, RolDeTraslado.EncargadoDeSucursal, Lima).Motivo);
        Assert.Equal(MotivoDeDenegacionDeTraslado.FueraDeSucursal, Permiso(operacion, RolDeTraslado.EncargadoDeSucursal, null).Motivo);
        Assert.Equal(MotivoDeDenegacionDeTraslado.FueraDeSucursal, Permiso(operacion, RolDeTraslado.EncargadoDeSucursal, Guid.Empty).Motivo);
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-03")]
    public void RecibirEsDelDestino_SolicitarSoloHaciaLaPropiaSucursal_ConsultarDelOrigenOElDestino()
    {
        Assert.True(Permiso(OperacionDeTraslado.Recibir, RolDeTraslado.EncargadoDeSucursal, Ayacucho).Permitido);
        Assert.False(Permiso(OperacionDeTraslado.Recibir, RolDeTraslado.EncargadoDeSucursal, Cusco).Permitido);
        Assert.True(Permiso(OperacionDeTraslado.Solicitar, RolDeTraslado.EncargadoDeSucursal, Ayacucho).Permitido);
        Assert.Equal(MotivoDeDenegacionDeTraslado.FueraDeSucursal, Permiso(OperacionDeTraslado.Solicitar, RolDeTraslado.EncargadoDeSucursal, Cusco).Motivo);
        Assert.True(Permiso(OperacionDeTraslado.Consultar, RolDeTraslado.EncargadoDeSucursal, Cusco).Permitido);
        Assert.True(Permiso(OperacionDeTraslado.Consultar, RolDeTraslado.EncargadoDeSucursal, Ayacucho).Permitido);
        Assert.False(Permiso(OperacionDeTraslado.Consultar, RolDeTraslado.EncargadoDeSucursal, Lima).Permitido);
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-10")]
    public void LaGuiaEsDeLogisticaYAdministracion_ElEncargadoNoLaRegistra()
    {
        Assert.True(Permiso(OperacionDeTraslado.RegistrarGuia, RolDeTraslado.Logistica, null).Permitido);
        Assert.True(Permiso(OperacionDeTraslado.RegistrarGuia, RolDeTraslado.AdministracionCentral, null).Permitido);
        Assert.Equal(MotivoDeDenegacionDeTraslado.FueraDeRol, Permiso(OperacionDeTraslado.RegistrarGuia, RolDeTraslado.EncargadoDeSucursal, Cusco).Motivo);
    }

    [Theory]
    [Trait("Criterio", "AC-TRA-03")]
    [InlineData(OperacionDeTraslado.Solicitar)]
    [InlineData(OperacionDeTraslado.Aprobar)]
    [InlineData(OperacionDeTraslado.Rechazar)]
    [InlineData(OperacionDeTraslado.Cancelar)]
    [InlineData(OperacionDeTraslado.Despachar)]
    [InlineData(OperacionDeTraslado.Recibir)]
    [InlineData(OperacionDeTraslado.RegistrarGuia)]
    [InlineData(OperacionDeTraslado.Consultar)]
    public void LaAdministracionPuedeTodo_LogisticaSoloGuiaYConsulta_ClienteYOtrosNada(OperacionDeTraslado operacion)
    {
        var logistica = operacion is OperacionDeTraslado.RegistrarGuia or OperacionDeTraslado.Consultar;

        Assert.True(Permiso(operacion, RolDeTraslado.AdministracionCentral, null).Permitido);
        Assert.Equal(logistica, Permiso(operacion, RolDeTraslado.Logistica, null).Permitido);
        Assert.Equal(MotivoDeDenegacionDeTraslado.FueraDeRol, Permiso(operacion, RolDeTraslado.Cliente, Cusco).Motivo);
        Assert.Equal(MotivoDeDenegacionDeTraslado.FueraDeRol, Permiso(operacion, RolDeTraslado.Otro, Cusco).Motivo);
        if (!logistica)
        {
            Assert.Equal(MotivoDeDenegacionDeTraslado.FueraDeRol, Permiso(operacion, RolDeTraslado.Logistica, null).Motivo);
        }
    }

    [Theory]
    [Trait("Criterio", "AC-TRA-03")]
    [InlineData(RolDeTraslado.AdministracionCentral, OperacionDeTraslado.Solicitar, true)]
    [InlineData(RolDeTraslado.AdministracionCentral, OperacionDeTraslado.RegistrarGuia, true)]
    [InlineData(RolDeTraslado.EncargadoDeSucursal, OperacionDeTraslado.Despachar, true)]
    [InlineData(RolDeTraslado.EncargadoDeSucursal, OperacionDeTraslado.RegistrarGuia, false)]
    [InlineData(RolDeTraslado.Logistica, OperacionDeTraslado.RegistrarGuia, true)]
    [InlineData(RolDeTraslado.Logistica, OperacionDeTraslado.Consultar, true)]
    [InlineData(RolDeTraslado.Logistica, OperacionDeTraslado.Aprobar, false)]
    [InlineData(RolDeTraslado.Logistica, OperacionDeTraslado.Solicitar, false)]
    [InlineData(RolDeTraslado.Cliente, OperacionDeTraslado.Consultar, false)]
    [InlineData(RolDeTraslado.Otro, OperacionDeTraslado.Consultar, false)]
    public void ElRolSinNingunPermisoSeRechazaAntesDeLeerElTraslado(RolDeTraslado rol, OperacionDeTraslado operacion, bool puede) =>
        Assert.Equal(puede, PermisosDeTraslado.RolPuedeIntentar(operacion, rol));

    // ---------- T03: reglas de solicitud y estimación ----------

    [Fact]
    [Trait("Criterio", "AC-TRA-19")]
    public void LasCuatroReglasDeLaSolicitud_Y_UnaValidaPasa()
    {
        Assert.Equal(MotivoDeRechazoDeSolicitud.MismoOrigenYDestino, ReglasDeSolicitud.Evaluar(Cusco, Cusco, 1, true, 10));
        Assert.Equal(MotivoDeRechazoDeSolicitud.CantidadNoPositiva, ReglasDeSolicitud.Evaluar(Cusco, Ayacucho, 0, true, 10));
        Assert.Equal(MotivoDeRechazoDeSolicitud.CantidadNoPositiva, ReglasDeSolicitud.Evaluar(Cusco, Ayacucho, int.MinValue, true, 10));
        Assert.Equal(MotivoDeRechazoDeSolicitud.EdicionNoImpresa, ReglasDeSolicitud.Evaluar(Cusco, Ayacucho, 1, false, 10));
        Assert.Equal(MotivoDeRechazoDeSolicitud.CantidadMayorQueElVendible, ReglasDeSolicitud.Evaluar(Cusco, Ayacucho, 11, true, 10));
        Assert.Equal(MotivoDeRechazoDeSolicitud.CantidadMayorQueElVendible, ReglasDeSolicitud.Evaluar(Cusco, Ayacucho, int.MaxValue, true, 10));
        Assert.Null(ReglasDeSolicitud.Evaluar(Cusco, Ayacucho, 10, true, 10));
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-20")]
    public void LaEstimacionUsaElPlazoDeLaRutaDeEseMomento_ElTrasladoNoLaCambiaSiLaRutaCambiaDespues_YSinRutaEsPorConfirmar()
    {
        var hoy = new DateOnly(2026, 11, 17);

        var conRuta = EstimacionDeLlegada.Calcular(hoy, 2);
        var traslado = Traslado.VincularAUnPedido(Guid.NewGuid(), Edicion, Cusco, Ayacucho, 1, Guid.NewGuid(), conRuta, null, Ahora);
        var rutaCambioA5 = EstimacionDeLlegada.Calcular(hoy, 5);
        var sinRuta = EstimacionDeLlegada.Calcular(hoy, null);

        Assert.Equal((new DateOnly(2026, 11, 19), false), (conRuta.Fecha, conRuta.PorConfirmar));
        Assert.Equal((conRuta.Fecha, false), (traslado.FechaEstimada, traslado.EstimadaPorConfirmar));
        Assert.NotEqual(rutaCambioA5.Fecha, traslado.FechaEstimada);
        Assert.Equal(((DateOnly?)null, true), (sinRuta.Fecha, sinRuta.PorConfirmar));
        Assert.True(EstimacionDeLlegada.Calcular(hoy, 0).PorConfirmar);
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-11")]
    public void ElNumeroSeAsignaUnaSolaVez()
    {
        var traslado = Reposicion();

        traslado.AsignarNumero(42);

        Assert.Equal(42, traslado.Numero);
        Assert.Throws<InvalidOperationException>(() => traslado.AsignarNumero(43));
        Assert.Throws<InvalidOperationException>(() => Reposicion().AsignarNumero(0));
    }
}
