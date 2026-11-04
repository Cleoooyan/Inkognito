$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$testExe = Join-Path $env:TEMP ('QuietReaderTests-' + [guid]::NewGuid().ToString('N') + '.exe')
try {
    & $compiler /nologo /target:exe /r:System.IO.Compression.dll /out:$testExe (Join-Path $root 'Core.cs') (Join-Path $root 'Epub.cs') (Join-Path $root 'Tests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Test build failed' }
    $novel = Get-ChildItem -LiteralPath (Join-Path (Split-Path -Parent $root) 'samples') -Filter '*.txt' | Select-Object -First 1
    & $testExe $novel.FullName
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }
} finally { Remove-Item -LiteralPath $testExe -ErrorAction SilentlyContinue }
