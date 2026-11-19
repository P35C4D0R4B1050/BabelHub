using System.Reflection;
using System.Text.Json;
using BabelHub.Ecommerce.Application.Envios;
using BabelHub.Ecommerce.Application.Envios.Interno;
using BabelHub.Ecommerce.Application.Pedidos;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Envios;

namespace BabelHub.Ecommerce.UnitTests.Envios;

/// <summary>
/// La privacidad de Envíos (T21; ENV-15, ENV-17, AC-ENV-17, AC-ENV-18): con un contacto <b>centinela</b> (nombre, dirección, teléfono y región reconocibles) una guía exitosa y una fallida —por rechazo, por respuesta inválida y por
/// una excepción cuyo texto repite la dirección— no dejan ninguno de esos valores en la auditoría, las alertas, los avisos, los mensajes de error, el estado guardado ni el resultado; y el operador recibe <b>solo</b> nombre,
/// dirección (con la región) y teléfono. El esquema sin columnas de contacto se prueba contra PostgreSQL en <c>RepositorioDeEnviosPostgresTests</c>.
/// </summary>
public sealed class PrivacidadDeEnviosTests
{
    private const string Nombre = "Zoila Centinela Rojas";
    private const string Telefono = "955123987";
    private const string Direccion = "Jr. Centinela 4321";
    private const string Region = "Huamanga";

    private static readonly string[] Centinelas = [Nombre, Telefono, Direccion, Region, "Centinela", "Zoila"];

    private static Envio Sembrar(EntornoDeEnvios entorno)
    {
        var pedidoId = Guid.NewGuid();
        var envio = Envio.Crear(Guid.NewGuid(), pedidoId, "lima-central", [Guid.NewGuid()], EntornoDeEnvios.AhoraInicial);
        entorno.Envios.Sembrar(envio);
        entorno.Contactos.Sembrar(pedidoId, LecturaDelContactoDeEntrega.Disponible(new ContactoDeEntrega(Nombre, Telefono, Direccion, Region)));
        entorno.SembrarPedidoDelEnvio(pedidoId);
        return envio;
    }

    /// <summary>Todo lo observable de una operación, serializado: lo que se audita, se alerta, se avisa, se guarda y se devuelve.</summary>
    private static string LoObservable(EntornoDeEnvios entorno, object? resultado)
    {
        var opciones = new JsonSerializerOptions { IncludeFields = true, MaxDepth = 32 };
        return string.Join(
            '\n',
            JsonSerializer.Serialize(entorno.Auditoria.Solicitudes.Select(s => new { s.Accion, s.Detalle }), opciones),
            JsonSerializer.Serialize(entorno.Alertas.Alertas, opciones),
            JsonSerializer.Serialize(entorno.Notificador.Solicitudes, opciones),
            JsonSerializer.Serialize(entorno.Envios.Todos.Select(e => new { e.Id, e.PedidoId, e.CodigoDeFuente, e.Estado, e.NumeroDeGuia, e.Operador, e.ClaveDeSolicitud }), opciones),
            JsonSerializer.Serialize(resultado, opciones));
    }

