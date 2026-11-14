namespace BabelHub.Ecommerce.Application.Plataforma;

/// <summary>Qué se hizo con la cuenta: se cancela o se anonimiza (PLA-37). Modelo propio del contrato: no expone tipos del Dominio.</summary>
public enum ClaseDeSupresion
{
    Cancelacion,
    Anonimizacion,
}

/// <summary>La intención se agrega antes de completar la supresión y el cierre al completarla (PLA-37).</summary>
public enum EtapaDeSupresion
{
    Intencion,
    Cierre,
}

/// <summary>Una entrada del registro de supresiones: solo el identificador opaco de la cuenta, nunca datos personales.</summary>
/// <param name="YaExistia">Verdadero si la entrada ya estaba escrita: repetirla es éxito (clave determinista) y trae la fecha de la original.</param>
public sealed record EntradaDeSupresionDto(
    string IdentificadorOpaco,
    ClaseDeSupresion Clase,
    EtapaDeSupresion Etapa,
    DateTimeOffset Fecha,
    string Suma,
    bool YaExistia);

/// <summary>El registro de supresiones no responde: la supresión no se completa si no puede agregarse la intención (PLA-37).</summary>
public sealed class RegistroDeSupresionesNoDisponibleException : Exception
{
    public RegistroDeSupresionesNoDisponibleException()
    {
    }

    public RegistroDeSupresionesNoDisponibleException(string message)
        : base(message)
    {
    }

    public RegistroDeSupresionesNoDisponibleException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// El registro de supresiones, que vive fuera de las bases respaldadas y solo admite agregar (PLA-38). La clave de cada entrada es
/// determinista, de modo que repetir una escritura es idempotente y dos instancias de Identidad no necesitan coordinarse.
/// </summary>
public interface IRegistroDeSupresiones
{
    /// <summary>
    /// Agrega la entrada con la fecha indicada. Si ya existía, no la sobrescribe y devuelve la original con <c>YaExistia</c> en verdadero.
    /// Lanza <see cref="RegistroDeSupresionesNoDisponibleException"/> si el almacén no responde.
    /// </summary>
    Task<EntradaDeSupresionDto> Agregar(string identificadorOpaco, ClaseDeSupresion clase, EtapaDeSupresion etapa, DateTimeOffset fecha, CancellationToken cancelacion = default);

    /// <summary>La entrada de una cuenta, clase y etapa, o <c>null</c> si no existe.</summary>
    Task<EntradaDeSupresionDto?> Obtener(string identificadorOpaco, ClaseDeSupresion clase, EtapaDeSupresion etapa, CancellationToken cancelacion = default);

