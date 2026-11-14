using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using BabelHub.Shared.Contratos;
using BabelHub.Shared.Errores;

namespace BabelHub.ArchitectureTests.Contratos;

/// <summary>
/// Forma de los contratos entre el E-commerce y el Inventario (<c>BabelHub.Shared.Contratos</c>, versión v1): solo DTOs
/// inmutables con nombres JSON explícitos, constantes y un catálogo de operaciones como dato; sin reglas, sin tipos del Dominio
/// ni de la Aplicación (AGENTS.md regla 6, <c>docs/03-diseno-de-software/04-modulos-y-contratos.md</c> §5 y §6).
/// </summary>
public sealed partial class ReglasDeContratosTests
{
    private const string EspacioDeContratos = "BabelHub.Shared.Contratos";
    private const string EspacioDeErrores = "BabelHub.Shared.Errores";

    private static readonly Type[] TiposSimples =
    [
        typeof(string), typeof(int), typeof(long), typeof(bool), typeof(Guid), typeof(DateTimeOffset), typeof(DateOnly),
    ];

    private static IEnumerable<Type> TiposDeContratos() =>
        BabelHub.Shared.AssemblyMarker.Assembly.GetTypes()
            .Where(t => t.IsPublic && t.Namespace is { } ns && (ns.StartsWith(EspacioDeContratos, StringComparison.Ordinal) || ns == EspacioDeErrores));

    private static bool EsRecord(Type tipo) => tipo.GetMethod("<Clone>$", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) is not null;

    private static bool EsDeConstantes(Type tipo) => tipo.IsAbstract && tipo.IsSealed;

    /// <summary>Los DTOs: records públicos (todo lo demás son constantes o el descriptor del catálogo).</summary>
    private static IEnumerable<Type> Dtos() => TiposDeContratos().Where(EsRecord).Where(t => t != typeof(OperacionDeContrato));

    [Fact]
    public void Todo_lo_publico_de_los_contratos_es_un_record_inmutable_o_una_clase_de_constantes()
    {
        var sueltos = TiposDeContratos().Where(t => !EsRecord(t) && !EsDeConstantes(t)).Select(t => t.FullName).ToList();

        Assert.True(sueltos.Count == 0, $"Los contratos solo tienen records y clases estáticas de constantes: {string.Join(", ", sueltos)}.");
    }

    [Fact]
    public void Los_dtos_son_records_sellados_y_Shared_no_referencia_ningun_proyecto_de_BabelHub()
    {
        foreach (var dto in Dtos())
        {
            Assert.True(dto.IsSealed || dto.IsAbstract || dto == typeof(ProblemaDto), $"{dto.Name} debe ser sellado (solo ProblemaDto es la base de los errores con cuerpo propio).");
        }

        var referencias = BabelHub.Shared.AssemblyMarker.Assembly.GetReferencedAssemblies().Select(a => a.Name!)
            .Where(n => n.StartsWith("BabelHub.", StringComparison.Ordinal)).ToList();
        Assert.Empty(referencias);
    }

    [Fact]
    public void Cada_propiedad_de_un_dto_tiene_su_nombre_json_en_camel_case_y_no_se_puede_asignar_despues_de_crearla()
    {
        foreach (var dto in Dtos())
        {
            foreach (var propiedad in PropiedadesDe(dto))
            {
                var nombre = propiedad.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name;
                Assert.True(nombre is not null, $"{dto.Name}.{propiedad.Name} no declara [JsonPropertyName].");
                Assert.True(CamelCase().IsMatch(nombre), $"{dto.Name}.{propiedad.Name}: el nombre JSON «{nombre}» no está en camelCase.");

                var asignador = propiedad.SetMethod;
                Assert.True(
                    asignador is null || asignador.ReturnParameter.GetRequiredCustomModifiers().Any(m => m.Name == "IsExternalInit"),
                    $"{dto.Name}.{propiedad.Name} es modificable después de crearse.");
            }
        }
    }

    [Fact]
    public void Las_propiedades_de_un_dto_son_tipos_simples_listas_de_ellos_u_otros_dtos()
    {
        var dtos = Dtos().ToHashSet();
        foreach (var dto in dtos)
        {
            foreach (var propiedad in PropiedadesDe(dto))
            {
                Assert.True(EsPermitido(propiedad.PropertyType, dtos), $"{dto.Name}.{propiedad.Name}: el tipo {propiedad.PropertyType} no es un tipo de contrato.");
            }
        }
    }

