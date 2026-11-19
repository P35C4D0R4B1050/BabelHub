namespace BabelHub.Ecommerce.Application.Plataforma.Interno;

/// <summary>
/// Las plantillas de los correos de seguridad (T29; IDE-67, AC-IDE-56): <b>cada una nombra a BabelHub, no lleva enlaces ni pide datos, contraseñas o códigos, y recuerda que nunca se pedirá el código por otro medio</b>. El
/// aviso de una respuesta a una solicitud sobre datos personales <b>no incluye su contenido</b>; el titular lo ve en su cuenta. El texto es fijo: ningún dato de quien provoca el correo entra en él (SEG-10). Los módulos de
/// reclamos y de pagos registran las suyas al construirse; una plantilla que no existe no sale (lanza).
/// </summary>
public static class PlantillasDeCorreoDeSeguridad
{
    public const string Firma = "BabelHub nunca te pedirá tu contraseña ni un código por teléfono, por mensajes ni respondiendo a este correo. Si no reconoces esta actividad, entra a tu cuenta desde la dirección de BabelHub que ya conoces.";

    private static string Cuerpo(string cuerpo) => $"Hola, te escribe BabelHub.\n\n{cuerpo}\n\n{Firma}";

    /// <summary>Las plantillas de los avisos de la cuenta, por su nombre.</summary>
    public static IReadOnlyDictionary<string, PlantillaDeCorreo> DeLaCuenta { get; } = new Dictionary<string, PlantillaDeCorreo>(StringComparer.Ordinal)
    {
        ["contrasena-cambiada"] = new("BabelHub: cambió la contraseña de tu cuenta", Cuerpo("La contraseña de tu cuenta se cambió hace un momento.")),
        ["segundo-factor-restablecido"] = new("BabelHub: se restableció tu verificación en dos pasos", Cuerpo("La administración restableció la verificación en dos pasos de tu cuenta. Tendrás que configurarla de nuevo la próxima vez que ingreses.")),
        ["cambio-de-correo-no-reconocido"] = new("BabelHub: se pidió cambiar el correo de tu cuenta", Cuerpo("Se pidió cambiar el correo de tu cuenta desde un dispositivo o una región que no reconocemos. El cambio queda pendiente: puedes rechazarlo desde tu cuenta.")),
        ["correo-cambiado"] = new("BabelHub: este es ahora el correo de tu cuenta", Cuerpo("El correo de tu cuenta de BabelHub se cambió a esta dirección.")),
        ["cancelacion-diferida"] = new("BabelHub: tu cancelación de cuenta está en espera", Cuerpo("Tu cancelación de cuenta quedó en espera porque tienes trámites pendientes. Puedes desistir desde tu cuenta.")),
        ["cancelacion-pendientes-cerrados"] = new("BabelHub: ya puedes confirmar la cancelación de tu cuenta", Cuerpo("Tus trámites pendientes se cerraron. Puedes confirmar la cancelación de tu cuenta desde tu cuenta durante los próximos días.")),
        ["cancelacion-descartada"] = new("BabelHub: tu cancelación de cuenta se descartó", Cuerpo("Tu cancelación de cuenta no se completó a tiempo y se descartó. Tu cuenta sigue activa y puedes volver a comprar.")),
        ["solicitud-de-datos-respondida"] = new("BabelHub: respondimos tu solicitud sobre tus datos", Cuerpo("Respondimos tu solicitud sobre tus datos personales. Puedes ver la respuesta en tu cuenta.")),
    };

    /// <summary>El asunto y el cuerpo de un código de un solo uso: el código es lo único variable y vence a los 10 minutos.</summary>
    public static PlantillaDeCorreo DeCodigo(PropositoDeCodigo proposito, string codigo) => proposito switch
    {
        PropositoDeCodigo.VerificacionDeCuenta => new("BabelHub: tu código para verificar tu correo", Cuerpo($"Tu código para verificar tu correo es {codigo}. Vence en 10 minutos.")),
        PropositoDeCodigo.RecuperacionDeAcceso => new("BabelHub: tu código para recuperar el acceso", Cuerpo($"Tu código para recuperar el acceso a tu cuenta es {codigo}. Vence en 10 minutos.")),
        _ => throw new ArgumentOutOfRangeException(nameof(proposito), proposito, "Propósito desconocido."),
    };

