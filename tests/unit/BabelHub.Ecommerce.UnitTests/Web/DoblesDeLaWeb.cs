using System.Text;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Web;
using BabelHub.Ecommerce.Application.Web.Interno;
using BabelHub.Ecommerce.Domain.Web;

namespace BabelHub.Ecommerce.UnitTests.Web;

/// <summary>Dobles de lo que el módulo <c>Web</c> usa y aún no existe (Identidad, Auditoría) o necesita una base de datos (T04).</summary>
internal sealed class AlmacenDeSesionesEnMemoria : IAlmacenDeSesionesWeb
{
    public Dictionary<string, SesionGuardada> Filas { get; } = [];

    public int CargasParaRenovar { get; private set; }

    public Task Insertar(SesionGuardada sesion, CancellationToken cancelacion = default)
    {
        Filas.Add(sesion.Sesion.Id.Valor, sesion);
        return Task.CompletedTask;
    }

    public Task<SesionGuardada?> Cargar(HashDeSesion id, CancellationToken cancelacion = default) =>
        Task.FromResult(Filas.GetValueOrDefault(id.Valor));

    public Task<SesionGuardada?> CargarParaRenovar(HashDeSesion id, CancellationToken cancelacion = default)
    {
        CargasParaRenovar++;
        return Task.FromResult(Filas.GetValueOrDefault(id.Valor));
    }

    public Task Guardar(SesionGuardada sesion, CancellationToken cancelacion = default)
    {
        Filas[sesion.Sesion.Id.Valor] = sesion;
        return Task.CompletedTask;
    }

    public Task ActualizarActividad(HashDeSesion id, DateTimeOffset ultimaActividad, CancellationToken cancelacion = default)
    {
        ActualizacionesDeActividad++;
        return Task.CompletedTask;
    }

    public int ActualizacionesDeActividad { get; private set; }

    public Task<IReadOnlyList<SesionGuardada>> CargarAbiertasDeUnUsuario(Guid usuarioId, CancellationToken cancelacion = default) =>
        Task.FromResult<IReadOnlyList<SesionGuardada>>([.. Filas.Values.Where(f => f.Sesion.UsuarioId == usuarioId && f.Sesion.Estado == EstadoDeSesion.Abierta)]);
}

internal sealed class AlmacenDeBorradoresEnMemoria : IAlmacenDeBorradores
{
    public List<BorradorGuardado> Filas { get; } = [];

    public Task<int> Contar(Guid usuarioId, DateTimeOffset ahora, CancellationToken cancelacion = default) =>
        Task.FromResult(Filas.Count(f => f.Borrador.UsuarioId == usuarioId && f.Borrador.VenceEn > ahora));

    public Task<BorradorGuardado?> Cargar(Guid usuarioId, string plantillaDeRuta, CancellationToken cancelacion = default) =>
        Task.FromResult(Filas.FirstOrDefault(f => f.Borrador.UsuarioId == usuarioId && f.Borrador.Plantilla == plantillaDeRuta));

    public Task Guardar(BorradorGuardado borrador, CancellationToken cancelacion = default)
    {
        Filas.RemoveAll(f => f.Borrador.UsuarioId == borrador.Borrador.UsuarioId && f.Borrador.Plantilla == borrador.Borrador.Plantilla);
        Filas.Add(borrador);
        return Task.CompletedTask;
    }

    public Task Borrar(Guid id, CancellationToken cancelacion = default)
    {
        Filas.RemoveAll(f => f.Borrador.Id == id);
        return Task.CompletedTask;
    }
}

/// <summary>Cifrado reversible de prueba que respeta lo esencial del contrato: el texto solo se descifra con el mismo propósito y los mismos datos asociados.</summary>
internal sealed class CifradorDePrueba : ICifradorDeSecretosDeSesion
{
    public short VersionVigente { get; set; } = 1;

    public bool Falla { get; set; }

    public TextoCifrado Cifrar(string texto, PropositoDeSecreto proposito, string datosAsociados) =>
        new(Encoding.UTF8.GetBytes($"{proposito}|{datosAsociados}|{texto}"), VersionVigente);

