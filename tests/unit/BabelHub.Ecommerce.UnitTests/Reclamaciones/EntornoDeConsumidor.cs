using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Reclamaciones;
using BabelHub.Ecommerce.Application.Reclamaciones.Interno;
using BabelHub.Ecommerce.UnitTests.Catalogo;
using BabelHub.Ecommerce.UnitTests.Soporte;

namespace BabelHub.Ecommerce.UnitTests.Reclamaciones;

/// <summary>El correo verificado de las cuentas (Identidad): lo que la prueba fije, o una falla, y cuántas veces lo consultaron.</summary>
internal sealed class CorreoVerificadoDoble : ICorreoVerificadoDeLaCuenta
{
    private readonly Dictionary<Guid, string?> _correos = [];

    public bool Falla { get; set; }

    public int Consultas { get; private set; }

    public void Fijar(Guid usuarioId, string? correo) => _correos[usuarioId] = correo;

    public Task<string?> De(Guid usuarioId, CancellationToken cancelacion = default)
    {
        Consultas++;
        return Falla ? throw new InvalidOperationException("Identidad no responde.") : Task.FromResult(_correos.GetValueOrDefault(usuarioId));
    }
}

/// <summary>
/// El módulo visto por un consumidor con sesión (un cliente) y, a su lado, la administración central que responde y descarta, sobre las mismas hojas, límites y auditoría. Sirve para las pruebas de «mis
/// hojas», del conteo de pendientes y de la cancelación de la cuenta.
/// </summary>
internal sealed class EntornoDeConsumidor
{
    private EntornoDeConsumidor(Guid usuarioId, string? correoVerificado, DateTimeOffset? ahora)
    {
        UsuarioId = usuarioId;
        Base = new EntornoDeReclamaciones(ActorDePrueba.DeAdministradorCentral(), ahora);
        Correos = new CorreoVerificadoDoble();
        Correos.Fijar(usuarioId, correoVerificado);
        Actor = ActorDePrueba.DeCliente(usuarioId);
        Autorizador = new AutorizadorDeReclamaciones(Actor, Base.Denegaciones);
        var listar = new ListarMisHojas(Autorizador, Correos, Base.Hojas, Base.Unidad);
        var obtener = new ObtenerMiHoja(Autorizador, Correos, Base.Hojas, Base.Unidad);
        Consumidor = new HojasDelConsumidorDeLaAplicacion(listar, obtener);
    }

    public static EntornoDeConsumidor Nuevo(Guid usuarioId, string? correoVerificado, DateTimeOffset? ahora = null) => new(usuarioId, correoVerificado, ahora);

    public Guid UsuarioId { get; }

    public EntornoDeReclamaciones Base { get; }

    public CorreoVerificadoDoble Correos { get; }

    public ActorDePrueba Actor { get; }

    public AutorizadorDeReclamaciones Autorizador { get; }

    public IHojasDelConsumidor Consumidor { get; }

    public RepositorioDeHojasEnMemoria Hojas => Base.Hojas;

    public DenegacionesDelCatalogoEspia Denegaciones => Base.Denegaciones;

    public IAdministracionDeReclamaciones Administrar => Base.Administracion;

    public Task<Guid> RegistrarUna() => Base.RegistrarUna();

    /// <summary>Registra una hoja válida con otro correo de contacto (la de otra persona).</summary>
    public async Task<Guid> RegistrarDeOtroCorreo(string correo)
    {
        var resultado = await Base.Reclamaciones.Registrar(Base.Entrada() with { Correo = correo });
        Assert.True(resultado.EsExito);
        return Hojas.Todas.Single(h => h.Numero.ToString() == resultado.Valor.Numero).Id;
    }
}
