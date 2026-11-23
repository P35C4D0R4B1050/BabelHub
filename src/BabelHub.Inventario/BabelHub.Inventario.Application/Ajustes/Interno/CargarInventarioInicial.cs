using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using BabelHub.Inventario.Application.Auditoria;
using BabelHub.Inventario.Application.Fuentes;
using BabelHub.Inventario.Application.Plataforma;

namespace BabelHub.Inventario.Application.Ajustes.Interno;

/// <summary>Lo que deja el registro de una corrida de la carga inicial en la auditoría (CIN-07): sin datos personales.</summary>
internal sealed record CorridaDeCargaInicial(Guid CorridaId, string HuellaSha256, int Cargadas, int YaCargadas, int Rechazadas);

/// <summary>El registro de la corrida (<c>CargaInicialEjecutada</c>) en la auditoría, en su propia transacción: si no se puede auditar, la operación falla (AUD-04) y la carga lo informa sin deshacer lo cargado.</summary>
internal sealed class RegistrarCorridaDeCargaInicial(AutorizadorDeAjustes autorizador, IAuditoria auditoria) : ICasoDeUso<CorridaDeCargaInicial, bool>
{
    public async Task<Resultado<bool>> Ejecutar(CorridaDeCargaInicial entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        if (await autorizador.ExigirAdministracionCentral(cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return Resultado<bool>.Fallo(rechazo);
        }

        await auditoria.Registrar(
            new SolicitudDeRegistro(AccionesAuditables.CargaInicialEjecutada)
            {
                Detalle = new Dictionary<string, string?>
                {
                    ["corridaId"] = entrada.CorridaId.ToString("D"),
                    ["huellaSha256"] = entrada.HuellaSha256,
                    ["cargadas"] = entrada.Cargadas.ToString(CultureInfo.InvariantCulture),
                    ["yaCargadas"] = entrada.YaCargadas.ToString(CultureInfo.InvariantCulture),
                    ["rechazadas"] = entrada.Rechazadas.ToString(CultureInfo.InvariantCulture),
                },
            },
            cancelacion).ConfigureAwait(false);
        return Resultado<bool>.Exito(true);
    }
}

/// <summary>
/// La carga inicial del inventario (CIN-08, CIN-11 a CIN-14, transversal-carga-inicial plan §1 y §11): no abre transacciones propias, llama a los <b>casos de uso públicos</b> de <c>Fuentes</c> y de <c>Ajustes</c> (cada uno con su
/// transacción, su auditoría y sus reglas) con el actor de la orden. Orden fijo: (1) autoriza (solo administración central) y valida <b>todo</b> antes de escribir; (2) asegura las sucursales (la primera registrada queda como la
/// recepción, FUE-19) y las rutas sin modificar las existentes; (3) registra la ficha de cada edición impresa; (4) por edición y sucursal, el stock: si la pareja ya tiene movimientos (un ajuste en su historial o físico o reservado
/// mayores que cero) la cuenta «ya cargada»; si no, ingreso de mercadería en la recepción o corrección de conteo en las demás, con la clave <c>carga-inicial-&lt;edición&gt;-&lt;código&gt;</c>. Ante el presupuesto agotado (SEG-30) se detiene
/// o, con <c>Esperar</c>, espera hasta 2 horas en total y reintenta; el presupuesto nunca se rodea ni se debilita.
/// </summary>
internal sealed class CargarInventarioInicial(
    AutorizadorDeAjustes autorizador,
    IDirectorioDeFuentes directorio,
    IExistencias existencias,
    ICasoDeUso<RegistrarFuenteEntrada, DetalleDeFuente> registrarFuente,
    ICasoDeUso<RegistrarRutaEntrada, RutaDefinida> registrarRuta,
    ICasoDeUso<RegistrarFichaDeEdicionEntrada, FichaDeEdicionGuardada> registrarFicha,
    ICasoDeUso<RegistrarIngresoDeMercaderiaEntrada, IngresoRegistrado> registrarIngreso,
    ICasoDeUso<RegistrarAjusteManualEntrada, AjusteRegistrado> registrarAjuste,
    ICasoDeUso<ConsultarHistorialDeAjustesEntrada, PaginaDeAjustes> historial,
    ICasoDeUso<CorridaDeCargaInicial, bool> registrarCorrida) : ICargaInicialDelInventario
{
    public static readonly TimeSpan EsperaMaxima = TimeSpan.FromHours(2);

    public const string Referencia = "carga inicial";

    public async Task<Resultado<ResumenDeCargaDeInventario>> Cargar(SolicitudDeCargaDeInventario solicitud, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        if (await autorizador.ExigirAdministracionCentral(cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return Resultado<ResumenDeCargaDeInventario>.Fallo(rechazo);
        }

        var sucursales = LectorDeSucursalesYRutas.LeerSucursales(solicitud.TextoDeLasSucursales);
        if (!sucursales.EsValido)
        {
            return Invalido(sucursales.Errores);
        }

        var codigos = sucursales.Valor!.Select(s => s.Codigo).ToList();
        var rutas = string.IsNullOrWhiteSpace(solicitud.TextoDeLasRutas) ? ResultadoDeLectura<IReadOnlyList<RutaDeLaCarga>>.Exito([]) : LectorDeSucursalesYRutas.LeerRutas(solicitud.TextoDeLasRutas, codigos);
        if (!rutas.EsValido)
        {
            return Invalido(rutas.Errores);
        }

        var plan = LectorDelPlanDeInventario.Leer(solicitud.TextoDelPlan, codigos);
        if (!plan.EsValido)
        {
            return Invalido(plan.Errores);
        }

        var corrida = new Corrida(Guid.NewGuid(), Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(solicitud.TextoDelPlan!))));
        Func<TimeSpan, CancellationToken, Task> espera = solicitud.Espera ?? ((tiempo, token) => Task.Delay(tiempo, token));

        await AsegurarSucursalesYRutas(sucursales.Valor!, rutas.Valor!, corrida, cancelacion).ConfigureAwait(false);
        var fichas = await AsegurarFichas(plan.Valor!, corrida, cancelacion).ConfigureAwait(false);
        await CargarElStock(plan.Valor!, fichas, solicitud.Esperar, espera, corrida, cancelacion).ConfigureAwait(false);

        var registroFallo = false;
        try
        {
            var registro = await registrarCorrida.Ejecutar(new CorridaDeCargaInicial(corrida.Id, corrida.Huella, corrida.StockCargado, corrida.YaCargado, corrida.Rechazadas), cancelacion).ConfigureAwait(false);
            registroFallo = !registro.EsExito;
        }
        catch (AuditoriaNoDisponibleException)
        {
            registroFallo = true;
        }

        return Resultado<ResumenDeCargaDeInventario>.Exito(corrida.Resumen(registroFallo));
    }

