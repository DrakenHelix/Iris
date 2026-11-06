param([string]$Python = 'python')
$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$build = Join-Path $PSScriptRoot 'build'
$app = Join-Path $PSScriptRoot 'app'

$presentMon = Join-Path $app 'Tools/PresentMon/PresentMon.exe'
$presentMonHash = 'B2A706BC6AD475749E3B7E3409263AA1E6906D45BDCF993F6DBC0F660188F1AF'
if (!(Test-Path -LiteralPath $presentMon)) {
    $download = "$presentMon.download"
    try {
        Invoke-WebRequest -UseBasicParsing -TimeoutSec 90 -Uri 'https://github.com/GameTechDev/PresentMon/releases/download/v2.6.0/PresentMon-2.6.0-x64.exe' -OutFile $download
        if ((Get-FileHash -LiteralPath $download).Hash -ne $presentMonHash) { throw 'PresentMon download digest does not match.' }
        Move-Item -LiteralPath $download -Destination $presentMon
    } finally {
        if (Test-Path -LiteralPath $download) { Remove-Item -LiteralPath $download }
    }
}
if ((Get-FileHash -LiteralPath $presentMon).Hash -ne $presentMonHash) { throw 'PresentMon has an unexpected digest.' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LICENSE') -Destination (Join-Path $app 'LICENSE-Iris.txt') -Force

& (Join-Path $app 'build.cmd')
if ($LASTEXITCODE -ne 0) { throw 'Iris application build failed.' }
& $Python (Join-Path $PSScriptRoot 'build-msi.py')
if ($LASTEXITCODE -ne 0) { throw 'Iris MSI build failed.' }

$versionSource = Join-Path $app 'source/ReleaseVersion.cs'
$match = [regex]::Match([IO.File]::ReadAllText($versionSource), '\bNumber = "(\d+\.\d+)\.\d+";')
if (!$match.Success) { throw 'Iris release version is missing.' }
$setup = Join-Path $build ("Iris-{0}-Setup.exe" -f $match.Groups[1].Value)
$args = @(
    '/nologo', '/target:winexe', '/platform:x64', '/optimize+',
    "/out:$setup",
    "/win32manifest:$(Join-Path $PSScriptRoot 'Setup.manifest')",
    "/win32icon:$(Join-Path $app 'Iris.ico')",
    "/resource:$(Join-Path $build 'Iris.msi'),Iris.Package",
    "/resource:$(Join-Path $app 'BrowserExtension/icon128.png'),Iris.Brand",
    "/reference:$(Join-Path $framework 'System.Xaml.dll')",
    "/reference:$(Join-Path $framework 'System.Windows.Forms.dll')",
    "/reference:$(Join-Path $framework 'WPF/WindowsBase.dll')",
    "/reference:$(Join-Path $framework 'WPF/PresentationCore.dll')",
    "/reference:$(Join-Path $framework 'WPF/PresentationFramework.dll')",
    $versionSource,
    (Join-Path $app 'source/WindowsStartup.cs'),
    (Join-Path $build 'InstallerIdentity.g.cs'),
    (Join-Path $PSScriptRoot 'Setup.cs')
)
& (Join-Path $framework 'csc.exe') @args
if ($LASTEXITCODE -ne 0) { throw 'Iris Setup build failed.' }
Get-Item -LiteralPath $setup | Select-Object Name, Length
