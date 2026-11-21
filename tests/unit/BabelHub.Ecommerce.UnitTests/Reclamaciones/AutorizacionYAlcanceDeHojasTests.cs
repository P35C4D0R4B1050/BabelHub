using BabelHub.Ecommerce.UnitTests.Catalogo;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Reclamaciones;
using BabelHub.Ecommerce.Application.Reclamaciones.Interno;
using BabelHub.Ecommerce.UnitTests.Soporte;

namespace BabelHub.Ecommerce.UnitTests.Reclamaciones;

/// <summary>
/// La matriz rol × operación de las hojas y el orden fijo sesión → rol → hoja y alcance → estado (T09; REC-10, REC-18): el visitante recibe «no autenticado» sin consultar nada; el encargado y la logística nunca ven datos de
/// hojas; la hoja ajena, inexistente o de una cuenta cancelada da el mismo «no encontrada» al consumidor, y la administración sí ve la de una cuenta cancelada; cada intento de una sesión deja un acceso denegado.
/// </summary>
public sealed class AutorizacionYAlcanceDeHojasTests
{
    private static readonly Guid Usuario = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    public enum Rol
    {
        Visitante,
        Cliente,
        Encargado,
        Logistica,
        Administrador,
    }

    public enum Operacion
    {
        Listar,
        Obtener,
        Responder,
        Descartar,
        ReintentarAviso,
        MisHojas,
        MiHoja,
    }

    private static ActorDePrueba Actor(Rol rol) => rol switch
    {
        Rol.Visitante => ActorDePrueba.Anonimo(),
        Rol.Cliente => ActorDePrueba.DeCliente(Usuario),
        Rol.Encargado => ActorDePrueba.DeEncargadoDe(Guid.NewGuid()),
        Rol.Logistica => ActorDePrueba.DePersonalDeLogistica(),
        _ => ActorDePrueba.DeAdministradorCentral(),
    };

    /// <summary>Un entorno con una hoja ya registrada, visto por el actor del rol (con administración y consumidor sobre las mismas hojas).</summary>
    private sealed class Mundo
    {
        public required EntornoDeConsumidor Consumidor { get; init; }

        public required EntornoDeReclamaciones Administracion { get; init; }

        public required Guid HojaId { get; init; }

        public required DenegacionesDelCatalogoEspia Denegaciones { get; init; }

        public required IHojasDelConsumidor Mis { get; init; }
    }

    private static async Task<Mundo> Armar(Rol rol)
    {
        var consumidor = EntornoDeConsumidor.Nuevo(Usuario, "ana.perez@correo.com");
        var id = await consumidor.RegistrarUna();
        var admin = new EntornoDeReclamaciones(Actor(rol));
        admin.Hojas.Sembrar(consumidor.Hojas.Todas.Single());
        var autorizador = new AutorizadorDeReclamaciones(Actor(rol), admin.Denegaciones);
        var limitador = new LimiteDeConsultaDeHojas(admin.Limites);
        var correos = new CorreoVerificadoDoble();
        correos.Fijar(Usuario, "ana.perez@correo.com");
        var mis = new HojasDelConsumidorDeLaAplicacion(new ListarMisHojas(autorizador, correos, admin.Vinculador, admin.Hojas, admin.Unidad, limitador), new ObtenerMiHoja(autorizador, correos, admin.Vinculador, admin.Hojas, admin.Unidad, limitador));
        return new Mundo { Consumidor = consumidor, Administracion = admin, HojaId = id, Denegaciones = admin.Denegaciones, Mis = mis };
    }

    private static async Task<CategoriaDeError?> Ejecutar(Mundo m, Operacion operacion)
    {
        ErrorDeAplicacion? error = operacion switch
        {
            Operacion.Listar => (await m.Administracion.Administracion.Listar(new ListarHojasEntrada())).Error,
            Operacion.Obtener => (await m.Administracion.Administracion.Obtener(m.HojaId)).Error,
            Operacion.Responder => (await m.Administracion.Administracion.Responder(new ResponderHojaEntrada(m.HojaId, "Atendido."))).Error,
            Operacion.Descartar => (await m.Administracion.Administracion.Descartar(m.HojaId)).Error,
            Operacion.ReintentarAviso => (await m.Administracion.Administracion.ReintentarAviso(m.HojaId)).Error,
            Operacion.MisHojas => (await m.Mis.Listar(new ListarMisHojasEntrada())).Error,
            _ => (await m.Mis.Obtener("2026-000001")).Error,
        };
        return error?.Categoria;
    }

    public static TheoryData<Rol, Operacion, CategoriaDeError?> Matriz()
    {
        var datos = new TheoryData<Rol, Operacion, CategoriaDeError?>();
        foreach (var operacion in new[] { Operacion.Listar, Operacion.Obtener, Operacion.Responder, Operacion.Descartar })
        {
            datos.Add(Rol.Visitante, operacion, CategoriaDeError.NoAutenticado);
            datos.Add(Rol.Cliente, operacion, CategoriaDeError.Prohibido);
            datos.Add(Rol.Encargado, operacion, CategoriaDeError.Prohibido);
            datos.Add(Rol.Logistica, operacion, CategoriaDeError.Prohibido);
            datos.Add(Rol.Administrador, operacion, null);
        }

        datos.Add(Rol.Visitante, Operacion.ReintentarAviso, CategoriaDeError.NoAutenticado);
        datos.Add(Rol.Cliente, Operacion.ReintentarAviso, CategoriaDeError.Prohibido);
        datos.Add(Rol.Encargado, Operacion.ReintentarAviso, CategoriaDeError.Prohibido);
        datos.Add(Rol.Logistica, Operacion.ReintentarAviso, CategoriaDeError.Prohibido);

        foreach (var operacion in new[] { Operacion.MisHojas, Operacion.MiHoja })
        {
            datos.Add(Rol.Visitante, operacion, CategoriaDeError.NoAutenticado);
            datos.Add(Rol.Cliente, operacion, null);
            datos.Add(Rol.Encargado, operacion, CategoriaDeError.Prohibido);
            datos.Add(Rol.Logistica, operacion, CategoriaDeError.Prohibido);
            datos.Add(Rol.Administrador, operacion, CategoriaDeError.Prohibido);
        }

        return datos;
    }