    private static Resultado<ResumenDeCargaDeInventario> Invalido(IReadOnlyList<ErrorDeLecturaDeLaCarga> errores) =>
        Resultado<ResumenDeCargaDeInventario>.Fallo(new ErrorDeAplicacion(CategoriaDeError.Validacion, "archivo-invalido", [.. errores.Take(20).Select(e => new ErrorDeCampo($"{e.Archivo}:{e.Linea.ToString(CultureInfo.InvariantCulture)}", e.Clave))]));

    private async Task AsegurarSucursalesYRutas(IReadOnlyList<SucursalDeLaCarga> sucursales, IReadOnlyList<RutaDeLaCarga> rutas, Corrida corrida, CancellationToken cancelacion)
    {
        foreach (var sucursal in sucursales)
        {
            if (await directorio.PorCodigo(sucursal.Codigo, cancelacion).ConfigureAwait(false) is not null)
            {
                corrida.SucursalesExistentes++;
                continue;
            }

            var alta = await registrarFuente.Ejecutar(new RegistrarFuenteEntrada(sucursal.Codigo, sucursal.Nombre, sucursal.Region, null), cancelacion).ConfigureAwait(false);
            if (alta.EsExito)
            {
                corrida.SucursalesNuevas++;
            }
            else if (alta.Error!.Categoria == CategoriaDeError.Conflicto)
            {
                // Otra ejecución la registró entre la consulta y el alta (la unicidad del código la garantiza el sistema, CIN-19).
                corrida.SucursalesExistentes++;
            }
            else
            {
                corrida.Rechazar($"sucursal {sucursal.Codigo}: {alta.Error!.Clave}");
            }
        }

        foreach (var ruta in rutas)
        {
            var origen = await directorio.PorCodigo(ruta.Origen, cancelacion).ConfigureAwait(false);
            var destino = await directorio.PorCodigo(ruta.Destino, cancelacion).ConfigureAwait(false);
            if (origen is null || destino is null)
            {
                corrida.Rechazar($"ruta {ruta.Origen}-{ruta.Destino}: sucursal-no-existe");
                continue;
            }

            // RegistrarRuta sobrescribe los días: una ruta existente no se llama (nunca se modifica lo que ya existe).
            if (await directorio.PlazoDeRuta(origen.Id, destino.Id, cancelacion).ConfigureAwait(false) is not null)
            {
                corrida.RutasExistentes++;
                continue;
            }

            var alta = await registrarRuta.Ejecutar(new RegistrarRutaEntrada(ruta.Origen, ruta.Destino, ruta.Dias), cancelacion).ConfigureAwait(false);
            if (alta.EsExito)
            {
                corrida.RutasNuevas++;
            }
            else
            {
                corrida.Rechazar($"ruta {ruta.Origen}-{ruta.Destino}: {alta.Error!.Clave}");
            }
        }
    }

