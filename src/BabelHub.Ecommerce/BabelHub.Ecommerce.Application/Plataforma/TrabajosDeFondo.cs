namespace BabelHub.Ecommerce.Application.Plataforma;

/// <summary>Resultado enumerado de una corrida (nunca texto libre: no puede arrastrar datos personales ni internos).</summary>
public enum ResultadoDeTrabajo
{
    Exito,
    Fallo,
}

/// <summary>
/// Registro de la última corrida de cada trabajo de fondo (<c>plataforma.trabajos_de_fondo</c>). Lo definen aquí los trabajos
/// de <c>transversal-seguridad</c>, que se construye primero; el adaptador de base de datos es de <c>transversal-plataforma</c>.
/// </summary>
public interface IRegistroDeTrabajosDeFondo
{
    Task Registrar(string trabajo, ResultadoDeTrabajo resultado, DateTimeOffset instante, CancellationToken cancelacion = default);
}