    [Fact]
    public void Ningun_dto_lleva_secretos_enlaces_trazas_ni_la_identidad_del_llamador()
    {
        var prohibidos = new[] { "token", "password", "contrasena", "url", "enlace", "tarjeta", "stack", "actor", "ip", "direccion" };
        foreach (var dto in Dtos())
        {
            foreach (var propiedad in PropiedadesDe(dto))
            {
                var nombre = propiedad.GetCustomAttribute<JsonPropertyNameAttribute>()!.Name.ToLowerInvariant();
                Assert.DoesNotContain(prohibidos, p => nombre == p || nombre.StartsWith(p, StringComparison.Ordinal) && char.IsUpper(propiedad.GetCustomAttribute<JsonPropertyNameAttribute>()!.Name[p.Length]));
            }
        }

        // El titular solo viaja en los dos cuerpos de la baja y de los pendientes; la identidad del llamador sale del token o del alcance.
        var conTitular = Dtos().Where(d => PropiedadesDe(d).Any(p => p.GetCustomAttribute<JsonPropertyNameAttribute>()!.Name == "titularId")).Select(d => d.Name).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(["BajaDeTitularDto", "ConsultaDePendientesDto"], conTitular);

        var solicitudes = CatalogoDeOperaciones.V1.Select(o => o.Solicitud).OfType<Type>().Distinct();
        foreach (var solicitud in solicitudes)
        {
            Assert.DoesNotContain(PropiedadesDe(solicitud), p => p.GetCustomAttribute<JsonPropertyNameAttribute>()!.Name is "usuarioId" or "clienteId");
        }
    }

    [Fact]
    public void Los_dtos_no_tienen_metodos_propios()
    {
        foreach (var dto in Dtos())
        {
            var propios = dto.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(m => !m.IsSpecialName && !EsMiembroDeRecord(m))
                .Select(m => m.Name)
                .ToList();

            Assert.True(propios.Count == 0, $"{dto.Name} tiene métodos propios (los contratos no llevan reglas): {string.Join(", ", propios)}.");
        }
    }

    [Fact]
    public void Las_clases_de_constantes_solo_exponen_constantes_o_listas_de_ellas()
    {
        foreach (var tipo in TiposDeContratos().Where(EsDeConstantes).Where(t => t != typeof(CodigosDeError)))
        {
            var metodos = tipo.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly).Where(m => !m.IsSpecialName).Select(m => m.Name).ToList();
            Assert.True(metodos.Count == 0, $"{tipo.Name} tiene métodos públicos (los contratos no llevan reglas): {string.Join(", ", metodos)}.");

            foreach (var campo in tipo.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                Assert.True(campo.IsLiteral || campo.IsInitOnly, $"{tipo.Name}.{campo.Name} es una variable pública modificable.");
            }

            foreach (var propiedad in tipo.GetProperties(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                Assert.True(propiedad.SetMethod is null, $"{tipo.Name}.{propiedad.Name} se puede modificar.");
            }
        }
    }

    [Fact]
    public void Todo_dto_se_serializa_con_los_nombres_declarados_y_se_lee_igual()
    {
        var opciones = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        foreach (var dto in Dtos().Where(t => !t.IsAbstract && !t.ContainsGenericParameters))
        {
            var muestra = Muestras.Crear(dto);
            var json = JsonSerializer.Serialize(muestra, dto, opciones);
            using var documento = JsonDocument.Parse(json);
            var esperados = PropiedadesDe(dto)
                .Select(p => p.GetCustomAttribute<JsonPropertyNameAttribute>()!.Name)
                .Order(StringComparer.Ordinal)
                .ToList();
            var presentes = documento.RootElement.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToList();
            Assert.Equal(esperados, presentes);

            var leido = JsonSerializer.Deserialize(json, dto, opciones);
            Assert.Equal(json, JsonSerializer.Serialize(leido, dto, opciones));
        }
    }