    private async Task<HashSet<Guid>> AsegurarFichas(PlanDeInventario plan, Corrida corrida, CancellationToken cancelacion)
    {
        var conFicha = new HashSet<Guid>();
        foreach (var edicion in plan.Ediciones)
        {
            var ficha = await registrarFicha.Ejecutar(new RegistrarFichaDeEdicionEntrada(edicion.EdicionId, FormatoDeEdicionRegistrado.Impreso), cancelacion).ConfigureAwait(false);
            if (!ficha.EsExito)
            {
                corrida.Rechazar($"edicion {edicion.EdicionId:N}: {ficha.Error!.Clave}");
                continue;
            }

            conFicha.Add(edicion.EdicionId);
            if (ficha.Valor.SeEscribio)
            {
                corrida.EdicionesRegistradas++;
            }
        }

        return conFicha;
    }

    private async Task CargarElStock(PlanDeInventario plan, HashSet<Guid> conFicha, bool esperar, Func<TimeSpan, CancellationToken, Task> espera, Corrida corrida, CancellationToken cancelacion)
    {
        var historiales = new Dictionary<string, HashSet<Guid>>(StringComparer.Ordinal);
        var pendientes = plan.Ediciones.Where(e => conFicha.Contains(e.EdicionId)).SelectMany(e => e.Stock.Select(s => (Edicion: e, Stock: s))).ToList();
        var esperado = TimeSpan.Zero;
        for (var i = 0; i < pendientes.Count; i++)
        {
            var (edicion, stock) = pendientes[i];
            var fuente = await directorio.PorCodigo(stock.Codigo, cancelacion).ConfigureAwait(false);
            if (fuente is null)
            {
                corrida.Rechazar($"stock {stock.Codigo}: sucursal-no-existe");
                continue;
            }

            if (await YaTieneMovimientos(fuente, edicion.EdicionId, historiales, cancelacion).ConfigureAwait(false))
            {
                corrida.YaCargado++;
                continue;
            }

            while (true)
            {
                var resultado = await AplicarElStock(fuente, edicion.EdicionId, stock.Cantidad, cancelacion).ConfigureAwait(false);
                if (resultado.EsExito)
                {
                    corrida.StockCargado++;
                    break;
                }

                var error = resultado.Error!;
                if (error.Categoria == CategoriaDeError.Limite && DetalleDeLimiteDeAjustes.TryLeer(error.Errores, out var limite) && limite is not null)
                {
                    var plazo = TimeSpan.FromSeconds(Math.Max(1, limite.EsperaEnSegundos));
                    if (esperar && esperado + plazo <= EsperaMaxima)
                    {
                        esperado += plazo;
                        await espera(plazo, cancelacion).ConfigureAwait(false);
                        continue;
                    }

                    // Se detiene sin perder lo cargado: lo que falta queda pendiente y repetir la orden lo reanuda.
                    corrida.PendientesPorLimite = pendientes.Count - i;
                    corrida.ReanudaDesde = limite.DisponibleDesde;
                    return;
                }

                if (error.Clave is "idempotencia-en-conflicto" or "idempotencia-en-curso" or ClavesDeAjustes.SinCambios)
                {
                    corrida.YaCargado++;
                }
                else
                {
                    corrida.Rechazar($"stock {stock.Codigo} edicion {edicion.EdicionId:N}: {error.Clave}");
                }

                break;
            }
        }
    }

