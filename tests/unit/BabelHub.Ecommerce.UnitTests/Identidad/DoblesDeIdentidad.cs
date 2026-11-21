using System.Security.Cryptography;
using System.Text;
using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Identidad.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Identidad;
using BabelHub.Ecommerce.Domain.Plataforma;
using PropositoDeCodigo = BabelHub.Ecommerce.Domain.Identidad.PropositoDeCodigo;

namespace BabelHub.Ecommerce.UnitTests.Identidad;

/// <summary>Una renovación guardada como un valor inmutable: sirve de instantánea simple para la reversión de la unidad de trabajo de prueba.</summary>
internal sealed record RenovacionGuardada(string Huella, Guid UsuarioId, Guid Familia, DateTimeOffset Emitida, DateTimeOffset ExpiraAbsoluta, DateTimeOffset? UsadaEn, DateTimeOffset? RevocadaEn)
{
    public Renovacion ADominio() => Renovacion.Restaurar(Huella, UsuarioId, Familia, Emitida, ExpiraAbsoluta, UsadaEn, RevocadaEn);

    public static RenovacionGuardada De(Renovacion r) => new(r.Huella, r.UsuarioId, r.Familia, r.Emitida, r.ExpiraAbsoluta, r.UsadaEn, r.RevocadaEn);
}

/// <summary>La «base de datos» en memoria de los casos de uso de Identidad, con instantánea y reversión (una transacción que falla no deja efectos).</summary>
internal sealed class BdIdentidad
{
    public Dictionary<Guid, CuentaDeIdentidad> Cuentas { get; private set; } = [];

    public List<RenovacionGuardada> Renovaciones { get; private set; } = [];

    public List<(DesafioDeIngreso Desafio, string Huella)> Desafios { get; private set; } = [];

    public List<Guid> AccionesAuditadas { get; private set; } = [];

    public List<Guid> CarritosEliminados { get; private set; } = [];

    public List<Dispositivo> Dispositivos { get; private set; } = [];

    public List<CambioDeCorreoPendiente> Cambios { get; private set; } = [];

    public List<SolicitudDeDatos> Solicitudes { get; private set; } = [];

    public List<SolicitudDeCancelacion> Cancelaciones { get; private set; } = [];

    public List<BajaDeTitular> Bajas { get; private set; } = [];

    public List<ClaveDeFirma> Claves { get; private set; } = [];

    public List<Guid> PorAnonimizar { get; private set; } = [];

    public List<(Guid UsuarioId, AvisoDeCuenta Aviso)> Avisos { get; private set; } = [];

    internal sealed record EstadoDeLaBd(
        Dictionary<Guid, CuentaDeIdentidad> Cuentas,
        List<RenovacionGuardada> Renovaciones,
        List<(DesafioDeIngreso, string)> Desafios,
        List<Guid> AccionesAuditadas,
        List<Guid> CarritosEliminados,
        List<(CuentaDeIdentidad Cuenta, CuentaDeIdentidad Copia)> CopiasDeCuentas,
        List<Dispositivo> Dispositivos,
        List<CambioDeCorreoPendiente> Cambios,
        List<SolicitudDeDatos> Solicitudes,
        List<SolicitudDeCancelacion> Cancelaciones,
        List<BajaDeTitular> Bajas,
        List<ClaveDeFirma> Claves,
        List<Guid> PorAnonimizar,
        List<(Guid UsuarioId, AvisoDeCuenta Aviso)> Avisos);

    /// <summary>La instantánea guarda una copia del estado de cada cuenta para restaurarlo <b>en el mismo objeto</b>: las pruebas conservan su referencia y ven la reversión como la vería una base de datos.</summary>
    public EstadoDeLaBd Instantanea() => new(
        new Dictionary<Guid, CuentaDeIdentidad>(Cuentas),
        [.. Renovaciones],
        [.. Desafios],
        [.. AccionesAuditadas],
        [.. CarritosEliminados],
        [.. Cuentas.Values.Select(c => (c, Copiar(c)))],
        [.. Dispositivos.Select(d => Dispositivo.Restaurar(d.UsuarioId, d.HuellaDeLaMarca, d.Region, d.Emitido, d.CongeladoHasta))],
        [.. Cambios.Select(c => CambioDeCorreoPendiente.Restaurar(c.Id, c.UsuarioId, c.CorreoNuevo, c.Creado, c.Estado))],
        [.. Solicitudes.Select(s => SolicitudDeDatos.Restaurar(s.Id, s.UsuarioId, s.Tipo, s.Estado, s.RecibidaEn, s.FechaLimite, s.AtendidaEn, s.Respuesta, s.Motivo))],
        [.. Cancelaciones.Select(c => SolicitudDeCancelacion.Restaurar(c.Id, c.UsuarioId, c.Estado, c.SolicitadaEn, c.ReconfirmarHasta, c.AvisoVistoEn, c.CerradaEn, c.EjecucionEnCurso))],
        [.. Bajas.Select(b => new BajaDeTitular(b.IdentificadorOpaco, b.Creada, b.EntregadaEn, b.CerradaEn))],
        [.. Claves.Select(c => ClaveDeFirma.Restaurar(c.Kid, c.Creada, c.Estado, c.DejoDeFirmarEn, c.ComprometidaEn))],
        [.. PorAnonimizar],
        [.. Avisos]);

    public void Restaurar(EstadoDeLaBd estado)
    {
        Cuentas = estado.Cuentas;
        Renovaciones = estado.Renovaciones;
        Desafios = estado.Desafios;
        AccionesAuditadas = estado.AccionesAuditadas;
        CarritosEliminados = estado.CarritosEliminados;
        Dispositivos = estado.Dispositivos;
        Cambios = estado.Cambios;
        Solicitudes = estado.Solicitudes;
        Cancelaciones = estado.Cancelaciones;
        Bajas = estado.Bajas;
        Claves = estado.Claves;
        PorAnonimizar = estado.PorAnonimizar;
        Avisos = estado.Avisos;
        foreach (var (cuenta, copia) in estado.CopiasDeCuentas)
        {
            cuenta.Usuario = Copiar(copia).Usuario;
            cuenta.Nombre = copia.Nombre;
            cuenta.ResumenDeAcceso = copia.ResumenDeAcceso;
            cuenta.ContrasenaTemporalHasta = copia.ContrasenaTemporalHasta;
            cuenta.DebeCambiarLaTemporal = copia.DebeCambiarLaTemporal;
            cuenta.Aceptacion = copia.Aceptacion;
            cuenta.CanceladaEn = copia.CanceladaEn;
            cuenta.Whatsapp = copia.Whatsapp;
        }
    }

