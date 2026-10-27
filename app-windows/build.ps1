# Builds STAWN CAD Pro for Windows 11: the C++ core (stawn.dll, stawn-cli, tests) with CMake and
# vcpkg, then the WinUI 3 app with dotnet. Run from a "Developer PowerShell for VS 2022".
#
#   .\app-windows\build.ps1              # Release, for this machine's architecture
#   .\app-windows\build.ps1 -Run         # ... and start the app
#
# Under Parallels on an Apple silicon Mac, Windows 11 is ARM64 and so is the build.
param(
    [ValidateSet("Release", "Debug")] [string]$Configuration = "Release",
    [ValidateSet("auto", "x64", "ARM64")] [string]$Arch = "auto",
    [switch]$Run,
    # Tests cannot run when cross-building ARM64 on an x64 machine (CI).
    [switch]$SkipTests
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
if ($Arch -eq "auto") { $Arch = if ($env:PROCESSOR_ARCHITECTURE -eq "ARM64") { "ARM64" } else { "x64" } }
$triplet = if ($Arch -eq "ARM64") { "arm64-windows" } else { "x64-windows" }
$rid = if ($Arch -eq "ARM64") { "win-arm64" } else { "win-x64" }

if (-not $env:VCPKG_ROOT) { throw "Set VCPKG_ROOT to your vcpkg clone (git clone https://github.com/microsoft/vcpkg; .\vcpkg\bootstrap-vcpkg.bat)." }

$build = Join-Path $root "build-windows"
cmake -S $root -B $build -G "Visual Studio 17 2022" -A $Arch `
    "-DCMAKE_TOOLCHAIN_FILE=$env:VCPKG_ROOT\scripts\buildsystems\vcpkg.cmake" "-DVCPKG_TARGET_TRIPLET=$triplet" -DBUILD_TESTING=ON
if ($LASTEXITCODE) { throw "CMake configure failed" }
cmake --build $build --config $Configuration --parallel
if ($LASTEXITCODE) { throw "Core build failed" }
if (-not $SkipTests) {
    ctest --test-dir $build -C $Configuration --output-on-failure
    if ($LASTEXITCODE) { throw "Core tests failed" }
}

# stawn.dll plus the OpenCascade and TBB DLLs vcpkg copied next to it.
$native = Join-Path $build "native"
New-Item -ItemType Directory -Force $native | Out-Null
Copy-Item (Join-Path $build "$Configuration\*.dll") $native -Force

$project = Join-Path $PSScriptRoot "StawnCADPro.Windows\StawnCADPro.Windows.csproj"
$out = Join-Path $build "app"
# WinUI needs Visual Studio's MSBuild: the dotnet CLI lacks the PRI resource tasks.
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$msbuild = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
if (-not $msbuild) { throw "MSBuild from Visual Studio 2022 was not found." }
& $msbuild $project -restore -t:Publish -v:minimal "-p:Configuration=$Configuration" "-p:Platform=$Arch" `
    "-p:RuntimeIdentifier=$rid" "-p:StawnNativeDir=$native" "-p:PublishDir=$out\"
if ($LASTEXITCODE) { throw "App build failed" }
Write-Host "Built $out\StawnCADPro.exe"
if ($Run) { Start-Process (Join-Path $out "StawnCADPro.exe") }
