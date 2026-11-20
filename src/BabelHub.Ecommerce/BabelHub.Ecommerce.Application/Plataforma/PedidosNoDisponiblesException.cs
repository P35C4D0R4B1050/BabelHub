namespace BabelHub.Ecommerce.Application.Plataforma;

/// <summary>
/// La base de pedidos no respondió a tiempo (conexión caída, interbloqueo, serialización o consulta cancelada): la Api la traduce a «servicio no disponible» (503) y es reintentable. Vive en <c>Plataforma</c> y no en el
/// <c>Interno</c> de Pedidos para que la Api la traduzca sin depender del interior del módulo (regla 13); el motivo es un código corto, nunca el texto del motor ni un dato del pedido.
/// </summary>
public sealed class PedidosNoDisponiblesException(string motivo) : Exception($"La base de pedidos no está disponible ({motivo}).");