    private static CuentaDeIdentidad Copiar(CuentaDeIdentidad c)
    {
        var usuario = Usuario.Restaurar(c.Usuario.Id, c.Usuario.Correo, c.Usuario.Rol, c.Usuario.Sucursal, c.Usuario.CorreoVerificado, c.Usuario.Activo);
        if (c.Usuario.SesionesValidasDesde is { } desde)
        {
            usuario.AvanzarElCorteDeSesiones(desde);
        }

        return new CuentaDeIdentidad
        {
            Usuario = usuario,
            Nombre = c.Nombre,
            ResumenDeAcceso = c.ResumenDeAcceso,
            ContrasenaTemporalHasta = c.ContrasenaTemporalHasta,
            DebeCambiarLaTemporal = c.DebeCambiarLaTemporal,
            Aceptacion = c.Aceptacion,
            CreadaEn = c.CreadaEn,
            CanceladaEn = c.CanceladaEn,
            Whatsapp = ConsentimientoDeWhatsApp.Restaurar(c.Whatsapp.Numero, c.Whatsapp.Consentido, c.Whatsapp.Fecha),
        };
    }
}

internal sealed class UnidadDeTrabajoEnMemoria(BdIdentidad bd) : IUnidadDeTrabajo
{
    public int Transacciones { get; private set; }

    public async Task<T> Ejecutar<T>(Func<CancellationToken, Task<T>> trabajo, CancellationToken cancelacion = default)
    {
        Transacciones++;
        var instantanea = bd.Instantanea();
        try
        {
            return await trabajo(cancelacion);
        }
        catch
        {
            bd.Restaurar(instantanea);
            throw;
        }
    }

    public Task EjecutarComo(EsquemaDeModulo esquema, Func<CancellationToken, Task> bloque, CancellationToken cancelacion = default) => bloque(cancelacion);
}

internal sealed class RepositorioDeCuentasEnMemoria(BdIdentidad bd) : IRepositorioDeCuentas
{
    /// <summary>Cuántas veces se leyó una cuenta tomando el bloqueo de su fila (<c>FOR UPDATE</c>).</summary>
    public int Bloqueos { get; private set; }

    public Task<CuentaDeIdentidad?> PorCorreo(string correoNormalizado, CancellationToken cancelacion = default) =>
        Task.FromResult(bd.Cuentas.Values.FirstOrDefault(c => c.Usuario.Correo == correoNormalizado));

    public Task<CuentaDeIdentidad?> PorId(Guid usuarioId, bool paraActualizar = false, CancellationToken cancelacion = default)
    {
        if (paraActualizar)
        {
            Bloqueos++;
        }

        return Task.FromResult(bd.Cuentas.GetValueOrDefault(usuarioId));
    }

    public Task<bool> Insertar(CuentaDeIdentidad cuenta, CancellationToken cancelacion = default)
    {
        if (bd.Cuentas.Values.Any(c => c.Usuario.Correo == cuenta.Usuario.Correo))
        {
            return Task.FromResult(false);
        }

        bd.Cuentas[cuenta.Usuario.Id] = cuenta;
        return Task.FromResult(true);
    }

    public Task Guardar(CuentaDeIdentidad cuenta, CancellationToken cancelacion = default)
    {
        bd.Cuentas[cuenta.Usuario.Id] = cuenta;
        return Task.CompletedTask;
    }

    public Task Eliminar(Guid usuarioId, CancellationToken cancelacion = default)
    {
        bd.Cuentas.Remove(usuarioId);
        return Task.CompletedTask;
    }

    /// <summary>Si se fija, simula que la lectura bajo bloqueo ve ese número de administradores activos (una desactivación simultánea ya ocurrió).</summary>
    public int? ForzarAdministradoresActivos { get; set; }

    public Task<IReadOnlyList<Guid>> IdsActivosConRol(IReadOnlyCollection<RolDeUsuario> roles, CancellationToken cancelacion = default) =>
        Task.FromResult<IReadOnlyList<Guid>>([.. bd.Cuentas.Values.Where(c => c is { CanceladaEn: null, Usuario.Activo: true } && roles.Contains(c.Usuario.Rol)).Select(c => c.Usuario.Id)]);

    public Task<int> ContarAdministradoresActivos(CancellationToken cancelacion = default) =>
        Task.FromResult(ForzarAdministradoresActivos ?? bd.Cuentas.Values.Count(c => c is { CanceladaEn: null, Usuario: { Rol: RolDeUsuario.AdministradorCentral, Activo: true } }));

    public Task<EncargadoDeSucursal?> EncargadoActivoDe(Guid sucursal, CancellationToken cancelacion = default)
    {
        var encargado = bd.Cuentas.Values.FirstOrDefault(c => c is { CanceladaEn: null, Usuario: { Rol: RolDeUsuario.EncargadoDeSucursal, Activo: true } } && c.Usuario.Sucursal == sucursal);
        return Task.FromResult<EncargadoDeSucursal?>(encargado is null ? null : new EncargadoDeSucursal(encargado.Usuario.Id, sucursal));
    }

    public Task<IReadOnlyList<CuentaDeIdentidad>> Listar(RolDeUsuario? rol, bool? activo, string? correo, string? despuesDelCorreo, bool soloPersonal, int tamano, CancellationToken cancelacion = default)
    {
        var normalizado = correo is null ? null : Usuario.NormalizarCorreo(correo);
        return Task.FromResult<IReadOnlyList<CuentaDeIdentidad>>([.. bd.Cuentas.Values
            .Where(c => c.CanceladaEn is null
                && (rol is null || c.Usuario.Rol == rol)
                && (activo is null || c.Usuario.Activo == activo)
                && (!soloPersonal || c.Usuario.Rol != RolDeUsuario.Cliente)
                && (normalizado is null || c.Usuario.Correo == normalizado)
                && (despuesDelCorreo is null || string.CompareOrdinal(c.Usuario.Correo, despuesDelCorreo) > 0))
            .OrderBy(c => c.Usuario.Correo, StringComparer.Ordinal)
            .Take(tamano)]);
    }
}

