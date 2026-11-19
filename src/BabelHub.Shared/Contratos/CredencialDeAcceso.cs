namespace BabelHub.Shared.Contratos;

/// <summary>
/// El contrato de la credencial de acceso y del contexto de lectura que firma Identidad (IDE-02, IDE-10, IDE-33, IDE-35): el algoritmo fijo, el emisor, las audiencias y los nombres de los reclamos, para que el E-commerce y el
/// Inventario lean lo mismo sin referenciarse. Solo constantes: ningún servicio puede emitir una credencial que otro acepte, porque la clave privada solo la tiene Identidad.
/// </summary>
public static class CredencialDeAcceso
{
    /// <summary>El único algoritmo aceptado (ECDSA con P-256 y SHA-256); cualquier otro, incluido <c>none</c>, se rechaza.</summary>
    public const string Algoritmo = "ES256";

    public const string Emisor = "babelhub-identidad";

    /// <summary>La credencial del usuario viaja con las dos audiencias (se reenvía al Inventario) y cada verificador exige la suya.</summary>
    public const string AudienciaEcommerce = "babelhub-ecommerce";

    public const string AudienciaInventario = "babelhub-inventario";

    /// <summary>La audiencia única del contexto de lectura del asistente: solo el módulo de pedidos lo acepta.</summary>
    public const string AudienciaDePedidos = "pedidos";

    public const string TipoDelContextoDeLectura = "lectura-asistente";

    public const string AlcanceDeLeerPedidos = "leer-pedidos";

    public const string ReclamoRol = "rol";

    public const string ReclamoSucursal = "sucursal";

    public const string ReclamoCorreoVerificado = "correo_verificado";

    /// <summary>El tipo de un contexto: una credencial de acceso no lo lleva, y un verificador de credenciales rechaza todo token que lo traiga.</summary>
    public const string ReclamoTipo = "typ";

    public const string ReclamoAlcance = "alcance";

    /// <summary>Cada servicio renueva su caché de claves y revocaciones al menos cada 5 minutos y rechaza toda credencial si no puede (IDE-61).</summary>
    public const int RenovacionDeClavesEnMinutos = 5;

    public const int VigenciaDeLaCredencialEnMinutos = 15;

    public const int VigenciaDelContextoDeLecturaEnMinutos = 5;

    /// <summary>El desfase de reloj que se tolera entre servicios.</summary>
    public const int DesfaseTolerado = 30;
}