    /// <summary>La alerta a la administración: solo el servicio, el motivo del catálogo cerrado, la primera ocurrencia y cuántas se agruparon.</summary>
    public static PlantillaDeCorreo DeAlerta(string servicio, string motivo, DateTimeOffset primeraOcurrencia, int ocurrencias) =>
        new($"BabelHub: alerta de {servicio}", Cuerpo($"Servicio: {servicio}\nMotivo: {motivo}\nPrimera ocurrencia (UTC): {primeraOcurrencia.UtcDateTime:yyyy-MM-dd HH:mm}\nOcurrencias agrupadas: {ocurrencias}"));
}

/// <summary>
/// El renderizador de producción (T29): pone en palabras cada correo con las plantillas del catálogo cerrado. Los módulos de reclamos y de pagos añaden las suyas por el constructor; un correo con una plantilla que no
/// existe <b>no sale</b> (lanza), y los datos de una plantilla de cuenta nunca entran al texto.
/// </summary>
public sealed class RenderizadorDeCorreoDeProduccion : IRenderizadorDeCorreo
{
    private readonly Dictionary<string, PlantillaDeCorreo> _plantillas;

    /// <summary>El renderizador con las plantillas que aportan los módulos (pagos y reclamaciones), además de las de seguridad.</summary>
    public static RenderizadorDeCorreoDeProduccion Con(IEnumerable<IProveedorDePlantillasDeCorreo> proveedores)
    {
        ArgumentNullException.ThrowIfNull(proveedores);
        return new RenderizadorDeCorreoDeProduccion(proveedores.SelectMany(p => p.Plantillas).ToList());
    }

    public RenderizadorDeCorreoDeProduccion(IEnumerable<KeyValuePair<string, PlantillaDeCorreo>>? adicionales = null)
    {
        _plantillas = new Dictionary<string, PlantillaDeCorreo>(PlantillasDeCorreoDeSeguridad.DeLaCuenta, StringComparer.Ordinal);
        foreach (var (nombre, plantilla) in adicionales ?? [])
        {
            if (!_plantillas.TryAdd(nombre, plantilla))
            {
                throw new ArgumentException($"La plantilla «{nombre}» ya existe.", nameof(adicionales));
            }
        }
    }

    public MensajeDeCorreo De(CorreoSaliente correo)
    {
        ArgumentNullException.ThrowIfNull(correo);
        if (!_plantillas.TryGetValue(correo.Plantilla, out var plantilla))
        {
            throw new InvalidOperationException("La plantilla del correo no está en el catálogo.");
        }

        return new MensajeDeCorreo([correo.Destinatario], Rellenar(plantilla.Asunto, plantilla, correo, esAsunto: true), Rellenar(plantilla.Cuerpo, plantilla, correo, esAsunto: false));
    }

    /// <summary>El largo máximo de un dato que entra a un correo: nada que pueda ser un volcado (SEG-10).</summary>
    public const int LargoMaximoDeUnDato = 2000;