    /// <summary>
    /// Todas las entradas del registro tal como las devuelve el almacén (con su clave y su hora de modificación en él), para verificarlas
    /// y reaplicarlas al restaurar. Lanza <see cref="RegistroDeSupresionesNoDisponibleException"/> si el registro no se puede leer.
    /// </summary>
    Task<IReadOnlyList<EntradaLeidaDto>> LeerConMetadatos(CancellationToken cancelacion = default);
}

/// <summary>Una entrada tal como la devuelve el almacén: su clave real y su hora de modificación en él (no la que diga su contenido).</summary>
public sealed record EntradaLeidaDto(string ClaveEnElAlmacen, EntradaDeSupresionDto Entrada, DateTimeOffset ModificadaEn);

/// <summary>Lo que dejó una reaplicación de supresiones al restaurar (PLA-19): solo identificadores opacos y contadores.</summary>
/// <param name="Reaplicadas">Cuentas cuyo cierre se volvió a aplicar y que estaban activas en el respaldo.</param>
/// <param name="YaEstabanAplicadas">Cuentas cuyo cierre ya estaba aplicado en el respaldo: es éxito, no hubo nada que hacer.</param>
/// <param name="IntencionesSinCierre">Cuentas con intención y sin cierre (supresión no completada): siguen activas y se informan a la persona responsable.</param>
/// <param name="CierresSinIntencion">Cierres que no tienen intención previa: no se reaplican y se informan.</param>
/// <param name="Fallidas">Cuentas cuyo cierre no pudo aplicarse: el servicio no se abre hasta resolverlo.</param>
/// <param name="RequiereConfirmacion">Hay más entradas nuevas que el umbral configurable respecto del conteo sellado: no se reaplicó nada sin confirmación explícita.</param>
/// <param name="ConfirmadaPorUmbral">Se reaplicó porque la persona responsable confirmó explícitamente un conteo por encima del umbral.</param>
public sealed record InformeDeReaplicacion(
    int Reaplicadas,
    int YaEstabanAplicadas,
    IReadOnlyList<string> IntencionesSinCierre,
    IReadOnlyList<string> CierresSinIntencion,
    IReadOnlyList<string> Fallidas,
    bool RequiereConfirmacion,
    bool ConfirmadaPorUmbral)
{
    /// <summary>El servicio puede abrirse: no quedó nada fallido ni pendiente de confirmar. Las intenciones sin cierre solo se informan.</summary>
    public bool PermiteAbrir => !RequiereConfirmacion && Fallidas.Count == 0;

    /// <summary>
    /// Solo contadores, para la constancia versionada de <c>progress/ensayos/</c>: nunca un identificador (PLA-19, Ley 29733). El
    /// detalle con identificadores sale solo por pantalla y en un archivo local fuera del repositorio (<see cref="Detalle"/>).
    /// </summary>
    public string Contadores() =>
        string.Join('\n',
            $"reaplicadas={Reaplicadas}",
            $"ya_aplicadas={YaEstabanAplicadas}",
            $"intenciones_sin_cierre={IntencionesSinCierre.Count}",
            $"cierres_sin_intencion={CierresSinIntencion.Count}",
            $"fallidas={Fallidas.Count}",
            $"confirmaciones_por_umbral={(ConfirmadaPorUmbral || RequiereConfirmacion ? 1 : 0)}");

    /// <summary>El informe para la persona responsable, con los identificadores opacos: va por pantalla y a un archivo local, nunca al repositorio.</summary>
    public string Detalle() =>
        string.Join('\n', new[]
        {
            Contadores(),
            Lista("intenciones sin cierre (siguen activas)", IntencionesSinCierre),
            Lista("cierres sin intención (no se reaplicaron)", CierresSinIntencion),
            Lista("cierres que no pudieron aplicarse", Fallidas),
        }.Where(l => l.Length > 0));

    private static string Lista(string titulo, IReadOnlyList<string> identificadores) =>
        identificadores.Count == 0 ? string.Empty : titulo + ":\n" + string.Join('\n', identificadores.Select(i => "  " + i));
}

/// <summary>Vuelve a aplicar, de forma idempotente, las supresiones con cierre antes de abrir el servicio restaurado (PLA-19).</summary>
public interface IReaplicadorDeSupresiones
{
    Task<InformeDeReaplicacion> Reaplicar(CancellationToken cancelacion = default);
}

public enum ResultadoDeAplicacionDeSupresion
{
    Aplicada,

    /// <summary>La cuenta ya estaba suprimida o anonimizada: es éxito (idempotente).</summary>
    YaEstabaAplicada,

    /// <summary>No se pudo aplicar: el cierre queda pendiente y se reintenta sin deshacer nada (PLA-41).</summary>
    Fallida,
}

/// <summary>Lo implementa Identidad: anonimiza o cancela la cuenta y reenvía la baja al Inventario (IDE-75). Idempotente.</summary>
public interface IAplicadorDeSupresion
{
    Task<ResultadoDeAplicacionDeSupresion> Aplicar(string identificadorOpaco, ClaseDeSupresion clase, CancellationToken cancelacion = default);
}

/// <summary>Lo implementa Identidad: tras restaurar, rota la clave de firma e invalida las sesiones y renovaciones, también las de la capa servidor de la Web (PLA-43).</summary>
public interface IRestauracionDeIdentidad
{
    Task<ResultadoDePaso> RotarClavesEInvalidarSesiones(CancellationToken cancelacion = default);
}
