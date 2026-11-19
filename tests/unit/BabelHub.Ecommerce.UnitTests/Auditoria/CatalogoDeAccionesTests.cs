using System.Reflection;
using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Auditoria.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Auditoria;
using BabelHub.Ecommerce.UnitTests.Plataforma;
using BabelHub.Ecommerce.UnitTests.Soporte;
using TipoDeActor = BabelHub.Ecommerce.Domain.Auditoria.TipoDeActor;

namespace BabelHub.Ecommerce.UnitTests.Auditoria;

/// <summary>
/// El catálogo concreto de acciones auditables (T11 a T16, D15): una prueba de contrato por acción con una operación de prueba que pasa por
/// <c>IAuditoria</c> o <c>IRegistroDeSeguridad</c>, y la comparación de las constantes de la raíz con el catálogo del módulo, para que una acción
/// nueva sin declarar (o declarada sin constante) no llegue a producción.
/// </summary>
public sealed class CatalogoDeAccionesTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 14, 15, 0, 0, TimeSpan.Zero);

    private static readonly CatalogoDeAcciones Catalogo = CatalogoDeAccionesDeAuditoria.Crear();

    public static TheoryData<string> Acciones => new(Catalogo.Acciones.Select(a => a.Identificador));

    private static IEnumerable<AccionAuditable> Constantes() =>
        typeof(AccionesAuditables)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(AccionAuditable))
            .Select(f => (AccionAuditable)f.GetValue(null)!);

    private sealed class Entorno
    {
        public Entorno(ActorDePrueba actor)
        {
            Actor = actor;
            var constructor = new ConstructorDeRegistros(ServicioDeOrigen.Ecommerce, Catalogo, new RelojFijo(Ahora), actor, new OrigenFijo(OrigenDeSolicitud.Desconocido));
            Auditoria = new AuditoriaDeAplicacion(constructor, Almacen);
            Seguridad = new RegistroDeSeguridadDeAplicacion(constructor, Almacen, Alertas, new RelojFijo(Ahora));
        }

        public ActorDePrueba Actor { get; }

        public AlmacenDeRegistrosEnMemoria Almacen { get; } = new();

        public AlertasTecnicasEspia Alertas { get; } = new();

        public IAuditoria Auditoria { get; }

        public IRegistroDeSeguridad Seguridad { get; }

        public Task Registrar(AccionAuditable accion, IReadOnlyDictionary<string, string?>? detalle = null, string? motivo = null, string? objetoTipo = null, string? objetoId = null, Guid? origen = null, Guid? destino = null)
        {
            var solicitud = new SolicitudDeRegistro(accion)
            {
                Detalle = detalle ?? new Dictionary<string, string?>(),
                Motivo = motivo,
                ObjetoTipo = objetoTipo,
                ObjetoId = objetoId,
                SucursalOrigen = origen,
                SucursalDestino = destino,
            };
            return Catalogo.Obtener(accion.Identificador).Clase == ClaseDeAccion.ConcedeOCambia
                ? Auditoria.Registrar(solicitud)
                : Seguridad.Registrar(solicitud with { Resultado = ResultadoAuditado.Rechazado });
        }
    }

    private static string ValorDePrueba(ClaveDeDetalle clave) => clave.Personal ? "Ana Perez" : clave.Valores?.First() ?? "dato";

    // ---------- Contrato de todo el catálogo ----------

    [Fact]
    [Trait("Criterio", "AC-AUD-23")]
    public void Cada_constante_de_la_raiz_tiene_su_accion_en_el_catalogo_y_al_reves()
    {
        var constantes = Constantes().ToList();

        Assert.NotEmpty(constantes);
        Assert.Equal(constantes.Count, constantes.Select(c => c.Identificador).Distinct().Count());
        foreach (var constante in constantes)
        {
            Assert.True(Catalogo.TryObtener(constante.Identificador, out var accion), $"La constante {constante.Identificador} no está en el catálogo.");
            Assert.Equal(accion!.Aislada, constante.Aislada);
        }

        var identificadores = constantes.Select(c => c.Identificador).ToHashSet();
        foreach (var accion in Catalogo.Acciones)
        {
            Assert.True(identificadores.Contains(accion.Identificador), $"La acción {accion.Identificador} no tiene constante en la raíz del contrato.");
        }
    }

    [Fact]
    public void El_nombre_de_cada_constante_es_el_identificador_de_su_accion()
    {
        foreach (var campo in typeof(AccionesAuditables).GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.FieldType == typeof(AccionAuditable)))
        {
            Assert.Equal(campo.Name, ((AccionAuditable)campo.GetValue(null)!).Identificador);
        }
    }

    [Theory]
    [MemberData(nameof(Acciones))]
    [Trait("Criterio", "AC-AUD-01")]
    public async Task Una_operacion_de_prueba_deja_un_registro_con_todos_los_campos_declarados_de_cada_accion(string identificador)
    {
        var accion = Catalogo.Obtener(identificador);
        var entorno = new Entorno(ActorDePrueba.DeAdministradorCentral());
        var detalle = accion.Detalle.ToDictionary(c => c.Nombre, c => (string?)ValorDePrueba(c));
        var motivo = accion.ExigeMotivo ? accion.Motivos[0] : null;
        var constante = Constantes().Single(c => c.Identificador == identificador);

        await entorno.Registrar(constante, detalle, motivo, accion.TiposDeObjeto!.FirstOrDefault(), accion.TiposDeObjeto!.Count > 0 ? "obj-1" : null, Guid.NewGuid(), accion.Tipo == TipoDeRegistro.Auditoria ? Guid.NewGuid() : null);

        var registro = Assert.Single(entorno.Almacen.Registros);
        Assert.Equal(identificador, registro.Accion);
        Assert.Equal(accion.Tipo, registro.Tipo);
        Assert.Equal(accion.Aislada, registro.Aislada);
        foreach (var clave in accion.Detalle)
        {
            Assert.Equal(ValorDePrueba(clave), registro.Detalle.Valores[clave.Nombre]);
            Assert.Equal(clave.Personal, registro.Detalle.EsPersonal(clave.Nombre));
        }

        Assert.Empty(entorno.Alertas.Alertas);
    }

    [Theory]
    [MemberData(nameof(Acciones))]
    [Trait("Criterio", "AC-AUD-05")]
    public void Cada_accion_declara_si_su_objeto_y_su_rol_son_personales_y_sus_claves_son_camelCase(string identificador)
    {
        var accion = Catalogo.Obtener(identificador);

        // Los constructores obligan a decidir objeto y rol (sin valor por defecto); aquí se vigila que el catálogo no los deje en «falso» por omisión
        // en una acción cuyo objeto es una cuenta o una persona.
        Assert.All(accion.Detalle, clave => Assert.Matches(@"^[a-z][A-Za-z0-9]*$", clave.Nombre));
        Assert.True(accion.Detalle.Count <= 12, "El detalle de una acción declara pocas claves: es una lista blanca, no un volcado.");
        Assert.DoesNotContain(accion.Detalle, c => c.Nombre is "contrasena" or "password" or "token" or "codigo" or "secreto" or "clave" or "tarjeta" or "enlace" or "url");
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-05")]
    public void Las_acciones_de_Pedidos_declaran_sus_claves_el_iniciador_como_campo_personal_y_nunca_el_contacto()
    {
        var esperado = new Dictionary<string, string[]>
        {
            [nameof(AccionesAuditables.ContactoDePedidoConsultado)] = ["rol"],
            [nameof(AccionesAuditables.OperacionDePedidoIniciada)] = ["operacion", "iniciadaPor"],
            [nameof(AccionesAuditables.PedidoCanceladoPorElCliente)] = ["estadoAnterior"],
            [nameof(AccionesAuditables.ReembolsoDePedidoReintentado)] = ["resultado", "monto", "moneda"],
            [nameof(AccionesAuditables.LineaDePedidoEntregada)] = ["lineaId", "iniciadaPor"],
            [nameof(AccionesAuditables.ContactoDePedidoAnonimizado)] = ["causa", "cantidad"],
            [nameof(AccionesAuditables.PedidosDepurados)] = ["cantidad"],
            [nameof(AccionesAuditables.EstadoDePedidoCambiado)] = ["estadoAnterior", "estadoNuevo", "iniciadaPor"],
        };

        foreach (var (nombre, claves) in esperado)
        {
            var accion = Catalogo.Obtener(nombre);
            Assert.Equal(claves, accion.Detalle.Select(c => c.Nombre));
            Assert.All(accion.Detalle, c => Assert.Equal(c.Nombre == "iniciadaPor", c.Personal));
            Assert.DoesNotContain(accion.Detalle, c => c.Nombre is "contacto" or "telefono" or "direccion" or "documento");
            Assert.False(accion.Aislada, nombre);
        }

        Assert.Equal(["pedido"], Catalogo.Obtener(nameof(AccionesAuditables.ContactoDePedidoConsultado)).TiposDeObjeto);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-05")]
    public void El_cambio_de_estado_de_un_envio_declara_el_pedido_la_guia_el_operador_la_fuente_y_la_fecha_sin_datos_personales()
    {
        var accion = Catalogo.Obtener(nameof(AccionesAuditables.EstadoDeEnvioCambiado));

        Assert.Equal(["estadoAnterior", "estadoNuevo", "pedidoId", "numeroDeGuia", "operador", "fuente", "fecha"], accion.Detalle.Select(c => c.Nombre));
        Assert.DoesNotContain(accion.Detalle, c => c.Personal);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-05")]
    public void Un_identificador_de_negocio_un_GUID_y_una_huella_corta_no_se_redactan_en_el_detalle()
    {
        var detalle = DetalleSeguro.Construir(
            [new("edicionId", false), new("isbn", false), new("huellaCorta", false)],
            new Dictionary<string, string?> { ["edicionId"] = Guid.NewGuid().ToString("D"), ["isbn"] = "9786124516900", ["huellaCorta"] = "9f86d081884c7d65" });

        Assert.Empty(detalle.ClavesRedactadas);
    }

    // ---------- T12: movimientos de stock, catálogo, licencias, fuentes y rutas ----------

    [Fact]
    [Trait("Criterio", "AC-AUD-13")]
    public async Task La_expiracion_automatica_de_una_reserva_queda_con_el_actor_sistema_la_edicion_y_la_cantidad()
    {
        var entorno = new Entorno(ActorDePrueba.DelSistema());
        var fuente = Guid.NewGuid();

        await entorno.Registrar(
            AccionesAuditables.ReservaExpirada,
            new Dictionary<string, string?> { ["edicionId"] = Guid.NewGuid().ToString("D"), ["cantidad"] = "2" },
            objetoTipo: "pedido",
            objetoId: "pd-1",
            origen: fuente);

        var registro = Assert.Single(entorno.Almacen.Registros);
        Assert.Equal(TipoDeActor.Sistema, registro.Actor.Tipo);
        Assert.Equal(fuente.ToString("D"), registro.SucursalOrigen);
        Assert.Equal("2", registro.Detalle.Valores["cantidad"]);
        Assert.Contains("edicionId", registro.Detalle.Valores.Keys);
    }

    [Theory]
    [InlineData(nameof(AccionesAuditables.ReservaConfirmada))]
    [InlineData(nameof(AccionesAuditables.ReservaLiberada))]
    [InlineData(nameof(AccionesAuditables.StockDescontadoPorEntrega))]
    [InlineData(nameof(AccionesAuditables.StockDescontadoPorReparto))]
    [Trait("Criterio", "AC-AUD-13")]
    public void Cada_movimiento_de_stock_lleva_la_edicion_y_la_cantidad(string accion)
    {
        var claves = Catalogo.Obtener(accion).Detalle.Select(c => c.Nombre).ToList();

        Assert.Contains("edicionId", claves);
        Assert.Contains("cantidad", claves);
    }

    [Theory]
    [InlineData(nameof(AccionesAuditables.StockDescontadoPorEntrega))]
    [InlineData(nameof(AccionesAuditables.StockDescontadoPorReparto))]
    public void El_descuento_del_stock_fisico_guarda_lo_que_habia_y_lo_que_quedo(string accion)
    {
        var claves = Catalogo.Obtener(accion).Detalle.Select(c => c.Nombre).ToList();

        Assert.Contains("cantidadAnterior", claves);
        Assert.Contains("cantidadPosterior", claves);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-14")]
    public async Task Una_ruta_modificada_guarda_el_plazo_anterior_y_el_nuevo_y_las_dos_fuentes()
    {
        var entorno = new Entorno(ActorDePrueba.DeAdministradorCentral());
        var cusco = Guid.NewGuid();
        var ayacucho = Guid.NewGuid();

        await entorno.Registrar(
            AccionesAuditables.RutaModificada,
            new Dictionary<string, string?> { ["plazoAnteriorEnDias"] = "2", ["plazoNuevoEnDias"] = "3" },
            objetoTipo: "ruta",
            origen: cusco,
            destino: ayacucho);

        var registro = Assert.Single(entorno.Almacen.Registros);
        Assert.Equal("2", registro.Detalle.Valores["plazoAnteriorEnDias"]);
        Assert.Equal("3", registro.Detalle.Valores["plazoNuevoEnDias"]);
        Assert.Equal(cusco.ToString("D"), registro.SucursalOrigen);
        Assert.Equal(ayacucho.ToString("D"), registro.SucursalDestino);
        Assert.Equal(TipoDeActor.Usuario, registro.Actor.Tipo);
    }

    [Theory]
    [InlineData(nameof(AccionesAuditables.FuenteRegistrada))]
    [InlineData(nameof(AccionesAuditables.FuenteEditada))]
    [Trait("Criterio", "AC-AUD-14")]
    public async Task El_numero_de_contacto_de_una_fuente_no_entra_a_la_auditoria(string nombre)
    {
        var entorno = new Entorno(ActorDePrueba.DeAdministradorCentral());
        var accion = Constantes().Single(c => c.Identificador == nombre);

        await entorno.Registrar(
            accion,
            new Dictionary<string, string?> { ["codigoDeFuente"] = "PIURA", ["camposCambiados"] = "contacto", ["contacto"] = "987654321", ["telefono"] = "987654321" },
            objetoTipo: "fuente",
            objetoId: "fu-1");

        var registro = Assert.Single(entorno.Almacen.Registros);
        Assert.DoesNotContain("contacto", registro.Detalle.Valores.Keys);
        Assert.DoesNotContain("telefono", registro.Detalle.Valores.Keys);
        Assert.DoesNotContain("987654321", registro.Detalle.Texto);
        Assert.True(registro.Detalle.CantidadDeClavesDescartadas >= 2);
    }

    [Theory]
    [InlineData(nameof(AccionesAuditables.ArchivoDeEdicionReemplazado))]
    [InlineData(nameof(AccionesAuditables.PortadaDeEdicionReemplazada))]
    [Trait("Criterio", "AC-AUD-14")]
    public async Task El_reemplazo_de_un_archivo_guarda_tipo_tamano_y_huella_sin_la_clave_del_almacen(string nombre)
    {
        var entorno = new Entorno(ActorDePrueba.DeAdministradorCentral());
        var accion = Constantes().Single(c => c.Identificador == nombre);

        await entorno.Registrar(
            accion,
            new Dictionary<string, string?>
            {
                ["tipo"] = "application/epub+zip",
                ["tamanoEnBytes"] = "1048576",
                ["huellaCorta"] = "9f86d081884c7d65",
                ["claveDelAlmacen"] = "edicion/9786124516900/original.epub",
                ["urlFirmada"] = "https://almacen.ejemplo/obj?firma=abc",
            },
            objetoTipo: "edicion",
            objetoId: "ed-9");

        var registro = Assert.Single(entorno.Almacen.Registros);
        Assert.Equal("application/epub+zip", registro.Detalle.Valores["tipo"]);
        Assert.Equal("1048576", registro.Detalle.Valores["tamanoEnBytes"]);
        Assert.Equal("9f86d081884c7d65", registro.Detalle.Valores["huellaCorta"]);
        Assert.DoesNotContain("claveDelAlmacen", registro.Detalle.Valores.Keys);
        Assert.DoesNotContain("urlFirmada", registro.Detalle.Valores.Keys);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-14")]
    public async Task Un_cambio_de_licencia_guarda_el_valor_anterior_y_el_nuevo()
    {
        var entorno = new Entorno(ActorDePrueba.DeAdministradorCentral());

        await entorno.Registrar(
            AccionesAuditables.LicenciaCambiada,
            new Dictionary<string, string?> { ["vigenciaAnterior"] = "2027-01-31", ["vigenciaNueva"] = "2028-01-31", ["tipoAnterior"] = "comercial", ["tipoNuevo"] = "comercial" },
            objetoTipo: "edicion",
            objetoId: "ed-9");

        var registro = Assert.Single(entorno.Almacen.Registros);
        Assert.Equal("2027-01-31", registro.Detalle.Valores["vigenciaAnterior"]);
        Assert.Equal("2028-01-31", registro.Detalle.Valores["vigenciaNueva"]);
    }

    [Theory]
    [InlineData(nameof(AccionesAuditables.ObraRegistrada))]
    [InlineData(nameof(AccionesAuditables.ObraEditada))]
    [InlineData(nameof(AccionesAuditables.ObraDadaDeBaja))]
    [InlineData(nameof(AccionesAuditables.EdicionRegistrada))]
    [InlineData(nameof(AccionesAuditables.EdicionEditada))]
    [InlineData(nameof(AccionesAuditables.EdicionDadaDeBaja))]
    [InlineData(nameof(AccionesAuditables.EdicionDigitalRetirada))]
    [InlineData(nameof(AccionesAuditables.FuenteRegistrada))]
    [InlineData(nameof(AccionesAuditables.FuenteEditada))]
    [InlineData(nameof(AccionesAuditables.RutaRegistrada))]
    [InlineData(nameof(AccionesAuditables.RutaModificada))]
    [InlineData(nameof(AccionesAuditables.RecepcionDesignada))]
    [InlineData(nameof(AccionesAuditables.LicenciaCambiada))]
    [InlineData(nameof(AccionesAuditables.ArchivoDeEdicionReemplazado))]
    [InlineData(nameof(AccionesAuditables.PortadaDeEdicionReemplazada))]
    [Trait("Criterio", "AC-AUD-14")]
    public async Task Las_operaciones_del_catalogo_las_fuentes_y_las_rutas_se_cancelan_si_no_se_puede_auditar(string nombre)
    {
        var entorno = new Entorno(ActorDePrueba.DeAdministradorCentral());
        entorno.Almacen.Falla = new InvalidOperationException("la base no responde");
        var accion = Constantes().Single(c => c.Identificador == nombre);

        await Assert.ThrowsAsync<AuditoriaNoDisponibleException>(() => entorno.Registrar(accion));
    }

    [Theory]
    [InlineData(nameof(AccionesAuditables.ReservaConfirmada))]
    [InlineData(nameof(AccionesAuditables.ReservaLiberada))]
    [InlineData(nameof(AccionesAuditables.ReservaExpirada))]
    [InlineData(nameof(AccionesAuditables.StockDescontadoPorEntrega))]
    [InlineData(nameof(AccionesAuditables.StockDescontadoPorReparto))]
    [Trait("Criterio", "AC-AUD-13")]
    public async Task Los_movimientos_de_stock_se_cancelan_si_no_se_puede_auditar(string nombre)
    {
        var entorno = new Entorno(ActorDePrueba.DeAdministradorCentral());
        entorno.Almacen.Falla = new InvalidOperationException("la base no responde");
        var accion = Constantes().Single(c => c.Identificador == nombre);

        await Assert.ThrowsAsync<AuditoriaNoDisponibleException>(() => entorno.Registrar(accion));
    }

    // ---------- T11: precios, anulaciones, ajustes, ingresos, reembolsos y estados (AUD-02) ----------

    [Fact]
    [Trait("Criterio", "AC-AUD-01")]
    public async Task Un_cambio_de_precio_guarda_el_precio_anterior_y_el_nuevo_con_el_actor_y_su_rol()
    {
        var entorno = new Entorno(ActorDePrueba.DeAdministradorCentral());

        await entorno.Registrar(
            AccionesAuditables.PrecioDeEdicionCambiado,
            new Dictionary<string, string?> { ["precioAnterior"] = "129.00", ["precioNuevo"] = "139.00", ["moneda"] = "PEN" },
            objetoTipo: "edicion",
            objetoId: "ed-77");

        var registro = Assert.Single(entorno.Almacen.Registros);
        Assert.Equal("129.00", registro.Detalle.Valores["precioAnterior"]);
        Assert.Equal("139.00", registro.Detalle.Valores["precioNuevo"]);
        Assert.Equal("administrador-central", registro.Actor.Rol);
        Assert.Equal(ServicioDeOrigen.Ecommerce, registro.Servicio);
        Assert.Equal(Ahora, registro.InstanteUtc);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-01")]
    public async Task Un_reembolso_automatico_del_sistema_no_lleva_numero_de_tarjeta()
    {
        var entorno = new Entorno(ActorDePrueba.DelSistema());

        await entorno.Registrar(
            AccionesAuditables.ReembolsoRegistrado,
            new Dictionary<string, string?>
            {
                ["monto"] = "59.90",
                ["moneda"] = "PEN",
                ["modalidad"] = "automatico",
                ["medio"] = "tarjeta",
                ["numeroDeTarjeta"] = "4111 1111 1111 1111",
                ["titular"] = "Ana Perez",
            },
            objetoTipo: "pedido",
            objetoId: "pd-5");

        var registro = Assert.Single(entorno.Almacen.Registros);
        Assert.Equal(TipoDeActor.Sistema, registro.Actor.Tipo);
        Assert.DoesNotContain("numeroDeTarjeta", registro.Detalle.Valores.Keys);
        Assert.DoesNotContain("titular", registro.Detalle.Valores.Keys);
        Assert.DoesNotContain("4111", registro.Detalle.Texto);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-01")]
    public async Task El_paso_de_una_solicitud_de_traslado_a_rechazada_guarda_el_origen_el_destino_y_los_estados()
    {
        var entorno = new Entorno(ActorDePrueba.DeAdministradorCentral());
        var cusco = Guid.NewGuid();
        var ayacucho = Guid.NewGuid();

        await entorno.Registrar(
            AccionesAuditables.TrasladoRechazado,
            new Dictionary<string, string?> { ["estadoAnterior"] = "solicitado", ["estadoNuevo"] = "rechazado" },
            objetoTipo: "traslado",
            objetoId: "tr-9",
            origen: cusco,
            destino: ayacucho);

        var registro = Assert.Single(entorno.Almacen.Registros);
        Assert.Equal(cusco.ToString("D"), registro.SucursalOrigen);
        Assert.Equal(ayacucho.ToString("D"), registro.SucursalDestino);
        Assert.Equal("rechazado", registro.Detalle.Valores["estadoNuevo"]);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-01")]
    public async Task El_rechazo_de_una_operacion_se_registra_con_resultado_rechazado_en_la_misma_accion()
    {
        var entorno = new Entorno(ActorDePrueba.DeEncargadoDe(Guid.NewGuid()));

        await entorno.Auditoria.Registrar(new SolicitudDeRegistro(AccionesAuditables.TrasladoRechazado)
        {
            ObjetoTipo = "traslado",
            ObjetoId = "tr-9",
            Resultado = ResultadoAuditado.Rechazado,
            Detalle = new Dictionary<string, string?> { ["estadoAnterior"] = "solicitado", ["estadoNuevo"] = "en-transito" },
        });

        Assert.Equal(ResultadoDeOperacion.Rechazado, Assert.Single(entorno.Almacen.Registros).Resultado);
    }

    [Theory]
    [InlineData(nameof(AccionesAuditables.PrecioDeEdicionCambiado))]
    [InlineData(nameof(AccionesAuditables.PedidoAnulado))]
    [InlineData(nameof(AccionesAuditables.AjusteRegistrado))]
    [InlineData(nameof(AccionesAuditables.IngresoDeMercaderiaRegistrado))]
    [InlineData(nameof(AccionesAuditables.ReingresoPorDevolucion))]
    [InlineData(nameof(AccionesAuditables.ReembolsoRegistrado))]
    [InlineData(nameof(AccionesAuditables.EstadoDePedidoCambiado))]
    [InlineData(nameof(AccionesAuditables.EstadoDeEnvioCambiado))]
    [InlineData(nameof(AccionesAuditables.DevolucionSolicitada))]
    [InlineData(nameof(AccionesAuditables.EstadoDeDevolucionCambiado))]
    [InlineData(nameof(AccionesAuditables.TrasladoSolicitado))]
    [InlineData(nameof(AccionesAuditables.TrasladoRechazado))]
    [Trait("Criterio", "AC-AUD-01")]
    public async Task Las_operaciones_de_AUD_02_se_cancelan_si_no_se_puede_auditar(string nombre)
    {
        var entorno = new Entorno(ActorDePrueba.DeAdministradorCentral());
        entorno.Almacen.Falla = new InvalidOperationException("la base no responde");
        var accion = Constantes().Single(c => c.Identificador == nombre);

        await Assert.ThrowsAsync<AuditoriaNoDisponibleException>(() => entorno.Registrar(accion));
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-23")]
    public async Task Una_operacion_que_otra_spec_declara_auditable_queda_con_el_contrato_de_AUD_01_y_AUD_23_si_se_agrega_al_catalogo()
    {
        // El contrato de AUD-26: la acción de otra feature se agrega al catálogo con su clase y su lista blanca; sin esa declaración no se registra.
        var propia = new AccionDelCatalogo(
            "OperacionDeOtraFeatureDePrueba",
            ClaseDeAccion.ConcedeOCambia,
            TipoDeRegistro.Auditoria,
            false,
            [new("resolucion", false)],
            false,
            false,
            [],
            false);
        var catalogo = new CatalogoDeAcciones([.. Catalogo.Acciones, propia]);
        var actor = ActorDePrueba.DeIntegracion("culqi");
        var almacen = new AlmacenDeRegistrosEnMemoria();
        var constructor = new ConstructorDeRegistros(ServicioDeOrigen.Ecommerce, catalogo, new RelojFijo(Ahora), actor, new OrigenFijo(OrigenDeSolicitud.Desconocido));
        var auditoria = new AuditoriaDeAplicacion(constructor, almacen);

        await auditoria.Registrar(new SolicitudDeRegistro(new AccionAuditable("OperacionDeOtraFeatureDePrueba")) { Detalle = new Dictionary<string, string?> { ["resolucion"] = "archivo-corregido" } });
        await Assert.ThrowsAsync<ArgumentException>(() => new AuditoriaDeAplicacion(new ConstructorDeRegistros(ServicioDeOrigen.Ecommerce, Catalogo, new RelojFijo(Ahora), actor, new OrigenFijo(OrigenDeSolicitud.Desconocido)), almacen)
            .Registrar(new SolicitudDeRegistro(new AccionAuditable("OperacionDeOtraFeatureDePrueba"))));

        var registro = Assert.Single(almacen.Registros);
        Assert.Equal(TipoDeActor.Integracion, registro.Actor.Tipo);
        Assert.Equal(Ahora, registro.InstanteUtc);
        Assert.Equal("archivo-corregido", registro.Detalle.Valores["resolucion"]);
    }

    // ---------- T13: usuarios, roles, cancelaciones, biblioteca, reclamaciones, consultas y claves (AUD-16, AUD-21, AUD-22, AUD-35) ----------

    private static readonly string[] AccionesDeIdentidad =
    [
        nameof(AccionesAuditables.UsuarioDadoDeAlta),
        nameof(AccionesAuditables.UsuarioDesactivado),
        nameof(AccionesAuditables.UsuarioReactivado),
        nameof(AccionesAuditables.CambioDeRol),
        nameof(AccionesAuditables.ContrasenaRestablecidaPorAdministracion),
        nameof(AccionesAuditables.SegundoFactorRestablecidoPorAdministracion),
        nameof(AccionesAuditables.CuentaCancelada),
        nameof(AccionesAuditables.CuentaReemplazada),
        nameof(AccionesAuditables.CancelacionDiferidaDescartada),
        nameof(AccionesAuditables.SolicitudDeDatosAtendida),
        nameof(AccionesAuditables.SolicitudDeDatosConsultada),
        nameof(AccionesAuditables.ClaveDeFirmaRotada),
        nameof(AccionesAuditables.ClaveDeFirmaDeclaradaComprometida),
    ];

    [Fact]
    [Trait("Criterio", "AC-AUD-11")]
    public void Todas_las_acciones_de_Identidad_van_al_almacen_aislado_y_las_de_otros_modulos_no()
    {
        foreach (var nombre in AccionesDeIdentidad)
        {
            Assert.True(Catalogo.Obtener(nombre).Aislada, $"{nombre} es de Identidad y debe ir al almacén aislado (D17).");
        }

        foreach (var nombre in new[]
        {
            nameof(AccionesAuditables.DescargasRestituidas),
            nameof(AccionesAuditables.AccesoDigitalRevocado),
            nameof(AccionesAuditables.ReporteDeArchivoResuelto),
            nameof(AccionesAuditables.HojaDeReclamacionRespondida),
            nameof(AccionesAuditables.HojaDeReclamacionDescartada),
            nameof(AccionesAuditables.HojaDeReclamacionAnonimizada),
            nameof(AccionesAuditables.BibliotecaDeClienteConsultada),
            nameof(AccionesAuditables.HojaDeReclamacionConsultada),
            nameof(AccionesAuditables.AvisosDeNumeroDesactivados),
        })
        {
            Assert.False(Catalogo.Obtener(nombre).Aislada, $"{nombre} no es de Identidad.");
        }
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-17")]
    public async Task Un_cambio_de_rol_es_un_unico_registro_de_auditoria_y_de_seguridad_con_motivo_y_origen()
    {
        var entorno = new Entorno(ActorDePrueba.DeAdministradorCentral());

        await entorno.Registrar(
            AccionesAuditables.CambioDeRol,
            new Dictionary<string, string?> { ["rolAnterior"] = "cliente", ["rolNuevo"] = "encargado-de-sucursal" },
            motivo: Catalogo.Obtener("CambioDeRol").Motivos[0],
            objetoTipo: "usuario",
            objetoId: Guid.NewGuid().ToString("D"));

        var registro = Assert.Single(entorno.Almacen.Registros);
        Assert.Equal(TipoDeRegistro.Ambos, registro.Tipo);
        Assert.True(registro.Aislada);
        Assert.NotNull(registro.Motivo);
        Assert.NotNull(registro.Origen);
        Assert.True(Catalogo.Obtener("CambioDeRol").ObjetoPersonal);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-05")]
    public async Task El_restablecimiento_de_una_contrasena_temporal_no_guarda_secretos()
    {
        var entorno = new Entorno(ActorDePrueba.DeAdministradorCentral());

        await entorno.Registrar(
            AccionesAuditables.ContrasenaRestablecidaPorAdministracion,
            new Dictionary<string, string?> { ["contrasenaTemporal"] = "Temp-Uno-2026", ["codigo"] = "123456", ["secreto"] = "JBSWY3DPEHPK3PXP" },
            objetoTipo: "usuario",
            objetoId: Guid.NewGuid().ToString("D"));

        var registro = Assert.Single(entorno.Almacen.Registros);
        Assert.Empty(registro.Detalle.Valores);
        Assert.DoesNotContain("Temp-Uno-2026", registro.Detalle.Texto);
        Assert.DoesNotContain("123456", registro.Detalle.Texto);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-11")]
    public async Task El_descarte_de_una_cancelacion_diferida_se_registra_con_el_actor_sistema()
    {
        var entorno = new Entorno(ActorDePrueba.DelSistema());

        await entorno.Registrar(AccionesAuditables.CancelacionDiferidaDescartada, objetoTipo: "cuenta", objetoId: Guid.NewGuid().ToString("D"));

        Assert.Equal(TipoDeActor.Sistema, Assert.Single(entorno.Almacen.Registros).Actor.Tipo);
    }

    [Theory]
    [InlineData(nameof(AccionesAuditables.UsuarioDadoDeAlta), true)]
    [InlineData(nameof(AccionesAuditables.UsuarioDesactivado), true)]
    [InlineData(nameof(AccionesAuditables.UsuarioReactivado), true)]
    [InlineData(nameof(AccionesAuditables.CuentaCancelada), true)]
    [InlineData(nameof(AccionesAuditables.CuentaReemplazada), true)]
    [InlineData(nameof(AccionesAuditables.CancelacionDiferidaDescartada), true)]
    [InlineData(nameof(AccionesAuditables.BibliotecaDeClienteConsultada), true)]
    [InlineData(nameof(AccionesAuditables.SolicitudDeDatosAtendida), false)]
    [InlineData(nameof(AccionesAuditables.SolicitudDeDatosConsultada), false)]
    [InlineData(nameof(AccionesAuditables.HojaDeReclamacionConsultada), false)]
    [Trait("Criterio", "AC-AUD-19")]
    public void Las_cuentas_y_los_clientes_como_objeto_son_personales_y_se_anonimizan_con_la_cuenta(string nombre, bool esperado) =>
        Assert.Equal(esperado, Catalogo.Obtener(nombre).ObjetoPersonal);

    [Fact]
    [Trait("Criterio", "AC-AUD-19")]
    public async Task La_rotacion_y_la_declaracion_de_compromiso_de_la_clave_de_firma_no_guardan_la_clave()
    {
        var entorno = new Entorno(ActorDePrueba.DeAdministradorCentral());

        await entorno.Registrar(
            AccionesAuditables.ClaveDeFirmaRotada,
            new Dictionary<string, string?> { ["versionAnterior"] = "v1", ["versionNueva"] = "v2", ["clavePrivada"] = "AGE-SECRET" + "-KEY-1ABCDEF", ["clave"] = "-----BEGIN " + "PRIVATE KEY-----" },
            objetoTipo: "clave-de-firma");
        await entorno.Registrar(
            AccionesAuditables.ClaveDeFirmaDeclaradaComprometida,
            new Dictionary<string, string?> { ["version"] = "v2" },
            motivo: Catalogo.Obtener("ClaveDeFirmaDeclaradaComprometida").Motivos[0],
            objetoTipo: "clave-de-firma");

        Assert.Equal(2, entorno.Almacen.Registros.Count);
        Assert.All(entorno.Almacen.Registros, r => Assert.DoesNotContain("AGE-SECRET", r.Detalle.Texto));
        Assert.Equal(TipoDeRegistro.Ambos, entorno.Almacen.Registros[1].Tipo);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-19")]
    public async Task La_desactivacion_de_avisos_de_un_numero_nunca_guarda_el_numero()
    {
        var entorno = new Entorno(ActorDePrueba.DeAdministradorCentral());

        await entorno.Registrar(
            AccionesAuditables.AvisosDeNumeroDesactivados,
            new Dictionary<string, string?> { ["causa"] = "reclamo-del-titular", ["numero"] = "987654321" },
            objetoTipo: "numero",
            objetoId: "hn-12345678");

        var registro = Assert.Single(entorno.Almacen.Registros);
        Assert.DoesNotContain("987654321", registro.Detalle.Texto);
        Assert.Contains("causa", registro.Detalle.Valores.Keys);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-11")]
    public async Task Las_acciones_de_T13_se_cancelan_si_no_se_puede_auditar()
    {
        var nombres = AccionesDeIdentidad.Concat(
        [
            nameof(AccionesAuditables.DescargasRestituidas),
            nameof(AccionesAuditables.AccesoDigitalRevocado),
            nameof(AccionesAuditables.ReporteDeArchivoResuelto),
            nameof(AccionesAuditables.HojaDeReclamacionRespondida),
            nameof(AccionesAuditables.HojaDeReclamacionDescartada),
            nameof(AccionesAuditables.HojaDeReclamacionAnonimizada),
            nameof(AccionesAuditables.BibliotecaDeClienteConsultada),
            nameof(AccionesAuditables.HojaDeReclamacionConsultada),
            nameof(AccionesAuditables.AvisosDeNumeroDesactivados),
        ]);

        foreach (var nombre in nombres)
        {
            var entorno = new Entorno(ActorDePrueba.DeAdministradorCentral());
            entorno.Almacen.Falla = new InvalidOperationException("la base no responde");
            var accion = Constantes().Single(c => c.Identificador == nombre);
            var definicion = Catalogo.Obtener(nombre);

            await Assert.ThrowsAsync<AuditoriaNoDisponibleException>(() => entorno.Registrar(accion, motivo: definicion.ExigeMotivo ? definicion.Motivos[0] : null));
        }
    }
}
