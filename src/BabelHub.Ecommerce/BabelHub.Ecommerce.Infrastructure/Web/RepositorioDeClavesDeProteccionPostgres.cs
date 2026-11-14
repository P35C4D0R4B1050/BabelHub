using System.Xml.Linq;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Web.Interno;
using BabelHub.Ecommerce.Domain.Web;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace BabelHub.Ecommerce.Infrastructure.Web;

/// <summary>
/// <see cref="IXmlRepository"/> de Data Protection sobre <c>web.claves_de_proteccion</c> (T12, plan §3 y §6): el anillo de claves del antiforgery vive en la base, compartido por
/// las réplicas <b>de un mismo host</b> (una fila lleva su <c>host</c>; cada réplica lee y escribe solo las de su host) y <b>cifrado con la clave maestra de ese host</b> (subclave del
/// propósito <see cref="PropositoDeSecreto.ClavesDeProteccion"/>, datos asociados ligados a la fila): un volcado de la base no entrega las claves de firma del antiforgery. Cada
/// operación corre bajo <c>rol_web</c> con la unidad de trabajo en curso (<c>EjecutarComo</c>).
/// </summary>
/// <remarks>
/// <para>
/// Un anillo por host: lo que una réplica <c>tienda</c> escribe no lo lee una <c>gestion</c> y viceversa, y como cada una descifra con su clave, tampoco podría. Una fila cifrada con
/// una clave que ya no está (la anterior se retiró) se omite y se cuenta, sin lanzar: Data Protection crea una clave nueva y los formularios abiertos piden otro token. Una fila
/// cifrada con la clave anterior se <b>re-cifra con la vigente al leerla</b> (solo cuando su versión difiere), de modo que retirar la anterior no deja el anillo ilegible.
/// </para>
/// <para>
/// Data Protection conoce este repositorio como cifrado por la propia implementación; no registra un cifrador de XML propio, así que escribe una advertencia genérica de «XML sin
/// cifrar» que aquí no aplica (el texto que llega a la base va cifrado).
/// </para>
/// </remarks>
public sealed class RepositorioDeClavesDeProteccionPostgres(
    IUnidadDeTrabajo unidad,
    IConexionDeLaUnidadDeTrabajo conexionDeLaUnidad,
    ICifradorDeSecretosDeSesion cifrador,
    HostDeSesion host,
    TimeProvider? tiempo = null,
    ILogger<RepositorioDeClavesDeProteccionPostgres>? registro = null) : IXmlRepository
{
    private readonly string _host = host == HostDeSesion.Tienda ? "tienda" : "gestion";
    private int _filasIlegibles;

    /// <summary>Cuántas filas se omitieron por no poder descifrarse (clave retirada o texto manipulado): para el diagnóstico, nunca el contenido.</summary>
    public int FilasIlegibles => Volatile.Read(ref _filasIlegibles);

    public IReadOnlyCollection<XElement> GetAllElements() =>
        AccesoAlEsquemaWeb.Ejecutar(unidad, conexionDeLaUnidad, LeerYMigrar, CancellationToken.None).GetAwaiter().GetResult();

    public void StoreElement(XElement element, string friendlyName)
    {
        ArgumentNullException.ThrowIfNull(element);
        var id = Guid.NewGuid();
        var cifrado = cifrador.Cifrar(element.ToString(SaveOptions.DisableFormatting), PropositoDeSecreto.ClavesDeProteccion, DatosAsociados(id));
        AccesoAlEsquemaWeb.Ejecutar(
            unidad,
            conexionDeLaUnidad,
            async (conexion, transaccion, token) =>
            {
                await using var comando = new NpgsqlCommand(
                    "INSERT INTO web.claves_de_proteccion (id, host, xml_cifrado, version_de_clave, creada_en) VALUES (@id, @host, @xml, @version, @creada)",
                    conexion,
                    transaccion);
                comando.Parameters.AddWithValue("id", id);
                comando.Parameters.AddWithValue("host", _host);
                comando.Parameters.AddWithValue("xml", cifrado.Bytes);
                comando.Parameters.AddWithValue("version", cifrado.VersionDeClave);
                comando.Parameters.AddWithValue("creada", (tiempo ?? TimeProvider.System).GetUtcNow());
                await comando.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                return 0;
            },
            CancellationToken.None).GetAwaiter().GetResult();
    }

    private static string DatosAsociados(Guid id) => $"{id:N}|claves-de-proteccion";

    private async Task<IReadOnlyCollection<XElement>> LeerYMigrar(NpgsqlConnection conexion, NpgsqlTransaction transaccion, CancellationToken cancelacion)
    {
        var filas = new List<(Guid Id, byte[] Cifrado, short Version)>();
        await using (var consulta = new NpgsqlCommand("SELECT id, xml_cifrado, version_de_clave FROM web.claves_de_proteccion WHERE host = @host ORDER BY creada_en, id", conexion, transaccion))
        {
            consulta.Parameters.AddWithValue("host", _host);
            await using var lector = await consulta.ExecuteReaderAsync(cancelacion).ConfigureAwait(false);
            while (await lector.ReadAsync(cancelacion).ConfigureAwait(false))
            {
                filas.Add((lector.GetGuid(0), lector.GetFieldValue<byte[]>(1), lector.GetInt16(2)));
            }
        }

        var elementos = new List<XElement>(filas.Count);
        foreach (var (id, cifrado, version) in filas)
        {
            string xml;
            try
            {
                xml = cifrador.Descifrar(cifrado, version, PropositoDeSecreto.ClavesDeProteccion, DatosAsociados(id));
            }
            catch (SecretoDeSesionIlegibleException)
            {
                Interlocked.Increment(ref _filasIlegibles);
                registro?.LogWarning("Una clave de protección del anillo del host no se pudo descifrar y se omitió.");
                continue;
            }

            elementos.Add(XElement.Parse(xml));
            if (version != cifrador.VersionVigente)
            {
                await Recifrar(conexion, transaccion, id, xml, version, cancelacion).ConfigureAwait(false);
            }
        }

        return elementos;
    }

    /// <summary>Re-cifra con la clave vigente solo si la fila sigue con la versión que se leyó (otra réplica del mismo host pudo hacerlo antes: es idempotente).</summary>
    private async Task Recifrar(NpgsqlConnection conexion, NpgsqlTransaction transaccion, Guid id, string xml, short versionLeida, CancellationToken cancelacion)
    {
        var nuevo = cifrador.Cifrar(xml, PropositoDeSecreto.ClavesDeProteccion, DatosAsociados(id));
        await using var comando = new NpgsqlCommand(
            "UPDATE web.claves_de_proteccion SET xml_cifrado = @xml, version_de_clave = @version WHERE id = @id AND version_de_clave = @leida",
            conexion,
            transaccion);
        comando.Parameters.AddWithValue("xml", nuevo.Bytes);
        comando.Parameters.AddWithValue("version", nuevo.VersionDeClave);
        comando.Parameters.AddWithValue("id", id);
        comando.Parameters.AddWithValue("leida", versionLeida);
        await comando.ExecuteNonQueryAsync(cancelacion).ConfigureAwait(false);
    }
}
