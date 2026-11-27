using System.Security.Cryptography;

namespace BabelHub.IntegrationTests.Imagenes;

/// <summary>
/// La configuración completa que un servicio exige para arrancar (las mismas variables que genera <c>tests/e2e/entorno/preparar-entorno.sh</c> para el entorno local), con <b>valores aleatorios en cada ejecución</b>: la prueba de humo de las imágenes
/// no debilita la validación de secretos de la imagen, la satisface. Las cuentas de base de datos apuntan a un PostgreSQL desechable al que se les da de alta antes de arrancar el servicio.
/// </summary>
internal sealed record EntornoDeHumo(IReadOnlyList<string> Argumentos, IReadOnlyList<(string Base, string Usuario, string Clave)> Cuentas)
{
    private static readonly string[] AlcancesDeCredenciales =
    [
        "LecturaDeDisponibilidad", "OperacionDeUsuario", "LecturaDeContacto", "ExpiracionDeReservas", "ConsultaDePendientes", "BajaDeTitular", "OrigenReenviado", "ConsultaDeAuditoria", "LecturaDeAlertas", "MarcarAlerta", "Historial", "SolicitarAviso",
    ];

    private static string Aleatorio(int largo) => new([.. Convert.ToBase64String(RandomNumberGenerator.GetBytes(largo * 2)).Where(char.IsLetterOrDigit).Take(largo)]);

    private static string Versionada() => "v1:" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    public static EntornoDeHumo Para(string servicio, string servidorDeBase)
    {
        var variables = new List<(string Nombre, string Valor)>
        {
            ("ASPNETCORE_ENVIRONMENT", "Development"),
            ("BABELHUB__Web__Host", "tienda"),
            ("BABELHUB__Web__HostsPermitidos", "www.babelhub.example"),
            ("BABELHUB__Auditoria__ClaveDeCursor", Aleatorio(40)),
            ("BABELHUB__Seguridad__ClaveDeHuella", Versionada()),
            ("BABELHUB__Seguridad__ClaveDeRedisDeLimites", Aleatorio(40)),
            ("BABELHUB__Seguridad__ServicioSinHttps", "true"),
        };
        variables.AddRange(AlcancesDeCredenciales.Select(a => ($"BABELHUB__Seguridad__Credenciales__{a}", Aleatorio(40))));

        var cuentas = new List<(string Base, string Usuario, string Clave)>();

        string Cadena(string baseDeDatos, string usuario)
        {
            var clave = Aleatorio(32);
            cuentas.Add((baseDeDatos, usuario, clave));
            return $"Host={servidorDeBase};Port=5432;Database={baseDeDatos};Username={usuario};Password={clave}";
        }

        if (servicio == "Inventario")
        {
            variables.Add(("BABELHUB__Inventario__Base__Aplicacion", Cadena("inventario", "inventario_acceso")));
            variables.Add(("BABELHUB__Inventario__Base__DepuracionDeAjustes", Cadena("inventario", "inventario_depurador_ajustes")));
            variables.Add(("BABELHUB__Inventario__Base__DepuracionDeAuditoria", Cadena("inventario", "inventario_depurador_auditoria")));
            variables.Add(("BABELHUB__Inventario__Ecommerce__Direccion", "http://localhost:8080/"));
            variables.Add(("BABELHUB__Inventario__ClaveDeBajas", Aleatorio(40)));
            variables.Add(("BABELHUB__Identidad__UrlDeLaClaveDeVerificacion", "http://localhost:8080/.well-known/jwks.json"));
            variables.Add(("BABELHUB__Identidad__UrlDeLasRevocaciones", "http://localhost:8080/.well-known/revocaciones"));
        }
        else
        {
            variables.Add(("BABELHUB__Ecommerce__Base__Aplicacion", Cadena("ecommerce", "ecommerce_acceso")));
            variables.Add(("BABELHUB__Ecommerce__Base__DepuracionDeAuditoria", Cadena("ecommerce", "ecommerce_depurador_auditoria")));
            variables.Add(("BABELHUB__Ecommerce__Base__DepuracionDeRegistros", Cadena("ecommerce", "ecommerce_depurador_registros")));
            variables.Add(("BABELHUB__Ecommerce__Inventario__Direccion", "http://localhost:8082/"));
            variables.Add(("BABELHUB__Ecommerce__ProxiesConocidos", "127.0.0.1,::1"));
            variables.Add(("BABELHUB__Catalogo__ClaveDeIngresos", Aleatorio(40)));
            variables.Add(("BABELHUB__Correo__ClaveDeLaCola", Aleatorio(40)));
            variables.Add(("BABELHUB__Seguridad__Turnstile__Secreto", Aleatorio(40)));
            variables.Add(("BABELHUB__Seguridad__Turnstile__Hostnames", "localhost"));
            variables.Add(("BABELHUB__Seguridad__Turnstile__ClaveDeSitio", "0x4AAAAAAAdeSitioLocal"));
            variables.Add(("BABELHUB__Identidad__ClaveDeCifradoDeClaves", Versionada()));
            variables.Add(("BABELHUB__Identidad__Pimienta", Versionada()));
            variables.Add(("BABELHUB__Web__ClaveDeSesionesTienda", Aleatorio(40)));
            variables.Add(("BABELHUB__Web__ClaveDeSesionesGestion", Aleatorio(40)));
            variables.Add(("BABELHUB__Carrito__CostoDeEnvio", "30.00"));
            variables.Add(("BABELHUB__Pagos__Pasarela", "simulador"));
            variables.Add(("BABELHUB__Pagos__ClaveDeIntentos", Aleatorio(40)));
            foreach (var almacen in new[] { "Catalogo", "Supresiones" })
            {
                variables.Add(($"BABELHUB__Almacen__{almacen}__Endpoint", "https://almacen.invalido.local/"));
                variables.Add(($"BABELHUB__Almacen__{almacen}__Region", "sa-lima-1"));
                variables.Add(($"BABELHUB__Almacen__{almacen}__ClaveDeAcceso", Aleatorio(32)));
                variables.Add(($"BABELHUB__Almacen__{almacen}__ClaveSecreta", Aleatorio(32)));
            }
        }

        // Una variable repetida (Web__Host, por ejemplo) se queda con su último valor.
        var argumentos = variables.GroupBy(v => v.Nombre).SelectMany(g => new[] { "--env", $"{g.Key}={g.Last().Valor}" }).ToList();
        return new EntornoDeHumo(argumentos, cuentas);
    }

    /// <summary>Las sentencias que dan de alta las bases y las cuentas del entorno en el PostgreSQL desechable (los nombres y claves son los que el servicio recibe).</summary>
    public IReadOnlyList<string> SentenciasDeAlta() =>
    [
        .. Cuentas.Select(c => c.Base).Distinct().Select(b => $"CREATE DATABASE {b}"),
        .. Cuentas.Select(c => $"CREATE ROLE {c.Usuario} LOGIN PASSWORD '{c.Clave}'"),
    ];
}
