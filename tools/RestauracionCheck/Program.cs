using BabelHub.Tools.RestauracionCheck;

// Comprobación de una restauración en el ensayo (PLA-36, PLA-40): salud de los dos servicios, las dos bases restauradas al mismo instante y la
// coherencia entre pedidos y reservas. Solo lectura, con el rol `restauracion-check`. Ver Ejecutor.Ayuda para el uso.
return await Ejecutor.EjecutarAsync(args, Console.Out, Console.Error, CancellationToken.None).ConfigureAwait(false);