internal sealed class RenovacionesEnMemoria(BdIdentidad bd) : IRepositorioDeRenovaciones
{
    public Task Guardar(Renovacion renovacion, CancellationToken cancelacion = default)
    {
        bd.Renovaciones.RemoveAll(r => r.Huella == renovacion.Huella);
        bd.Renovaciones.Add(RenovacionGuardada.De(renovacion));
        return Task.CompletedTask;
    }

    public Task<Renovacion?> PorHuella(string huella, CancellationToken cancelacion = default) =>
        Task.FromResult(bd.Renovaciones.FirstOrDefault(r => r.Huella == huella)?.ADominio());

    public Task<bool> MarcarUsada(string huella, DateTimeOffset ahora, CancellationToken cancelacion = default)
    {
        var indice = bd.Renovaciones.FindIndex(r => r.Huella == huella && r.UsadaEn is null);
        if (indice < 0)
        {
            return Task.FromResult(false);
        }

        bd.Renovaciones[indice] = bd.Renovaciones[indice] with { UsadaEn = ahora };
        return Task.FromResult(true);
    }

    public Task RevocarFamilia(Guid usuarioId, Guid familia, DateTimeOffset ahora, CancellationToken cancelacion = default) =>
        Revocar(r => r.UsuarioId == usuarioId && r.Familia == familia, ahora);

    public Task RevocarTodas(Guid usuarioId, DateTimeOffset ahora, CancellationToken cancelacion = default) => Revocar(r => r.UsuarioId == usuarioId, ahora);

    public Task RevocarLasDeTodos(DateTimeOffset ahora, CancellationToken cancelacion = default) => Revocar(_ => true, ahora);

    public Task RevocarLasDemas(Guid usuarioId, Guid familiaQueSeConserva, DateTimeOffset ahora, CancellationToken cancelacion = default) =>
        Revocar(r => r.UsuarioId == usuarioId && r.Familia != familiaQueSeConserva, ahora);

    private Task Revocar(Func<RenovacionGuardada, bool> criterio, DateTimeOffset ahora)
    {
        for (var i = 0; i < bd.Renovaciones.Count; i++)
        {
            if (criterio(bd.Renovaciones[i]) && bd.Renovaciones[i].RevocadaEn is null)
            {
                bd.Renovaciones[i] = bd.Renovaciones[i] with { RevocadaEn = ahora };
            }
        }

        return Task.CompletedTask;
    }
}

internal sealed class DesafiosEnMemoria(BdIdentidad bd) : IRepositorioDeDesafios
{
    public Task Guardar(DesafioDeIngreso desafio, string huella, CancellationToken cancelacion = default)
    {
        bd.Desafios.Add((desafio, huella));
        return Task.CompletedTask;
    }

    public Task<DesafioDeIngreso?> PorHuella(string huella, CancellationToken cancelacion = default) =>
        Task.FromResult(bd.Desafios.FirstOrDefault(d => d.Huella == huella).Desafio is { } d ? DesafioDeIngreso.Restaurar(d.Id, d.UsuarioId, d.Modo, d.Creado, d.Usado) : null);

    public Task<bool> MarcarUsado(Guid desafioId, CancellationToken cancelacion = default)
    {
        var entrada = bd.Desafios.FirstOrDefault(d => d.Desafio.Id == desafioId);
        if (entrada.Desafio is null || entrada.Desafio.Usado)
        {
            return Task.FromResult(false);
        }

        bd.Desafios.RemoveAll(d => d.Desafio.Id == desafioId);
        bd.Desafios.Add((DesafioDeIngreso.Restaurar(entrada.Desafio.Id, entrada.Desafio.UsuarioId, entrada.Desafio.Modo, entrada.Desafio.Creado, usado: true), entrada.Huella));
        return Task.FromResult(true);
    }
}

/// <summary>El hasher de prueba: el «resumen» es un PHC válido derivado de la contraseña; cuenta los cálculos señuelo.</summary>
internal sealed class HasherDePrueba : IHasherDeContrasenas
{
    public int Resumenes { get; private set; }

    public int Senuelos { get; private set; }

    public static string ResumenDe(string contrasenaNormalizada)
    {
        var huella = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(contrasenaNormalizada))).TrimEnd('=');
        return $"$argon2id$v=19$m=19456,t=2,p=1${new string('s', 22)}${huella}";
    }

    /// <summary>Un gancho para simular que otra operación cambia la cuenta mientras se calcula el resumen (Argon2id tarda).</summary>
    public Action? AlResumir { get; set; }

    public Task<string> Resumir(Contrasena contrasena, CancellationToken cancelacion = default)
    {
        Resumenes++;
        AlResumir?.Invoke();
        return Task.FromResult(ResumenDe(contrasena.Revelar()));
    }

    public Task<bool> Verificar(string? resumen, Contrasena presentada, CancellationToken cancelacion = default)
    {
        if (resumen is null)
        {
            Senuelos++;
            return Task.FromResult(false);
        }

        return Task.FromResult(string.Equals(resumen, ResumenDe(presentada.Revelar()), StringComparison.Ordinal));
    }
}

internal sealed class ListaDeContrasenasDePrueba : IListaDeContrasenasComunes
{
    public static readonly HashSet<string> Comunes = ["contrasena123", "1234567890", "qwertyuiop"];

    public bool EsComun(string normalizada) => Comunes.Contains(normalizada);
}

internal sealed class VersionesDePrueba(VersionesDeTextos vigentes) : IVersionesDeTextosVigentes
{
    public VersionesDeTextos Vigentes { get; set; } = vigentes;

    public DateOnly VigenteDesde { get; set; } = new(2026, 11, 14);

    public Task<VersionesDeTextos> Obtener(CancellationToken cancelacion = default) => Task.FromResult(Vigentes);

