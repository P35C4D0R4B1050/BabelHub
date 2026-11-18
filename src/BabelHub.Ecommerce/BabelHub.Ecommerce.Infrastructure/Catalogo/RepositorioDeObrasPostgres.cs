using BabelHub.Ecommerce.Application.Catalogo.Interno;
using BabelHub.Ecommerce.Domain.Catalogo;
using Npgsql;

namespace BabelHub.Ecommerce.Infrastructure.Catalogo;

/// <summary>
/// El repositorio de obras sobre el esquema <c>catalogo</c> (T14): la obra se lee con todas sus ediciones, licencias y archivos; el ISBN repetido lo detecta el <b>índice único</b> con <c>INSERT … ON CONFLICT (isbn13) DO NOTHING</c> (sin una
/// transacción abortada, así el título se lee después en la misma transacción) y la concurrencia optimista de la versión es <c>UPDATE … WHERE version = @leida</c>. «Para actualizar» toma la fila de la obra con <c>FOR UPDATE</c>.
/// </summary>
public sealed class RepositorioDeObrasPostgres(AccesoAlEsquemaDelCatalogo acceso) : IRepositorioDeObras
{
    internal const string ColumnasDeObra = "id, titulo, autor, editorial, categoria, portada_clave, portada_huella, portada_tipo, estado, dada_de_baja_en, version, creada_en";

    internal const string ColumnasDeEdicion = """
        e.id, e.tipo, e.isbn_ingresado, e.precio, e.estado, e.motivo_de_retiro, e.retirada_en, e.dada_de_baja_en,
        l.tipo, l.titular, l.territorios, l.vigente_hasta, l.formato,
        a.clave_en_almacen, a.tipo_detectado, a.tamano, a.huella_sha256, a.subido_en, a.subido_por
        """;

