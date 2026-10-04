param(
    [Parameter(Mandatory = $true)][string]$PlayerPath,
    [string]$OutputDirectory = "$PSScriptRoot\..\ConquestusTerrarumClient\Builds\ReconnectResults",
    [string]$ConnectionString = 'Host=localhost;Port=5432;Database=conquestus_terrarum;Username=conquestus;Password=conquestus_local_only'
)
$ErrorActionPreference = 'Stop'
# Requires an isolated development player containing RecoverySmoke.cs and a UNIQUE company/product
# identity. Never use the normal delivered player or a live player's PlayerPrefs for this test.
$taskRepo = (Resolve-Path "$PSScriptRoot\..\..").Path
$taskPlayer = (Resolve-Path -LiteralPath $PlayerPath).Path
$taskOutput = [IO.Path]::GetFullPath($OutputDirectory)
if ((Test-Path -LiteralPath $taskOutput) -and @(Get-ChildItem -LiteralPath $taskOutput -Force).Count -gt 0) {
    throw 'Use a new empty output directory so stale signals/results cannot satisfy the smoke checks.'
}
New-Item -ItemType Directory -Force $taskOutput | Out-Null
$taskCreator = Join-Path $taskOutput 'creator-public.json'
$taskJoiner = Join-Path $taskOutput 'joiner-public.json'
$taskBackend = $null
$taskPlayers = @()

function Start-Backend {
    $env:ConnectionStrings__Postgres = $ConnectionString
    $env:ASPNETCORE_URLS = 'http://localhost:5108'
    $env:Logging__EventLog__LogLevel__Default = 'None'
    $taskApiDll = Join-Path $taskRepo 'src\ConquestusTerrarum.Api\bin\Debug\net10.0\ConquestusTerrarum.Api.dll'
    $script:taskBackend = Start-Process 'C:\Program Files\dotnet\dotnet.exe' -ArgumentList ('"' + $taskApiDll + '"') -WorkingDirectory $taskRepo -WindowStyle Hidden -PassThru
    $taskDeadline = [DateTime]::UtcNow.AddSeconds(20)
    do {
        Start-Sleep -Milliseconds 150
        if ($script:taskBackend.HasExited) { throw 'Verification API exited before startup.' }
        # The API has no health endpoint. Its root returns a sanitized 404 once routing is ready.
        try { Invoke-WebRequest 'http://localhost:5108/' -UseBasicParsing -TimeoutSec 1 | Out-Null; return }
        catch { if ($_.Exception.Response -and [int]$_.Exception.Response.StatusCode -eq 404) { return } }
    } while ([DateTime]::UtcNow -lt $taskDeadline)
    throw 'Verification API startup timed out.'
}
function Stop-Backend {
    if ($script:taskBackend -and !$script:taskBackend.HasExited) { $script:taskBackend.Kill(); $script:taskBackend.WaitForExit() }
    $script:taskBackend = $null
}
function Start-Case([string]$Mode, [string]$Profile = 'create') {
    $taskMeta = if ($Profile -eq 'join') { $taskJoiner } else { $taskCreator }
    $taskResult = Join-Path $taskOutput "$Profile-$Mode.txt"
    $taskArgs = @('-batchmode','-nographics','-logFile',('"'+(Join-Path $taskOutput "$Profile-$Mode.log")+'"'),
        '--ct-mode',$Mode,'--ct-profile',$Profile,'--ct-meta',('"'+$taskMeta+'"'),
        '--ct-share',('"'+$taskCreator+'"'),'--ct-result',('"'+$taskResult+'"'))
    $taskProcess = Start-Process $taskPlayer -ArgumentList $taskArgs -WindowStyle Hidden -PassThru
    $script:taskPlayers += $taskProcess
    return @{ Process = $taskProcess; Result = $taskResult }
}
function Complete-Case($Case) {
    if (!$Case.Process.WaitForExit(40000)) { throw 'Verification player timed out.' }
    if (!(Test-Path -LiteralPath $Case.Result)) { throw 'Verification player produced no result.' }
    $taskResultText = Get-Content -Raw -LiteralPath $Case.Result
    if ($Case.Process.ExitCode -ne 0 -or !$taskResultText.StartsWith('PASS:')) { throw $taskResultText }
    Write-Output $taskResultText
}

try {
    # This script owns the API it stops; refuse to touch an existing listener.
    $taskProbe = [Net.Sockets.TcpClient]::new()
    try { $taskProbe.Connect('localhost', 5108); throw 'Stop the existing API before this isolated outage test.' }
    catch [Net.Sockets.SocketException] { }
    finally { $taskProbe.Dispose() }
    Start-Backend
    $taskIssued = Start-Case 'issued-offline'
    $taskDeadline = [DateTime]::UtcNow.AddSeconds(25)
    while (!(Test-Path -LiteralPath ($taskCreator + '.issued'))) {
        if ([DateTime]::UtcNow -gt $taskDeadline) { throw 'Credential issuance timed out.' }
        Start-Sleep -Milliseconds 100
    }
    Stop-Backend
    Set-Content -LiteralPath ($taskCreator + '.offline') -Value 'offline'
    Complete-Case $taskIssued
    Complete-Case (Start-Case 'offline')
    $taskRetry = Start-Case 'offline-retry'
    $taskDeadline = [DateTime]::UtcNow.AddSeconds(25)
    while (!(Test-Path -LiteralPath ($taskCreator + '.retry-ready'))) {
        if ([DateTime]::UtcNow -gt $taskDeadline) { throw 'Offline retry timed out.' }
        Start-Sleep -Milliseconds 100
    }
    Start-Backend
    Set-Content -LiteralPath ($taskCreator + '.online') -Value 'online'
    Complete-Case $taskRetry
    Complete-Case (Start-Case 'resume')
    Complete-Case (Start-Case 'races')
    Complete-Case (Start-Case 'invalid')
    Complete-Case (Start-Case 'empty')
    Complete-Case (Start-Case 'create')
    Complete-Case (Start-Case 'resume-moving')
    Complete-Case (Start-Case 'join' 'join')
    Complete-Case (Start-Case 'resume-moving' 'join')
    $taskBattleA = Start-Case 'resume-battle'
    $taskBattleB = Start-Case 'resume-battle' 'join'
    Complete-Case $taskBattleA
    Complete-Case $taskBattleB
    # Let the lazy simulation's battle finish while both clients are closed.
    Start-Sleep -Seconds 45
    Complete-Case (Start-Case 'resume-ended')
    Complete-Case (Start-Case 'resume-ended' 'join')
    Complete-Case (Start-Case 'missing' 'join')
    Complete-Case (Start-Case 'empty' 'join')
    Complete-Case (Start-Case 'forget')
    Complete-Case (Start-Case 'empty')
}
finally {
    foreach ($taskProcess in $taskPlayers) { if (!$taskProcess.HasExited) { $taskProcess.Kill(); $taskProcess.WaitForExit() } }
    Stop-Backend
}
