using BabelHub.Ecommerce.Application.Carrito;
using BabelHub.Ecommerce.Application.Pagos;
using BabelHub.Ecommerce.Application.Pagos.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.UnitTests.Soporte;
using static BabelHub.Ecommerce.UnitTests.Pagos.InstantaneasDeCompraDePrueba;

namespace BabelHub.Ecommerce.UnitTests.Pagos;

/// <summary>La clave firmada de un intento, que se emite sin escribir nada (T10; PAG-04, PAG-16, D04).</summary>
public sealed class EmitirClaveDeIntentoTests
{
    private static async Task<EntornoDePagos> ConIntencion()
    {
        var e = new EntornoDePagos();
        await e.CrearMixta();
        return e;
    }

    private static EmitirClaveDeIntentoEntrada Entrada() => new(Pedido);

    [Fact]
    [Trait("Criterio", "AC-PAG-03")]
    public async Task Al_mostrar_el_formulario_se_emite_un_token_firmado_con_el_total_la_huella_y_30_minutos_sin_escribir_nada()
    {
        var e = await ConIntencion();
        var operaciones = e.Repositorio.Operaciones;

        var resultado = await e.EmitirClave.Ejecutar(Entrada());

        Assert.True(resultado.EsExito);
        var emitida = resultado.Valor;
        Assert.Equal(140m, emitida.Total);
        Assert.Equal(e.Reloj.Ahora.AddMinutes(30), emitida.ExpiraEn);
        Assert.Equal(e.Reloj.Ahora.AddMinutes(15), emitida.VenceEn);
        Assert.Equal(ResultadoDeVerificarClave.Valida, e.Firmador.Verificar(emitida.Token, e.Reloj.Ahora, out var datos));
        Assert.Equal(Pedido, datos!.PedidoId);
        Assert.Equal(Cuenta, datos.CuentaId);
        Assert.Equal("huella-a", datos.HuellaDeInstantanea);
        Assert.Equal(14_000, datos.TotalEnCentimos);
        Assert.Empty(e.Repositorio.Intentos);
        // Una sola lectura sin bloqueo: ninguna escritura.
        Assert.Equal(operaciones + 1, e.Repositorio.Operaciones);
        Assert.Equal(1, e.Repositorio.LecturasConBloqueo); // solo la de la creación
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-05")]
    public async Task Cada_emision_trae_un_intento_nuevo_con_su_propia_clave()
    {
        var e = await ConIntencion();

        var a = (await e.EmitirClave.Ejecutar(Entrada())).Valor;
        var b = (await e.EmitirClave.Ejecutar(Entrada())).Valor;

        e.Firmador.Verificar(a.Token, e.Reloj.Ahora, out var datosA);
        e.Firmador.Verificar(b.Token, e.Reloj.Ahora, out var datosB);
        Assert.NotEqual(datosA!.IntentoId, datosB!.IntentoId);
        Assert.NotEqual(a.Token, b.Token);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-03")]
    public async Task Un_token_alterado_vencido_o_firmado_con_otra_clave_se_rechaza()
    {
        var e = await ConIntencion();
        var token = (await e.EmitirClave.Ejecutar(Entrada())).Valor.Token;
        var partes = token.Split('.');
        var alterado = $"{partes[0]}.{partes[1]}.{(partes[2][0] == 'A' ? 'B' : 'A')}{partes[2][1..]}";
        var otroFirmador = new FirmadorDeClavesDeIntento(new OpcionesDeClavesDeIntento(System.Text.Encoding.UTF8.GetBytes("otra-clave-de-intentos-distinta-de-32-bytes-o-mas")));

        Assert.Equal(ResultadoDeVerificarClave.Invalida, e.Firmador.Verificar(alterado, e.Reloj.Ahora, out _));
        Assert.Equal(ResultadoDeVerificarClave.Invalida, otroFirmador.Verificar(token, e.Reloj.Ahora, out _));
        Assert.Equal(ResultadoDeVerificarClave.Valida, e.Firmador.Verificar(token, e.Reloj.Ahora.AddMinutes(30).AddSeconds(-2), out _));
        Assert.Equal(ResultadoDeVerificarClave.Vencida, e.Firmador.Verificar(token, e.Reloj.Ahora.AddMinutes(30), out _));
    }

    [Theory]
    [Trait("Criterio", "AC-PAG-03")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("basura")]
    [InlineData("v1.a.b")]
    [InlineData("v2.e30.e30")]
    [InlineData("v1.%%%.%%%")]
    public async Task Un_texto_que_no_es_un_token_se_rechaza_sin_lanzar(string? token)
    {
        var e = await ConIntencion();

        Assert.Equal(ResultadoDeVerificarClave.Invalida, e.Firmador.Verificar(token, e.Reloj.Ahora, out var datos));
        Assert.Null(datos);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-12")]
    public void Un_token_firmado_con_la_clave_anterior_sigue_valido_durante_la_rotacion()
    {
        var anterior = System.Text.Encoding.UTF8.GetBytes("clave-anterior-de-intentos-de-32-bytes-o-mas!");
        var nueva = System.Text.Encoding.UTF8.GetBytes("clave-nueva-de-intentos-de-32-bytes-o-mas-1234");
        var antiguo = new FirmadorDeClavesDeIntento(new OpcionesDeClavesDeIntento(anterior));
        var rotando = new FirmadorDeClavesDeIntento(new OpcionesDeClavesDeIntento(nueva, anterior));
        var token = antiguo.Emitir(new DatosDeLaClaveDeIntento(Guid.NewGuid(), Pedido, Cuenta, "h", 100, IntencionesDePrueba.Inicio.AddMinutes(30)));

        Assert.Equal(ResultadoDeVerificarClave.Valida, rotando.Verificar(token, IntencionesDePrueba.Inicio, out _));
        Assert.Equal(ResultadoDeVerificarClave.Invalida, new FirmadorDeClavesDeIntento(new OpcionesDeClavesDeIntento(nueva)).Verificar(token, IntencionesDePrueba.Inicio, out _));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-10")]
    public void Una_clave_de_intentos_corta_no_se_acepta_y_el_texto_de_las_opciones_no_la_muestra()
    {
        Assert.Throws<ArgumentException>(() => new OpcionesDeClavesDeIntento([1, 2, 3]));
        Assert.Equal("OpcionesDeClavesDeIntento { (secreto) }", new OpcionesDeClavesDeIntento(new byte[32]).ToString());
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-21")]
    public async Task Un_pedido_ajeno_o_inexistente_responde_no_encontrado_y_deja_un_acceso_denegado_por_cada_intento()
    {
        var e = await ConIntencion();
        e.Actor.Establecer(ActorDePrueba.DeCliente().Actual);

        var ajeno = await e.EmitirClave.Ejecutar(Entrada());
        var inexistente = await e.EmitirClave.Ejecutar(new EmitirClaveDeIntentoEntrada(Guid.NewGuid()));

        Assert.Equal(CategoriaDeError.NoEncontrado, ajeno.Error!.Categoria);
        Assert.Equal(CategoriaDeError.NoEncontrado, inexistente.Error!.Categoria);
        Assert.Equal(ajeno.Error.Clave, inexistente.Error.Clave);
        Assert.Equal(2, e.Denegaciones.Eventos.Count);
        Assert.Equal(0, e.Checkout.Consultas);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-21")]
    public async Task Un_visitante_o_sin_contexto_recibe_no_autenticado_sin_leer_nada()
    {
        var e = await ConIntencion();
        var operaciones = e.Repositorio.Operaciones;

        e.Actor.Establecer(ActorDePrueba.Anonimo().Actual);
        var visitante = await e.EmitirClave.Ejecutar(Entrada());
        e.Actor.Establecer(null);
        var sinContexto = await e.EmitirClave.Ejecutar(Entrada());

        Assert.Equal(CategoriaDeError.NoAutenticado, visitante.Error!.Categoria);
        Assert.Equal(CategoriaDeError.NoAutenticado, sinContexto.Error!.Categoria);
        Assert.Equal(operaciones, e.Repositorio.Operaciones);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-07")]
    public async Task Con_la_reserva_expirada_no_se_emite()
    {
        var e = await ConIntencion();
        e.Reloj.Ahora = e.Reloj.Ahora.AddMinutes(16);

        var resultado = await e.EmitirClave.Ejecutar(Entrada());

        Assert.Equal(CategoriaDeError.Conflicto, resultado.Error!.Categoria);
        Assert.Equal("reserva-expirada", resultado.Error.Clave);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-07")]
    public async Task Una_intencion_cancelada_no_emite()
    {
        var e = await ConIntencion();
        await e.Creador.Cancelar(Cuenta, Pedido);

        var resultado = await e.EmitirClave.Ejecutar(Entrada());

        Assert.Equal("intencion-cancelada", resultado.Error!.Clave);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-14")]
    public async Task Una_intencion_obsoleta_o_con_un_cobro_en_curso_no_emite_y_dice_por_que()
    {
        var obsoleta = await ConIntencion();
        await obsoleta.Creador.Reabrir(Cuenta, Pedido);
        var conCobro = await ConIntencion();
        var intencion = conCobro.Repositorio.Intenciones.Single();
        intencion.IniciarCobro(conCobro.Reloj.Ahora.AddSeconds(30));
        conCobro.Repositorio.Sembrar(intencion);

        Assert.Equal("instantanea-obsoleta", (await obsoleta.EmitirClave.Ejecutar(Entrada())).Error!.Clave);
        Assert.Equal("pago-en-curso", (await conCobro.EmitirClave.Ejecutar(Entrada())).Error!.Clave);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-14")]
    public async Task Con_el_pago_pendiente_en_carrito_no_se_emite()
    {
        var e = await ConIntencion();
        e.Checkout.Respuesta = Resultado<CheckoutVivoParaPagar>.Exito(new CheckoutVivoParaPagar(Pedido, EstadoVisibleDelCheckout.PagoPendiente));

        var resultado = await e.EmitirClave.Ejecutar(Entrada());

        Assert.Equal(CategoriaDeError.Conflicto, resultado.Error!.Categoria);
        Assert.Equal("pago-en-curso", resultado.Error.Clave);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-14")]
    public async Task Si_carrito_no_devuelve_el_pedido_de_esta_intencion_o_hay_un_retiro_pendiente_dice_que_el_carrito_cambio()
    {
        var otroPedido = await ConIntencion();
        otroPedido.Checkout.Respuesta = Resultado<CheckoutVivoParaPagar>.Exito(new CheckoutVivoParaPagar(Guid.NewGuid(), EstadoVisibleDelCheckout.EnPago));
        var sinVivo = await ConIntencion();
        sinVivo.Checkout.Respuesta = Resultado<CheckoutVivoParaPagar>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoEncontrado, "no-encontrado"));

        Assert.Equal("instantanea-obsoleta", (await otroPedido.EmitirClave.Ejecutar(Entrada())).Error!.Clave);
        Assert.Equal("instantanea-obsoleta", (await sinVivo.EmitirClave.Ejecutar(Entrada())).Error!.Clave);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-21")]
    public async Task Si_carrito_no_responde_o_falla_no_se_emite_y_se_dice_servicio_no_disponible()
    {
        var cae = await ConIntencion();
        cae.Checkout.Falla = true;
        var errorDeCarrito = await ConIntencion();
        errorDeCarrito.Checkout.Respuesta = Resultado<CheckoutVivoParaPagar>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoDisponible, "servicio-no-disponible"));

        Assert.Equal(CategoriaDeError.NoDisponible, (await cae.EmitirClave.Ejecutar(Entrada())).Error!.Categoria);
        Assert.Equal(CategoriaDeError.NoDisponible, (await errorDeCarrito.EmitirClave.Ejecutar(Entrada())).Error!.Categoria);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-02")]
    public async Task La_clave_emitida_no_se_muestra_en_su_texto_ni_en_json()
    {
        var e = await ConIntencion();
        var emitida = (await e.EmitirClave.Ejecutar(Entrada())).Valor;

        var texto = emitida + System.Text.Json.JsonSerializer.Serialize(emitida);

        Assert.DoesNotContain(emitida.Token, texto, StringComparison.Ordinal);
    }
}