    public Task<Obra?> Obtener(Guid obraId, bool paraActualizar, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(async (conexion, transaccion, token) => await Cargar(conexion, transaccion, "id = @id", [new NpgsqlParameter("id", obraId)], paraActualizar, token).ConfigureAwait(false), cancelacion);

    public Task<Obra?> ObtenerPorEdicion(Guid edicionId, bool paraActualizar, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (conexion, transaccion, token) =>
                await Cargar(conexion, transaccion, "id = (SELECT obra_id FROM catalogo.ediciones WHERE id = @edicion)", [new NpgsqlParameter("edicion", edicionId)], paraActualizar, token).ConfigureAwait(false),
            cancelacion);

    public Task<ResultadoDeEscrituraDeObra> Agregar(Obra obra, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(obra);
        return acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                await using (var comando = new NpgsqlCommand(
                    """
                    INSERT INTO catalogo.obras (id, titulo, autor, editorial, categoria, portada_clave, portada_huella, portada_tipo, estado, dada_de_baja_en, version, creada_en)
                    VALUES (@id, @titulo, @autor, @editorial, @categoria, @clave, @huella, @tipo, @estado, @baja, @version, @creada)
                    """,
                    conexion,
                    transaccion))
                {
                    ParametrosDeObra(comando, obra, obra.Version);
                    comando.Parameters.AddWithValue("creada", obra.CreadaEn.ToUniversalTime());
                    await comando.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }

                foreach (var edicion in obra.Ediciones)
                {
                    if (!await InsertarEdicion(conexion, transaccion, obra.Id, edicion, token).ConfigureAwait(false))
                    {
                        return ResultadoDeEscrituraDeObra.IsbnDuplicado;
                    }
                }

                return ResultadoDeEscrituraDeObra.Escrita;
            },
            cancelacion);
    }

    public Task<ResultadoDeEscrituraDeObra> Guardar(Obra obra, long versionLeida, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(obra);
        return acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                await using (var comando = new NpgsqlCommand(
                    """
                    UPDATE catalogo.obras
                    SET titulo = @titulo, autor = @autor, editorial = @editorial, categoria = @categoria, portada_clave = @clave, portada_huella = @huella, portada_tipo = @tipo,
                        estado = @estado, dada_de_baja_en = @baja, version = @version
                    WHERE id = @id AND version = @leida
                    """,
                    conexion,
                    transaccion))
                {
                    ParametrosDeObra(comando, obra, obra.Version);
                    comando.Parameters.AddWithValue("leida", versionLeida);
                    if (await comando.ExecuteNonQueryAsync(token).ConfigureAwait(false) == 0)
                    {
                        return ResultadoDeEscrituraDeObra.ConflictoDeVersion;
                    }
                }

                var existentes = new HashSet<Guid>();
                await using (var consulta = new NpgsqlCommand("SELECT id FROM catalogo.ediciones WHERE obra_id = @obra", conexion, transaccion))
                {
                    consulta.Parameters.AddWithValue("obra", obra.Id);
                    await using var filas = await consulta.ExecuteReaderAsync(token).ConfigureAwait(false);
                    while (await filas.ReadAsync(token).ConfigureAwait(false))
                    {
                        existentes.Add(filas.GetGuid(0));
                    }
                }

                foreach (var edicion in obra.Ediciones)
                {
                    if (existentes.Contains(edicion.Id))
                    {
                        await ActualizarEdicion(conexion, transaccion, edicion, token).ConfigureAwait(false);
                    }
                    else if (!await InsertarEdicion(conexion, transaccion, obra.Id, edicion, token).ConfigureAwait(false))
                    {
                        return ResultadoDeEscrituraDeObra.IsbnDuplicado;
                    }
                }

                return ResultadoDeEscrituraDeObra.Escrita;
            },
            cancelacion);
    }

    public Task<string?> TituloDeLaEdicionConIsbn(string isbn13, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                await using var comando = new NpgsqlCommand("SELECT o.titulo FROM catalogo.ediciones e JOIN catalogo.obras o ON o.id = e.obra_id WHERE e.isbn13 = @isbn", conexion, transaccion);
                comando.Parameters.AddWithValue("isbn", isbn13);
                return await comando.ExecuteScalarAsync(token).ConfigureAwait(false) as string;
            },
            cancelacion);

    /// <summary>Lee obras con todas sus ediciones (también las dadas de baja) según el filtro; el llamador pone el orden y los límites en <paramref name="filtro"/>.</summary>
    internal static async Task<Obra?> Cargar(NpgsqlConnection conexion, NpgsqlTransaction transaccion, string filtro, NpgsqlParameter[] parametros, bool paraActualizar, CancellationToken cancelacion)
    {
        var obras = await CargarVarias(conexion, transaccion, $"WHERE {filtro}", parametros, paraActualizar ? "FOR UPDATE" : string.Empty, cancelacion).ConfigureAwait(false);
        return obras.Count == 0 ? null : obras[0];
    }

    internal static async Task<List<Obra>> CargarVarias(NpgsqlConnection conexion, NpgsqlTransaction transaccion, string clausulas, NpgsqlParameter[] parametros, string bloqueo, CancellationToken cancelacion)
    {
        var cabeceras = new List<(Guid Id, DatosDeObra Datos, PortadaDeObra Portada, long Version, EstadoDeObra Estado, DateTimeOffset? Baja, DateTimeOffset Creada)>();
        await using (var comando = new NpgsqlCommand($"SELECT {ColumnasDeObra} FROM catalogo.obras {clausulas} {bloqueo}", conexion, transaccion))
        {
            comando.Parameters.AddRange(parametros.Select(Clonar).ToArray());
            await using var filas = await comando.ExecuteReaderAsync(cancelacion).ConfigureAwait(false);
            while (await filas.ReadAsync(cancelacion).ConfigureAwait(false))
            {
                TiposDeArchivo.TryDesdeNombre(filas.GetString(7), out var tipoDePortada);
                cabeceras.Add((
                    filas.GetGuid(0),
                    new DatosDeObra(filas.GetString(1), filas.GetString(2), filas.GetString(3), filas.GetString(4)),
                    new PortadaDeObra(filas.GetString(5), tipoDePortada, HuellaDeArchivo.Desde(filas.GetFieldValue<byte[]>(6))),
                    filas.GetInt64(10),
                    filas.GetString(8) == "baja" ? EstadoDeObra.Baja : EstadoDeObra.Activa,
                    AccesoAlEsquemaDelCatalogo.LeerNulable<DateTimeOffset>(filas, 9),
                    filas.GetFieldValue<DateTimeOffset>(11)));
            }
        }

        // Todas las ediciones de las obras leídas en una sola consulta (también las dadas de baja), agrupadas por obra.
        var porObra = new Dictionary<Guid, List<Edicion>>();
        if (cabeceras.Count > 0)
        {
            await using var comando = new NpgsqlCommand(
                $"""
                SELECT {ColumnasDeEdicion}, e.obra_id
                FROM catalogo.ediciones e
                LEFT JOIN catalogo.licencias l ON l.edicion_id = e.id
                LEFT JOIN catalogo.archivos_digitales a ON a.edicion_id = e.id
                WHERE e.obra_id = ANY(@obras)
                ORDER BY e.tipo, e.id
                """,
                conexion,
                transaccion);
            comando.Parameters.AddWithValue("obras", cabeceras.Select(c => c.Id).ToArray());
            await using var filas = await comando.ExecuteReaderAsync(cancelacion).ConfigureAwait(false);
            while (await filas.ReadAsync(cancelacion).ConfigureAwait(false))
            {
                var obraId = filas.GetGuid(19);
                if (!porObra.TryGetValue(obraId, out var lista))
                {
                    lista = [];
                    porObra[obraId] = lista;
                }

                lista.Add(LeerEdicion(filas));
            }
        }

        return cabeceras
            .Select(c => Obra.Rehidratar(c.Id, c.Datos, c.Portada, c.Version, c.Estado, c.Baja, c.Creada, porObra.GetValueOrDefault(c.Id) ?? []))
            .ToList();
    }

    private static NpgsqlParameter Clonar(NpgsqlParameter p) => new(p.ParameterName, p.Value);

    private static Edicion LeerEdicion(NpgsqlDataReader filas)
    {
        var digital = filas.GetString(1) == "digital";
        LicenciaDigital? licencia = null;
        ArchivoDeEdicion? archivo = null;
        if (digital && !filas.IsDBNull(8))
        {
            licencia = LicenciaDigital.Rehidratar(
                filas.GetString(8) == "comercial" ? TipoDeLicencia.Comercial : TipoDeLicencia.DominioPublico,
                filas.GetString(9),
                filas.GetFieldValue<string[]>(10),
                AccesoAlEsquemaDelCatalogo.LeerNulable<DateOnly>(filas, 11),
                filas.GetString(12) == "epub" ? FormatoDigital.Epub : FormatoDigital.Pdf);
        }

        if (digital && !filas.IsDBNull(13))
        {
            TiposDeArchivo.TryDesdeNombre(filas.GetString(14), out var tipo);
            archivo = new ArchivoDeEdicion(filas.GetString(13), tipo, filas.GetInt64(15), HuellaDeArchivo.Desde(filas.GetFieldValue<byte[]>(16)), filas.GetFieldValue<DateTimeOffset>(17), filas.GetGuid(18));
        }

        var estado = filas.GetString(4) switch
        {
            "retirada_de_venta" => EstadoDeEdicion.RetiradaDeVenta,
            "baja" => EstadoDeEdicion.Baja,
            _ => EstadoDeEdicion.Activa,
        };

        return Edicion.Rehidratar(
            filas.GetGuid(0),
            digital ? TipoDeEdicion.Digital : TipoDeEdicion.Impresa,
            Isbn.Crear(filas.GetString(2)),
            Precio.Crear(filas.GetDecimal(3)),
            estado,
            AccesoAlEsquemaDelCatalogo.LeerTexto(filas, 5),
            AccesoAlEsquemaDelCatalogo.LeerNulable<DateTimeOffset>(filas, 6),
            AccesoAlEsquemaDelCatalogo.LeerNulable<DateTimeOffset>(filas, 7),
            licencia,
            archivo);
    }

    private static void ParametrosDeObra(NpgsqlCommand comando, Obra obra, long version)
    {
        comando.Parameters.AddWithValue("id", obra.Id);
        comando.Parameters.AddWithValue("titulo", obra.Titulo);
        comando.Parameters.AddWithValue("autor", obra.Autor);
        comando.Parameters.AddWithValue("editorial", obra.Editorial);
        comando.Parameters.AddWithValue("categoria", obra.Categoria);
        comando.Parameters.AddWithValue("clave", obra.Portada.ClaveDelAlmacen);
        comando.Parameters.AddWithValue("huella", obra.Portada.Huella.ComoBytes());
        comando.Parameters.AddWithValue("tipo", TiposDeArchivo.Nombre(obra.Portada.TipoDetectado));
        comando.Parameters.AddWithValue("estado", obra.EstaDadaDeBaja ? "baja" : "activa");
        comando.Parameters.AddWithValue("baja", AccesoAlEsquemaDelCatalogo.UtcONulo(obra.DadaDeBajaEn));
        comando.Parameters.AddWithValue("version", version);
    }

    private static string NombreDeEstado(EstadoDeEdicion estado) =>
        estado switch
        {
            EstadoDeEdicion.RetiradaDeVenta => "retirada_de_venta",
            EstadoDeEdicion.Baja => "baja",
            _ => "activa",
        };

    /// <summary>Inserta una edición con <c>ON CONFLICT (isbn13) DO NOTHING</c>: <see langword="false"/> si el ISBN ya existe (nada se escribió y la transacción no se aborta).</summary>
    private static async Task<bool> InsertarEdicion(NpgsqlConnection conexion, NpgsqlTransaction transaccion, Guid obraId, Edicion edicion, CancellationToken cancelacion)
    {
        await using (var comando = new NpgsqlCommand(
            """
            INSERT INTO catalogo.ediciones (id, obra_id, tipo, isbn13, isbn_ingresado, precio, estado, motivo_de_retiro, retirada_en, dada_de_baja_en)
            VALUES (@id, @obra, @tipo, @isbn13, @ingresado, @precio, @estado, @motivo, @retirada, @baja)
            ON CONFLICT (isbn13) DO NOTHING
            """,
            conexion,
            transaccion))
        {
            comando.Parameters.AddWithValue("id", edicion.Id);
            comando.Parameters.AddWithValue("obra", obraId);
            comando.Parameters.AddWithValue("tipo", edicion.EsDigital ? "digital" : "impresa");
            comando.Parameters.AddWithValue("isbn13", edicion.Isbn.Isbn13);
            comando.Parameters.AddWithValue("ingresado", edicion.Isbn.TextoIngresado);
            comando.Parameters.AddWithValue("precio", edicion.Precio.Valor);
            comando.Parameters.AddWithValue("estado", NombreDeEstado(edicion.Estado));
            comando.Parameters.AddWithValue("motivo", AccesoAlEsquemaDelCatalogo.ONulo(edicion.MotivoDeRetiro));
            comando.Parameters.AddWithValue("retirada", AccesoAlEsquemaDelCatalogo.UtcONulo(edicion.RetiradaEn));
            comando.Parameters.AddWithValue("baja", AccesoAlEsquemaDelCatalogo.UtcONulo(edicion.DadaDeBajaEn));
            if (await comando.ExecuteNonQueryAsync(cancelacion).ConfigureAwait(false) == 0)
            {
                return false;
            }
        }

        await EscribirLicenciaYArchivo(conexion, transaccion, edicion, cancelacion).ConfigureAwait(false);
        return true;
    }

    private static async Task ActualizarEdicion(NpgsqlConnection conexion, NpgsqlTransaction transaccion, Edicion edicion, CancellationToken cancelacion)
    {
        await using (var comando = new NpgsqlCommand(
            "UPDATE catalogo.ediciones SET precio = @precio, estado = @estado, motivo_de_retiro = @motivo, retirada_en = @retirada, dada_de_baja_en = @baja WHERE id = @id",
            conexion,
            transaccion))
        {
            comando.Parameters.AddWithValue("id", edicion.Id);
            comando.Parameters.AddWithValue("precio", edicion.Precio.Valor);
            comando.Parameters.AddWithValue("estado", NombreDeEstado(edicion.Estado));
            comando.Parameters.AddWithValue("motivo", AccesoAlEsquemaDelCatalogo.ONulo(edicion.MotivoDeRetiro));
            comando.Parameters.AddWithValue("retirada", AccesoAlEsquemaDelCatalogo.UtcONulo(edicion.RetiradaEn));
            comando.Parameters.AddWithValue("baja", AccesoAlEsquemaDelCatalogo.UtcONulo(edicion.DadaDeBajaEn));
            await comando.ExecuteNonQueryAsync(cancelacion).ConfigureAwait(false);
        }

        await EscribirLicenciaYArchivo(conexion, transaccion, edicion, cancelacion).ConfigureAwait(false);
    }

    private static async Task EscribirLicenciaYArchivo(NpgsqlConnection conexion, NpgsqlTransaction transaccion, Edicion edicion, CancellationToken cancelacion)
    {
        if (edicion.Licencia is { } licencia)
        {
            await using var comando = new NpgsqlCommand(
                """
                INSERT INTO catalogo.licencias (edicion_id, tipo, titular, territorios, vigente_hasta, formato)
                VALUES (@edicion, @tipo, @titular, @territorios, @vigente, @formato)
                ON CONFLICT (edicion_id) DO UPDATE SET tipo = EXCLUDED.tipo, titular = EXCLUDED.titular, territorios = EXCLUDED.territorios, vigente_hasta = EXCLUDED.vigente_hasta, formato = EXCLUDED.formato
                """,
                conexion,
                transaccion);
            comando.Parameters.AddWithValue("edicion", edicion.Id);
            comando.Parameters.AddWithValue("tipo", licencia.Tipo == TipoDeLicencia.Comercial ? "comercial" : "dominio_publico");
            comando.Parameters.AddWithValue("titular", licencia.Titular);
            comando.Parameters.AddWithValue("territorios", licencia.Territorios.ToArray());
            comando.Parameters.Add(new NpgsqlParameter("vigente", NpgsqlTypes.NpgsqlDbType.Date) { Value = licencia.VigenteHasta is { } v ? v : DBNull.Value });
            comando.Parameters.AddWithValue("formato", licencia.Formato == FormatoDigital.Epub ? "epub" : "pdf");
            await comando.ExecuteNonQueryAsync(cancelacion).ConfigureAwait(false);
        }

        if (edicion.Archivo is { } archivo)
        {
            await using var comando = new NpgsqlCommand(
                """
                INSERT INTO catalogo.archivos_digitales (edicion_id, clave_en_almacen, tipo_detectado, tamano, huella_sha256, subido_en, subido_por)
                VALUES (@edicion, @clave, @tipo, @tamano, @huella, @subido, @por)
                ON CONFLICT (edicion_id) DO UPDATE SET clave_en_almacen = EXCLUDED.clave_en_almacen, tipo_detectado = EXCLUDED.tipo_detectado, tamano = EXCLUDED.tamano,
                    huella_sha256 = EXCLUDED.huella_sha256, subido_en = EXCLUDED.subido_en, subido_por = EXCLUDED.subido_por
                """,
                conexion,
                transaccion);
            comando.Parameters.AddWithValue("edicion", edicion.Id);
            comando.Parameters.AddWithValue("clave", archivo.ClaveDelAlmacen);
            comando.Parameters.AddWithValue("tipo", TiposDeArchivo.Nombre(archivo.TipoDetectado));
            comando.Parameters.AddWithValue("tamano", archivo.TamanoEnBytes);
            comando.Parameters.AddWithValue("huella", archivo.Huella.ComoBytes());
            comando.Parameters.AddWithValue("subido", archivo.SubidoEn.ToUniversalTime());
            comando.Parameters.AddWithValue("por", archivo.SubidoPor);
            await comando.ExecuteNonQueryAsync(cancelacion).ConfigureAwait(false);
        }
    }
}
