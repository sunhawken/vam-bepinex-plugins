# Rebuild the shim without generated stubs, regenerate VamStubs.g.cs for every DLL in ./in, rebuild shim + runner.
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
dotnet build StubGen/StubGen.csproj -c Release --nologo -v q -o stubgen_bin | Out-Null
dotnet build UnityShim/UnityShim.csproj -c Release --nologo -v q -p:NoStubs=true -o bin_nostubs | Select-String 'error' | Select-Object -First 5
$dlls = @(Get-ChildItem in -Filter *.dll | ForEach-Object { $_.FullName }) + @(Get-ChildItem in\cores -Filter *.dll -ErrorAction SilentlyContinue | ForEach-Object { '@types:' + $_.FullName })
& .\stubgen_bin\stubgen.exe UnityShim\VamStubs.g.cs bin_nostubs\UnityShim.dll 'T:\New folder\VaM_Data\Managed' @dlls | Where-Object { $_ -notmatch '^note' }
dotnet build Runner/Runner.csproj -c Release --nologo -v q -o bin | Select-String 'error' | Select-Object -First 15
