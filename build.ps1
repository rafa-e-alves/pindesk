# Compila o PinDesk.exe usando o compilador C# que já vem com o Windows (.NET Framework 4.x).
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$src = Join-Path $root 'src'
$out = Join-Path $root 'PinDesk.exe'
$ico = Join-Path $src 'PinDesk.ico'
$csc = Join-Path ([Runtime.InteropServices.RuntimeEnvironment]::GetRuntimeDirectory()) 'csc.exe'

if (Get-Process PinDesk -ErrorAction SilentlyContinue) {
    throw 'O PinDesk esta aberto. Clica com o botao direito no alfinete da bandeja > Sair, e roda o build de novo.'
}

# Gera o .ico do exe com o mesmo desenho do icone da bandeja.
Add-Type -Path (Join-Path $src 'IconArt.cs') -ReferencedAssemblies System.Drawing
[PinDesk.IconArt]::SaveIco($ico)

$sources = Get-ChildItem $src -Filter *.cs | ForEach-Object { $_.FullName }
& $csc /nologo /target:winexe /optimize+ /platform:anycpu "/out:$out" "/win32icon:$ico" `
    /r:System.Windows.Forms.dll /r:System.Drawing.dll $sources
if ($LASTEXITCODE -ne 0) { throw 'Falhou a compilacao.' }

Write-Host "Pronto: $out"