    private async Task<Resultado<bool>> AplicarElStock(FuenteDeStock fuente, Guid edicionId, int cantidad, CancellationToken cancelacion)
    {
        var clave = $"carga-inicial-{edicionId:N}-{fuente.Codigo}";
        if (fuente.EsRecepcion)
        {
            var ingreso = await registrarIngreso.Ejecutar(new RegistrarIngresoDeMercaderiaEntrada(edicionId, null, cantidad, Referencia, null, clave), cancelacion).ConfigureAwait(false);
            return ingreso.EsExito ? Resultado<bool>.Exito(true) : Resultado<bool>.Fallo(ingreso.Error!);
        }

        var ajuste = await registrarAjuste.Ejecutar(new RegistrarAjusteManualEntrada(fuente.Codigo, edicionId, "correccion-de-conteo", null, cantidad, Referencia, clave), cancelacion).ConfigureAwait(false);
        return ajuste.EsExito ? Resultado<bool>.Exito(true) : Resultado<bool>.Fallo(ajuste.Error!);
    }

    /// <summary>CIN-13: la pareja (sucursal, edición) ya tiene movimientos si hay un ajuste en el historial de la sucursal o existencias físicas o reservadas.</summary>
    private async Task<bool> YaTieneMovimientos(FuenteDeStock fuente, Guid edicionId, Dictionary<string, HashSet<Guid>> historiales, CancellationToken cancelacion)
    {
        var pagina = await existencias.DeLaFuente(new ConsultaDeExistenciasDeFuente(fuente.Id, [edicionId], null, 1, 1), cancelacion).ConfigureAwait(false);
        if (pagina.Elementos.Any(e => e.Fisico > 0 || e.Reservado > 0))
        {
            return true;
        }

        if (!historiales.TryGetValue(fuente.Codigo, out var conAjustes))
        {
            conAjustes = await LeerElHistorial(fuente.Codigo, cancelacion).ConfigureAwait(false);
            historiales[fuente.Codigo] = conAjustes;
        }

        return conAjustes.Contains(edicionId);
    }

    private async Task<HashSet<Guid>> LeerElHistorial(string codigo, CancellationToken cancelacion)
    {
        const int Tamano = 100;
        var ediciones = new HashSet<Guid>();
        for (var numero = 1; ; numero++)
        {
            var pagina = await historial.Ejecutar(new ConsultarHistorialDeAjustesEntrada(codigo, numero, Tamano), cancelacion).ConfigureAwait(false);
            if (!pagina.EsExito)
            {
                return ediciones;
            }

            foreach (var ajuste in pagina.Valor.Elementos)
            {
                ediciones.Add(ajuste.EdicionId);
            }

            if (numero * Tamano >= pagina.Valor.Total || pagina.Valor.Elementos.Count == 0)
            {
                return ediciones;
            }
        }
    }

    private sealed class Corrida(Guid id, string huella)
    {
        private readonly List<string> _problemas = [];

        public Guid Id { get; } = id;

        public string Huella { get; } = huella;

        public int SucursalesNuevas { get; set; }

        public int SucursalesExistentes { get; set; }

        public int RutasNuevas { get; set; }

        public int RutasExistentes { get; set; }

        public int EdicionesRegistradas { get; set; }

        public int StockCargado { get; set; }

        public int YaCargado { get; set; }

        public int Rechazadas { get; private set; }

        public int PendientesPorLimite { get; set; }

        public DateTimeOffset? ReanudaDesde { get; set; }

        public void Rechazar(string problema)
        {
            Rechazadas++;
            if (_problemas.Count < 50)
            {
                _problemas.Add(problema);
            }
        }

        public ResumenDeCargaDeInventario Resumen(bool registroFallo) =>
            new(Id, Huella, SucursalesNuevas, SucursalesExistentes, RutasNuevas, RutasExistentes, EdicionesRegistradas, StockCargado, YaCargado, Rechazadas, PendientesPorLimite, ReanudaDesde, registroFallo, _problemas);
    }
}
