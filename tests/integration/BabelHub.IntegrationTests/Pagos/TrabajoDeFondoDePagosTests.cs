using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Infrastructure.Pagos;
using BabelHub.Shared.Contratos.Auditoria;

namespace BabelHub.IntegrationTests.Pagos;

/// <summary>
/// T27: el ejecutor de los trabajos de Pagos (AC-PAG-10, PAG-18): cada corrida va dentro del alcance de sistema que da el anfitrión, su resultado se registra, un fallo nunca deja caer al servicio, la alerta <c>trabajo-de-pagos-sin-correr</c> sale tras tres
/// fallos seguidos y de nuevo cada tres, también por pasar un día sin una corrida exitosa, y solo la cancelación del apagado se propaga.
/// </summary>
public sealed class TrabajoDeFondoDePagosTests
{
    private static readonly DateTimeOffset Inicio = new(2026, 11, 19, 10, 0, 0, TimeSpan.Zero);

    private sealed class Entorno
    {
        public Entorno(Func<CancellationToken, Task> corrida, TimeSpan? intervalo = null)
        {
            Trabajo = new TrabajoDeFondoDePagos("pagos-prueba", corrida, Registro, EnElAlcance, Alertas, Reloj, new OpcionesDelTrabajoDePagos(intervalo ?? TimeSpan.FromMinutes(1)));
        }

        public RelojManual Reloj { get; } = new(Inicio);

        public RegistroEspia Registro { get; } = new();

        public AlertasEspia Alertas { get; } = new();

        public int AlcancesAbiertos { get; private set; }

        public TrabajoDeFondoDePagos Trabajo { get; }

        private async Task EnElAlcance(Func<CancellationToken, Task> corrida, CancellationToken cancelacion)
        {
            AlcancesAbiertos++;
            await corrida(cancelacion);
        }
    }

    [Fact]
    public async Task Una_corrida_exitosa_corre_dentro_del_alcance_de_sistema_y_se_registra()
    {
        var corridas = 0;
        var e = new Entorno(_ => { corridas++; return Task.CompletedTask; });

        await e.Trabajo.EjecutarUnaVez();

        Assert.Equal((1, 1), (corridas, e.AlcancesAbiertos));
        Assert.Equal(("pagos-prueba", ResultadoDeTrabajo.Exito), Assert.Single(e.Registro.Corridas));
        Assert.Empty(e.Alertas.Publicadas);
    }

    [Fact]
    public async Task Un_fallo_se_registra_no_se_propaga_y_alerta_al_tercero_seguido_y_de_nuevo_al_sexto()
    {
        var e = new Entorno(_ => throw new InvalidOperationException("la base cayó"));

        for (var i = 1; i <= 6; i++)
        {
            await e.Trabajo.EjecutarUnaVez();
            Assert.Equal(i >= 6 ? 2 : i >= 3 ? 1 : 0, e.Alertas.Publicadas.Count);
        }

        Assert.All(e.Registro.Corridas, c => Assert.Equal(ResultadoDeTrabajo.Fallo, c.Resultado));
        Assert.All(e.Alertas.Publicadas, a => Assert.Equal(MotivosDeAlertaDeAuditoria.TrabajoDePagosSinCorrer, a.Motivo));
    }

    [Fact]
    public async Task Un_dia_sin_una_corrida_exitosa_alerta_aunque_no_lleguen_a_tres_fallos()
    {
        var e = new Entorno(_ => throw new InvalidOperationException("la base cayó"));
        await e.Trabajo.EjecutarUnaVez();
        e.Reloj.Avanzar(TrabajosDeFondoDePagos.SinCorrerHasta);

        await e.Trabajo.EjecutarUnaVez();

        Assert.Single(e.Alertas.Publicadas);
    }

    private sealed class RelojManual(DateTimeOffset inicio) : IReloj
    {
        public DateTimeOffset Ahora { get; private set; } = inicio;

        public DateOnly HoyEnLima => DateOnly.FromDateTime(Ahora.UtcDateTime.AddHours(-5));

        public void Avanzar(TimeSpan tiempo) => Ahora += tiempo;
    }

    private sealed class RegistroEspia : IRegistroDeTrabajosDeFondo
    {
        public List<(string Trabajo, ResultadoDeTrabajo Resultado)> Corridas { get; } = [];

        public bool Falla { get; set; }

        public Task Registrar(string trabajo, ResultadoDeTrabajo resultado, DateTimeOffset instante, CancellationToken cancelacion = default)
        {
            if (Falla)
            {
                throw new InvalidOperationException("El registro no responde.");
            }

            Corridas.Add((trabajo, resultado));
            return Task.CompletedTask;
        }
    }

    private sealed class AlertasEspia : IPublicadorDeAlertasTecnicas
    {
        public List<AlertaTecnica> Publicadas { get; } = [];

        public bool Falla { get; set; }

        public Task Publicar(AlertaTecnica alerta, CancellationToken cancelacion = default)
        {
            if (Falla)
            {
                throw new InvalidOperationException("Las alertas no responden.");
            }

            Publicadas.Add(alerta);
            return Task.CompletedTask;
        }
    }
}
