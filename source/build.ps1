$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$files = @('Core.cs','Epub.cs','Hotkeys.cs','Ios.cs','Skins.cs','ReaderForm.cs','Dialogs.cs','Program.cs') | ForEach-Object { Join-Path $root $_ }
$exe = Join-Path (Split-Path -Parent $root) 'Inkognito.exe'
$manifest = '/win32manifest:' + (Join-Path $root 'app.manifest')
& $compiler /nologo /optimize+ /target:winexe /platform:anycpu /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.IO.Compression.dll $manifest /out:$exe $files
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
Write-Output $exe