    private static void SinCentinelas(string texto)
    {
        foreach (var centinela in Centinelas)
        {
            Assert.DoesNotContain(centinela, texto, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-18")]
    public async Task Una_guia_exitosa_no_deja_el_contacto_en_ningun_registro_y_el_operador_recibe_solo_nombre_direccion_y_telefono()
    {
        var entorno = new EntornoDeEnvios();
        var envio = Sembrar(entorno);

        var resultado = await entorno.SolicitarGuia.Ejecutar(envio.Id);

        Assert.True(resultado.EsExito);
        SinCentinelas(LoObservable(entorno, resultado.EsExito ? resultado.Valor : resultado.Error));
        var recibida = Assert.Single(entorno.Operador.Recibidas);
        Assert.Equal(Nombre, recibida.Nombre);
        Assert.Equal($"{Direccion}, {Region}", recibida.Direccion);
        Assert.Equal(Telefono, recibida.Telefono);
    }

    [Theory]
    [Trait("Criterio", "AC-ENV-18")]
    [InlineData("rechaza")]
    [InlineData("respuesta-invalida")]
    [InlineData("no-responde")]
    [InlineData("lanza-con-la-direccion")]
    public async Task Una_guia_fallida_no_deja_el_contacto_en_la_auditoria_las_alertas_los_avisos_ni_el_mensaje_de_error(string falla)
    {
        var entorno = new EntornoDeEnvios();
        var envio = Sembrar(entorno);
        switch (falla)
        {
            case "rechaza":
                entorno.Operador.Forzado = new ResultadoDelOperador.Rechazada();
                break;
            case "respuesta-invalida":
                entorno.Operador.Forzado = new ResultadoDelOperador.RespuestaInvalida();
                break;
            case "no-responde":
                entorno.Operador.Forzado = new ResultadoDelOperador.NoDisponible();
                break;
            default:
                entorno.Operador.AlLlamar = () => throw new InvalidOperationException($"Dirección rechazada: {Nombre}, {Direccion}, {Region}, {Telefono}");
                break;
        }

        var resultado = await entorno.SolicitarGuia.Ejecutar(envio.Id);

        // Control positivo: el contacto llegó al operador y el fallo es el esperado, no uno anterior que dejara las listas vacías.
        var recibida = Assert.Single(entorno.Operador.Recibidas);
        Assert.Equal(Nombre, recibida.Nombre);
        Assert.Equal(CategoriaDeError.NoDisponible, resultado.Error!.Categoria);
        Assert.Equal("operador-no-disponible", resultado.Error.Clave);
        Assert.False(resultado.EsExito);
        SinCentinelas(LoObservable(entorno, resultado.EsExito ? resultado.Valor : resultado.Error));
        SinCentinelas(resultado.Error!.Clave + string.Concat(resultado.Error.Errores.Select(e => e.Campo + e.Clave)));
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-18")]
    public void El_detector_de_centinelas_detecta_un_contacto_serializado()
    {
        var texto = JsonSerializer.Serialize(new { detalle = $"{Nombre} {Direccion}" });

        Assert.ThrowsAny<Exception>(() => SinCentinelas(texto));
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-18")]
    public async Task La_auditoria_de_una_guia_exitosa_lleva_exactamente_las_claves_cerradas_y_ningun_dato_de_contacto()
    {
        var entorno = new EntornoDeEnvios();
        var envio = Sembrar(entorno);

        await entorno.SolicitarGuia.Ejecutar(envio.Id);

        var solicitud = Assert.Single(entorno.Auditoria.Solicitudes);
        Assert.Equal(["estadoAnterior", "estadoNuevo", "fuente", "numeroDeGuia", "operador", "pedidoId"], solicitud.Detalle.Keys.Order());
        Assert.Equal("operador-a", solicitud.Detalle["operador"]);
        Assert.StartsWith("GUIA-", solicitud.Detalle["numeroDeGuia"], StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-18")]
    public async Task Un_numero_de_guia_repetido_publica_una_alerta_sin_contacto_y_deja_el_envio_pendiente()
    {
        var entorno = new EntornoDeEnvios();
        var envio = Sembrar(entorno);
        entorno.Envios.GuiaRepetidaAlGuardar = true;

        var resultado = await entorno.SolicitarGuia.Ejecutar(envio.Id);

        Assert.Equal("envio-guia-repetida", Assert.Single(entorno.Alertas.Alertas).Motivo);
        Assert.Single(entorno.Operador.Recibidas);
        SinCentinelas(LoObservable(entorno, resultado.EsExito ? resultado.Valor : resultado.Error));
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-18")]
    public async Task Con_la_auditoria_caida_tras_crear_la_guia_el_reintento_recupera_la_misma_sin_contacto_en_ningun_registro()
    {
        var entorno = new EntornoDeEnvios();
        var envio = Sembrar(entorno);
        entorno.Auditoria.Caida = true;

        var primero = await entorno.SolicitarGuia.Ejecutar(envio.Id);
        entorno.Auditoria.Caida = false;
        var reintento = await entorno.SolicitarGuia.Ejecutar(envio.Id);

        Assert.False(primero.EsExito);
        Assert.True(reintento.EsExito);
        Assert.Equal(2, entorno.Operador.Recibidas.Count);
        Assert.Single(entorno.Operador.Recibidas.Select(r => r.Clave).Distinct());
        Assert.Equal(1, entorno.Operador.GuiasCreadas);
        SinCentinelas(LoObservable(entorno, primero.Error));
        SinCentinelas(LoObservable(entorno, reintento.Valor));
    }

    [Theory]
    [Trait("Criterio", "AC-ENV-18")]
    [InlineData("nombre")]
    [InlineData("direccion")]
    [InlineData("telefono")]
    public async Task El_contacto_incompleto_nombra_solo_el_campo_nunca_el_valor_de_los_otros(string faltante)
    {
        var entorno = new EntornoDeEnvios();
        var envio = Sembrar(entorno);
        var campo = faltante switch
        {
            "nombre" => CampoDelContactoDeEntrega.Nombre,
            "direccion" => CampoDelContactoDeEntrega.Direccion,
            _ => CampoDelContactoDeEntrega.Telefono,
        };
        entorno.Contactos.Sembrar(envio.PedidoId, LecturaDelContactoDeEntrega.Faltante(campo));

        var resultado = await entorno.SolicitarGuia.Ejecutar(envio.Id);

        Assert.Equal("contacto-incompleto", resultado.Error!.Clave);
        Assert.Equal(faltante, Assert.Single(resultado.Error.Errores).Campo);
        SinCentinelas(LoObservable(entorno, resultado.EsExito ? resultado.Valor : resultado.Error));
        Assert.Empty(entorno.Operador.Recibidas);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-17")]
    public void Ningun_tipo_que_guarda_o_devuelve_Envios_tiene_un_campo_de_contacto()
    {
        var prohibidos = new[] { "nombre", "direccion", "telefono", "correo", "email", "region", "destinatario", "contacto" };
        var tipos = new[]
        {
            typeof(Envio), typeof(LineaDeEnvio), typeof(DetalleDeEnvio), typeof(ResumenDeEnvio), typeof(PaginaDeEnvios), typeof(EnvioDeMiPedido), typeof(EnviosDeMiPedido), typeof(ListarEnviosEntrada),
            typeof(BabelHub.Ecommerce.Application.Auditoria.SolicitudDeRegistro), typeof(AlertaTecnica),
        };

        foreach (var tipo in tipos)
        {
            var miembros = tipo.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Select(p => p.Name)
                .Concat(tipo.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Select(f => f.Name));
            Assert.NotEmpty(miembros);
            Assert.DoesNotContain(miembros, m => prohibidos.Any(p => m.Contains(p, StringComparison.OrdinalIgnoreCase)));
        }
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-17")]
    public void El_destinatario_para_la_guia_se_redacta_al_imprimirlo_y_al_serializarlo()
    {
        var destinatario = DestinatarioParaGuia.Validar(Nombre, $"{Direccion}, {Region}", Telefono).Destinatario!;

        Assert.Contains("[redactado]", JsonSerializer.Serialize(destinatario), StringComparison.Ordinal);
        SinCentinelas(destinatario.ToString()!);
        SinCentinelas(JsonSerializer.Serialize(destinatario));
        SinCentinelas(JsonSerializer.Serialize(new { destinatario }));
    }
}