    public Task<(VersionesDeTextos Versiones, DateOnly VigenteDesde)> ObtenerConVigencia(CancellationToken cancelacion = default) => Task.FromResult((Vigentes, VigenteDesde));
}

internal sealed class CalculadorDeHuellaDePrueba : ICalculadorDeHuella
{
    private static string Texto(string valor) => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(valor))).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public HuellaDeOrigen DeOrigen(string direccion) => HuellaDeOrigen.Crear(Texto(direccion), "v1");

    public HuellaDeOrigen? DeOrigenPorRed48(string direccion) => null;

    public IReadOnlyList<HuellaDeOrigen> DeOrigenEnRotacion(string direccion) => [DeOrigen(direccion)];

    public HuellaDeSujeto DeSujeto(string valor, string proposito) => HuellaDeSujeto.Crear(Texto(proposito + "|" + valor), "v1");

    public IReadOnlyList<HuellaDeSujeto> DeSujetoEnRotacion(string valor, string proposito) => [DeSujeto(valor, proposito)];
}

/// <summary>Los límites en memoria con los máximos de las políticas del Dominio (sin ventana de tiempo): lo suficiente para probar que cada caso de uso reserva, rechaza y libera.</summary>
internal sealed class LimitesEnMemoria : IControlDeLimites
{
    /// <summary>Sustituye el máximo de una política (por ejemplo, para una prueba que pide códigos desde un mismo origen para muchos correos).</summary>
    public Dictionary<string, int> Maximos { get; } = [];

    private readonly Dictionary<(string Politica, string Sujeto), int> _cuentas = [];

    public bool Caido { get; set; }

    public List<(string Politica, string Sujeto)> Reservas { get; } = [];

    public int Cuenta(string politica, string sujeto) => _cuentas.GetValueOrDefault((politica, sujeto));

    public Task<ResultadoDeLimite> Reservar(
        ClaveDePolitica politica,
        string? sujeto = null,
        string? claveDelLlamador = null,
        HuellaDeSujeto? huellaDelLlamador = null,
        ClaveDeIdempotencia? idempotencia = null,
        CancellationToken cancelacion = default)
    {
        if (Caido)
        {
            return Task.FromResult(ResultadoDeLimite.NoDisponible());
        }

        var clave = huellaDelLlamador?.Valor ?? sujeto ?? "origen";
        var maximo = Maximos.TryGetValue(politica.Valor, out var propio) ? propio : PoliticasDeSeguridad.Todas.Single(p => p.Nombre == politica.Valor).Maximo;
        var cuenta = _cuentas.GetValueOrDefault((politica.Valor, clave)) + 1;
        _cuentas[(politica.Valor, clave)] = cuenta;
        Reservas.Add((politica.Valor, clave));
        var ahora = DateTimeOffset.UtcNow;
        return Task.FromResult(cuenta > maximo
            ? ResultadoDeLimite.Excedido(politica.Valor, 900, ahora.AddSeconds(900))
            : ResultadoDeLimite.Permitido(new ReservaDeLimite(politica.Valor, clave, Guid.NewGuid().ToString("N"), ahora)));
    }

    public Task Liberar(ReservaDeLimite reserva, CancellationToken cancelacion = default)
    {
        _cuentas[(reserva.Politica, reserva.Sujeto)] = Math.Max(0, _cuentas.GetValueOrDefault((reserva.Politica, reserva.Sujeto)) - 1);
        return Task.CompletedTask;
    }

    public Task Completar(ReservaDeLimite reserva, ResultadoMinimo resultado, CancellationToken cancelacion = default) => Task.CompletedTask;

    public Task Fallar(ReservaDeLimite reserva, CancellationToken cancelacion = default) => Task.CompletedTask;
}

internal sealed class PendientesDePrueba : IPendientesDeCuenta
{
    public int Cantidad { get; set; }

    public bool Falla { get; set; }

    public Task<int> Contar(Guid usuarioId, CancellationToken cancelacion = default) =>
        Falla ? throw new InvalidOperationException("El Inventario no responde.") : Task.FromResult(Cantidad);
}

internal sealed class CarritoDePrueba(BdIdentidad bd) : ICarritoDeCuentaReemplazada
{
    public List<Guid> Eliminados => bd.CarritosEliminados;

    public bool Falla { get; set; }

    public Task Eliminar(Guid usuarioId, CancellationToken cancelacion = default)
    {
        if (Falla)
        {
            throw new InvalidOperationException("Carrito no responde.");
        }

        Eliminados.Add(usuarioId);
        return Task.CompletedTask;
    }
}

internal sealed class AuditoriaDePrueba(BdIdentidad bd) : IAuditoria
{
    public List<SolicitudDeRegistro> Registradas { get; } = [];

    public bool Falla { get; set; }

    public Task Registrar(SolicitudDeRegistro solicitud, CancellationToken cancelacion = default)
    {
        if (Falla)
        {
            throw new AuditoriaNoDisponibleException();
        }

        Registradas.Add(solicitud);
        bd.AccionesAuditadas.Add(Guid.NewGuid());
        return Task.CompletedTask;
    }
}

internal sealed class SeguridadDePrueba : IRegistroDeSeguridad
{
    public List<SolicitudDeRegistro> Eventos { get; } = [];

    public bool Falla { get; set; }

    public Task Registrar(SolicitudDeRegistro solicitud, CancellationToken cancelacion = default)
    {
        if (Falla)
        {
            throw new InvalidOperationException("Auditoría de seguridad caída.");
        }

        Eventos.Add(solicitud);
        return Task.CompletedTask;
    }
}

internal sealed class CodigosEnMemoria : ICodigosDeUnSoloUso
{
    public List<CodigoDeUnSoloUso> Codigos { get; } = [];

    public Task Emitir(CodigoDeUnSoloUso codigo, CancellationToken cancelacion = default)
    {
        foreach (var anterior in Codigos.Where(c => c.UsuarioId == codigo.UsuarioId && c.Proposito == codigo.Proposito))
        {
            anterior.Invalidar();
        }

        Codigos.Add(codigo);
        return Task.CompletedTask;
    }

