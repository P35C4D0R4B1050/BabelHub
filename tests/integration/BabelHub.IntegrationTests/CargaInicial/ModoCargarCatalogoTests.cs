using System.Reflection;
using BabelHub.Ecommerce.Api.Plataforma;
using BabelHub.Ecommerce.Application.Catalogo;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace BabelHub.IntegrationTests.CargaInicial;

/// <summary>La orden <c>cargar-catalogo</c> del E-commerce: sus argumentos, su fallo sin configuración y que ninguna ruta la expone (T05; CIN-13, CIN-17, CIN-18, CIN-20; AC-CIN-13).</summary>
public sealed class ModoCargarCatalogoTests
{
    [Fact]
    [Trait("Criterio", "AC-CIN-13")]
    public void Los_argumentos_se_leen_y_un_argumento_desconocido_o_faltante_se_rechaza_con_una_clave()
    {
        var (completos, _) = ArgumentosDeCargarCatalogo.Leer(["--plan", "p.json", "--archivo", "c.csv", "--portadas", "carpeta"]);

        Assert.Equal(new ArgumentosDeCargarCatalogo("c.csv", "carpeta", "p.json"), completos);
        Assert.Equal("falta-archivo", ArgumentosDeCargarCatalogo.Leer(["--portadas", "c", "--plan", "p"]).Error);
        Assert.Equal("falta-portadas", ArgumentosDeCargarCatalogo.Leer(["--archivo", "a", "--plan", "p"]).Error);
        Assert.Equal("falta-plan", ArgumentosDeCargarCatalogo.Leer(["--archivo", "a", "--portadas", "c"]).Error);
        Assert.Equal("falta-el-valor-de-plan", ArgumentosDeCargarCatalogo.Leer(["--plan"]).Error);
        Assert.Equal("falta-el-valor-de-archivo", ArgumentosDeCargarCatalogo.Leer(["--archivo", "--plan", "p"]).Error);
        Assert.Equal("argumento-desconocido", ArgumentosDeCargarCatalogo.Leer(["--archivo", "a", "--portadas", "c", "--plan", "p", "--forzar"]).Error);
    }

