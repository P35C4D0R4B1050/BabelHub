namespace BabelHub.Inventario.Application.Plataforma;

/// <summary>
/// <b>Copia del E-commerce que el Inventario no lanza</b>: el manejo de errores de la plataforma es una copia idéntica en los dos servicios (regla 5) y traduce esta excepción a 503; existe aquí solo para que la copia compile.
/// La base de pedidos no respondió a tiempo (conexión caída, interbloqueo, serialización o consulta cancelada): la Api la traduce a «servicio no disponible» (503) y es reintentable. Vive en <c>Plataforma</c> y no en el
/// <c>Interno</c> de Pedidos para que la Api la traduzca sin depender del interior del módulo (regla 13); el motivo es un código corto, nunca el texto del motor ni un dato del pedido.
/// </summary>
public sealed class PedidosNoDisponiblesException(string motivo) : Exception($"La base de pedidos no está disponible ({motivo}).");