    [Fact]
    public void El_catalogo_declara_cada_operacion_con_nombre_ruta_y_alcance_validos()
    {
        var nombres = new HashSet<string>(StringComparer.Ordinal);
        var rutas = new HashSet<string>(StringComparer.Ordinal);
        foreach (var operacion in CatalogoDeOperaciones.V1)
        {
            Assert.Matches(NombreDeOperacion(), operacion.Nombre);
            Assert.True(nombres.Add(operacion.Nombre), $"Operación repetida: {operacion.Nombre}.");

            var servicio = operacion.Nombre.Split('.')[0];
            var prefijo = servicio == "inventario" ? RutasDeServicio.Inventario : RutasDeServicio.Ecommerce;
            Assert.StartsWith(prefijo + "/", operacion.Ruta, StringComparison.Ordinal);
            Assert.Contains(operacion.Metodo, new[] { "GET", "POST", "PUT", "DELETE", "PATCH" });

            var firma = operacion.Metodo + " " + RutaSinNombres().Replace(operacion.Ruta, "{}");
            Assert.True(rutas.Add(firma), $"El método y la ruta de {operacion.Nombre} están repetidos: {firma}.");

            Assert.Contains(operacion.Alcance, AlcancesDeCredencial.Todos);
            Assert.True(
                operacion.Alcance != AlcancesDeCredencial.OperacionDeUsuario || operacion.RequiereTokenDeUsuario,
                $"{operacion.Nombre}: una operación de usuario exige el token del usuario.");

            foreach (var codigo in operacion.Errores)
            {
                Assert.Matches(CodigoDeError(), codigo);
                Assert.StartsWith(servicio == "inventario" ? "INV-" : "ECO-", codigo, StringComparison.Ordinal);
            }

            Assert.True(operacion.Metodo != "GET" || operacion.Solicitud is null, $"{operacion.Nombre}: un GET no lleva cuerpo.");
            Assert.True(operacion.Metodo != "GET" || operacion.EsLectura, $"{operacion.Nombre}: un GET es de solo lectura.");
            Assert.True(!operacion.EsLectura || !operacion.RequiereClaveDeIdempotencia, $"{operacion.Nombre}: una lectura no necesita clave de idempotencia.");
        }
    }

    [Fact]
    public void Las_rutas_usan_segmentos_en_minusculas_con_guiones_y_cada_operacion_declara_sus_errores_basicos()
    {
        // Las dos consultas de administración cuyo cuerpo define transversal-auditoria (AUD-2) todavía no tienen DTO.
        var cuerpoPendiente = new[] { "inventario.auditoria.consultar", "inventario.alertas.listar" };
        foreach (var operacion in CatalogoDeOperaciones.V1)
        {
            foreach (var segmento in operacion.Ruta.Split('/', StringSplitOptions.RemoveEmptyEntries).Skip(2))
            {
                Assert.Matches("^([a-z0-9]+(-[a-z0-9]+)*|\\{[a-zA-Z]+\\})$", segmento);
            }

            var servicio = operacion.Nombre.StartsWith("inventario.", StringComparison.Ordinal) ? "INV" : "ECO";
            Assert.Contains($"{servicio}-NO-AUTENTICADO", operacion.Errores);
            Assert.True(
                operacion.Solicitud is null && operacion.Parametros.Count == 0 || operacion.Errores.Contains($"{servicio}-VALIDACION-INVALIDA"),
                $"{operacion.Nombre}: una operación con entrada declara VALIDACION-INVALIDA.");
            Assert.True(
                operacion.Respuesta is not null || operacion.CodigoDeExito is 202 or 204 || cuerpoPendiente.Contains(operacion.Nombre),
                $"{operacion.Nombre}: sin cuerpo de respuesta solo con 202 o 204.");
            Assert.True(operacion.Metodo != "GET" || operacion.CodigoDeExito == 200, $"{operacion.Nombre}: un GET responde 200.");
        }
    }

    [Fact]
    public void Ninguna_operacion_repite_un_codigo_de_error()
    {
        foreach (var operacion in CatalogoDeOperaciones.V1)
        {
            Assert.True(operacion.Errores.Distinct(StringComparer.Ordinal).Count() == operacion.Errores.Count, $"{operacion.Nombre} repite un código de error.");
        }
    }

    [Fact]
    public void Crear_una_reserva_sobre_un_pedido_cuya_reserva_ya_no_esta_pendiente_responde_estado_invalido()
    {
        var crear = CatalogoDeOperaciones.V1.Single(o => o.Nombre == "inventario.reservas.crear");

        Assert.Contains("INV-RESERVA-ESTADO-INVALIDO", crear.Errores);
    }

    [Fact]
    public void Los_parametros_de_consulta_solo_se_declaran_para_operaciones_del_catalogo()
    {
        var parametrizadas = CatalogoDeOperaciones.V1.Where(o => o.Parametros.Count > 0).Select(o => o.Nombre).Order(StringComparer.Ordinal).ToList();

        Assert.Equal(
            [
                "inventario.alertas.listar", "inventario.auditoria.consultar", "inventario.fuentes.existencias", "inventario.historial.consultar",
                "inventario.ingresos.listar", "inventario.traslados.listar", "inventario.unidades-vendidas.consultar",
            ],
            parametrizadas);
    }

