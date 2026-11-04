$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$testExe = Join-Path $env:TEMP ('QuietReaderUI-' + [guid]::NewGuid().ToString('N') + '.exe')
try {
    $files = @('Core.cs','Epub.cs','Hotkeys.cs','Ios.cs','Skins.cs','ReaderForm.cs','UITests.cs') | ForEach-Object { Join-Path $root $_ }
    $dialogs = Join-Path $root 'Dialogs.cs'
    if (Test-Path -LiteralPath $dialogs) { $files += $dialogs }
    & $compiler /nologo /target:exe /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.IO.Compression.dll /out:$testExe $files
    if ($LASTEXITCODE -ne 0) { throw 'UI test build failed' }
    $novel = Get-ChildItem -LiteralPath (Join-Path (Split-Path -Parent $root) 'samples') -Filter '*.txt' | Select-Object -First 1
    & $testExe $novel.FullName
    if ($LASTEXITCODE -ne 0) { throw 'UI tests failed' }
} finally { Remove-Item -LiteralPath $testExe -ErrorAction SilentlyContinue }