    [Theory]
    [MemberData(nameof(Matriz))]
    [Trait("Criterio", "AC-REC-16")]
    public async Task La_matriz_rol_por_operacion_responde_lo_que_fija_el_plan(Rol rol, Operacion operacion, CategoriaDeError? esperado)
    {
        var m = await Armar(rol);

        var categoria = await Ejecutar(m, operacion);

        Assert.Equal(esperado, categoria);
    }

    [Theory]
    [Trait("Criterio", "AC-REC-16")]
    [InlineData(Rol.Encargado)]
    [InlineData(Rol.Logistica)]
    [InlineData(Rol.Cliente)]
    public async Task Cada_intento_prohibido_deja_su_propio_acceso_denegado(Rol rol)
    {
        var m = await Armar(rol);

        await Ejecutar(m, Operacion.Obtener);
        await Ejecutar(m, Operacion.Obtener);
        await Ejecutar(m, Operacion.Listar);

        Assert.Equal(3, m.Denegaciones.Eventos.Count);
        Assert.Empty(m.Administracion.Auditoria.Solicitudes);
    }

    [Theory]
    [Trait("Criterio", "AC-REC-16")]
    [InlineData(Operacion.MisHojas)]
    [InlineData(Operacion.MiHoja)]
    public async Task El_visitante_no_consulta_nada_ni_genera_evento_en_las_rutas_del_consumidor(Operacion operacion)
    {
        var m = await Armar(Rol.Visitante);
        var antes = m.Administracion.Hojas.Operaciones;

        await Ejecutar(m, operacion);

        Assert.Equal(antes, m.Administracion.Hojas.Operaciones);
        Assert.Empty(m.Denegaciones.Eventos);
        Assert.Equal(0, m.Administracion.Limites.Consultas);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-18")]
    public async Task La_hoja_de_una_cuenta_cancelada_da_no_encontrado_al_consumidor_y_la_administracion_la_sigue_viendo()
    {
        var m = await Armar(Rol.Cliente);
        var hoja = m.Administracion.Hojas.Todas.Single();
        hoja.MarcarDeCuentaCancelada();
        m.Administracion.Hojas.Sembrar(hoja);
        var admin = new EntornoDeReclamaciones(ActorDePrueba.DeAdministradorCentral());
        admin.Hojas.Sembrar(hoja);

        var consumidor = await Ejecutar(m, Operacion.MiHoja);
        var vistaAdmin = await admin.Administracion.Obtener(m.HojaId);

        Assert.Equal(CategoriaDeError.NoEncontrado, consumidor);
        Assert.True(vistaAdmin.EsExito);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-08")]
    public async Task El_orden_es_sesion_rol_hoja_y_estado_una_hoja_ya_respondida_no_cambia_lo_que_ve_otro_rol()
    {
        var m = await Armar(Rol.Encargado);

        var responder = await Ejecutar(m, Operacion.Responder);

        // El rol se comprueba antes que la hoja y su estado: ni se lee la hoja.
        Assert.Equal(CategoriaDeError.Prohibido, responder);
        Assert.Equal(0, m.Administracion.Hojas.LecturasConBloqueo);
    }

    // ---------- Límites de consulta ----------

    [Fact]
    [Trait("Criterio", "AC-REC-08")]
    public async Task Las_consultas_del_consumidor_pasan_por_el_limite_de_60_por_usuario_y_minuto_y_el_exceso_es_429_con_su_espera()
    {
        var m = await Armar(Rol.Cliente);
        m.Administracion.Limites.MaximoDeConsultas = 2;

        var primera = await Ejecutar(m, Operacion.MisHojas);
        var segunda = await Ejecutar(m, Operacion.MiHoja);
        var tercera = await m.Mis.Listar(new ListarMisHojasEntrada());

        Assert.Null(primera);
        Assert.Null(segunda);
        Assert.Equal(CategoriaDeError.Limite, tercera.Error!.Categoria);
        Assert.Equal(ClavesDeReclamaciones.LimiteExcedido, tercera.Error.Clave);
        Assert.Contains(tercera.Error.Errores!, c => c.Campo == ClavesDeReclamaciones.EsperaEnSegundos);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-08")]
    public async Task Un_limite_excedido_no_consulta_las_hojas()
    {
        var m = await Armar(Rol.Cliente);
        m.Administracion.Limites.MaximoDeConsultas = 0;
        var antes = m.Administracion.Hojas.Operaciones;

        await Ejecutar(m, Operacion.MisHojas);

        Assert.Equal(antes, m.Administracion.Hojas.Operaciones);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-08")]
    public async Task Si_el_motor_de_limites_no_responde_la_consulta_es_servicio_no_disponible()
    {
        var m = await Armar(Rol.Cliente);
        m.Administracion.Limites.Caido = true;

        var categoria = await Ejecutar(m, Operacion.MisHojas);

        Assert.Equal(CategoriaDeError.NoDisponible, categoria);
    }
}
