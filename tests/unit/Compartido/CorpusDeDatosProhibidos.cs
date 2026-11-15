namespace BabelHub.UnitTests.Compartido;

/// <summary>
/// El corpus común de valores que nunca deben llegar a un registro de auditoría (AUD-06, AC-AUD-05): lo comparten las pruebas de contrato de los dos servicios (T21) para que ambos
/// redacten lo mismo. Cada valor lleva la clase de dato que representa. Los valores se arman por partes donde un escáner de secretos los confundiría con uno real.
/// </summary>
public static class CorpusDeDatosProhibidos
{
    public static IReadOnlyList<(string Clase, string Valor)> Valores { get; } =
    [
        ("tarjeta", "4111 1111 1111 1111"),
        ("tarjeta", "4111-1111-1111-1111"),
        ("tarjeta", "4111111111111111"),
        ("tarjeta", "pagó con 5500 0000 0000 0004 ayer"),
        ("tarjeta", "378282246310005"),
        ("token", "ey" + "JhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9." + "ey" + "JzdWIiOiIxMjM0NTY3ODkwIn0." + "SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c"),
        ("token", "Bear" + "er abc123def456"),
        ("token", new string((char)97, 40)),
        ("token", "QWxhZGRpbjpvcGVuIHNl" + "c2FtZVF1ZUVzVW5Ub2tl" + "bkxhcmdvRGVBcGlLZXk="),
        ("enlace", "https://cdn.babelhub.pe/libros/x.epub?firma=abc&expira=123"),
        ("enlace", "HTTP://ejemplo.pe/a"),
        ("enlace", "ftp://servidor/archivo"),
        ("enlace", "ver www.ejemplo.pe/descarga"),
        ("direccion", "200.37.12.4"),
        ("direccion", "desde 10.0.0.1 hoy"),
        ("direccion", "2001:db8:85a3:0:0:8a2e:370:7334"),
        ("direccion", "fe80::1ff:fe23:4567:890a"),
        ("correo", "persona@ejemplo.pe"),
    ];

    /// <summary>Los fragmentos que no pueden quedar en ningún texto guardado ni mostrado.</summary>
    public static IReadOnlyList<string> FragmentosQueNoDebenQuedar { get; } =
    [
        "4111", "5500 0000", "378282", "Bearer", "abc123def456", "SflKxw", "QWxhZGRpbjpv", "firma=abc", "cdn.babelhub.pe", "ejemplo.pe", "200.37.12.4", "10.0.0.1", "2001:db8", "fe80::", "persona@",
    ];
}
