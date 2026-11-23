<#
Completa los datos que NO son de redacción de tools/carga-inicial/catalogo.csv de forma determinista: el ISBN provisional, el precio en soles y el stock inicial por sucursal.
Solo llena las celdas vacías (lo que ya está escrito, por ejemplo un ISBN real, no se toca), así que se puede volver a ejecutar sin cambiar nada.

Regla (la misma que documenta docs/07-proceso-de-desarrollo/09-seleccion-del-catalogo-inicial.md):
  u(clave, etiqueta) = FNV-1a de 32 bits de «babelhub-carga-inicial-2026-11|<clave>|<etiqueta>» dividido entre 2^32 (un número en [0, 1)); ninguna otra fuente de azar.
  - Precio impreso: piso(25 + u(clave,"precio") × 95) + 0,90, es decir, de S/ 25,90 a S/ 119,90.
  - Precio digital (solo si hay edición digital): piso(precio impreso × 0,45) + 0,90.
  - Stock: Lima (la sucursal de recepción) siempre, más las DOS sucursales restantes con menor u(clave,"orden-<código>"); en cada una, 3 + piso(u(clave,"stock-<código>") × 23), es decir, de 3 a 25 unidades.
    Las demás sucursales quedan vacías (un conteo en blanco no se carga): así una carga completa cabe en dos ventanas del presupuesto de ajustes (60 por hora, SEG-30).
  - ISBN provisional: 979-0 + un número de serie de 8 dígitos (la fila para el impreso, 1000 + la fila para el digital) + el dígito de control. El prefijo 979-0 no se asigna a libros
    (es el de las partituras): nunca coincide con el ISBN real de un título. Es un identificador PROVISIONAL de formato válido (el catálogo exige un ISBN con dígito de control), no el
    ISBN de la edición real: si se conoce el ISBN real, escríbelo en el CSV antes de cargar (el ISBN de una edición no se puede cambiar después).
Uso: powershell -File tools/carga-inicial/completar-datos.ps1 [-Archivo tools/carga-inicial/catalogo.csv]
#>
param([string]$Archivo = (Join-Path $PSScriptRoot 'catalogo.csv'))
$ErrorActionPreference = 'Stop'
$semilla = 'babelhub-carga-inicial-2026-11'
$sucursales = @('lima', 'arequipa', 'trujillo', 'cusco', 'piura', 'ayacucho')

function U([string]$clave, [string]$etiqueta) {
  $h = [uint64]2166136261
  foreach ($b in [Text.Encoding]::UTF8.GetBytes("$semilla|$clave|$etiqueta")) { $h = (($h -bxor $b) * [uint64]16777619) % [uint64]4294967296 }
  [double]$h / 4294967296.0
}
function DigitoDeControl([string]$doce) {
  $suma = 0; for ($i = 0; $i -lt 12; $i++) { $suma += ([int][string]$doce[$i]) * $(if ($i % 2 -eq 0) { 1 } else { 3 }) }
  [string]((10 - ($suma % 10)) % 10)
}
function Isbn([long]$serie) { $doce = '9790' + $serie.ToString('D8'); $doce + (DigitoDeControl $doce) }
function Moneda([double]$valor) { $valor.ToString('0.00', [Globalization.CultureInfo]::InvariantCulture) }

$lineas = [IO.File]::ReadAllLines($Archivo, [Text.Encoding]::UTF8)
$cabecera = $lineas[0].Split(';')
$resultado = New-Object System.Collections.Generic.List[string]
$resultado.Add($lineas[0])
for ($n = 1; $n -lt $lineas.Count; $n++) {
  if ([string]::IsNullOrWhiteSpace($lineas[$n])) { continue }
  $c = $lineas[$n].Split(';')
  if ($c.Count -ne $cabecera.Count) { throw "Fila $n`: $($c.Count) columnas y la cabecera tiene $($cabecera.Count)." }
  $f = @{}; for ($i = 0; $i -lt $cabecera.Count; $i++) { $f[$cabecera[$i]] = $c[$i] }
  $clave = $f['clave']
  if ($f['isbn_impreso'] -eq '') { $f['isbn_impreso'] = Isbn $n }
  if ($f['precio_impreso'] -eq '') { $f['precio_impreso'] = Moneda ([Math]::Floor(25 + (U $clave 'precio') * 95) + 0.90) }
  if ($f['formato_digital'] -ne '') {
    if ($f['isbn_digital'] -eq '') { $f['isbn_digital'] = Isbn (1000 + $n) }
    if ($f['precio_digital'] -eq '') { $f['precio_digital'] = Moneda ([Math]::Floor([double]::Parse($f['precio_impreso'], [Globalization.CultureInfo]::InvariantCulture) * 0.45) + 0.90) }
  }
  $sinStock = ($sucursales | Where-Object { $f["stock_$_"] -ne '' }).Count -eq 0
  if ($sinStock) {
    $otras = $sucursales | Where-Object { $_ -ne 'lima' } | Sort-Object { U $clave "orden-$_" } | Select-Object -First 2
    foreach ($s in (@('lima') + $otras)) { $f["stock_$s"] = [string](3 + [Math]::Floor((U $clave "stock-$s") * 23)) }
  }
  $resultado.Add((($cabecera | ForEach-Object { $f[$_] }) -join ';'))
}
[IO.File]::WriteAllLines($Archivo, $resultado, (New-Object Text.UTF8Encoding $false))
"Completadas $($resultado.Count - 1) filas en $Archivo"
