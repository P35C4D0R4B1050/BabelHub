using System.Net;
using System.Text.RegularExpressions;
using BabelHub.Tools.RestauracionCheck;
using Npgsql;
using Testcontainers.PostgreSql;

namespace BabelHub.IntegrationTests.Restauracion;

/// <summary>
/// <c>tools/RestauracionCheck</c> (T30; PLA-36, PLA-40; AC-PLA-11, AC-PLA-40): la salud de los dos servicios, las dos bases restauradas al mismo
/// instante y la coherencia de pedidos y reservas, en solo lectura y con el rol <c>restauracion-check</c>, con un informe de contadores. Con
/// PostgreSQL real: un clúster con las dos bases (el instante único de una copia física) y otro aparte para el caso de bases de clústeres
/// distintos. Las contraseñas se generan en cada ejecución.
/// </summary>
[Collection(nameof(RestauracionCheckColeccion))]
public sealed class RestauracionCheckTests(RestauracionCheckFixture entorno)
{
    private static readonly DateTime Instante = new(2026, 11, 14, 2, 30, 0, DateTimeKind.Utc);
    private static readonly string Uuid = @"[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}";

    private static string Sql(Guid id) => id.ToString();

    private async Task Preparar(Action<Escenario> armar)
    {
        await entorno.Limpiar();
        var escenario = new Escenario();
        armar(escenario);
        await entorno.Ejecutar("ecommerce", escenario.Ecommerce);
        await entorno.Ejecutar("inventario", escenario.Inventario);
    }

    private async Task<(int Codigo, string Salida, string Errores)> Comprobar(object? sinUso = null, HttpStatusCode ecommerce = HttpStatusCode.OK, HttpStatusCode inventario = HttpStatusCode.OK, params string[] extra)
    {
        var salida = new StringWriter();
        var errores = new StringWriter();
        string[] args =
        [
            "--bases", entorno.CadenaDe("ecommerce"), entorno.CadenaDe("inventario"),
            "--instante", Instante.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            "--salud-ecommerce", "http://ecommerce.prueba/health/ready", "--salud-inventario", "http://inventario.prueba/health/ready",
            .. extra,
        ];
        using var manejador = new SaludDePrueba(ecommerce, inventario);
        var codigo = await Ejecutor.EjecutarAsync(args, salida, errores, CancellationToken.None, manejador, _ => null);
        return (codigo, salida.ToString(), errores.ToString());
    }

    // ---------- AC-PLA-40 ----------

