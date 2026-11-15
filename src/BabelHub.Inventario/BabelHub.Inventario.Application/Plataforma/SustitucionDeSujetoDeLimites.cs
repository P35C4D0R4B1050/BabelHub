namespace BabelHub.Inventario.Application.Plataforma;

/// <summary>
/// Sustituye el sujeto (el usuario) de las claves de los eventos de límite ya guardados por otro identificador: lo usa la baja de un titular (SEL-34, SEL-35) para que el identificador original
/// desaparezca de <c>plataforma.eventos_limite</c> <b>sin cambiar el conteo</b> (los eventos siguen contando para el origen y para el cliente anónimo). Es un puerto aparte de <see cref="IControlDeLimites"/>
/// porque solo existe en el Inventario (el motor del E-commerce no lo necesita y su motor local es copia vigilada del de este servicio).
/// </summary>
public interface ISustitutorDeSujetoDeLimites
{
    /// <summary>Reemplaza <paramref name="sujetoActual"/> por <paramref name="sujetoNuevo"/> en las claves de los eventos de las políticas por usuario. Devuelve cuántos eventos cambió; repetirlo cambia cero.</summary>
    Task<int> Sustituir(string sujetoActual, string sujetoNuevo, CancellationToken cancelacion = default);
}
