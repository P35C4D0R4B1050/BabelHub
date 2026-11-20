using BabelHub.Ecommerce.Application.Biblioteca.Interno;
using BabelHub.Ecommerce.Application.Catalogo;
using BabelHub.Ecommerce.UnitTests.Auditoria;
using BabelHub.Ecommerce.UnitTests.Carrito;
using BabelHub.Ecommerce.UnitTests.Catalogo;
using BabelHub.Ecommerce.UnitTests.Envios;
using BabelHub.Ecommerce.UnitTests.Plataforma;
using BabelHub.Ecommerce.UnitTests.Soporte;

namespace BabelHub.Ecommerce.UnitTests.Biblioteca;

/// <summary>El entorno de las pruebas de Biblioteca: los casos de uso reales con repositorios y contratos de otros módulos en memoria. Crece con cada tarea.</summary>
internal sealed class EntornoDeBiblioteca
{
    public static readonly DateTimeOffset AhoraInicial = new(2026, 11, 20, 15, 0, 0, TimeSpan.Zero);

    public EntornoDeBiblioteca(ActorDePrueba? actor = null, DateTimeOffset? ahora = null)
    {
        Actor = actor ?? ActorDePrueba.DeCliente();
        Reloj = new RelojFijo(ahora ?? AhoraInicial);
        Accesos = new RepositorioDeAccesosEnMemoria();
        Descargas = new RepositorioDeDescargasEnMemoria(Accesos);
        Descargas.Enlazar();
        Auditoria = new AuditoriaDeEnviosEspia();
        Unidad = new UnidadDeTrabajoReversible(Accesos, Descargas, Auditoria);
        Denegaciones = new DenegacionesDelCatalogoEspia();
        Cuentas = new EstadoDeCuentaFalso();
        Catalogo = new CatalogoDeCarritoFalso();
        Referencias = new ReferenciaDeArchivosFalsa();
        Entrega = new EntregaDeArchivosDoble(Unidad);
        Limites = new LimitesDeBibliotecaDoble();
        Alertas = new AlertasTecnicasEspia();

        Autorizador = new AutorizadorDeBiblioteca(Actor, Denegaciones, Cuentas);
        Proyector = new ProyectorDeAccesos(Catalogo, Referencias, Descargas);
        Listar = new ListarMiBiblioteca(Autorizador, Accesos, Proyector);
        Obtener = new ObtenerAcceso(Autorizador, Accesos, Proyector);
        ConsultarDeUnCliente = new ConsultarBibliotecaDeUnCliente(Autorizador, Accesos, Proyector, Auditoria, Unidad);
    }

    public ActorDePrueba Actor { get; }

    public RelojFijo Reloj { get; }

    public RepositorioDeAccesosEnMemoria Accesos { get; }

    public RepositorioDeDescargasEnMemoria Descargas { get; }

    public AuditoriaDeEnviosEspia Auditoria { get; }

    public UnidadDeTrabajoReversible Unidad { get; }

    public DenegacionesDelCatalogoEspia Denegaciones { get; }

    public EstadoDeCuentaFalso Cuentas { get; }

    public CatalogoDeCarritoFalso Catalogo { get; }

    public ReferenciaDeArchivosFalsa Referencias { get; }

    public EntregaDeArchivosDoble Entrega { get; }

    public LimitesDeBibliotecaDoble Limites { get; }

    public AlertasTecnicasEspia Alertas { get; }

    public AutorizadorDeBiblioteca Autorizador { get; }

    public ProyectorDeAccesos Proyector { get; }

    public ListarMiBiblioteca Listar { get; }

    public ObtenerAcceso Obtener { get; }

    public ConsultarBibliotecaDeUnCliente ConsultarDeUnCliente { get; }

    /// <summary>Un libro digital en el catálogo con su archivo y un acceso sembrado para la cuenta del actor (o la dada).</summary>
    public Domain.Biblioteca.Acceso SembrarLibro(string titulo, Guid? cuenta = null, DateTimeOffset? compra = null, int descargas = 0, FormatoDeArchivoDigital formato = FormatoDeArchivoDigital.Epub)
    {
        var edicion = Guid.NewGuid();
        Catalogo.Poner(edicion, FormatoDeEdicion.Digital, 25m, titulo);
        Referencias.Poner(edicion, $"libros/{edicion:N}.epub", formato);
        var acceso = Accesos.Sembrar(cuenta ?? Actor.Actual!.UsuarioId!.Value, edicion, null, compra);
        for (var i = 0; i < descargas; i++)
        {
            acceso.ConsumirDescarga();
        }

        return acceso;
    }
}
