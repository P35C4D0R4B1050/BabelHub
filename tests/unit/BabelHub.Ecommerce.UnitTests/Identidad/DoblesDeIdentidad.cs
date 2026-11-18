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

    /// <summary>La instantánea guarda una copia del estado de cada cuenta para restaurarlo <b>en el mismo objeto</b>: las pruebas conservan su referencia y ven la reversión como la vería una base de datos.</summary>
    public (Dictionary<Guid, CuentaDeIdentidad>, List<RenovacionGuardada>, List<(DesafioDeIngreso, string)>, List<Guid>, List<Guid>, List<(CuentaDeIdentidad Cuenta, CuentaDeIdentidad Copia)>) Instantanea() =>
        (new Dictionary<Guid, CuentaDeIdentidad>(Cuentas), [.. Renovaciones], [.. Desafios], [.. AccionesAuditadas], [.. CarritosEliminados], [.. Cuentas.Values.Select(c => (c, Copiar(c)))]);

    public void Restaurar((Dictionary<Guid, CuentaDeIdentidad>, List<RenovacionGuardada>, List<(DesafioDeIngreso, string)>, List<Guid>, List<Guid>, List<(CuentaDeIdentidad Cuenta, CuentaDeIdentidad Copia)>) estado)
    {
        Cuentas = estado.Item1;
        Renovaciones = estado.Item2;
        Desafios = estado.Item3;
        AccionesAuditadas = estado.Item4;
        CarritosEliminados = estado.Item5;
        foreach (var (cuenta, copia) in estado.Item6)
        {
            cuenta.Usuario = Copiar(copia).Usuario;
            cuenta.Nombre = copia.Nombre;
            cuenta.ResumenDeContrasena = copia.ResumenDeContrasena;
            cuenta.ContrasenaTemporalHasta = copia.ContrasenaTemporalHasta;
            cuenta.DebeCambiarLaContrasena = copia.DebeCambiarLaContrasena;
            cuenta.Aceptacion = copia.Aceptacion;
            cuenta.CanceladaEn = copia.CanceladaEn;
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
            ResumenDeContrasena = c.ResumenDeContrasena,
            ContrasenaTemporalHasta = c.ContrasenaTemporalHasta,
            DebeCambiarLaContrasena = c.DebeCambiarLaContrasena,
            Aceptacion = c.Aceptacion,
            CreadaEn = c.CreadaEn,
            CanceladaEn = c.CanceladaEn,
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
    public Task<CuentaDeIdentidad?> PorCorreo(string correoNormalizado, CancellationToken cancelacion = default) =>
        Task.FromResult(bd.Cuentas.Values.FirstOrDefault(c => c.Usuario.Correo == correoNormalizado));

    public Task<CuentaDeIdentidad?> PorId(Guid usuarioId, bool paraActualizar = false, CancellationToken cancelacion = default) =>
        Task.FromResult(bd.Cuentas.GetValueOrDefault(usuarioId));

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

    public Task<string> Resumir(Contrasena contrasena, CancellationToken cancelacion = default)
    {
        Resumenes++;
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

    public Task<VersionesDeTextos> Obtener(CancellationToken cancelacion = default) => Task.FromResult(Vigentes);
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

    public Task<ResultadoDeEnvio> Enviar(CorreoSaliente correo, CancellationToken cancelacion = default)
    {
        if (Falla)
        {
            throw new InvalidOperationException("SMTP caído.");
        }

        Correos.Add(correo);
        return Task.FromResult(ResultadoDeEnvio.Enviado);
    }

    public Task<ResultadoDeEnvio> EnviarCodigo(CorreoDeCodigo correo, CancellationToken cancelacion = default)
    {
        if (Falla)
        {
            throw new InvalidOperationException("SMTP caído.");
        }

        Codigos.Add(correo);
        return Task.FromResult(ResultadoDeEnvio.Enviado);
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