    /// <summary>
    /// Pone los datos en los campos que la plantilla <b>declara</b>, y nada más: un dato no declarado se ignora, uno que falta hace que el correo no salga (lanza) y los saltos de línea y los controles de un dato
    /// se vuelven espacios, de modo que ningún dato puede abrir una cabecera ni partir el asunto. Una plantilla sin campos es de texto fijo.
    /// </summary>
    private static readonly System.Text.RegularExpressions.Regex Marca = new(@"\{([A-Za-z][A-Za-z0-9]*)\}", System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));

    private static string Rellenar(string texto, PlantillaDeCorreo plantilla, CorreoSaliente correo, bool esAsunto)
    {
        if (plantilla.Campos is not { Count: > 0 } campos)
        {
            return texto;
        }

        // Una sola pasada sobre el texto de la plantilla: un valor insertado nunca se vuelve a examinar, así que un dato que contenga «{otroCampo}» no puede colar el de otro campo.
        return Marca.Replace(texto, coincidencia =>
        {
            var campo = coincidencia.Groups[1].Value;
            if (!campos.Contains(campo))
            {
                return coincidencia.Value;
            }

            if (!correo.Datos.TryGetValue(campo, out var valor) || string.IsNullOrEmpty(valor))
            {
                throw new InvalidOperationException("Faltan datos de la plantilla del correo.");
            }

            return Limpiar(valor, conSaltos: !esAsunto);
        });
    }

    /// <summary>Los controles, los separadores de línea y de párrafo Unicode (U+2028, U+2029) y U+0085 se vuelven espacios; en el cuerpo se conserva solo el salto de línea.</summary>
    private static string Limpiar(string valor, bool conSaltos)
    {
        var recortado = valor.Length > LargoMaximoDeUnDato ? valor[..LargoMaximoDeUnDato] : valor;
        return string.Create(recortado.Length, (recortado, conSaltos), static (destino, par) =>
        {
            var (origen, conSaltos) = par;
            for (var i = 0; i < origen.Length; i++)
            {
                var c = origen[i];
                var categoria = char.GetUnicodeCategory(c);
                var peligroso = char.IsControl(c) || categoria is System.Globalization.UnicodeCategory.LineSeparator or System.Globalization.UnicodeCategory.ParagraphSeparator;
                destino[i] = peligroso && !(conSaltos && c == (char)10) ? (char)32 : c;
            }
        });
    }

    public MensajeDeCorreo DeCodigo(CorreoDeCodigo correo)
    {
        ArgumentNullException.ThrowIfNull(correo);
        var plantilla = PlantillasDeCorreoDeSeguridad.DeCodigo(correo.Proposito, correo.Codigo.Revelar());
        return new MensajeDeCorreo([correo.Destinatario], plantilla.Asunto, plantilla.Cuerpo);
    }

    public MensajeDeCorreo DeAlerta(string servicio, string motivo, DateTimeOffset primeraOcurrencia, int ocurrencias, IReadOnlyList<string> direcciones)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(servicio);
        ArgumentException.ThrowIfNullOrWhiteSpace(motivo);
        ArgumentNullException.ThrowIfNull(direcciones);
        var plantilla = PlantillasDeCorreoDeSeguridad.DeAlerta(servicio, motivo, primeraOcurrencia, ocurrencias);
        return new MensajeDeCorreo(direcciones, plantilla.Asunto, plantilla.Cuerpo);
    }
}

/// <summary>
/// La plantilla de la respuesta a una hoja de reclamación (PLA-X, REC-07, REC-13): el número de la hoja, la fecha de la respuesta y la respuesta misma; <b>sin el documento ni el domicilio</b> del consumidor (D11).
/// El texto de la respuesta es de quien responde y entra tal cual, sin saltos que abran una cabecera.
/// </summary>
public sealed class PlantillasDeCorreoDeReclamos : IProveedorDePlantillasDeCorreo
{
    public const string RespuestaDeReclamo = "respuesta-de-reclamo";

    public IReadOnlyDictionary<string, PlantillaDeCorreo> Plantillas { get; } = new Dictionary<string, PlantillaDeCorreo>(StringComparer.Ordinal)
    {
        [RespuestaDeReclamo] = new(
            "BabelHub: respondimos tu hoja de reclamación {numero}",
            "Hola, te escribe BabelHub.\n\nRespondimos tu hoja de reclamación N.° {numero} el {fecha}. Esta es la respuesta:\n\n{respuesta}\n\n" + PlantillasDeCorreoDeSeguridad.Firma,
            ["numero", "fecha", "respuesta"]),
    };
}