    [Fact]
    [Trait("Criterio", "AC-PLA-40")]
    [Trait("Criterio", "AC-PLA-11")]
    public async Task ElCasoDelCriterio_PedidoSoloDigital_PedidoAnuladoConReservasLiberadasYReservaReciente_NoTieneFallos()
    {
        await Preparar(e =>
        {
            var digital = e.Edicion("digital");
            var impresa = e.Edicion("impreso");
            e.Pedido("confirmado", (digital, 1));
            var anulado = e.Pedido("anulado", (impresa, 1));
            e.Reserva(anulado.Id, impresa, "liberada", Instante.AddDays(-1));
            // Confirmada hace 2 minutos y su pedido aún no existe: todavía no se exige.
            e.Reserva(Guid.NewGuid(), impresa, "confirmada", Instante.AddMinutes(-2));
        });

        var (codigo, salida, errores) = await Comprobar();

        Assert.True(codigo == 0, salida + errores);
        Assert.Contains("ok pedidos-con-reservas", salida, StringComparison.Ordinal);
        Assert.Contains("ok reservas-con-pedido", salida, StringComparison.Ordinal);
        Assert.Contains("contador lineas_impresas_de_pedidos_no_anulados=0", salida, StringComparison.Ordinal);
        Assert.Contains("contador reservas_confirmadas_hace_mas_de_10_min=0", salida, StringComparison.Ordinal);
        Assert.Contains("Restauración comprobada: todo cumple.", salida, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-40")]
    public async Task UnPedidoConfirmadoConLineasImpresasSinReserva_Falla()
    {
        await Preparar(e =>
        {
            var impresa = e.Edicion("impreso");
            e.Pedido("confirmado", (impresa, 1));
        });

        var (codigo, salida, _) = await Comprobar();

        Assert.Equal(1, codigo);
        Assert.Contains("FALLA pedidos-con-reservas: 1 línea(s) impresa(s) de pedidos no anulados no tienen reserva en el Inventario", salida, StringComparison.Ordinal);
        Assert.Contains("contador lineas_impresas_sin_reserva=1", salida, StringComparison.Ordinal);
        Assert.Contains("Restauración NO comprobada", salida, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("pendiente")]
    [InlineData("confirmada")]
    [InlineData("cerrada")]
    [InlineData("liberada")]
    [InlineData("expirada")]
    [Trait("Criterio", "AC-PLA-40")]
    public async Task LaReservaDeLaLineaImpresaCuentaEnCualquierEstado(string estado)
    {
        await Preparar(e =>
        {
            var impresa = e.Edicion("impreso");
            var pedido = e.Pedido("confirmado", (impresa, 2));
            e.Reserva(pedido.Id, impresa, estado, Instante.AddHours(-3));
        });

        var (codigo, salida, errores) = await Comprobar();

        Assert.True(codigo == 0, salida + errores);
        Assert.Contains("contador lineas_impresas_de_pedidos_no_anulados=1", salida, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-40")]
    public async Task UnaReservaDeOtraEdicionDelMismoPedido_NoCubreLaLineaImpresa()
    {
        await Preparar(e =>
        {
            var uno = e.Edicion("impreso");
            var otro = e.Edicion("impreso");
            var pedido = e.Pedido("confirmado", (uno, 1), (otro, 1));
            e.Reserva(pedido.Id, uno, "confirmada", Instante.AddHours(-1));
        });

        var (codigo, salida, _) = await Comprobar();

        Assert.Equal(1, codigo);
        Assert.Contains("contador lineas_impresas_sin_reserva=1", salida, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-40")]
    public async Task UnPedidoAnuladoConLineasImpresasSinReserva_NoSeExige()
    {
        await Preparar(e =>
        {
            var impresa = e.Edicion("impreso");
            e.Pedido("anulado", (impresa, 1));
        });

        var (codigo, salida, errores) = await Comprobar();

        Assert.True(codigo == 0, salida + errores);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-40")]
    public async Task UnaReservaConfirmadaDeHace30MinutosSinPedido_Falla()
    {
        await Preparar(e =>
        {
            var impresa = e.Edicion("impreso");
            e.Reserva(Guid.NewGuid(), impresa, "confirmada", Instante.AddMinutes(-30));
        });

        var (codigo, salida, _) = await Comprobar();

        Assert.Equal(1, codigo);
        Assert.Contains("FALLA reservas-con-pedido: 1 reserva(s) confirmada(s) hace más de 10 minutos no tienen pedido", salida, StringComparison.Ordinal);
        Assert.Contains("contador reservas_confirmadas_sin_pedido=1", salida, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(-9, true)]
    [InlineData(-10, false)]
    [InlineData(-11, false)]
    [Trait("Criterio", "AC-PLA-40")]
    public async Task ElMargenDeDiezMinutosSeMideContraLaFechaDeConfirmacion_ElLimiteYaSeExige(int minutos, bool pasa)
    {
        await Preparar(e =>
        {
            var impresa = e.Edicion("impreso");
            e.Reserva(Guid.NewGuid(), impresa, "confirmada", Instante.AddMinutes(minutos));
        });

        var (codigo, salida, _) = await Comprobar();

        Assert.Equal(pasa ? 0 : 1, codigo);
        Assert.Contains(pasa ? "ok reservas-con-pedido" : "FALLA reservas-con-pedido", salida, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-40")]
    public async Task UnaReservaPendienteOLiberadaViejaSinPedido_NoSeExige_SoloLasConfirmadas()
    {
        await Preparar(e =>
        {
            var impresa = e.Edicion("impreso");
            e.Reserva(Guid.NewGuid(), impresa, "pendiente", Instante.AddDays(-2));
            e.Reserva(Guid.NewGuid(), impresa, "liberada", Instante.AddDays(-2));
            e.Reserva(Guid.NewGuid(), impresa, "expirada", Instante.AddDays(-2));
        });

        var (codigo, salida, errores) = await Comprobar();

        Assert.True(codigo == 0, salida + errores);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-40")]
    public async Task ElInformeSoloTieneContadores_NiIdentificadoresNiCadenasDeConexionNiContrasenas()
    {
        await Preparar(e =>
        {
            var impresa = e.Edicion("impreso");
            e.Pedido("confirmado", (impresa, 1));
            e.Reserva(Guid.NewGuid(), impresa, "confirmada", Instante.AddMinutes(-30));
        });

        var (_, salida, errores) = await Comprobar();

        Assert.DoesNotMatch(Uuid, salida + errores);
        Assert.DoesNotContain(entorno.Contrasena, salida + errores, StringComparison.Ordinal);
        Assert.DoesNotContain("Host=", salida + errores, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Password", salida + errores, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-40")]
    public async Task ConOtrosNombresDeTablas_SePuedeComprobarSinCambiarElCodigo()
    {
        await entorno.Limpiar();
        await entorno.Ejecutar("ecommerce", "CREATE TABLE IF NOT EXISTS pedidos.pedidos_alt (LIKE pedidos.pedidos); GRANT SELECT ON pedidos.pedidos_alt TO \"restauracion-check\";");

        var (codigo, salida, errores) = await Comprobar(null, HttpStatusCode.OK, HttpStatusCode.OK, "--tabla-pedidos", "pedidos.pedidos_alt");

        Assert.True(codigo == 0, salida + errores);
        Assert.DoesNotContain("opción desconocida", errores, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-40")]
    public async Task SiUnaTablaNoExiste_FallaConElCodigoDeError_SinDatos()
    {
        var (codigo, salida, _) = await Comprobar(null, HttpStatusCode.OK, HttpStatusCode.OK, "--tabla-reservas", "seleccion.no_existe");

        Assert.Equal(1, codigo);
        Assert.Contains("FALLA pedidos-y-reservas: no se pudo comprobar (PostgresException 42P01)", salida, StringComparison.Ordinal);
    }

    // ---------- PLA-36: salud, mismo instante y rol ----------

    [Fact]
    [Trait("Criterio", "AC-PLA-11")]
    public async Task ConLosDosServiciosSanosYLasDosBasesDelMismoClaster_PasaLaComprobacionDeRestauracion()
    {
        await Preparar(_ => { });

        var (codigo, salida, errores) = await Comprobar();

        Assert.True(codigo == 0, salida + errores);
        Assert.Contains("ok salud-ecommerce: responde 200", salida, StringComparison.Ordinal);
        Assert.Contains("ok salud-inventario: responde 200", salida, StringComparison.Ordinal);
        Assert.Contains("ok mismo-instante: ", salida, StringComparison.Ordinal);
        Assert.Contains("ok rol-ecommerce: solo lectura", salida, StringComparison.Ordinal);
        Assert.Contains("ok rol-inventario: solo lectura", salida, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK, "FALLA salud-ecommerce: responde 503")]
    [InlineData(HttpStatusCode.OK, HttpStatusCode.InternalServerError, "FALLA salud-inventario: responde 500")]
    [InlineData(HttpStatusCode.NotFound, HttpStatusCode.NotFound, "FALLA salud-ecommerce")]
    [Trait("Criterio", "AC-PLA-11")]
    public async Task SiAlgunServicioNoResponde200_FallaLaRestauracion(HttpStatusCode ecommerce, HttpStatusCode inventario, string esperado)
    {
        await Preparar(_ => { });

        var (codigo, salida, _) = await Comprobar(null, ecommerce, inventario);

        Assert.Equal(1, codigo);
        Assert.Contains(esperado, salida, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-11")]
    public async Task SiUnServicioNoSeAlcanza_FallaSinImprimirLaExcepcionNiLaDireccion()
    {
        await Preparar(_ => { });
        var salida = new StringWriter();
        string[] args =
        [
            "--bases", entorno.CadenaDe("ecommerce"), entorno.CadenaDe("inventario"), "--instante", Instante.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            "--salud-ecommerce", "http://127.0.0.1:1/health/ready", "--salud-inventario", "http://127.0.0.1:1/health/ready",
        ];

        var codigo = await Ejecutor.EjecutarAsync(args, salida, new StringWriter(), CancellationToken.None);

        Assert.Equal(1, codigo);
        Assert.Contains("FALLA salud-ecommerce: no responde (HttpRequestException)", salida.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("127.0.0.1", salida.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-11")]
    public async Task SiLasDosBasesSonDeClustersDistintos_NoEstanAlMismoInstante_Falla()
    {
        await Preparar(_ => { });
        var salida = new StringWriter();
        string[] args =
        [
            "--bases", entorno.CadenaDe("ecommerce"), entorno.CadenaDeOtroCluster(), "--instante", Instante.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            "--salud-ecommerce", "http://ecommerce.prueba/health/ready", "--salud-inventario", "http://inventario.prueba/health/ready",
        ];
        using var manejador = new SaludDePrueba(HttpStatusCode.OK, HttpStatusCode.OK);

        var codigo = await Ejecutor.EjecutarAsync(args, salida, new StringWriter(), CancellationToken.None, manejador);

        Assert.Equal(1, codigo);
        Assert.Contains("FALLA mismo-instante: no son del mismo clúster restaurado", salida.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-11")]
    public async Task ConUnRolConPermisosDeMas_SuperusuarioOConEscritura_FallaYNoEscribeNada()
    {
        await Preparar(e => e.Pedido("confirmado"));
        var administrador = entorno.CadenaComo("postgres", entorno.Contrasena, "ecommerce");
        await entorno.Ejecutar("ecommerce", "GRANT INSERT ON pedidos.pedidos TO \"escritor-de-prueba\"", ignorarFallo: false, previo: "DO $$ BEGIN IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'escritor-de-prueba') THEN CREATE ROLE \"escritor-de-prueba\" LOGIN PASSWORD '" + entorno.Contrasena + "'; END IF; END $$; GRANT CONNECT ON DATABASE ecommerce TO \"escritor-de-prueba\"; GRANT USAGE ON SCHEMA pedidos TO \"escritor-de-prueba\"; GRANT SELECT ON ALL TABLES IN SCHEMA pedidos TO \"escritor-de-prueba\";");
        var antes = await entorno.Contar("ecommerce", "pedidos.pedidos");

        var comoSuperusuario = await EjecutarCon(administrador, entorno.CadenaComo("postgres", entorno.Contrasena, "inventario"));
        var comoEscritor = await EjecutarCon(entorno.CadenaComo("escritor-de-prueba", entorno.Contrasena, "ecommerce"), entorno.CadenaDe("inventario"));

        Assert.Equal(1, comoSuperusuario.Codigo);
        Assert.Contains("el rol conectado no es «restauracion-check»", comoSuperusuario.Salida, StringComparison.Ordinal);
        Assert.Contains("el rol tiene atributos de administración", comoSuperusuario.Salida, StringComparison.Ordinal);
        Assert.Equal(1, comoEscritor.Codigo);
        Assert.Contains("el rol puede escribir en 1 tabla(s)", comoEscritor.Salida, StringComparison.Ordinal);
        Assert.Equal(antes, await entorno.Contar("ecommerce", "pedidos.pedidos"));
    }

    private async Task<(int Codigo, string Salida)> EjecutarCon(string ecommerce, string inventario)
    {
        var salida = new StringWriter();
        string[] args =
        [
            "--bases", ecommerce, inventario, "--instante", Instante.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            "--salud-ecommerce", "http://ecommerce.prueba/health/ready", "--salud-inventario", "http://inventario.prueba/health/ready",
        ];
        using var manejador = new SaludDePrueba(HttpStatusCode.OK, HttpStatusCode.OK);
        var codigo = await Ejecutor.EjecutarAsync(args, salida, new StringWriter(), CancellationToken.None, manejador);
        return (codigo, salida.ToString());
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-11")]
    public async Task SiLaContrasenaEsIncorrecta_FallaSinImprimirNingunaCadenaDeConexion()
    {
        var mala = entorno.CadenaComo("restauracion-check", "contrasena-incorrecta", "ecommerce");

        var (codigo, salida) = await EjecutarCon(mala, entorno.CadenaDe("inventario"));

        Assert.Equal(1, codigo);
        Assert.Contains("FALLA conexion-ecommerce: no se pudo abrir la base (PostgresException)", salida, StringComparison.Ordinal);
        Assert.DoesNotContain("contrasena-incorrecta", salida, StringComparison.Ordinal);
        Assert.DoesNotContain(entorno.Contrasena, salida, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-11")]
    public async Task LasCadenasDeConexionPuedenLlegarPorVariablesDeEntorno_SinViajarEnLosArgumentos()
    {
        await Preparar(_ => { });
        var salida = new StringWriter();
        string[] args =
        [
            "--instante", Instante.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            "--salud-ecommerce", "http://ecommerce.prueba/health/ready", "--salud-inventario", "http://inventario.prueba/health/ready",
        ];
        using var manejador = new SaludDePrueba(HttpStatusCode.OK, HttpStatusCode.OK);

        var codigo = await Ejecutor.EjecutarAsync(
            args,
            salida,
            new StringWriter(),
            CancellationToken.None,
            manejador,
            nombre => nombre switch { Opciones.VariableEcommerce => entorno.CadenaDe("ecommerce"), Opciones.VariableInventario => entorno.CadenaDe("inventario"), _ => null });

        Assert.True(codigo == 0, salida.ToString());
    }

    // ---------- Uso inválido ----------

    [Theory]
    [InlineData("--instante", "ayer")]
    [InlineData("--salud-ecommerce", "ftp://x/health")]
    [InlineData("--tabla-pedidos", "pedidos.pedidos; DROP TABLE x")]
    [InlineData("--tabla-reservas", "Reservas")]
    [InlineData("--tabla-lineas", "a.b.c")]
    [InlineData("--desconocida", "x")]
    [Trait("Criterio", "AC-PLA-11")]
    public async Task UnUsoInvalido_SeRechazaConCodigo2_SinConectarse(string opcion, string valor)
    {
        var errores = new StringWriter();
        string[] args =
        [
            "--bases", "Host=no-se-usa", "Host=no-se-usa", "--instante", "2026-11-14T02:30:00Z",
            "--salud-ecommerce", "http://a/health", "--salud-inventario", "http://b/health", opcion, valor,
        ];

        var codigo = await Ejecutor.EjecutarAsync(args, new StringWriter(), errores, CancellationToken.None);

        Assert.Equal(2, codigo);
        Assert.Contains("Error: ", errores.ToString(), StringComparison.Ordinal);
        Assert.Contains("Uso:", errores.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("sin-bases")]
    [InlineData("sin-instante")]
    [InlineData("sin-salud")]
    [Trait("Criterio", "AC-PLA-11")]
    public async Task SiFaltaUnaOpcionObligatoria_SeRechazaConCodigo2(string falta)
    {
        var args = new List<string> { "--bases", "Host=x", "Host=y", "--instante", "2026-11-14T02:30:00Z", "--salud-ecommerce", "http://a/h", "--salud-inventario", "http://b/h" };
        switch (falta)
        {
            case "sin-bases":
                args.RemoveRange(0, 3);
                break;
            case "sin-instante":
                args.RemoveRange(3, 2);
                break;
            default:
                args.RemoveRange(5, 4);
                break;
        }

        var errores = new StringWriter();

        var codigo = await Ejecutor.EjecutarAsync([.. args], new StringWriter(), errores, CancellationToken.None, null, _ => null);

        Assert.Equal(2, codigo);
        Assert.NotEmpty(errores.ToString());
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-13")]
    public void LaHerramienta_NoReferenciaProyectosDeLosServicios_NiEsReferenciadaPorEllos()
    {
        var raiz = BabelHub.IntegrationTests.VmAux.EntornoDeVmAux.RaizDelRepositorio();
        var proyecto = File.ReadAllText(Path.Combine(raiz, "tools", "RestauracionCheck", "RestauracionCheck.csproj"));
        var referencias = Regex.Matches(proyecto, "ProjectReference Include=\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToList();

        Assert.Empty(referencias);
        foreach (var servicio in Directory.EnumerateFiles(Path.Combine(raiz, "src"), "*.csproj", SearchOption.AllDirectories))
        {
            Assert.DoesNotContain("RestauracionCheck", File.ReadAllText(servicio), StringComparison.Ordinal);
        }
    }

    // ---------- Soporte ----------

    /// <summary>Un cliente HTTP de prueba: responde con el estado dado según el servicio, sin red.</summary>
    private sealed class SaludDePrueba(HttpStatusCode ecommerce, HttpStatusCode inventario) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(request.RequestUri!.Host.StartsWith("ecommerce", StringComparison.Ordinal) ? ecommerce : inventario));
    }

    /// <summary>Arma los datos de un escenario en SQL: ediciones, pedidos y líneas (E-commerce) y reservas (Inventario).</summary>
    private sealed class Escenario
    {
        private readonly List<string> _ecommerce = [];
        private readonly List<string> _inventario = [];

        public string Ecommerce => string.Join(";\n", _ecommerce);

        public string Inventario => string.Join(";\n", _inventario);

        public Guid Edicion(string formato)
        {
            var id = Guid.NewGuid();
            _ecommerce.Add($"INSERT INTO catalogo.ediciones(id, formato) VALUES ('{Sql(id)}', '{formato}')");
            return id;
        }

        public (Guid Id, string Estado) Pedido(string estado, params (Guid Edicion, int Cantidad)[] lineas)
        {
            var id = Guid.NewGuid();
            _ecommerce.Add($"INSERT INTO pedidos.pedidos(id, estado, fecha) VALUES ('{Sql(id)}', '{estado}', now())");
            foreach (var (edicion, _) in lineas)
            {
                _ecommerce.Add($"INSERT INTO pedidos.pedido_lineas(pedido_id, edicion_id) VALUES ('{Sql(id)}', '{Sql(edicion)}')");
            }

            return (id, estado);
        }

        public void Reserva(Guid pedido, Guid edicion, string estado, DateTime confirmada) =>
            _inventario.Add($"INSERT INTO seleccion.reservas(id, pedido_id, edicion_id, estado, fecha_confirmacion) VALUES ('{Sql(Guid.NewGuid())}', '{Sql(pedido)}', '{Sql(edicion)}', '{estado}', '{confirmada:O}')");
    }
}

[CollectionDefinition(nameof(RestauracionCheckColeccion))]
public sealed class RestauracionCheckColeccion : ICollectionFixture<RestauracionCheckFixture>;

/// <summary>Un clúster de PostgreSQL con las bases <c>ecommerce</c> e <c>inventario</c> y el rol <c>restauracion-check</c>, y otro clúster aparte.</summary>
public sealed class RestauracionCheckFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _cluster;
    private readonly PostgreSqlContainer _otro;

    public RestauracionCheckFixture()
    {
        Contrasena = "p" + Guid.NewGuid().ToString("N");
        _cluster = new PostgreSqlBuilder("postgres:17-alpine").WithDatabase("postgres").WithUsername("postgres").WithPassword(Contrasena).Build();
        _otro = new PostgreSqlBuilder("postgres:17-alpine").WithDatabase("inventario").WithUsername("postgres").WithPassword(Contrasena).Build();
    }

    public string Contrasena { get; }

    public string CadenaComo(string usuario, string contrasena, string baseDeDatos) =>
        new NpgsqlConnectionStringBuilder(_cluster.GetConnectionString()) { Username = usuario, Password = contrasena, Database = baseDeDatos }.ConnectionString;

    public string CadenaDe(string baseDeDatos) => CadenaComo("restauracion-check", Contrasena, baseDeDatos);

    public string CadenaDeOtroCluster() =>
        new NpgsqlConnectionStringBuilder(_otro.GetConnectionString()) { Username = "restauracion-check", Password = Contrasena, Database = "inventario" }.ConnectionString;

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_cluster.StartAsync(), _otro.StartAsync());
        await Administrar(_cluster, "postgres", "CREATE DATABASE ecommerce; CREATE DATABASE inventario;");
        await Administrar(_cluster, "postgres", $"CREATE ROLE \"restauracion-check\" LOGIN PASSWORD '{Contrasena}'");
        await Administrar(_cluster, "ecommerce", "CREATE SCHEMA catalogo; CREATE SCHEMA pedidos; CREATE TABLE catalogo.ediciones(id uuid PRIMARY KEY, formato text NOT NULL); CREATE TABLE pedidos.pedidos(id uuid PRIMARY KEY, estado text NOT NULL, fecha timestamptz NOT NULL); CREATE TABLE pedidos.pedido_lineas(pedido_id uuid NOT NULL, edicion_id uuid NOT NULL); GRANT CONNECT ON DATABASE ecommerce TO \"restauracion-check\"; GRANT USAGE ON SCHEMA catalogo, pedidos TO \"restauracion-check\"; GRANT SELECT ON ALL TABLES IN SCHEMA catalogo, pedidos TO \"restauracion-check\";");
        await Administrar(_cluster, "inventario", "CREATE SCHEMA seleccion; CREATE TABLE seleccion.reservas(id uuid PRIMARY KEY, pedido_id uuid, edicion_id uuid NOT NULL, estado text NOT NULL, fecha_confirmacion timestamptz); GRANT CONNECT ON DATABASE inventario TO \"restauracion-check\"; GRANT USAGE ON SCHEMA seleccion TO \"restauracion-check\"; GRANT SELECT ON ALL TABLES IN SCHEMA seleccion TO \"restauracion-check\";");
        await Administrar(_otro, "inventario", $"CREATE ROLE \"restauracion-check\" LOGIN PASSWORD '{Contrasena}'; CREATE SCHEMA seleccion; GRANT CONNECT ON DATABASE inventario TO \"restauracion-check\"; GRANT USAGE ON SCHEMA seleccion TO \"restauracion-check\";");
    }

    public async Task DisposeAsync()
    {
        await _cluster.DisposeAsync();
        await _otro.DisposeAsync();
    }

    private static async Task Administrar(PostgreSqlContainer contenedor, string baseDeDatos, string sql)
    {
        await using var conexion = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(contenedor.GetConnectionString()) { Database = baseDeDatos }.ConnectionString);
        await conexion.OpenAsync();
        await using var orden = new NpgsqlCommand(sql, conexion);
        await orden.ExecuteNonQueryAsync();
    }

    public async Task Ejecutar(string baseDeDatos, string sql, bool ignorarFallo = false, string? previo = null)
    {
        if (previo is not null)
        {
            await Administrar(_cluster, baseDeDatos, previo);
        }

        if (string.IsNullOrWhiteSpace(sql))
        {
            return;
        }

        try
        {
            await Administrar(_cluster, baseDeDatos, sql);
        }
        catch (PostgresException) when (ignorarFallo)
        {
            // Se pidió ignorar el fallo.
        }
    }

    public async Task Limpiar()
    {
        await Administrar(_cluster, "ecommerce", "TRUNCATE pedidos.pedido_lineas, pedidos.pedidos, catalogo.ediciones");
        await Administrar(_cluster, "inventario", "TRUNCATE seleccion.reservas");
    }

    public async Task<long> Contar(string baseDeDatos, string tabla)
    {
        await using var conexion = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(_cluster.GetConnectionString()) { Database = baseDeDatos }.ConnectionString);
        await conexion.OpenAsync();
        await using var orden = new NpgsqlCommand($"SELECT count(*) FROM {tabla}", conexion);
        return (long)(await orden.ExecuteScalarAsync())!;
    }
}
