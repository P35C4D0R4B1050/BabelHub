<#
Genera las portadas de los libros genéricos del prototipo (los que no tienen una portada real) como PNG de 600x900 con la paleta de la marca (prototype/ui-v2/assets/data.js).
Uso (Windows, PowerShell 5.1 o superior):  powershell -File tools/carga-inicial/generar-portadas-genericas.ps1 -Salida D:\Trabajo\BHPortadas
La salida va FUERA del repositorio (ninguna imagen entra al repositorio). Es determinista: el mismo script produce los mismos dibujos.
#>
param([Parameter(Mandatory = $true)][string]$Salida)
Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Force $Salida | Out-Null

# archivo, título, autor, fondo, texto, acento, motivo
$portadas = @(
  @('aprende-csharp-con-net.png', 'Aprende C# con .NET', 'A. Quispe (ficticio)', '#172554', '#F5EBD8', '#FF665A', 'rejilla'),
  @('cuentos-andinos-para-ninos.png', 'Cuentos andinos para niños', 'L. Huamán (ficticio)', '#36C8D4', '#172554', '#FF665A', 'ondas'),
  @('aves-sin-nido.png', 'Aves sin nido', 'Clorinda Matto de Turner', '#A9E85E', '#172554', '#7857D9', 'lineas'),
  @('paco-yunque.png', 'Paco Yunque', 'César Vallejo', '#FF665A', '#172554', '#F5EBD8', 'arcos'),
  @('tradiciones-peruanas.png', 'Tradiciones peruanas', 'Ricardo Palma', '#F5EBD8', '#172554', '#7857D9', 'orbita')
)
function Color($hex) { [System.Drawing.ColorTranslator]::FromHtml($hex) }

foreach ($p in $portadas) {
  $bmp = New-Object System.Drawing.Bitmap 600, 900
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = 'AntiAlias'; $g.TextRenderingHint = 'AntiAliasGridFit'
  $g.Clear((Color $p[3]))
  $acento = New-Object System.Drawing.Pen ((Color $p[5]), 6)
  switch ($p[6]) {
    'rejilla' { for ($i = 0; $i -le 600; $i += 60) { $g.DrawLine($acento, $i, 0, $i, 520); $g.DrawLine($acento, 0, $i * 0.86, 600, $i * 0.86) } }
    'ondas'   { for ($i = 0; $i -lt 8; $i++) { $g.DrawArc($acento, -100 + $i * 20, 40 + $i * 55, 800 - $i * 40, 220, 0, 180) } }
    'lineas'  { for ($i = 0; $i -lt 12; $i++) { $g.DrawLine($acento, 0, 40 + $i * 40, 600, 200 + $i * 40) } }
    'arcos'   { for ($i = 0; $i -lt 6; $i++) { $g.DrawArc($acento, 60 + $i * 40, 40 + $i * 40, 480 - $i * 80, 480 - $i * 80, 180, 180) } }
    'orbita'  { for ($i = 1; $i -le 5; $i++) { $g.DrawEllipse($acento, 300 - $i * 55, 280 - $i * 55, $i * 110, $i * 110) }; $g.FillEllipse((New-Object System.Drawing.SolidBrush (Color $p[5])), 285, 265, 30, 30) }
  }
  $pincel = New-Object System.Drawing.SolidBrush (Color $p[4])
  $titulo = New-Object System.Drawing.Font 'Georgia', 44, ([System.Drawing.FontStyle]::Bold)
  $autor = New-Object System.Drawing.Font 'Georgia', 24
  $marca = New-Object System.Drawing.Font 'Arial', 14, ([System.Drawing.FontStyle]::Bold)
  $formato = New-Object System.Drawing.StringFormat; $formato.Alignment = 'Center'
  $g.DrawString($p[1], $titulo, $pincel, (New-Object System.Drawing.RectangleF 40, 540, 520, 230), $formato)
  $g.DrawString($p[2], $autor, $pincel, (New-Object System.Drawing.RectangleF 40, 790, 520, 50), $formato)
  $g.DrawString('BabelHub · portada de ejemplo', $marca, $pincel, (New-Object System.Drawing.RectangleF 40, 860, 520, 30), $formato)
  $bmp.Save((Join-Path $Salida $p[0]), [System.Drawing.Imaging.ImageFormat]::Png)
  $g.Dispose(); $bmp.Dispose()
}
"portadas genéricas generadas en $Salida"
