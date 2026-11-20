using BabelHub.Ecommerce.Application.Catalogo;
using BabelHub.Ecommerce.Application.Plataforma;

namespace BabelHub.Ecommerce.Application.Biblioteca;

// Los contratos públicos de Biblioteca (plan §2 y §4). Modelos propios: ninguno expone el Dominio ni el interior. Las firmas ya declaradas por otros módulos se adoptan sin cambiarlas:
// `IAccesosDeBiblioteca` (Pedidos), `IPosesionDeEdiciones` (Carrito, en el espacio de nombres de Carrito) e `IDescargasDeLaBiblioteca` (web-base). `IAccesosParaDevoluciones` es de este módulo.

/// <summary>El estado de un acceso para quien lo ve (BIB-02, BIB-10).</summary>
public enum EstadoDelAccesoDigital
{
    Vigente,
    Suspendido,
    Revocado,
}

/// <summary>Un libro de la biblioteca de una cuenta (BIB-02): título y formato del archivo (de Catálogo), fecha de compra, estado y descargas usadas y restantes («usadas de 5»).</summary>
public sealed record AccesoDeBiblioteca(
    Guid AccesoId,
    Guid EdicionId,
    string Titulo,
    FormatoDeArchivoDigital? Formato,
    DateTimeOffset FechaDeCompra,
    EstadoDelAccesoDigital Estado,
    int DescargasUsadas,
    int DescargasMaximas,
    int DescargasRestantes,
    bool TieneAyudaAbierta);

public sealed record PaginaDeAccesos(IReadOnlyList<AccesoDeBiblioteca> Accesos, int Pagina, int Tamano, int Total);

public enum EstadoDeLaAyudaDeBiblioteca
{
    Abierta,
    Cerrada,
}

/// <summary>Una solicitud de ayuda (BIB-16): el acceso, el motivo de lista cerrada y la fecha. <b>Sin datos de la cuenta</b> (RNF23).</summary>
public sealed record AyudaDeBiblioteca(Guid AyudaId, Guid AccesoId, string Motivo, EstadoDeLaAyudaDeBiblioteca Estado, DateTimeOffset CreadaEn, DateTimeOffset? CerradaEn);

public sealed record PaginaDeAyudas(IReadOnlyList<AyudaDeBiblioteca> Ayudas, int Pagina, int Tamano, int Total);

/// <summary>Las listas cerradas de motivos que la interfaz ofrece (D06): sin texto libre.</summary>
public static class MotivosDeBiblioteca
{
    public static IReadOnlyList<string> Ayuda { get; } = ["archivo-no-abre", "archivo-no-disponible", "descargas-agotadas", "otro"];

    public static IReadOnlyList<string> Restitucion { get; } = ["descarga-fallida", "archivo-defectuoso", "error-del-sistema", "otro"];
}

public sealed record RestituirDescargasEntrada(Guid AccesoId, int Cantidad, string Motivo, Guid? AyudaId = null);

/// <summary>La biblioteca del cliente (BIB-02, BIB-12): solo lo del actor (la cuenta sale de <c>IActorActual</c>, nunca del llamador).</summary>
public interface IBibliotecaDelCliente
{
    Task<Resultado<PaginaDeAccesos>> Listar(int pagina, int tamano, CancellationToken cancelacion = default);

    /// <summary>Un acceso propio; uno ajeno o inexistente responde lo mismo (<c>ECO-NO-ENCONTRADO</c>) con un acceso denegado registrado.</summary>
    Task<Resultado<AccesoDeBiblioteca>> Obtener(Guid accesoId, CancellationToken cancelacion = default);
}

/// <summary>Pedir ayuda sobre un acceso propio vigente o suspendido (BIB-16): una abierta por acceso; pedirla de nuevo devuelve la existente.</summary>
public interface IAyudaDeLaBiblioteca
{
    Task<Resultado<AyudaDeBiblioteca>> Pedir(Guid accesoId, string motivo, CancellationToken cancelacion = default);
}

/// <summary>La administración central de la biblioteca (BIB-08, BIB-12): solo el administrador central, comprobado en cada operación.</summary>
public interface IAdministracionDeLaBiblioteca
{
    /// <summary>La biblioteca de un cliente; <b>cada consulta se audita</b> en la misma transacción y, si no se puede auditar, no se muestra (D07).</summary>
    Task<Resultado<IReadOnlyList<AccesoDeBiblioteca>>> ConsultarBibliotecaDeUnCliente(Guid cuentaId, CancellationToken cancelacion = default);

    Task<Resultado<PaginaDeAyudas>> ListarAyudas(EstadoDeLaAyudaDeBiblioteca? estado, int pagina, int tamano, CancellationToken cancelacion = default);

    /// <summary>Devuelve de 1 hasta las usadas, con motivo de lista cerrada, y audita en la misma transacción (BIB-08). Si trae <c>AyudaId</c>, comprueba que sea del mismo acceso antes de cerrarla.</summary>
    Task<Resultado<AccesoDeBiblioteca>> RestituirDescargas(RestituirDescargasEntrada entrada, CancellationToken cancelacion = default);

    Task<Resultado<AyudaDeBiblioteca>> CerrarAyuda(Guid ayudaId, CancellationToken cancelacion = default);
}