    public Task<ResultadoDelCodigo> Verificar(Guid usuarioId, PropositoDeCodigo proposito, string huellaPresentada, DateTimeOffset ahora, CancellationToken cancelacion = default)
    {
        var vigente = Codigos.LastOrDefault(c => c.UsuarioId == usuarioId && c.Proposito == proposito);
        return Task.FromResult(vigente is null ? ResultadoDelCodigo.NoVigente : vigente.Verificar(huellaPresentada, ahora));
    }

    public Task Invalidar(Guid usuarioId, PropositoDeCodigo proposito, CancellationToken cancelacion = default)
    {
        foreach (var codigo in Codigos.Where(c => c.UsuarioId == usuarioId && c.Proposito == proposito))
        {
            codigo.Invalidar();
        }

        return Task.CompletedTask;
    }
}

internal sealed class HuellasDeIdentidadDePrueba : IHuellasDeIdentidad
{
    public string DeCodigo(Guid usuarioId, PropositoDeCodigo proposito, string codigo) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{usuarioId:N}|{proposito}|{codigo}")));

    public string DeCodigoDeRespaldo(Guid usuarioId, string codigoCanonico) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{usuarioId:N}|respaldo|{codigoCanonico}")));

    public string DeValorOpaco(string valor) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(valor))).ToLowerInvariant();
}

internal sealed class GeneradorDeSecretosDePrueba : IGeneradorDeSecretos
{
    private int _n;

    public Guid NuevoIdentificador() => Guid.NewGuid();

    public string ValorOpaco() => $"opaco-{++_n:D6}-" + new string('x', 30);

    public string ContrasenaTemporal() => "Temporal-" + Guid.NewGuid().ToString("N");

    public byte[] SecretoDelSegundoFactor() => [.. Enumerable.Range(1, 20).Select(i => (byte)(i * 7))];
}

internal sealed class SolicitudesDeCodigoDePrueba : IRegistroDeSolicitudesDeCodigo
{
    public List<(string Huella, PropositoDeCodigo Proposito)> Registradas { get; } = [];

    public Task Registrar(string huellaDelCorreo, PropositoDeCodigo proposito, DateTimeOffset ahora, CancellationToken cancelacion = default)
    {
        // La misma restricción que la columna `huella_correo` (64 hexadecimales en minúscula): una huella de otra forma solo fallaba contra la base real.
        if (!System.Text.RegularExpressions.Regex.IsMatch(huellaDelCorreo, "^[0-9a-f]{64}$"))
        {
            throw new InvalidOperationException("La huella del correo no cumple la restricción de la columna huella_correo.");
        }

        Registradas.Add((huellaDelCorreo, proposito));
        return Task.CompletedTask;
    }
}

internal sealed class ColaDeCodigosDePrueba : IColaDeCodigos
{
    public List<SolicitudDeCodigoEnSegundoPlano> Encoladas { get; } = [];

    public bool Llena { get; set; }

    public bool Encolar(SolicitudDeCodigoEnSegundoPlano solicitud)
    {
        if (Llena)
        {
            return false;
        }

        Encoladas.Add(solicitud);
        return true;
    }
}

internal sealed class EnviadorDeCorreoDePrueba : IEnviadorDeCorreo
{
    public List<CorreoDeCodigo> Codigos { get; } = [];

    public List<CorreoSaliente> Correos { get; } = [];

    public bool Falla { get; set; }

    /// <summary>Lo que responde el envío de un correo que no es un código: salió, se encoló o se suprimió.</summary>
    public ResultadoDeEnvio Resultado { get; set; } = ResultadoDeEnvio.Enviado;

    public Task<ResultadoDeEnvio> Enviar(CorreoSaliente correo, CancellationToken cancelacion = default)
    {
        if (Falla)
        {
            throw new InvalidOperationException("SMTP caído.");
        }

        Correos.Add(correo);
        return Task.FromResult(Resultado);
    }

    public Task<ResultadoDeEnvio> EnviarCodigo(CorreoDeCodigo correo, CancellationToken cancelacion = default)
    {
        if (Falla)
        {
            throw new InvalidOperationException("SMTP caído.");
        }

        Codigos.Add(correo);
        return Task.FromResult(Resultado);
    }
}

internal sealed class EmisorDeCredencialesDePrueba : IEmisorDeCredenciales
{
    public List<DatosDeLaCredencial> Emitidas { get; } = [];

    public Task<CredencialEmitida> Emitir(DatosDeLaCredencial datos, DateTimeOffset ahora, CancellationToken cancelacion = default)
    {
        Emitidas.Add(datos);
        return Task.FromResult(new CredencialEmitida($"jwt.{datos.UsuarioId:N}.{datos.Rol}.{datos.CorreoVerificado}", ahora.AddMinutes(15)));
    }
}

internal sealed class CifradorDePrueba : ICifradorDeSecretos
{
    public byte[] Cifrar(ReadOnlySpan<byte> secreto, Guid usuarioId) => [.. secreto.ToArray().Select(b => (byte)(b ^ 0x5A))];

    public byte[] Descifrar(ReadOnlySpan<byte> cifrado, Guid usuarioId) => [.. cifrado.ToArray().Select(b => (byte)(b ^ 0x5A))];
}

internal sealed class SegundoFactorEnMemoria : ISegundoFactorAlmacen
{
    private readonly Dictionary<Guid, (SegundoFactor Factor, byte[] Secreto)> _factores = [];

    public Task<SegundoFactorGuardado?> Obtener(Guid usuarioId, CancellationToken cancelacion = default) =>
        Task.FromResult(_factores.TryGetValue(usuarioId, out var f) ? new SegundoFactorGuardado(SegundoFactor.Restaurar(usuarioId, f.Factor.Estado, f.Factor.UltimoPasoUsado, f.Factor.HuellasDisponibles), f.Secreto) : null);

    public Task GuardarSinConfirmar(Guid usuarioId, byte[] secretoCifrado, CancellationToken cancelacion = default)
    {
        _factores[usuarioId] = (SegundoFactor.Nuevo(usuarioId), secretoCifrado);
        return Task.CompletedTask;
    }

    public Task Activar(Guid usuarioId, long pasoConfirmado, IReadOnlyList<string> huellasDeRespaldo, CancellationToken cancelacion = default)
    {
        var secreto = _factores[usuarioId].Secreto;
        _factores[usuarioId] = (SegundoFactor.Restaurar(usuarioId, EstadoDelSegundoFactor.Activo, pasoConfirmado, huellasDeRespaldo), secreto);
        return Task.CompletedTask;
    }