    [Fact]
    public void Todo_tipo_publico_de_Shared_esta_en_los_espacios_de_contratos_o_de_errores()
    {
        var fuera = BabelHub.Shared.AssemblyMarker.Assembly.GetTypes()
            .Where(t => t.IsPublic && t != typeof(BabelHub.Shared.AssemblyMarker))
            .Where(t => t.Namespace is not { } ns || !(ns.StartsWith(EspacioDeContratos, StringComparison.Ordinal) || ns == EspacioDeErrores))
            .Select(t => t.FullName)
            .ToList();

        Assert.True(fuera.Count == 0, $"Tipos públicos de Shared fuera de los espacios vigilados: {string.Join(", ", fuera)}.");
    }

    [Fact]
    // Alcance conocido: no resuelve propiedades de MSBuild ni cubre Update/Remove ni Directory.Build.props o .targets.
    public void Los_proyectos_de_produccion_no_enlazan_codigo_de_afuera_de_su_carpeta()
    {
        var enlaces = new List<string>();
        foreach (var proyecto in Directory.EnumerateFiles(Path.Combine(Proyectos.Raiz, "src"), "*.csproj", SearchOption.AllDirectories)
                     .Where(r => !r.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)))
        {
            var xml = System.Xml.Linq.XDocument.Load(proyecto);
            foreach (var elemento in xml.Descendants().Where(e => e.Name.LocalName is "Compile" or "Content" or "None" or "EmbeddedResource" or "Reference" or "Import"))
            {
                var valor = (string?)elemento.Attribute("Include") ?? (string?)elemento.Attribute("HintPath") ?? (string?)elemento.Attribute("Project") ?? string.Empty;
                if (valor.Contains("..", StringComparison.Ordinal))
                {
                    enlaces.Add($"{Path.GetFileName(proyecto)}: {elemento.Name.LocalName} {valor}");
                }
            }
        }

