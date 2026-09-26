param([switch]$Test)
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw '.NET Framework x64 C# compiler not found.' }
$source = Join-Path $PSScriptRoot 'src\FixedVolumeMixer.cs'
$icon = Join-Path $PSScriptRoot 'assets\FixedVolumeMixer.ico'
$dist = Join-Path $PSScriptRoot 'dist'
New-Item -ItemType Directory -Force -Path $dist | Out-Null
$refs = @('/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll','/reference:System.Web.Extensions.dll')
& $compiler /nologo /target:winexe /platform:x64 /optimize+ "/win32icon:$icon" "/resource:$icon,FixedVolumeMixer.AppIcon" "/out:$dist\FixedVolumeMixer.exe" @refs $source
if ($LASTEXITCODE -ne 0) { throw 'Application compilation failed.' }
Write-Host 'Built dist/FixedVolumeMixer.exe'
if ($Test) {
    $build = Join-Path $PSScriptRoot '.build'
    New-Item -ItemType Directory -Force -Path $build | Out-Null
    $selfReport = Join-Path $build 'self-test.txt'
    $self = Start-Process -FilePath "$dist\FixedVolumeMixer.exe" -ArgumentList @('--self-test',('"'+$selfReport+'"')) -WindowStyle Hidden -Wait -PassThru
    if ($self.ExitCode -ne 0) { if(Test-Path $selfReport){Get-Content $selfReport}; throw 'Self-test failed.' }
    Get-Content -LiteralPath $selfReport
    & $compiler /nologo /target:winexe /platform:x64 /main:FixedVolumeMixer.MixerSessionProbe "/out:$build\MixerSessionProbe.exe" @refs $source (Join-Path $PSScriptRoot 'tests\MixerSessionProbe.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Probe compilation failed.' }
    Copy-Item -LiteralPath "$build\MixerSessionProbe.exe" -Destination "$build\MixerDiscovered.exe" -Force
    & $compiler /nologo /target:exe /platform:x64 /main:FixedVolumeMixer.MixerIntegrationTest "/out:$build\MixerIntegrationTest.exe" @refs $source (Join-Path $PSScriptRoot 'tests\MixerIntegrationTest.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Integration test compilation failed.' }
    & "$build\MixerIntegrationTest.exe" $build
    if ($LASTEXITCODE -ne 0) { throw 'Integration test failed.' }
}
