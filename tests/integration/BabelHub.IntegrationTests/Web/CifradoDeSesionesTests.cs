using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Auditoria.Interno;
using BabelHub.Ecommerce.Application.Web;
using BabelHub.Ecommerce.Application.Web.Interno;
using BabelHub.Ecommerce.Domain.Web;
using BabelHub.Ecommerce.Infrastructure.Web;
using Microsoft.Extensions.Configuration;

namespace BabelHub.IntegrationTests.Web;

/// <summary>El cifrado de los secretos de la sesión (T11, D02): AES-256-GCM con subclaves HKDF por propósito, datos asociados a la fila, una clave por host y rotación en dos pasos.</summary>
public sealed class CifradoDeSesionesTests
{
    private const string ClaveA = "clave-A-de-sesiones-0123456789-abcdefghij";
    private const string ClaveB = "clave-B-de-sesiones-9876543210-jihgfedcba";
    private const string Aad = "id-de-la-fila|Credencial|usuario";

    private static AesGcmCifradorDeSecretosDeSesion Cifrador(string vigente, string? anterior = null, HostDeSesion host = HostDeSesion.Tienda) =>
        new(ConfiguracionDeSesionesWeb.Crear(host, vigente, anterior));

    [Fact]
    [Trait("Criterio", "AC-WEB-03")]
    public void Un_texto_cifrado_se_descifra_con_la_misma_clave_proposito_y_datos_asociados_y_no_deja_el_texto_a_la_vista()
    {
        var cifrador = Cifrador(ClaveA);
        const string jwt = "eyJhbGciOiJFUzI1NiJ9.eyJzdWIiOiJ1c3VhcmlvIn0.firma-ñ";

        var cifrado = cifrador.Cifrar(jwt, PropositoDeSecreto.Credencial, Aad);

        Assert.Equal(jwt, cifrador.Descifrar(cifrado.Bytes, cifrado.VersionDeClave, PropositoDeSecreto.Credencial, Aad));
        Assert.DoesNotContain("eyJhbGci", System.Text.Encoding.UTF8.GetString(cifrado.Bytes), StringComparison.Ordinal);
        Assert.Equal(cifrador.VersionVigente, cifrado.VersionDeClave);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-03")]
    public void Cada_cifrado_usa_un_nonce_nuevo_y_dos_cifrados_del_mismo_texto_difieren()
    {
        var cifrador = Cifrador(ClaveA);

        var uno = cifrador.Cifrar("igual", PropositoDeSecreto.Renovacion, Aad);
        var otro = cifrador.Cifrar("igual", PropositoDeSecreto.Renovacion, Aad);

        Assert.NotEqual(uno.Bytes, otro.Bytes);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-03")]
    public void Un_texto_movido_a_otra_fila_u_otro_proposito_o_manipulado_no_se_descifra()
    {
        var cifrador = Cifrador(ClaveA);
        var cifrado = cifrador.Cifrar("secreto", PropositoDeSecreto.Credencial, Aad);

        Assert.Throws<SecretoDeSesionIlegibleException>(() => cifrador.Descifrar(cifrado.Bytes, cifrado.VersionDeClave, PropositoDeSecreto.Credencial, "otra-fila|Credencial|usuario"));
        Assert.Throws<SecretoDeSesionIlegibleException>(() => cifrador.Descifrar(cifrado.Bytes, cifrado.VersionDeClave, PropositoDeSecreto.Renovacion, Aad));
        Assert.Throws<SecretoDeSesionIlegibleException>(() => cifrador.Descifrar(cifrado.Bytes, unchecked((short)(cifrado.VersionDeClave + 1)), PropositoDeSecreto.Credencial, Aad));

        var manipulado = (byte[])cifrado.Bytes.Clone();
        manipulado[^1] ^= 0x01;
        Assert.Throws<SecretoDeSesionIlegibleException>(() => cifrador.Descifrar(manipulado, cifrado.VersionDeClave, PropositoDeSecreto.Credencial, Aad));
        Assert.Throws<SecretoDeSesionIlegibleException>(() => cifrador.Descifrar(cifrado.Bytes[..10], cifrado.VersionDeClave, PropositoDeSecreto.Credencial, Aad));
        Assert.Throws<SecretoDeSesionIlegibleException>(() => cifrador.Descifrar([], cifrado.VersionDeClave, PropositoDeSecreto.Credencial, Aad));
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-03")]
    public void Una_replica_no_descifra_las_filas_del_otro_host_ni_con_la_misma_clave_por_error()
    {
        var tienda = Cifrador(ClaveA, host: HostDeSesion.Tienda);
        var gestion = Cifrador(ClaveB, host: HostDeSesion.Gestion);
        var mismaClaveEnGestion = Cifrador(ClaveA, host: HostDeSesion.Gestion);
        var deTienda = tienda.Cifrar("credencial", PropositoDeSecreto.Credencial, Aad);

        Assert.Throws<SecretoDeSesionIlegibleException>(() => gestion.Descifrar(deTienda.Bytes, deTienda.VersionDeClave, PropositoDeSecreto.Credencial, Aad));
        // Aunque una clave se repitiera por error entre los hosts, el host va en los datos asociados: sus filas no se intercambian.
        Assert.Throws<SecretoDeSesionIlegibleException>(() => mismaClaveEnGestion.Descifrar(deTienda.Bytes, deTienda.VersionDeClave, PropositoDeSecreto.Credencial, Aad));
        Assert.Equal("credencial", tienda.Descifrar(deTienda.Bytes, deTienda.VersionDeClave, PropositoDeSecreto.Credencial, Aad));
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-03")]
    public void La_rotacion_en_dos_pasos_descifra_con_la_anterior_cifra_con_la_vigente_y_al_retirarla_lo_no_recifrado_deja_de_abrirse()
    {
        var antes = Cifrador(ClaveA);
        var viejo = antes.Cifrar("de-la-clave-anterior", PropositoDeSecreto.Borrador, Aad);

        // Paso intermedio: B vigente y A anterior.
        var intermedio = Cifrador(ClaveB, ClaveA);
        Assert.Equal("de-la-clave-anterior", intermedio.Descifrar(viejo.Bytes, viejo.VersionDeClave, PropositoDeSecreto.Borrador, Aad));
        Assert.NotEqual(intermedio.VersionVigente, viejo.VersionDeClave);
        var nuevo = intermedio.Cifrar("de-la-clave-vigente", PropositoDeSecreto.Borrador, Aad);
        Assert.Equal(intermedio.VersionVigente, nuevo.VersionDeClave);

        // Retiro de la anterior: lo cifrado con B sigue abriéndose; lo que no se re-cifró, no.
        var despues = Cifrador(ClaveB);
        Assert.Equal("de-la-clave-vigente", despues.Descifrar(nuevo.Bytes, nuevo.VersionDeClave, PropositoDeSecreto.Borrador, Aad));
        Assert.Throws<SecretoDeSesionIlegibleException>(() => despues.Descifrar(viejo.Bytes, viejo.VersionDeClave, PropositoDeSecreto.Borrador, Aad));
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-03")]
    public void El_arranque_falla_si_la_clave_falta_es_corta_o_casi_no_varia_y_el_mensaje_nombra_la_variable_y_nunca_el_valor()
    {
        var corta = Assert.Throws<ClaveDeSesionesInvalidaException>(() => ConfiguracionDeSesionesWeb.Crear(HostDeSesion.Tienda, "corta-pero-secreta"));
        Assert.Contains("BABELHUB__Web__ClaveDeSesionesTienda: es débil (menos de 32 bytes)", corta.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("corta-pero-secreta", corta.Message, StringComparison.Ordinal);

        var sinVariedad = Assert.Throws<ClaveDeSesionesInvalidaException>(() => ConfiguracionDeSesionesWeb.Crear(HostDeSesion.Gestion, new string('a', 40)));
        Assert.Contains("BABELHUB__Web__ClaveDeSesionesGestion: es débil (casi no varía)", sinVariedad.Message, StringComparison.Ordinal);

        var falta = Assert.Throws<ClaveDeSesionesInvalidaException>(() => ConfiguracionDeSesionesWeb.Leer(new ConfigurationBuilder().Build(), HostDeSesion.Gestion));
        Assert.Equal(["BABELHUB__Web__ClaveDeSesionesGestion: falta"], falta.Problemas);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-03")]
    public void El_arranque_falla_si_la_anterior_repite_a_la_vigente_o_la_clave_se_repite_con_la_del_otro_host()
    {
        var igual = Assert.Throws<ClaveDeSesionesInvalidaException>(() => ConfiguracionDeSesionesWeb.Crear(HostDeSesion.Tienda, ClaveA, ClaveA));
        Assert.Equal(["BABELHUB__Web__ClaveDeSesionesTienda" + "Anterior: es igual a BABELHUB__Web__ClaveDeSesionesTienda"], igual.Problemas);

        var configuracion = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Web:ClaveDeSesionesTienda"] = ClaveA, ["Web:ClaveDeSesionesGestion"] = ClaveA })
            .Build();
        var repetida = Assert.Throws<ClaveDeSesionesInvalidaException>(() => ConfiguracionDeSesionesWeb.Leer(configuracion, HostDeSesion.Tienda));
        Assert.Contains("es igual a BABELHUB__Web__ClaveDeSesionesGestion", repetida.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(ClaveA, repetida.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-03")]
    public void La_configuracion_entrega_sus_secretos_para_que_el_servicio_los_compare_con_los_demas()
    {
        var configuracion = ConfiguracionDeSesionesWeb.Crear(HostDeSesion.Gestion, ClaveB, ClaveA);

        Assert.Equal([("Web:ClaveDeSesionesGestion", ClaveB), ("Web:ClaveDeSesionesGestionAnterior", ClaveA)], configuracion.Secretos);
        Assert.Equal(HostDeSesion.Gestion, configuracion.Host);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-03")]
    public async Task El_registro_de_seguridad_de_la_web_usa_motivos_del_catalogo_y_nunca_lanza()
    {
        var catalogo = CatalogoDeAccionesDeAuditoria.Crear().Obtener(nameof(AccionesAuditables.AccesoDenegado));
        foreach (var motivo in Enum.GetValues<MotivoDeDenegacionWeb>())
        {
            Assert.Contains(RegistroDeSeguridadDeLaWebEnAuditoria.MotivoDelCatalogo(motivo), catalogo.Motivos);
        }

        var espia = new DenegacionesEspia();
        await new RegistroDeSeguridadDeLaWebEnAuditoria(espia, RegistroDeBloqueosPorTextosPendientesTests.SinBloqueos()).AccesoDenegado(MotivoDeDenegacionWeb.SesionDeOtroHost, Guid.NewGuid());
        Assert.Equal("sin-sesion", Assert.Single(espia.Registradas).Motivo);
        Assert.Null(espia.Registradas[0].Ruta);

        espia.Falla = true;
        await new RegistroDeSeguridadDeLaWebEnAuditoria(espia, RegistroDeBloqueosPorTextosPendientesTests.SinBloqueos()).AccesoDenegado(MotivoDeDenegacionWeb.SecretoIlegible, null);
    }

    private sealed class DenegacionesEspia : IDenegacionDeAcceso
    {
        public List<DenegacionDeAcceso> Registradas { get; } = [];

        public bool Falla { get; set; }

        public Task Registrar(DenegacionDeAcceso denegacion, CancellationToken cancelacion = default)
        {
            if (Falla)
            {
                throw new InvalidOperationException("Auditoría caída.");
            }

            Registradas.Add(denegacion);
            return Task.CompletedTask;
        }
    }
}

/// <summary>El cifrado real contra la base (T11): las filas guardan texto cifrado, una clave que cambia no abre sesiones ajenas y la rotación re-cifra al renovar.</summary>
[Collection(ColeccionPostgresDeLaWeb.Nombre)]
public sealed class CifradoDeSesionesEnPostgresTests(PostgresDeLaWebFixture bd) : IAsyncLifetime
{
    private const string ClaveA = "clave-A-de-sesiones-0123456789-abcdefghij";
    private const string ClaveB = "clave-B-de-sesiones-9876543210-jihgfedcba";

    public Task InitializeAsync() => bd.Limpiar();

    public Task DisposeAsync() => Task.CompletedTask;

    private static AesGcmCifradorDeSecretosDeSesion Cifrador(string vigente, string? anterior = null, HostDeSesion host = HostDeSesion.Tienda) =>
        new(ConfiguracionDeSesionesWeb.Crear(host, vigente, anterior));

    [Fact]
    [Trait("Criterio", "AC-WEB-03")]
    public async Task La_fila_no_guarda_la_credencial_ni_la_renovacion_en_claro()
    {
        var e = new EntornoDeSesiones(bd, cifrador: Cifrador(ClaveA));

        await e.Abrir();

        var fila = (await bd.Consultar<string>("SELECT row(t.*)::text FROM web.sesiones_web t"))!;
        Assert.DoesNotContain("jwt-ok-1", fila, StringComparison.Ordinal);
        Assert.DoesNotContain("renov-1", fila, StringComparison.Ordinal);
        Assert.Equal(1L, await bd.Consultar<long>("SELECT count(*) FROM web.sesiones_web WHERE octet_length(credencial_cifrada) > 28 AND get_byte(credencial_cifrada, 0) = 1"));
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-03")]
    public async Task Una_replica_con_otra_clave_no_puede_usar_la_sesion_de_la_otra_y_la_cierra_con_un_evento()
    {
        var tienda = new EntornoDeSesiones(bd, cifrador: Cifrador(ClaveA));
        var cookie = await tienda.Abrir();
        var intruso = new EntornoDeSesiones(bd, cifrador: Cifrador(ClaveB, host: HostDeSesion.Gestion));

        // Aunque la cookie llegue al otro host, ni la fila es suya (host) ni su clave la abre; con la fila forzada a su host, el descifrado falla.
        await bd.Ejecutar($"UPDATE web.sesiones_web SET host = 'gestion' WHERE id_hash = decode('{ValorDeCookieDeSesion.Hash(cookie).Valor}', 'hex')");
        var resultado = await intruso.Web.AsegurarCredencial(cookie, HostWeb.Gestion);

        Assert.Equal(EstadoDeCredencial.SesionCerrada, resultado.Estado);
        Assert.Contains(MotivoDeDenegacionWeb.SecretoIlegible, intruso.Seguridad.Eventos);
        Assert.Equal("cerrada_por_revocacion", await bd.Consultar<string>($"SELECT motivo_de_cierre FROM web.sesiones_web WHERE id_hash = decode('{ValorDeCookieDeSesion.Hash(cookie).Valor}', 'hex')"));
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-03")]
    public async Task Con_la_rotacion_en_curso_la_sesion_se_renueva_y_queda_cifrada_con_la_clave_vigente()
    {
        var antes = new EntornoDeSesiones(bd, cifrador: Cifrador(ClaveA));
        var cookie = await antes.Abrir();
        var cifradorNuevo = Cifrador(ClaveB, ClaveA);
        var despues = new EntornoDeSesiones(bd, cifrador: cifradorNuevo);
        var id = ValorDeCookieDeSesion.Hash(cookie);
        Assert.NotEqual(cifradorNuevo.VersionVigente, (await despues.Sesiones.Cargar(id))!.VersionDeClave);

        var resultado = await despues.Web.AsegurarCredencial(cookie, HostWeb.Tienda);

        Assert.Equal(EstadoDeCredencial.Asegurada, resultado.Estado);
        Assert.Equal(1, despues.Identidad.LlamadasARenovar);
        Assert.Equal(cifradorNuevo.VersionVigente, (await despues.Sesiones.Cargar(id))!.VersionDeClave);

        // Una fila que ya está con la clave vigente no se renueva por esto.
        await despues.Web.AsegurarCredencial(cookie, HostWeb.Tienda);
        Assert.Equal(1, despues.Identidad.LlamadasARenovar);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-03")]
    public async Task Al_retirar_la_clave_anterior_una_sesion_no_recifrada_se_cierra_limpiamente_y_con_un_evento()
    {
        var antes = new EntornoDeSesiones(bd, cifrador: Cifrador(ClaveA));
        var cookie = await antes.Abrir();
        var despues = new EntornoDeSesiones(bd, cifrador: Cifrador(ClaveB));

        var resultado = await despues.Web.AsegurarCredencial(cookie, HostWeb.Tienda);

        Assert.Equal(EstadoDeCredencial.SesionCerrada, resultado.Estado);
        Assert.Contains(MotivoDeDenegacionWeb.SecretoIlegible, despues.Seguridad.Eventos);
        Assert.Equal(EstadoDeCarga.Cerrada, (await despues.Web.Cargar(cookie, HostWeb.Tienda, OrigenDeLaAccion.AccionDelUsuario)).Estado);
    }
}
