# 编译 QuoteTodo.exe
# 用法： powershell -ExecutionPolicy Bypass -File build.ps1

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$src  = Join-Path $root "src\QuoteTodo.cs"
$out  = Join-Path $root "QuoteTodo.exe"

Add-Type -AssemblyName PresentationFramework
Add-Type -AssemblyName PresentationCore
Add-Type -AssemblyName WindowsBase
Add-Type -AssemblyName System.Xaml

$fw = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
$refs = @(
  [System.Windows.Window].Assembly.Location,
  [System.Windows.Media.Visual].Assembly.Location,
  [System.Windows.DependencyObject].Assembly.Location,
  [System.Xaml.XamlReader].Assembly.Location,
  "$fw\System.dll",
  "$fw\System.Core.dll",
  "$fw\System.Web.Extensions.dll",
  "$fw\System.Security.dll",
  "$fw\System.Windows.Forms.dll",
  "$fw\System.Drawing.dll",
  "$fw\Microsoft.CSharp.dll"
)

$argsList = @("/nologo", "/target:winexe", "/platform:anycpu", "/optimize+", "/out:$out")
foreach ($r in $refs) { $argsList += "/r:$r" }
$argsList += $src

& "$fw\csc.exe" @argsList
if ($LASTEXITCODE -ne 0) { throw "编译失败 (exit $LASTEXITCODE)" }

$f = Get-Item $out
"OK  {0}  {1:N0} bytes" -f $f.Name, $f.Length