    public Task<bool> AceptarPaso(Guid usuarioId, long paso, CancellationToken cancelacion = default)
    {
        var actual = _factores[usuarioId];
        if (actual.Factor.UltimoPasoUsado is { } ultimo && ultimo >= paso)
        {
            return Task.FromResult(false);
        }

        _factores[usuarioId] = (SegundoFactor.Restaurar(usuarioId, actual.Factor.Estado, paso, actual.Factor.HuellasDisponibles), actual.Secreto);
        return Task.FromResult(true);
    }

    public Task<bool> ConsumirCodigoDeRespaldo(Guid usuarioId, string huella, DateTimeOffset ahora, CancellationToken cancelacion = default)
    {
        var actual = _factores[usuarioId];
        if (!actual.Factor.HuellasDisponibles.Contains(huella))
        {
            return Task.FromResult(false);
        }

        _factores[usuarioId] = (SegundoFactor.Restaurar(usuarioId, actual.Factor.Estado, actual.Factor.UltimoPasoUsado, actual.Factor.HuellasDisponibles.Where(h => h != huella)), actual.Secreto);
        return Task.FromResult(true);
    }

    public Task Desactivar(Guid usuarioId, CancellationToken cancelacion = default)
    {
        _factores.Remove(usuarioId);
        return Task.CompletedTask;
    }

    public Task ReemplazarCodigosDeRespaldo(Guid usuarioId, IReadOnlyList<string> huellasNuevas, CancellationToken cancelacion = default)
    {
        var actual = _factores[usuarioId];
        _factores[usuarioId] = (SegundoFactor.Restaurar(usuarioId, actual.Factor.Estado, actual.Factor.UltimoPasoUsado, huellasNuevas), actual.Secreto);
        return Task.CompletedTask;
    }

    /// <summary>Deja un segundo factor activo con el secreto dado y los códigos de respaldo dados (por su huella).</summary>
    public void Sembrar(Guid usuarioId, byte[] secretoEnClaro, IEnumerable<string> huellasDeRespaldo, CifradorDePrueba cifrador) =>
        _factores[usuarioId] = (SegundoFactor.Restaurar(usuarioId, EstadoDelSegundoFactor.Activo, null, huellasDeRespaldo), cifrador.Cifrar(secretoEnClaro, usuarioId));
}

internal sealed class AlertasDeAuditoriaDePrueba : IAlertas
{
    public List<string> Tecnicas { get; } = [];

    public List<string> DeSeguridad { get; } = [];

    public bool Falla { get; set; }

    public Task PublicarTecnica(CodigoDeAlerta motivo, CancellationToken cancelacion = default)
    {
        Tecnicas.Add(motivo.Valor);
        return Task.CompletedTask;
    }

    public Task PublicarDeSeguridad(CodigoDeAlerta motivo, CancellationToken cancelacion = default)
    {
        if (Falla)
        {
            throw new InvalidOperationException("Sin registro técnico.");
        }

        DeSeguridad.Add(motivo.Valor);
        return Task.CompletedTask;
    }
}

internal sealed class ActorDePrueba : IActorActual
{
    public ActorActual? Actual { get; set; }
}

internal sealed class DenegacionesDePrueba : IDenegacionDeAcceso
{
    public List<DenegacionDeAcceso> Registradas { get; } = [];

    public Task Registrar(DenegacionDeAcceso denegacion, CancellationToken cancelacion = default)
    {
        Registradas.Add(denegacion);
        return Task.CompletedTask;
    }
}

internal sealed class CancelacionDeAvisosDePrueba : ICancelacionDeAvisos
{
    public List<Guid> Cancelados { get; } = [];

    public bool Falla { get; set; }

    public Task CancelarLosAvisosDe(Guid usuarioId, CancellationToken cancelacion = default)
    {
        if (Falla)
        {
            throw new InvalidOperationException("Notificaciones no responde.");
        }

        Cancelados.Add(usuarioId);
        return Task.CompletedTask;
    }
}

internal sealed class AvisosDeCuentaEnMemoria(BdIdentidad bd) : IAvisosDeCuentaAlmacen
{
    public List<(Guid UsuarioId, AvisoDeCuenta Aviso)> Avisos => bd.Avisos;

    public Guid Sembrar(Guid usuarioId, string tipo, DateTimeOffset fecha)
    {
        var id = Guid.NewGuid();
        Avisos.Add((usuarioId, new AvisoDeCuenta(id, tipo, fecha, false)));
        return id;
    }

    public Task<IReadOnlyList<AvisoDeCuenta>> Listar(Guid usuarioId, CancellationToken cancelacion = default) =>
        Task.FromResult<IReadOnlyList<AvisoDeCuenta>>([.. Avisos.Where(a => a.UsuarioId == usuarioId).Select(a => a.Aviso)]);

    public Task Agregar(Guid usuarioId, string tipo, DateTimeOffset ahora, CancellationToken cancelacion = default)
    {
        Avisos.Add((usuarioId, new AvisoDeCuenta(Guid.NewGuid(), tipo, ahora, false)));
        return Task.CompletedTask;
    }

    public Task<bool> MarcarVisto(Guid usuarioId, Guid avisoId, DateTimeOffset ahora, CancellationToken cancelacion = default)
    {
        var indice = Avisos.FindIndex(a => a.UsuarioId == usuarioId && a.Aviso.Id == avisoId);
        if (indice < 0)
        {
            return Task.FromResult(false);
        }

        Avisos[indice] = (usuarioId, Avisos[indice].Aviso with { Visto = true });
        return Task.FromResult(true);
    }
}

internal sealed class DispositivosEnMemoria(BdIdentidad bd) : IRepositorioDeDispositivos
{
    public Task<IReadOnlyList<Dispositivo>> Listar(Guid usuarioId, CancellationToken cancelacion = default) =>
        Task.FromResult<IReadOnlyList<Dispositivo>>([.. bd.Dispositivos.Where(d => d.UsuarioId == usuarioId)]);

