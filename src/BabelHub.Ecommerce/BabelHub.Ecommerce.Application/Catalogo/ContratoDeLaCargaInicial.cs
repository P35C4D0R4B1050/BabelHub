using System.Text;
using System.Text.Json;
using BabelHub.Ecommerce.Application.Plataforma;

namespace BabelHub.Ecommerce.Application.Catalogo;

// El contrato público de la carga inicial del catálogo (transversal-carga-inicial, CIN-01 a CIN-10, CIN-15, CIN-19, CIN-20): modelos propios del módulo, sin tipos del Dominio. Lo llama solo la orden de
// operación `cargar-catalogo` de la Api (nunca una ruta de red, CIN-18), con la identidad de operación de la carga como actor.

/// <summary>De dónde se leen las portadas de la carga: una carpeta indicada por parámetro (CIN-05, CIN-09). Nunca guarda nada.</summary>
public interface IOrigenDeArchivosDeLaCarga
{
    /// <summary>
    /// Abre de solo lectura la portada de ese nombre simple, con una lectura acotada a 2 MB más un byte (el tope de CAT-16 lo decide el caso de uso de subida), o devuelve <see langword="null"/> si el nombre no es el de un archivo
    /// regular simple dentro de la carpeta (separadores, ruta absoluta, <c>..</c>, enlace simbólico, flujo alterno, nombre reservado) o si no existe. Quien llama cierra el flujo.
    /// </summary>
    Task<Stream?> AbrirPortada(string nombre, CancellationToken cancelacion = default);
}

/// <summary>Carga el catálogo inicial desde el texto de un archivo de datos y una carpeta de portadas. Solo la administración central (la identidad de operación «carga inicial»), comprobado antes de leer el archivo.</summary>
public interface ICargaInicialDelCatalogo
{
    /// <summary>
    /// Un archivo ilegible en conjunto (tamaño, filas, caracteres, columnas) es un fallo antes de escribir nada; una fila inválida o que el catálogo rechaza es el resultado de <b>esa fila</b> y las demás siguen. El resultado
    /// exitoso lleva el resumen, el plan de inventario y si el registro de la corrida en la auditoría falló.
    /// </summary>
    Task<Resultado<ResumenDeLaCargaDelCatalogo>> Cargar(SolicitudDeCargaDelCatalogo solicitud, CancellationToken cancelacion = default);
}

/// <summary>El texto del archivo de datos (ya leído con su tope de 2 MB y como UTF-8 estricto) y el origen de las portadas.</summary>
public sealed record SolicitudDeCargaDelCatalogo(string TextoDeDatos, IOrigenDeArchivosDeLaCarga Portadas)
{
    /// <summary>El contenido del archivo no se imprime.</summary>
    public override string ToString() => nameof(SolicitudDeCargaDelCatalogo);
}

public enum ResultadoDeFilaDeCarga
{
    Cargada,

    /// <summary>Su ISBN ya pertenece a la misma obra (título y autor): no se modificó nada (CIN-02).</summary>
    YaCargada,

    Rechazada,
}

/// <summary>El resultado de una fila: su número en el archivo (la cabecera es la fila 1), su clave, el campo y el motivo simbólico si fue rechazada (nunca el valor recibido).</summary>
public sealed record FilaDeLaCarga(int Fila, string Etiqueta, ResultadoDeFilaDeCarga Resultado, string? Campo = null, string? Motivo = null);

/// <summary>Una edición impresa del plan de inventario: su identificador, su ISBN-13 y el stock por código de sucursal, en el orden de las columnas del archivo.</summary>
public sealed record EdicionDelPlanDeInventario(Guid EdicionId, string Isbn, IReadOnlyList<KeyValuePair<string, int>> Stock);

/// <summary>El plan de inventario v1 (CIN-10): solo identificadores, ISBN y cantidades; sin hora y sin datos personales, de modo que repetir la carga produce el mismo archivo.</summary>
public sealed record PlanDeInventarioDeLaCarga(IReadOnlyList<EdicionDelPlanDeInventario> Ediciones)
{
    public const int Version = 1;

    /// <summary>El JSON v1 que lee la carga del inventario: <c>{"version":1,"ediciones":[{"edicionId":"…","isbn":"…","stock":{"código":n}}]}</c>.</summary>
    public string ComoJson()
    {
        using var memoria = new MemoryStream();
        using (var escritor = new Utf8JsonWriter(memoria, new JsonWriterOptions { Indented = true }))
        {
            escritor.WriteStartObject();
            escritor.WriteNumber("version", Version);
            escritor.WriteStartArray("ediciones");
            foreach (var edicion in Ediciones)
            {
                escritor.WriteStartObject();
                escritor.WriteString("edicionId", edicion.EdicionId.ToString("D"));
                escritor.WriteString("isbn", edicion.Isbn);
                escritor.WriteStartObject("stock");
                foreach (var (codigo, cantidad) in edicion.Stock)
                {
                    escritor.WriteNumber(codigo, cantidad);
                }

                escritor.WriteEndObject();
                escritor.WriteEndObject();
            }

            escritor.WriteEndArray();
            escritor.WriteEndObject();
        }

        return new UTF8Encoding(false).GetString(memoria.ToArray()) + "\n";
    }
}

/// <summary>El resumen de la carga: la corrida, la huella SHA-256 del archivo, el resultado de cada fila, el plan de inventario y si el registro de la corrida en la auditoría falló (CIN-07).</summary>
public sealed record ResumenDeLaCargaDelCatalogo(
    Guid CorridaId,
    string HuellaDelTexto,
    IReadOnlyList<FilaDeLaCarga> Filas,
    PlanDeInventarioDeLaCarga Plan,
    bool RegistroDeLaCorridaFallo)
{
    public int Cargadas => Filas.Count(f => f.Resultado == ResultadoDeFilaDeCarga.Cargada);

    public int YaCargadas => Filas.Count(f => f.Resultado == ResultadoDeFilaDeCarga.YaCargada);

    public int Rechazadas => Filas.Count(f => f.Resultado == ResultadoDeFilaDeCarga.Rechazada);

    /// <summary>Todo cargado o ya cargado y la corrida registrada: el código de salida 0; con filas rechazadas (también por el almacén caído) es el éxito parcial.</summary>
    public bool Completa => Rechazadas == 0 && !RegistroDeLaCorridaFallo;
}
