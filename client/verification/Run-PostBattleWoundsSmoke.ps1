param(
    [Parameter(Mandatory = $true)][string]$PlayerPath,
    [string]$OutputDirectory = "$PSScriptRoot\..\ConquestusTerrarumClient\Builds\WoundsResults",
    [string]$ConnectionString = 'Host=localhost;Port=5432;Database=conquestus_terrarum;Username=conquestus;Password=conquestus_local_only'
)
$ErrorActionPreference = 'Stop'
# Requires an isolated player with PostBattleWoundsSmoke.cs and a unique company/product save identity.
$taskRepo = (Resolve-Path "$PSScriptRoot\..\..").Path
$taskPlayer = (Resolve-Path -LiteralPath $PlayerPath).Path
$taskOutput = [IO.Path]::GetFullPath($OutputDirectory)
if ((Test-Path -LiteralPath $taskOutput) -and @(Get-ChildItem -LiteralPath $taskOutput -Force).Count -gt 0) {
    throw 'Use a fresh empty output directory.'
}
New-Item -ItemType Directory -Force $taskOutput | Out-Null
$taskShare = Join-Path $taskOutput 'world-public.json'
$taskBackend = $null
$taskPlayers = @()
function Start-Backend {
    $env:ConnectionStrings__Postgres = $ConnectionString
    $env:CT_TEST_CONNECTION_STRING = $ConnectionString
    $env:ASPNETCORE_URLS = 'http://localhost:5108'
    $env:Logging__EventLog__LogLevel__Default = 'None'
    $taskDll = Join-Path $taskRepo 'src\ConquestusTerrarum.Api\bin\Debug\net10.0\ConquestusTerrarum.Api.dll'
    $script:taskBackend = Start-Process 'C:\Program Files\dotnet\dotnet.exe' -ArgumentList ('"'+$taskDll+'"') -WorkingDirectory $taskRepo -WindowStyle Hidden -PassThru
    $taskDeadline = [DateTime]::UtcNow.AddSeconds(20)
    do {
        Start-Sleep -Milliseconds 100
        if ($script:taskBackend.HasExited) { throw 'Verification API exited.' }
        try { Invoke-WebRequest 'http://localhost:5108/' -UseBasicParsing -TimeoutSec 1 | Out-Null; return }
        catch { if ($_.Exception.Response -and [int]$_.Exception.Response.StatusCode -eq 404) { return } }
    } while ([DateTime]::UtcNow -lt $taskDeadline)
    throw 'API startup timed out.'
}
function Stop-Backend {
    if ($script:taskBackend -and !$script:taskBackend.HasExited) { $script:taskBackend.Kill(); $script:taskBackend.WaitForExit() }
    $script:taskBackend = $null
}
function Start-Player([string]$Role) {
    $taskResult = Join-Path $taskOutput "$Role-result.txt"
    $taskArgs = @('-batchmode','-nographics','-logFile',('"'+(Join-Path $taskOutput "$Role.log")+'"'),
        '--ct-wounds-role',$Role,'--ct-share',('"'+$taskShare+'"'),'--ct-result',('"'+$taskResult+'"'))
    $taskProcess = Start-Process $taskPlayer -ArgumentList $taskArgs -WindowStyle Hidden -PassThru
    $script:taskPlayers += $taskProcess
    return @{ Process = $taskProcess; Result = $taskResult }
}
function Complete-Player($Case) {
    if (!$Case.Process.WaitForExit(120000)) { throw 'Wounds player timed out.' }
    if (!(Test-Path -LiteralPath $Case.Result)) { throw 'Wounds player produced no result.' }
    $taskText = Get-Content -LiteralPath $Case.Result -Raw
    if ($Case.Process.ExitCode -ne 0 -or !$taskText.StartsWith('PASS:')) { throw $taskText }
    Write-Output $taskText
}
try {
    $taskProbe = [Net.Sockets.TcpClient]::new()
    try { $taskProbe.Connect('localhost', 5108); throw 'Stop the existing API before this isolated verification.' }
    catch [Net.Sockets.SocketException] { }
    finally { $taskProbe.Dispose() }
    # Restore/compile before launching players; package/SDK setup must not consume their wait window.
    & 'C:\Program Files\dotnet\dotnet.exe' build "$taskRepo\src\ConquestusTerrarum.Api\ConquestusTerrarum.Api.csproj"
    if ($LASTEXITCODE -ne 0) { throw 'Verification API build failed.' }
    & 'C:\Program Files\dotnet\dotnet.exe' build "$PSScriptRoot\WoundsSetup\WoundsSetup.csproj"
    if ($LASTEXITCODE -ne 0) { throw 'Verification fixture build failed.' }
    Start-Backend
    $taskCreator = Start-Player 'create'
    $taskJoiner = Start-Player 'join'
    $taskDeadline = [DateTime]::UtcNow.AddSeconds(45)
    while (!(Test-Path -LiteralPath ($taskShare + '.ready.create')) -or !(Test-Path -LiteralPath ($taskShare + '.ready.join'))) {
        if ([DateTime]::UtcNow -gt $taskDeadline -or $taskCreator.Process.HasExited -or $taskJoiner.Process.HasExited) {
            throw 'Players failed to become active; inspect their sanitized logs/results.'
        }
        Start-Sleep -Milliseconds 100
    }
    $taskFirst = Get-Content -LiteralPath $taskShare -Raw | ConvertFrom-Json
    $taskSecond = Get-Content -LiteralPath ($taskShare + '.joiner') -Raw | ConvertFrom-Json
    & 'C:\Program Files\dotnet\dotnet.exe' "$PSScriptRoot\WoundsSetup\bin\Debug\net10.0\WoundsSetup.dll" $taskFirst.matchId $taskFirst.playerId $taskSecond.playerId
    if ($LASTEXITCODE -ne 0) { throw 'Verification health fixture failed.' }
    Set-Content -LiteralPath ($taskShare + '.configured') -Value 'configured'
    Complete-Player $taskCreator
    Complete-Player $taskJoiner
    Stop-Backend
    Start-Backend
    Complete-Player (Start-Player 'resume-create')
    Complete-Player (Start-Player 'resume-join')
}
finally {
    foreach ($taskProcess in $taskPlayers) { if (!$taskProcess.HasExited) { $taskProcess.Kill(); $taskProcess.WaitForExit() } }
    Stop-Backend
}