    [Fact]
    [Trait("Criterio", "AC-CIN-13")]
    public async Task Sin_la_configuracion_del_servicio_termina_antes_de_escribir_nombrando_las_variables_y_sin_imprimir_valores_ni_rutas()
    {
        var carpeta = Path.Combine(Path.GetTempPath(), "babelhub-carga-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(carpeta);
        try
        {
            var archivo = Path.Combine(carpeta, "catalogo.csv");
            var plan = Path.Combine(carpeta, "plan.json");
            await File.WriteAllTextAsync(archivo, "clave;titulo\n");
            var salida = new StringWriter();
            var error = new StringWriter();

            var codigo = await ModoCargarCatalogo.Ejecutar(["cargar-catalogo", "--archivo", archivo, "--portadas", carpeta, "--plan", plan], salida, error);

            var texto = error.ToString();
            Assert.Equal(1, codigo);
            Assert.Contains("configuración inválida", texto, StringComparison.Ordinal);
            Assert.Contains("BABELHUB__", texto, StringComparison.Ordinal);
            Assert.DoesNotContain(carpeta, texto, StringComparison.Ordinal);
            Assert.Equal(string.Empty, salida.ToString());
            Assert.False(File.Exists(plan));
            Assert.False(File.Exists(plan + ".tmp"));
        }
        finally
        {
            Directory.Delete(carpeta, recursive: true);
        }
    }

    [Fact]
    [Trait("Criterio", "AC-CIN-13")]
    public async Task Un_archivo_ilegible_o_que_no_es_texto_se_rechaza_sin_nombrar_la_ruta()
    {
        var carpeta = Path.Combine(Path.GetTempPath(), "babelhub-carga-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(carpeta);
        try
        {
            var binario = Path.Combine(carpeta, "binario.csv");
            await File.WriteAllBytesAsync(binario, [0xFF, 0xFE, 0xFD, 0x00]);
            var error = new StringWriter();

            var inexistente = await ModoCargarCatalogo.Ejecutar(["cargar-catalogo", "--archivo", Path.Combine(carpeta, "no-existe.csv"), "--portadas", carpeta, "--plan", Path.Combine(carpeta, "p.json")], new StringWriter(), error);
            var invalido = await ModoCargarCatalogo.Ejecutar(["cargar-catalogo", "--archivo", binario, "--portadas", carpeta, "--plan", Path.Combine(carpeta, "p.json")], new StringWriter(), error);

            Assert.Equal((1, 1), (inexistente, invalido));
            Assert.Contains("archivo-ilegible", error.ToString(), StringComparison.Ordinal);
            Assert.Contains("no-es-texto-valido", error.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain(carpeta, error.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(carpeta, recursive: true);
        }
    }

    [Fact]
    [Trait("Criterio", "AC-CIN-13")]
    public async Task Un_archivo_de_mas_de_2_MB_se_rechaza_antes_de_componer_nada()
    {
        var carpeta = Path.Combine(Path.GetTempPath(), "babelhub-carga-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(carpeta);
        try
        {
            var grande = Path.Combine(carpeta, "grande.csv");
            await File.WriteAllBytesAsync(grande, new byte[ModoCargarCatalogo.MaximoDeBytes + 1]);
            var error = new StringWriter();

            var codigo = await ModoCargarCatalogo.Ejecutar(["cargar-catalogo", "--archivo", grande, "--portadas", carpeta, "--plan", Path.Combine(carpeta, "p.json")], new StringWriter(), error);

            Assert.Equal(1, codigo);
            Assert.Contains("archivo-demasiado-grande", error.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(carpeta, recursive: true);
        }
    }

    [Fact]
    [Trait("Criterio", "AC-CIN-13")]
    public void Ninguna_ruta_ni_accion_del_servicio_expone_la_carga_y_solo_la_orden_de_operacion_usa_su_contrato()
    {
        var api = typeof(ModoCargarCatalogo).Assembly;
        var contrato = typeof(ICargaInicialDelCatalogo);
        var usuarios = api.GetTypes().Where(t => Usa(t, contrato)).Select(t => (t.DeclaringType ?? t).Name).Distinct().ToList();

        Assert.Equal(new[] { nameof(ModoCargarCatalogo) }, usuarios.ToArray());
        var plantillas = api.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t))
            .SelectMany(t => t.GetCustomAttributes<RouteAttribute>(inherit: true).Select(a => a.Template ?? string.Empty)
                .Concat(t.GetMethods().SelectMany(m => m.GetCustomAttributes<HttpMethodAttribute>().Select(a => a.Template ?? string.Empty))))
            .ToList();
        Assert.DoesNotContain(plantillas, p => System.Text.RegularExpressions.Regex.IsMatch(p, "(^|[/{}-])(carga|cargar|carga-inicial|cargar-catalogo)([/{}-]|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase));
    }

    private static bool Usa(Type tipo, Type contrato)
    {
        const BindingFlags Todo = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        if (tipo.GetConstructors(Todo).Any(c => c.GetParameters().Any(p => p.ParameterType == contrato)) || tipo.GetFields(Todo).Any(f => f.FieldType == contrato) || tipo.GetProperties(Todo).Any(p => p.PropertyType == contrato))
        {
            return true;
        }

        // Un método del tipo que pida el contrato por servicio (GetRequiredService<ICargaInicialDelCatalogo>) lo declara en su cuerpo: se busca por el cuerpo de IL.
        return tipo.GetMethods(Todo).Any(m => m.GetMethodBody()?.GetILAsByteArray() is { } il && ContieneElToken(il, m.Module, contrato));
    }

    private static bool ContieneElToken(byte[] il, Module modulo, Type tipo)
    {
        // Los argumentos genéricos de una llamada (GetRequiredService<T>) llegan como un MethodSpec; se comparan por los tokens de método y de tipo del cuerpo.
        for (var i = 0; i + 4 < il.Length; i++)
        {
            var token = BitConverter.ToInt32(il, i + 1);
            if (il[i] is 0x28 or 0x6F or 0x73 or 0x74 or 0xD0)
            {
                try
                {
                    var miembro = modulo.ResolveMember(token, tipo.IsGenericType ? tipo.GetGenericArguments() : null, null);
                    if (miembro is MethodInfo { IsGenericMethod: true } metodo && metodo.GetGenericArguments().Contains(tipo))
                    {
                        return true;
                    }

                    if (miembro is Type t && t == tipo)
                    {
                        return true;
                    }
                }
                catch (Exception e) when (e is ArgumentException or NotSupportedException or InvalidOperationException or BadImageFormatException)
                {
                    // Un token que no es de un miembro: se sigue con el siguiente byte.
                }
            }
        }

        return false;
    }
}