    public string Descifrar(byte[] cifrado, short versionDeClave, PropositoDeSecreto proposito, string datosAsociados)
    {
        var texto = Encoding.UTF8.GetString(cifrado);
        var prefijo = $"{proposito}|{datosAsociados}|";
        if (Falla || !texto.StartsWith(prefijo, StringComparison.Ordinal))
        {
            throw new SecretoDeSesionIlegibleException();
        }

        return texto[prefijo.Length..];
    }
}

internal sealed class AleatoriosDePrueba : IAleatoriosSeguros
{
    private byte _siguiente = 1;

    public byte[] Bytes(int cantidad)
    {
        var bytes = new byte[cantidad];
        Array.Fill(bytes, _siguiente++);
        return bytes;
    }
}

internal sealed class SesionesDeIdentidadFalsas : ISesionesDeIdentidad
{
    public Queue<ResultadoDeRenovacion> Renovaciones { get; } = [];

    public int LlamadasARenovar { get; private set; }

    public List<string> Revocadas { get; } = [];

    public bool ConfirmaRevocaciones { get; set; } = true;

    public List<Guid> CerradasTodas { get; } = [];

    public bool CierraTodas { get; set; } = true;

    public Func<Task>? AntesDeRenovar { get; set; }

    public async Task<ResultadoDeRenovacion> Renovar(string renovacion, CancellationToken cancelacion = default)
    {
        LlamadasARenovar++;
        if (AntesDeRenovar is not null)
        {
            await AntesDeRenovar();
        }

        return Renovaciones.Count > 0 ? Renovaciones.Dequeue() : new ResultadoDeRenovacion.Transitoria();
    }

    public Task<bool> RevocarRenovacion(string renovacion, CancellationToken cancelacion = default)
    {
        Revocadas.Add(renovacion);
        return Task.FromResult(ConfirmaRevocaciones);
    }

    public Task<bool> CerrarTodasLasSesiones(Guid usuarioId, CancellationToken cancelacion = default)
    {
        CerradasTodas.Add(usuarioId);
        return Task.FromResult(CierraTodas);
    }
}

internal sealed class VerificadorFalso : IVerificadorDeCredencial
{
    public Func<string, DateTimeOffset, VerificacionDeCredencial> Regla { get; set; } = (_, _) => new VerificacionDeCredencial(EstadoDeVerificacion.Invalida, null);

    public int Llamadas { get; private set; }

    public VerificacionDeCredencial Verificar(string credencial, DateTimeOffset ahora)
    {
        Llamadas++;
        return Regla(credencial, ahora);
    }
}

internal sealed class RegistroDeSeguridadEspia : IRegistroDeSeguridadDeLaWeb
{
    public List<(MotivoDeDenegacionWeb Motivo, Guid? Usuario)> Eventos { get; } = [];

    public Task AccesoDenegado(MotivoDeDenegacionWeb motivo, Guid? usuarioId, CancellationToken cancelacion = default)
    {
        Eventos.Add((motivo, usuarioId));
        return Task.CompletedTask;
    }
}

internal sealed class AlertasTecnicasEspia : IPublicadorDeAlertasTecnicas
{
    public List<AlertaTecnica> Alertas { get; } = [];

    public Task Publicar(AlertaTecnica alerta, CancellationToken cancelacion = default)
    {
        Alertas.Add(alerta);
        return Task.CompletedTask;
    }
}

/// <summary>Una unidad de trabajo que ejecuta el trabajo tal cual: el Dominio y la Aplicación se prueban sin base de datos.</summary>
internal sealed class UnidadDeTrabajoSimple : IUnidadDeTrabajo
{
    public int Ejecuciones { get; private set; }

    public async Task<T> Ejecutar<T>(Func<CancellationToken, Task<T>> trabajo, CancellationToken cancelacion = default)
    {
        Ejecuciones++;
        return await trabajo(cancelacion);
    }

    public Task EjecutarComo(EsquemaDeModulo esquema, Func<CancellationToken, Task> bloque, CancellationToken cancelacion = default) => bloque(cancelacion);
}