    public Task Guardar(Dispositivo dispositivo, CancellationToken cancelacion = default)
    {
        bd.Dispositivos.RemoveAll(d => d.UsuarioId == dispositivo.UsuarioId && d.HuellaDeLaMarca == dispositivo.HuellaDeLaMarca);
        bd.Dispositivos.Add(dispositivo);
        var propios = bd.Dispositivos.Where(d => d.UsuarioId == dispositivo.UsuarioId).OrderBy(d => d.Emitido).ToList();
        foreach (var sobrante in propios.Take(Math.Max(0, propios.Count - IRepositorioDeDispositivos.Maximo)))
        {
            bd.Dispositivos.Remove(sobrante);
        }

        return Task.CompletedTask;
    }
}

internal sealed class CambiosDeCorreoEnMemoria(BdIdentidad bd) : IRepositorioDeCambiosDeCorreo
{
    public Task<CambioDeCorreoPendiente?> Obtener(Guid usuarioId, CancellationToken cancelacion = default) =>
        Task.FromResult(bd.Cambios.FirstOrDefault(c => c.UsuarioId == usuarioId));

    public Task Guardar(CambioDeCorreoPendiente cambio, CancellationToken cancelacion = default)
    {
        bd.Cambios.RemoveAll(c => c.UsuarioId == cambio.UsuarioId);
        bd.Cambios.Add(cambio);
        return Task.CompletedTask;
    }
}

internal sealed class SolicitudesDeDatosEnMemoria(BdIdentidad bd) : IRepositorioDeSolicitudesDeDatos
{
    public Task Guardar(SolicitudDeDatos solicitud, CancellationToken cancelacion = default)
    {
        bd.Solicitudes.RemoveAll(s => s.Id == solicitud.Id);
        bd.Solicitudes.Add(solicitud);
        return Task.CompletedTask;
    }

    public Task<SolicitudDeDatos?> Obtener(Guid solicitudId, bool paraActualizar = false, CancellationToken cancelacion = default) =>
        Task.FromResult(bd.Solicitudes.FirstOrDefault(s => s.Id == solicitudId));

    public Task<IReadOnlyList<SolicitudDeDatos>> DelUsuario(Guid usuarioId, CancellationToken cancelacion = default) =>
        Task.FromResult<IReadOnlyList<SolicitudDeDatos>>([.. bd.Solicitudes.Where(s => s.UsuarioId == usuarioId)]);

    public Task<IReadOnlyList<SolicitudDeDatos>> Todas(CancellationToken cancelacion = default) =>
        Task.FromResult<IReadOnlyList<SolicitudDeDatos>>([.. bd.Solicitudes]);
}

internal sealed class CancelacionesEnMemoria(BdIdentidad bd) : IRepositorioDeCancelaciones
{
    public Task Guardar(SolicitudDeCancelacion solicitud, CancellationToken cancelacion = default)
    {
        bd.Cancelaciones.RemoveAll(s => s.Id == solicitud.Id);
        bd.Cancelaciones.Add(solicitud);
        return Task.CompletedTask;
    }

    public Task<SolicitudDeCancelacion?> AbiertaDe(Guid usuarioId, bool paraActualizar = false, CancellationToken cancelacion = default) =>
        Task.FromResult(bd.Cancelaciones.FirstOrDefault(s => s.UsuarioId == usuarioId && s.Estado is EstadoDeCancelacion.Diferida or EstadoDeCancelacion.EsperandoReconfirmacion));

    public Task<SolicitudDeCancelacion?> UltimaDe(Guid usuarioId, CancellationToken cancelacion = default) =>
        Task.FromResult(bd.Cancelaciones.Where(s => s.UsuarioId == usuarioId).OrderByDescending(s => s.SolicitadaEn).FirstOrDefault());

    public Task<IReadOnlyList<SolicitudDeCancelacion>> AbiertasParaRevisar(int limite, CancellationToken cancelacion = default) =>
        Task.FromResult<IReadOnlyList<SolicitudDeCancelacion>>([.. bd.Cancelaciones.Where(s => s.Estado is EstadoDeCancelacion.Diferida or EstadoDeCancelacion.EsperandoReconfirmacion).Take(limite)]);
}

/// <summary>El ejecutor de la cancelación de prueba (T23 hace el real): deja la cuenta cancelada dentro de la transacción o falla.</summary>
internal sealed class EjecutorDeCancelacionDePrueba(BabelHub.Ecommerce.UnitTests.Plataforma.RelojFijo reloj) : IEjecutorDeLaCancelacion
{
    public List<Guid> Ejecutadas { get; } = [];

    public bool Falla { get; set; }

    public Task Ejecutar(CuentaDeIdentidad cuenta, CancellationToken cancelacion = default)
    {
        if (Falla)
        {
            throw new InvalidOperationException("Un módulo no pudo anonimizar.");
        }

        cuenta.CanceladaEn = reloj.Ahora;
        cuenta.Usuario.Desactivar();
        Ejecutadas.Add(cuenta.Usuario.Id);
        return Task.CompletedTask;
    }
}

internal sealed class BajasEnMemoria(BdIdentidad bd) : IRepositorioDeBajas
{
    public Task Agregar(BajaDeTitular baja, CancellationToken cancelacion = default)
    {
        bd.Bajas.Add(baja);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<BajaDeTitular>> Pendientes(int limite, CancellationToken cancelacion = default) =>
        Task.FromResult<IReadOnlyList<BajaDeTitular>>([.. bd.Bajas.Where(b => b.Pendiente).Take(limite)]);

    public Task Guardar(BajaDeTitular baja, CancellationToken cancelacion = default) => Task.CompletedTask;
}

/// <summary>Un módulo receptor de la cancelación de prueba: anota lo que se le pidió o falla.</summary>
internal sealed class AplicadorDePrueba(string modulo) : IAplicadorDeCancelacion
{
    public string Modulo => modulo;

    public List<(Guid UsuarioId, Guid Opaco)> Aplicados { get; } = [];

    /// <summary>El orden global de llamada (para comprobar el orden de los receptores).</summary>
    public static List<string> OrdenDeLlamada { get; } = [];

    public bool Falla { get; set; }

