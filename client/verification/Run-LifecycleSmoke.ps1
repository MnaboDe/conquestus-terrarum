param(
    [Parameter(Mandatory = $true)][string]$PlayerPath,
    [string]$OutputDirectory = "$PSScriptRoot\..\ConquestusTerrarumClient\Builds\LifecycleResults",
    [string]$ConnectionString = 'Host=localhost;Port=5432;Database=conquestus_terrarum;Username=conquestus;Password=conquestus_local_only'
)
$ErrorActionPreference = 'Stop'
# Requires an isolated player containing LifecycleSmoke.cs and a unique company/product identity.
$taskRepo = (Resolve-Path "$PSScriptRoot\..\..").Path
$taskPlayer = (Resolve-Path -LiteralPath $PlayerPath).Path
$taskOutput = [IO.Path]::GetFullPath($OutputDirectory)
if ((Test-Path -LiteralPath $taskOutput) -and @(Get-ChildItem -LiteralPath $taskOutput -Force).Count -gt 0) {
    throw 'Use a new empty output directory.'
}
New-Item -ItemType Directory -Force $taskOutput | Out-Null
$taskShare = Join-Path $taskOutput 'session-public.json'
$taskBackend = $null
$taskPlayers = @()
function Start-Player([string]$Role) {
    $taskResult = Join-Path $taskOutput "$Role-result.txt"
    $taskArgs = @('-batchmode','-nographics','-logFile',('"'+(Join-Path $taskOutput "$Role.log")+'"'),
        '--ct-lifecycle-role',$Role,'--ct-share',('"'+$taskShare+'"'),'--ct-result',('"'+$taskResult+'"'))
    $taskProcess = Start-Process $taskPlayer -ArgumentList $taskArgs -WindowStyle Hidden -PassThru
    $script:taskPlayers += $taskProcess
    return @{ Process = $taskProcess; Result = $taskResult }
}
function Complete-Player($Case) {
    if (!$Case.Process.WaitForExit(120000)) { throw 'Lifecycle player timed out.' }
    if (!(Test-Path -LiteralPath $Case.Result)) { throw 'Lifecycle player produced no result.' }
    $taskText = Get-Content -LiteralPath $Case.Result -Raw
    if ($Case.Process.ExitCode -ne 0 -or !$taskText.StartsWith('PASS:')) { throw $taskText }
    Write-Output $taskText
}
try {
    $taskProbe = [Net.Sockets.TcpClient]::new()
    try { $taskProbe.Connect('localhost', 5108); throw 'Stop the existing API before this isolated test.' }
    catch [Net.Sockets.SocketException] { }
    finally { $taskProbe.Dispose() }
    $env:ConnectionStrings__Postgres = $ConnectionString
    $env:CT_TEST_CONNECTION_STRING = $ConnectionString
    $env:ASPNETCORE_URLS = 'http://localhost:5108'
    $env:Logging__EventLog__LogLevel__Default = 'None'
    $taskDll = Join-Path $taskRepo 'src\ConquestusTerrarum.Api\bin\Debug\net10.0\ConquestusTerrarum.Api.dll'
    $taskBackend = Start-Process 'C:\Program Files\dotnet\dotnet.exe' -ArgumentList ('"'+$taskDll+'"') -WorkingDirectory $taskRepo -WindowStyle Hidden -PassThru
    $taskReady = $false
    $taskDeadline = [DateTime]::UtcNow.AddSeconds(20)
    do {
        Start-Sleep -Milliseconds 100
        if ($taskBackend.HasExited) { throw 'Verification API exited.' }
        try { Invoke-WebRequest 'http://localhost:5108/' -UseBasicParsing -TimeoutSec 1 | Out-Null; $taskReady = $true }
        catch { if ($_.Exception.Response -and [int]$_.Exception.Response.StatusCode -eq 404) { $taskReady = $true } }
    } while (!$taskReady -and [DateTime]::UtcNow -lt $taskDeadline)
    if (!$taskReady) { throw 'API startup timed out.' }
    $taskCreator = Start-Player 'create'
    $taskJoiner = Start-Player 'join'
    $taskDeadline = [DateTime]::UtcNow.AddSeconds(45)
    while (!(Test-Path -LiteralPath ($taskShare + '.ready.create')) -or !(Test-Path -LiteralPath ($taskShare + '.ready.join'))) {
        if ([DateTime]::UtcNow -gt $taskDeadline -or $taskCreator.Process.HasExited -or $taskJoiner.Process.HasExited) {
            throw 'Clients failed to become active; inspect their sanitized logs/results.'
        }
        Start-Sleep -Milliseconds 100
    }
    $taskPublic = Get-Content -LiteralPath $taskShare -Raw | ConvertFrom-Json
    & 'C:\Program Files\dotnet\dotnet.exe' run --project "$PSScriptRoot\LifecycleSetup\LifecycleSetup.csproj" -- $taskPublic.matchId $taskPublic.playerId
    if ($LASTEXITCODE -ne 0) { throw 'Verification combat setup failed.' }
    Set-Content -LiteralPath ($taskShare + '.configured') -Value 'configured'
    Complete-Player $taskCreator
    Complete-Player $taskJoiner
    Complete-Player (Start-Player 'resume')
}
finally {
    foreach ($taskProcess in $taskPlayers) { if (!$taskProcess.HasExited) { $taskProcess.Kill(); $taskProcess.WaitForExit() } }
    if ($taskBackend -and !$taskBackend.HasExited) { $taskBackend.Kill(); $taskBackend.WaitForExit() }
}