        Assert.True(enlaces.Count == 0, $"Un proyecto de producción enlaza archivos de afuera de su carpeta y evade las reglas de referencias (1, 2, 5, 6 y 13): {string.Join("; ", enlaces)}.");
    }

    [Fact]
    public void Los_tipos_de_solicitud_y_respuesta_del_catalogo_son_dtos_y_no_queda_ningun_dto_huerfano()
    {
        var dtos = Dtos().ToHashSet();
        var alcanzables = new HashSet<Type>();
        foreach (var operacion in CatalogoDeOperaciones.V1)
        {
            foreach (var tipo in new[] { operacion.Solicitud, operacion.Respuesta }.OfType<Type>())
            {
                Assert.Contains(tipo.IsGenericType ? tipo.GetGenericTypeDefinition() : tipo, dtos);
                Alcanzar(tipo, dtos, alcanzables);
            }
        }

        // Los errores con cuerpo propio (derivados de ProblemaDto) viajan en la respuesta de error, no en la de éxito.
        foreach (var conCuerpo in dtos.Where(t => typeof(ProblemaDto).IsAssignableFrom(t)))
        {
            Alcanzar(conCuerpo, dtos, alcanzables);
        }

        var huerfanos = dtos.Except(alcanzables).Select(t => t.Name).Order(StringComparer.Ordinal).ToList();
        Assert.True(huerfanos.Count == 0, $"DTOs que ninguna operación usa: {string.Join(", ", huerfanos)}.");
    }

    [Fact]
    public void Los_alcances_de_credencial_son_los_doce_de_la_matriz_y_tienen_nombres_estables()
    {
        Assert.Equal(12, AlcancesDeCredencial.Todos.Count);
        Assert.Equal(AlcancesDeCredencial.Todos.Count, AlcancesDeCredencial.Todos.Distinct(StringComparer.Ordinal).Count());
        Assert.All(AlcancesDeCredencial.Todos, a => Assert.Matches("^[a-z][a-z-]{2,40}$", a));
    }

    private static IEnumerable<PropertyInfo> PropiedadesDe(Type dto) =>
        dto.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.Name != "EqualityContract");

    private static bool EsPermitido(Type tipo, IReadOnlySet<Type> dtos)
    {
        var subyacente = Nullable.GetUnderlyingType(tipo) ?? tipo;
        if (subyacente.IsGenericParameter || TiposSimples.Contains(subyacente) || dtos.Contains(subyacente))
        {
            return true;
        }

        if (!subyacente.IsGenericType)
        {
            return false;
        }

        var definicion = subyacente.GetGenericTypeDefinition();
        var argumentos = subyacente.GetGenericArguments();
        if (definicion == typeof(IReadOnlyList<>))
        {
            return EsPermitido(argumentos[0], dtos);
        }

        if (definicion == typeof(IReadOnlyDictionary<,>))
        {
            return argumentos[0] == typeof(string) && EsPermitido(argumentos[1], dtos);
        }

        return dtos.Contains(definicion) && argumentos.All(a => EsPermitido(a, dtos));
    }

    private static void Alcanzar(Type tipo, IReadOnlySet<Type> dtos, ISet<Type> visitados)
    {
        var subyacente = Nullable.GetUnderlyingType(tipo) ?? tipo;
        if (subyacente.IsGenericType)
        {
            var definicion = subyacente.GetGenericTypeDefinition();
            if (dtos.Contains(definicion))
            {
                if (!visitados.Add(definicion))
                {
                    return;
                }

                foreach (var propiedad in PropiedadesDe(definicion))
                {
                    Alcanzar(propiedad.PropertyType, dtos, visitados);
                }
            }

            foreach (var argumento in subyacente.GetGenericArguments())
            {
                Alcanzar(argumento, dtos, visitados);
            }

            return;
        }

        if (!dtos.Contains(subyacente) || !visitados.Add(subyacente))
        {
            return;
        }

        foreach (var propiedad in PropiedadesDe(subyacente))
        {
            Alcanzar(propiedad.PropertyType, dtos, visitados);
        }
    }

    private static bool EsMiembroDeRecord(MethodInfo metodo) =>
        metodo.Name is "Equals" or "GetHashCode" or "ToString" or "Deconstruct" or "<Clone>$" or "PrintMembers"
        || metodo.Name.StartsWith("op_", StringComparison.Ordinal);

    [GeneratedRegex("^[a-z][a-zA-Z0-9]*$")]
    private static partial Regex CamelCase();

    [GeneratedRegex("^(inventario|ecommerce)(\\.[a-z0-9]+(-[a-z0-9]+)*){2,3}$")]
    private static partial Regex NombreDeOperacion();

    [GeneratedRegex("\\{[^}]+\\}")]
    private static partial Regex RutaSinNombres();

    [GeneratedRegex("^(INV|ECO)(-[A-Z0-9]+)+$")]
    private static partial Regex CodigoDeError();
}

/// <summary>Instancias de muestra de los DTOs, con todos sus valores rellenos, para comprobar la serialización.</summary>
internal static class Muestras
{
    public static object Crear(Type tipo)
    {
        var constructor = tipo.GetConstructors(BindingFlags.Public | BindingFlags.Instance).OrderByDescending(c => c.GetParameters().Length).First();
        var argumentos = constructor.GetParameters().Select(p => Valor(p.ParameterType)).ToArray();
        return constructor.Invoke(argumentos);
    }

    private static object? Valor(Type tipo)
    {
        var subyacente = Nullable.GetUnderlyingType(tipo) ?? tipo;
        if (subyacente == typeof(string))
        {
            return "valor";
        }

        if (subyacente == typeof(int) || subyacente == typeof(long))
        {
            return Convert.ChangeType(7, subyacente, CultureInfo.InvariantCulture);
        }

        if (subyacente == typeof(bool))
        {
            return true;
        }

        if (subyacente == typeof(Guid))
        {
            return Guid.Parse("11111111-2222-3333-4444-555555555555");
        }

        if (subyacente == typeof(DateTimeOffset))
        {
            return new DateTimeOffset(2026, 11, 14, 15, 0, 0, TimeSpan.Zero);
        }

        if (subyacente == typeof(DateOnly))
        {
            return new DateOnly(2026, 11, 14);
        }

        if (subyacente.IsGenericType && subyacente.GetGenericTypeDefinition() == typeof(IReadOnlyList<>))
        {
            var elemento = subyacente.GetGenericArguments()[0];
            var lista = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(elemento))!;
            lista.Add(Valor(elemento));
            return lista;
        }

        if (subyacente.IsGenericType && subyacente.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>))
        {
            var argumentos = subyacente.GetGenericArguments();
            var diccionario = (IDictionary)Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(argumentos))!;
            diccionario.Add("clave", Valor(argumentos[1]));
            return diccionario;
        }

        return Crear(subyacente);
    }
}