    public Task Aplicar(Guid usuarioId, Guid identificadorOpaco, CancellationToken cancelacion = default)
    {
        if (Falla)
        {
            throw new InvalidOperationException($"El módulo {modulo} no pudo aplicar la cancelación.");
        }

        Aplicados.Add((usuarioId, identificadorOpaco));
        lock (OrdenDeLlamada)
        {
            OrdenDeLlamada.Add(modulo);
        }

        return Task.CompletedTask;
    }
}

/// <summary>La parte de la matriz del esquema <c>identidad</c>, simulada sobre la base en memoria (la real es T33).</summary>
internal sealed class PurgaDeIdentidadDePrueba(BdIdentidad bd) : IPurgaDeIdentidad
{
    public bool Falla { get; set; }

    public int Llamadas { get; private set; }

    public Task EliminarLosDatosDe(Guid usuarioId, CancellationToken cancelacion = default)
    {
        Llamadas++;
        if (Falla)
        {
            throw new InvalidOperationException("La purga del esquema identidad falló.");
        }

        bd.Dispositivos.RemoveAll(d => d.UsuarioId == usuarioId);
        bd.Cambios.RemoveAll(c => c.UsuarioId == usuarioId);
        bd.Avisos.RemoveAll(a => a.UsuarioId == usuarioId);
        foreach (var solicitud in bd.Solicitudes.Where(s => s.UsuarioId == usuarioId))
        {
            solicitud.BorrarElContenido();
        }

        return Task.CompletedTask;
    }
}

internal sealed class EntregaDeBajasDePrueba : IEntregaDeBajas
{
    public List<Guid> Entregadas { get; } = [];

    public bool Falla { get; set; }

    public Task<bool> Entregar(Guid identificadorOpaco, CancellationToken cancelacion = default)
    {
        if (Falla)
        {
            throw new InvalidOperationException("El Inventario no responde.");
        }

        Entregadas.Add(identificadorOpaco);
        return Task.FromResult(true);
    }
}

internal sealed class AlmacenDeClavesEnMemoria(BdIdentidad bd) : IAlmacenDeClavesDeFirma
{
    public Dictionary<string, string> Parametros { get; } = [];

    public int Bloqueos { get; private set; }

    public Task<JuegoDeClavesDeFirma> Cargar(bool paraActualizar, CancellationToken cancelacion = default)
    {
        if (paraActualizar)
        {
            Bloqueos++;
        }

        return Task.FromResult(JuegoDeClavesDeFirma.Restaurar(bd.Claves.Select(c => ClaveDeFirma.Restaurar(c.Kid, c.Creada, c.Estado, c.DejoDeFirmarEn, c.ComprometidaEn))));
    }

    public Task Guardar(JuegoDeClavesDeFirma juego, CancellationToken cancelacion = default)
    {
        bd.Claves.Clear();
        bd.Claves.AddRange(juego.Claves.Select(c => ClaveDeFirma.Restaurar(c.Kid, c.Creada, c.Estado, c.DejoDeFirmarEn, c.ComprometidaEn)));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyDictionary<string, string>> ParametrosPublicos(IReadOnlyCollection<string> kids, CancellationToken cancelacion = default) =>
        Task.FromResult<IReadOnlyDictionary<string, string>>(kids.Where(Parametros.ContainsKey).ToDictionary(k => k, k => Parametros[k]));
}

internal sealed class GeneradorDeClavesDePrueba(AlmacenDeClavesEnMemoria almacen) : IGeneradorDeClavesDeFirma
{
    private int _n;

    public int Generadas => _n;

    public Task<(ClaveDeFirma Clave, string ParametrosPublicos)> Generar(DateTimeOffset ahora, CancellationToken cancelacion = default)
    {
        var kid = $"kid-generada-{++_n:D4}";
        var publico = $"jwk-publico-de-{kid}";
        almacen.Parametros[kid] = publico;
        return Task.FromResult((ClaveDeFirma.Nueva(kid, ahora), publico));
    }
}

internal sealed class PorAnonimizarEnMemoria(BdIdentidad bd) : IPorAnonimizar
{
    public Task Agregar(Guid usuarioId, CancellationToken cancelacion = default)
    {
        if (!bd.PorAnonimizar.Contains(usuarioId))
        {
            bd.PorAnonimizar.Add(usuarioId);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Guid>> Listar(int maximo, CancellationToken cancelacion = default) =>
        Task.FromResult<IReadOnlyList<Guid>>([.. bd.PorAnonimizar.Take(maximo)]);
}

/// <summary>El receptor de los cambios de cuenta de prueba (IDE-99): registra el correo que recibe y el correo vigente al recibirlo, y puede fallar con una excepción que lleva el correo en el mensaje.</summary>
internal sealed class ReceptorDeCambiosDeCuentaDePrueba : IReceptorDeCambiosDeCuenta
{
    public string Modulo => "reclamaciones";

    public List<(Guid UsuarioId, string CorreoAnterior)> AlCambiarElCorreoLlamadas { get; } = [];

    public List<Guid> AlReemplazarLaCuentaLlamadas { get; } = [];

    /// <summary>Lo que ve el receptor al llamarlo: por ejemplo, el correo de la cuenta en ese instante (la prueba de que el correo anterior sigue vigente).</summary>
    public Func<Guid, string?>? CorreoVigenteDe { get; set; }

    public List<string?> CorreosVigentesAlLlamar { get; } = [];

    public bool Falla { get; set; }

    public Task AlCambiarElCorreo(Guid usuarioId, string correoAnteriorNormalizado, CancellationToken cancelacion = default)
    {
        AlCambiarElCorreoLlamadas.Add((usuarioId, correoAnteriorNormalizado));
        CorreosVigentesAlLlamar.Add(CorreoVigenteDe?.Invoke(usuarioId));
        return Falla ? throw new InvalidOperationException($"No se pudo vincular las hojas de {correoAnteriorNormalizado}.") : Task.CompletedTask;
    }

    public Task AlReemplazarLaCuenta(Guid usuarioId, CancellationToken cancelacion = default)
    {
        AlReemplazarLaCuentaLlamadas.Add(usuarioId);
        return Falla ? throw new InvalidOperationException("No se pudo desvincular las hojas.") : Task.CompletedTask;
    }
}
