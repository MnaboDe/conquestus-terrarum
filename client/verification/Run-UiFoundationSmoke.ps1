param(
    [Parameter(Mandatory = $true)][string]$PlayerPath,
    [string]$OutputDirectory = "$PSScriptRoot\..\ConquestusTerrarumClient\Builds\UiFoundationResults",
    [string]$ConnectionString = 'Host=localhost;Port=5432;Database=conquestus_terrarum;Username=conquestus;Password=conquestus_local_only'
)
$ErrorActionPreference = 'Stop'
# Isolated graphical development player only, with UiFoundationSmoke.cs and unique product identity.
$taskRepo = (Resolve-Path "$PSScriptRoot\..\..").Path
$taskPlayer = (Resolve-Path -LiteralPath $PlayerPath).Path
$taskOutput = [IO.Path]::GetFullPath($OutputDirectory)
if ((Test-Path -LiteralPath $taskOutput) -and @(Get-ChildItem -LiteralPath $taskOutput -Force).Count -gt 0) { throw 'Use a fresh empty output directory.' }
New-Item -ItemType Directory -Force $taskOutput | Out-Null
$taskShare = Join-Path $taskOutput 'world-public.json'
$taskBackend = $null
$taskPlayers = @()
function Start-Backend {
    $env:ConnectionStrings__Postgres = $ConnectionString
    $env:ASPNETCORE_URLS = 'http://localhost:5108'
    $env:Logging__EventLog__LogLevel__Default = 'None'
    $taskDll = Join-Path $taskRepo 'src\ConquestusTerrarum.Api\bin\Debug\net10.0\ConquestusTerrarum.Api.dll'
    $script:taskBackend = Start-Process 'C:\Program Files\dotnet\dotnet.exe' -ArgumentList ('"'+$taskDll+'"') -WorkingDirectory $taskRepo -WindowStyle Hidden -PassThru
    $taskDeadline = [DateTime]::UtcNow.AddSeconds(20)
    do {
        Start-Sleep -Milliseconds 100
        if ($script:taskBackend.HasExited) { throw 'UI verification API exited.' }
        try { Invoke-WebRequest 'http://localhost:5108/' -UseBasicParsing -TimeoutSec 1 | Out-Null; return }
        catch { if ($_.Exception.Response -and [int]$_.Exception.Response.StatusCode -eq 404) { return } }
    } while ([DateTime]::UtcNow -lt $taskDeadline)
    throw 'UI verification API startup timed out.'
}
function Stop-Backend {
    if ($script:taskBackend -and !$script:taskBackend.HasExited) { $script:taskBackend.Kill(); $script:taskBackend.WaitForExit() }
    $script:taskBackend = $null
}
function Start-Player([string]$Mode) {
    $taskResult = Join-Path $taskOutput "$Mode-result.txt"
    $taskArgs = @('-force-d3d11','-screen-fullscreen','0','-screen-width','1280','-screen-height','720',
        '-logFile',('"'+(Join-Path $taskOutput "$Mode.log")+'"'),'--ct-ui-mode',$Mode,
        '--ct-share',('"'+$taskShare+'"'),'--ct-result',('"'+$taskResult+'"'),'--ct-output',('"'+$taskOutput+'"'))
    $taskProcess = Start-Process $taskPlayer -ArgumentList $taskArgs -WindowStyle Hidden -PassThru
    $script:taskPlayers += $taskProcess
    return @{ Process = $taskProcess; Result = $taskResult }
}
function Complete-Player($Case) {
    if (!$Case.Process.WaitForExit(150000)) { throw 'UI player timed out.' }
    if (!(Test-Path -LiteralPath $Case.Result)) { throw 'UI player produced no result.' }
    $taskText = Get-Content -LiteralPath $Case.Result -Raw
    if ($Case.Process.ExitCode -ne 0 -or !$taskText.StartsWith('PASS:')) { throw $taskText }
    Write-Output $taskText
}
try {
    $taskProbe = [Net.Sockets.TcpClient]::new()
    try { $taskProbe.Connect('localhost', 5108); throw 'Stop the existing API before isolated UI/outage verification.' }
    catch [Net.Sockets.SocketException] { }
    finally { $taskProbe.Dispose() }
    Start-Backend
    $taskCreator = Start-Player 'create'
    $taskJoiner = Start-Player 'join'
    Complete-Player $taskCreator
    Complete-Player $taskJoiner
    Stop-Backend
    $taskOffline = Start-Player 'offline'
    $taskDeadline = [DateTime]::UtcNow.AddSeconds(60)
    while (!(Test-Path -LiteralPath ($taskShare + '.retry-ready'))) {
        if ([DateTime]::UtcNow -gt $taskDeadline -or $taskOffline.Process.HasExited) { throw 'UI offline recovery failed; inspect sanitized result/log.' }
        Start-Sleep -Milliseconds 100
    }
    Start-Backend
    Set-Content -LiteralPath ($taskShare + '.online') -Value 'online'
    Complete-Player $taskOffline
    Complete-Player (Start-Player 'resume')
    Complete-Player (Start-Player 'empty')
}
finally {
    foreach ($taskProcess in $taskPlayers) { if (!$taskProcess.HasExited) { $taskProcess.Kill(); $taskProcess.WaitForExit() } }
    Stop-Backend
}
