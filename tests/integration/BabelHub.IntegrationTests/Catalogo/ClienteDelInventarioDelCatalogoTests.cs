using BabelHub.Ecommerce.Application.Catalogo;
using BabelHub.Ecommerce.Application.Catalogo.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Infrastructure.Catalogo;

namespace BabelHub.IntegrationTests.Catalogo;

/// <summary>El adaptador del Inventario del Catálogo sobre el cliente de Plataforma (T16; AC-CAT-09, AC-CAT-16, AC-CAT-18, SEG-13): traducción, «sin respuesta» ante 429 o falla y validación de lo que devuelve el Inventario.</summary>
public sealed class ClienteDelInventarioDelCatalogoTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 14, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid Edicion = Guid.Parse("11111111-0000-0000-0000-000000000001");

    private sealed class ClienteFalso : IClienteDelInventario
    {
        public List<EdicionARegistrar> Registros { get; } = [];

        public List<(IngresoARegistrar Ingreso, string Clave)> Ingresos { get; } = [];

        public ResultadoDelInventario Registro { get; set; } = ResultadoDelInventario.Correcta;

        public ResultadoDelInventario<DisponibilidadDeVariasEdiciones> Disponibilidad { get; set; } = ResultadoDelInventario<DisponibilidadDeVariasEdiciones>.SinRespuesta;

        public ResultadoDelInventario<IngresoRegistrado> Ingreso { get; set; } = ResultadoDelInventario<IngresoRegistrado>.SinRespuesta;

        public ResultadoDelInventario<IReadOnlyList<IngresoRegistrado>> Lista { get; set; } = ResultadoDelInventario<IReadOnlyList<IngresoRegistrado>>.SinRespuesta;

        public Task<ResultadoDelInventario> RegistrarEdicion(EdicionARegistrar edicion, CancellationToken cancelacion = default)
        {
            Registros.Add(edicion);
            return Task.FromResult(Registro);
        }

        public Task<ResultadoDelInventario<DisponibilidadDeVariasEdiciones>> ConsultarDisponibilidad(IReadOnlyList<Guid> edicionIds, CancellationToken cancelacion = default) => Task.FromResult(Disponibilidad);

        public Task<ResultadoDelInventario<IngresoRegistrado>> RegistrarIngreso(IngresoARegistrar ingreso, string claveDeIdempotencia, CancellationToken cancelacion = default)
        {
            Ingresos.Add((ingreso, claveDeIdempotencia));
            return Task.FromResult(Ingreso);
        }

        public Task<ResultadoDelInventario<IReadOnlyList<IngresoRegistrado>>> ListarIngresos(Guid edicionId, int limite, CancellationToken cancelacion = default) => Task.FromResult(Lista);

        public Task<ResultadoDelInventario<IReadOnlyList<SucursalPublica>>> ListarSucursales(CancellationToken cancelacion = default) => throw new NotSupportedException();
    }

    private static (ClienteDelInventarioDelCatalogo Adaptador, ClienteFalso Cliente) Crear()
    {
        var cliente = new ClienteFalso();
        return (new ClienteDelInventarioDelCatalogo(cliente), cliente);
    }

    private static DisponibilidadDeLaEdicion Disp(Guid id, int vendible = 5, params FuenteConVendible[] fuentes) => new(id, vendible, fuentes, Ahora);

    [Fact]
    [Trait("Criterio", "AC-CAT-16")]
    public async Task Registrar_la_ficha_envia_el_formato_impreso_y_solo_un_exito_cuenta()
    {
        var (adaptador, cliente) = Crear();

        var ok = await adaptador.RegistrarEdicionImpresa(Edicion);
        cliente.Registro = ResultadoDelInventario.Rechazo(422, "INV-FORMATO-EN-CONFLICTO");
        var conflicto = await adaptador.RegistrarEdicionImpresa(Edicion);
        cliente.Registro = ResultadoDelInventario.SinRespuesta;
        var caido = await adaptador.RegistrarEdicionImpresa(Edicion);

        Assert.True(ok);
        Assert.False(conflicto);
        Assert.False(caido);
        Assert.All(cliente.Registros, r => Assert.Equal(new EdicionARegistrar(Edicion, "impreso"), r));
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-09")]
    public async Task La_disponibilidad_se_traduce_con_sus_fuentes_y_las_no_registradas()
    {
        var (adaptador, cliente) = Crear();
        var otra = Guid.NewGuid();
        cliente.Disponibilidad = ResultadoDelInventario<DisponibilidadDeVariasEdiciones>.ConValor(
            new DisponibilidadDeVariasEdiciones([Disp(Edicion, 7, new FuenteConVendible("LIMA", "Lima", 7, "disponible"))], [otra]));

        var respuesta = await adaptador.ConsultarDisponibilidad([Edicion, otra]);

        Assert.NotNull(respuesta);
        var dato = Assert.Single(respuesta.Ediciones);
        Assert.Equal(7, dato.VendibleEnLaRed);
        Assert.Equal(new DisponibilidadPorFuente("LIMA", "Lima", 7), Assert.Single(dato.PorFuente));
        Assert.Equal([otra], respuesta.NoRegistradas);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-09")]
    public async Task Un_429_una_falla_un_rechazo_o_una_respuesta_invalida_de_disponibilidad_son_sin_respuesta()
    {
        var (adaptador, cliente) = Crear();
        var casos = new List<ResultadoDelInventario<DisponibilidadDeVariasEdiciones>>
        {
            ResultadoDelInventario<DisponibilidadDeVariasEdiciones>.Limitada("INV-LIMITE-EXCEDIDO"),
            ResultadoDelInventario<DisponibilidadDeVariasEdiciones>.SinRespuesta,
            ResultadoDelInventario<DisponibilidadDeVariasEdiciones>.Rechazo(422, "INV-EDICION-NO-IMPRESA"),
            ResultadoDelInventario<DisponibilidadDeVariasEdiciones>.ConValor(new DisponibilidadDeVariasEdiciones([Disp(Guid.NewGuid())], [])),
            ResultadoDelInventario<DisponibilidadDeVariasEdiciones>.ConValor(new DisponibilidadDeVariasEdiciones([Disp(Edicion, -1)], [])),
            ResultadoDelInventario<DisponibilidadDeVariasEdiciones>.ConValor(new DisponibilidadDeVariasEdiciones([Disp(Edicion), Disp(Edicion)], [])),
            ResultadoDelInventario<DisponibilidadDeVariasEdiciones>.ConValor(new DisponibilidadDeVariasEdiciones([Disp(Edicion)], [Edicion])),
            ResultadoDelInventario<DisponibilidadDeVariasEdiciones>.ConValor(new DisponibilidadDeVariasEdiciones([Disp(Edicion, 3, new FuenteConVendible(new string('C', 41), "X", 3, "ok"))], [])),
            ResultadoDelInventario<DisponibilidadDeVariasEdiciones>.ConValor(new DisponibilidadDeVariasEdiciones([Disp(Edicion, 3, new FuenteConVendible("LIMA", new string('N', 201), 3, "ok"))], [])),
            ResultadoDelInventario<DisponibilidadDeVariasEdiciones>.ConValor(
                new DisponibilidadDeVariasEdiciones([Disp(Edicion, 3, Enumerable.Range(0, 101).Select(i => new FuenteConVendible($"F{i}", "F", 1, "ok")).ToArray())], [])),
        };

        foreach (var caso in casos)
        {
            cliente.Disponibilidad = caso;
            Assert.Null(await adaptador.ConsultarDisponibilidad([Edicion]));
        }
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-18")]
    public async Task El_ingreso_envia_la_clave_de_idempotencia_en_32_hexadecimales_sin_fuente_y_un_201_coincidente_es_creado()
    {
        var (adaptador, cliente) = Crear();
        var clave = Guid.Parse("22222222-0000-0000-0000-000000000002");
        var ingresoId = Guid.NewGuid();
        cliente.Ingreso = ResultadoDelInventario<IngresoRegistrado>.ConValor(new IngresoRegistrado(ingresoId, Edicion, "RECEPCION", 30, "GR-001", null, Guid.NewGuid(), Ahora));

        var respuesta = await adaptador.RegistrarIngreso(new SolicitudDeIngresoAlInventario(Edicion, clave, 30, "GR-001", "nota"));

        Assert.Equal(TipoDeRespuestaDeIngreso.Creado, respuesta.Tipo);
        Assert.Equal(ingresoId, respuesta.IngresoId);
        var (enviado, claveEnviada) = Assert.Single(cliente.Ingresos);
        Assert.Equal("22222222000000000000000000000002", claveEnviada);
        Assert.Matches("^[0-9a-f]{32}$", claveEnviada);
        Assert.Null(enviado.CodigoDeFuente);
        Assert.Equal(new IngresoARegistrar(Edicion, null, 30, "GR-001", "nota"), enviado);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-18")]
    public async Task Un_201_de_otro_ingreso_o_sin_identificador_es_sin_respuesta_y_no_se_da_por_registrado()
    {
        var (adaptador, cliente) = Crear();
        var solicitud = new SolicitudDeIngresoAlInventario(Edicion, Guid.NewGuid(), 30, null, null);

        foreach (var devuelto in new[]
        {
            new IngresoRegistrado(Guid.NewGuid(), Guid.NewGuid(), "RECEPCION", 30, null, null, null, Ahora),
            new IngresoRegistrado(Guid.NewGuid(), Edicion, "RECEPCION", 31, null, null, null, Ahora),
            new IngresoRegistrado(Guid.Empty, Edicion, "RECEPCION", 30, null, null, null, Ahora),
        })
        {
            cliente.Ingreso = ResultadoDelInventario<IngresoRegistrado>.ConValor(devuelto);
            var respuesta = await adaptador.RegistrarIngreso(solicitud);
            Assert.Equal(TipoDeRespuestaDeIngreso.SinRespuesta, respuesta.Tipo);
            Assert.Null(respuesta.IngresoId);
        }
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-18")]
    public async Task Un_rechazo_conserva_el_estado_y_el_codigo_y_un_429_o_una_falla_son_sin_respuesta()
    {
        var (adaptador, cliente) = Crear();
        var solicitud = new SolicitudDeIngresoAlInventario(Edicion, Guid.NewGuid(), 30, null, null);

        cliente.Ingreso = ResultadoDelInventario<IngresoRegistrado>.Rechazo(409, "INV-IDEMPOTENCIA-EN-CONFLICTO");
        var rechazado = await adaptador.RegistrarIngreso(solicitud);
        cliente.Ingreso = ResultadoDelInventario<IngresoRegistrado>.Rechazo(401, null);
        var sinCodigo = await adaptador.RegistrarIngreso(solicitud);
        cliente.Ingreso = ResultadoDelInventario<IngresoRegistrado>.Limitada("INV-LIMITE-EXCEDIDO");
        var limitado = await adaptador.RegistrarIngreso(solicitud);
        cliente.Ingreso = ResultadoDelInventario<IngresoRegistrado>.SinRespuesta;
        var caido = await adaptador.RegistrarIngreso(solicitud);

        Assert.Equal(new RespuestaDeIngreso(TipoDeRespuestaDeIngreso.Rechazado, null, "INV-IDEMPOTENCIA-EN-CONFLICTO", 409), rechazado);
        Assert.Equal(new RespuestaDeIngreso(TipoDeRespuestaDeIngreso.Rechazado, null, null, 401), sinCodigo);
        Assert.Equal(TipoDeRespuestaDeIngreso.SinRespuesta, limitado.Tipo);
        Assert.Equal(TipoDeRespuestaDeIngreso.SinRespuesta, caido.Tipo);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-18")]
    public async Task La_lista_de_ingresos_se_traduce_y_se_descarta_si_trae_ingresos_de_otra_edicion_o_repetidos()
    {
        var (adaptador, cliente) = Crear();
        var usuario = Guid.NewGuid();
        var uno = new IngresoRegistrado(Guid.NewGuid(), Edicion, "RECEPCION", 30, "GR-001", null, usuario, Ahora);
        cliente.Lista = ResultadoDelInventario<IReadOnlyList<IngresoRegistrado>>.ConValor([uno]);

        var lista = await adaptador.ListarIngresos(Edicion, 50);

        Assert.Equal(new IngresoDelInventario(uno.IngresoId, Edicion, "RECEPCION", 30, "GR-001", null, usuario, Ahora), Assert.Single(lista!));

        foreach (var invalida in new IReadOnlyList<IngresoRegistrado>[]
        {
            [uno with { EdicionId = Guid.NewGuid() }],
            [uno, uno],
            [uno with { Cantidad = 0 }],
        })
        {
            cliente.Lista = ResultadoDelInventario<IReadOnlyList<IngresoRegistrado>>.ConValor(invalida);
            Assert.Null(await adaptador.ListarIngresos(Edicion, 50));
        }

        cliente.Lista = ResultadoDelInventario<IReadOnlyList<IngresoRegistrado>>.SinRespuesta;
        Assert.Null(await adaptador.ListarIngresos(Edicion, 50));
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-09")]
    public void La_memoria_invalida_sin_perder_el_respaldo_no_guarda_cantidades_negativas_y_respeta_su_tope()
    {
        var memoria = new MemoriaDeDisponibilidadEnMemoria(2);
        var a = new EntradaDeDisponibilidad(Guid.NewGuid(), 5, [], Ahora);
        var b = new EntradaDeDisponibilidad(Guid.NewGuid(), 6, [], Ahora.AddMinutes(1));
        var c = new EntradaDeDisponibilidad(Guid.NewGuid(), 7, [], Ahora.AddMinutes(2));

        memoria.Guardar(a);
        memoria.Guardar(b);
        memoria.Invalidar(a.EdicionId);
        memoria.Guardar(new EntradaDeDisponibilidad(Guid.NewGuid(), -1, [], Ahora));
        memoria.Guardar(c);

        Assert.Null(memoria.Obtener(a.EdicionId));
        Assert.Equal(6, memoria.Obtener(b.EdicionId)!.VendibleEnLaRed);
        Assert.Equal(7, memoria.Obtener(c.EdicionId)!.VendibleEnLaRed);

        memoria.Invalidar(b.EdicionId);
        var invalidada = memoria.Obtener(b.EdicionId)!;
        Assert.True(invalidada.Invalidada);
        Assert.Equal(6, invalidada.VendibleEnLaRed);
        memoria.Guardar(b with { VendibleEnLaRed = 9 });
        Assert.False(memoria.Obtener(b.EdicionId)!.Invalidada);
        Assert.Throws<ArgumentOutOfRangeException>(() => new MemoriaDeDisponibilidadEnMemoria(0));
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-18")]
    public void El_adaptador_no_abre_un_cliente_http_ni_toca_el_token()
    {
        var campos = typeof(ClienteDelInventarioDelCatalogo).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
        var constructor = typeof(ClienteDelInventarioDelCatalogo).GetConstructors().Single();

        Assert.Equal([typeof(IClienteDelInventario)], constructor.GetParameters().Select(p => p.ParameterType).ToArray());
        Assert.DoesNotContain(campos, c => c.FieldType.Name.Contains("HttpClient", StringComparison.Ordinal) || c.FieldType.Name.Contains("Token", StringComparison.Ordinal));
    }
}
