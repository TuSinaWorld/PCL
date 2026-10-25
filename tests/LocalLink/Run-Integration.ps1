[CmdletBinding()]
param([string]$OfficialZip, [string]$ComponentDirectory, [string]$OutputRoot, [ValidateSet("x86_64","i686","arm64")][string]$Architecture="x86_64")
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
if ([string]::IsNullOrWhiteSpace($OfficialZip) -eq [string]::IsNullOrWhiteSpace($ComponentDirectory)) { throw "Specify exactly one of -OfficialZip or -ComponentDirectory. This test never downloads components." }
if ([string]::IsNullOrWhiteSpace($OutputRoot)) { $OutputRoot=Join-Path $PSScriptRoot ("bin\integration-"+[guid]::NewGuid().ToString("N")) }
New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null
$testRoot=(Resolve-Path -LiteralPath $OutputRoot).Path
$repoRoot=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
& (Join-Path $PSScriptRoot "Run.ps1")
Add-Type -Path (Join-Path $repoRoot 'Plain Craft Launcher 2\Resources\Newtonsoft.Json.dll')
Add-Type -Path (Join-Path $repoRoot 'tests\LocalLink\bin\checks.dll')
if ($OfficialZip) {
    $OfficialZip=(Resolve-Path -LiteralPath $OfficialZip).Path
    $runtime=[LocalLinkChecks]::InstallOfficial($OfficialZip,(Join-Path $testRoot 'runtime'),$Architecture)
} else {
    $runtime=(Resolve-Path -LiteralPath $ComponentDirectory).Path
    if (-not [LocalLinkChecks]::VerifyOfficial($runtime,$Architecture)) { throw 'Component directory does not have a verified pinned installation manifest. Use -OfficialZip to install the official archive.' }
}
$csc=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$echoExe=Join-Path $testRoot 'EchoFixture.exe'
& $csc /nologo /target:exe ('/out:'+$echoExe) (Join-Path $PSScriptRoot 'EchoFixture.cs')
if($LASTEXITCODE -ne 0){throw 'echo fixture compile failed'}
function Get-FreePort {
    $listener=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0)
    $listener.Start(); $port=$listener.LocalEndpoint.Port; $listener.Stop(); return $port
}
$echoPort=Get-FreePort
$forwardPort=Get-FreePort
$hostListen=Get-FreePort
$clientListen=Get-FreePort
$hostRpc=Get-FreePort
$clientRpc=Get-FreePort
$network='PCLF-'+[guid]::NewGuid().ToString('N')
$secret=[guid]::NewGuid().ToString('N')
$hostProcess=$null; $clientProcess=$null; $echoProcess=$null
$logNames=@('host.out.log','host.err.log','client.out.log','client.err.log')
try {
    $echoProcess=Start-Process -FilePath $echoExe -ArgumentList @($echoPort.ToString()) -WindowStyle Hidden -PassThru
    $common=@('--no-tun','--use-smoltcp','--stun-servers','--stun-servers-v6','--disable-kcp-input','--disable-quic-input','--bind-device','false',"--network-name=$network","--network-secret=$secret",'--private-mode','true')
    $hostArgs=$common+@('-i','10.114.114.114','--hostname=Server-local-test',"--listeners=tcp://127.0.0.1:$hostListen","--rpc-portal=127.0.0.1:$hostRpc","--tcp-whitelist=$echoPort","--udp-whitelist=$echoPort")
    $hostProcess=Start-Process -FilePath (Join-Path $runtime 'easytier-core.exe') -WorkingDirectory $runtime -ArgumentList $hostArgs -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $testRoot 'host.out.log') -RedirectStandardError (Join-Path $testRoot 'host.err.log')
    $hostReady=$false
    for($attempt=0;$attempt -lt 20;$attempt++) {
        if($hostProcess.HasExited){throw 'Host core exited before publishing its invitation'}
        try {
            $hostOnlyPeers=[LocalLinkChecks]::QueryOfficial($runtime,$hostRpc)
            if([LocalLinkChecks]::HostIsReady($hostOnlyPeers)){$hostReady=$true;break}
        } catch { }
        Start-Sleep -Milliseconds 500
    }
    if(-not $hostReady){throw 'A single host cannot publish its invitation before players join'}
    $hostOnlyPeers | Set-Content -LiteralPath (Join-Path $testRoot 'host-alone-peer.json') -Encoding utf8
    $clientArgs=$common+@('-d','--hostname=Client-local-test',"--listeners=tcp://127.0.0.1:$clientListen","--rpc-portal=127.0.0.1:$clientRpc",'--tcp-whitelist=0','--udp-whitelist=0',"-p=tcp://127.0.0.1:$hostListen",'--port-forward',"tcp://127.0.0.1:$forwardPort/10.114.114.114:$echoPort",'--port-forward',"udp://127.0.0.1:$forwardPort/10.114.114.114:$echoPort")
    $clientProcess=Start-Process -FilePath (Join-Path $runtime 'easytier-core.exe') -WorkingDirectory $runtime -ArgumentList $clientArgs -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $testRoot 'client.out.log') -RedirectStandardError (Join-Path $testRoot 'client.err.log')
    $connected=$false
    for($attempt=0;$attempt -lt 30;$attempt++){
        if($hostProcess.HasExited -or $clientProcess.HasExited){throw 'A core exited before connection'}
        try {
            $clientPeers=[LocalLinkChecks]::QueryOfficial($runtime,$clientRpc)
            $clientPeers | Set-Content -LiteralPath (Join-Path $testRoot "last-client-peer.json") -Encoding utf8
            $peerRows=$clientPeers | ConvertFrom-Json
            $selfReady=@($peerRows | Where-Object {$_.cost -eq 'Local' -and $_.ipv4 -match '^10\.114\.114\.\d+$'}).Count -gt 0
            $remoteReady=@($peerRows | Where-Object {$_.ipv4 -eq '10.114.114.114' -and $_.lat_ms -match '^\d+(\.\d+)?$' -and [double]::Parse($_.lat_ms,[Globalization.CultureInfo]::InvariantCulture) -gt 0}).Count -gt 0
            if($selfReady -and $remoteReady){
                $hostPeers=[LocalLinkChecks]::QueryOfficial($runtime,$hostRpc) | ConvertFrom-Json
                if(@($hostPeers | Where-Object {$_.hostname -eq 'Client-local-test' -and $_.ipv4 -match '^10\.114\.114\.\d+$'}).Count -gt 0){$connected=$true;break}
            }
        } catch { $_.Exception.Message | Set-Content -LiteralPath (Join-Path $testRoot 'last-cli-error.txt') }
        Start-Sleep -Milliseconds 500
    }
    if(-not $connected){throw 'Two Core connection timed out'}
    $clientPeers | Set-Content -LiteralPath (Join-Path $testRoot 'client-peer.json') -Encoding utf8
    [LocalLinkChecks]::QueryOfficial($runtime,$hostRpc) | Set-Content -LiteralPath (Join-Path $testRoot 'host-peer.json') -Encoding utf8
    $payload=[Text.Encoding]::UTF8.GetBytes("PCLF real TCP and UDP transport fixture`n")
    $tcp=[Net.Sockets.TcpClient]::new()
    try {
        $tcp.Connect('127.0.0.1',$forwardPort)
        $tcp.ReceiveTimeout=5000; $tcp.SendTimeout=5000
        $stream=$tcp.GetStream(); $stream.Write($payload,0,$payload.Length)
        $buffer=New-Object byte[] 4096
        $received=0
        while ($received -lt $payload.Length) {
            $count=$stream.Read($buffer,$received,$buffer.Length-$received)
            if($count -eq 0){break}
            $received+=$count
        }
        if([Text.Encoding]::UTF8.GetString($buffer,0,$received) -ne [Text.Encoding]::UTF8.GetString($payload)){throw 'TCP payload differs'}
    } finally {$tcp.Dispose()}
    $udp=[Net.Sockets.UdpClient]::new()
    try {
        $udp.Client.ReceiveTimeout=3000
        $udp.Connect('127.0.0.1',$forwardPort)
        $udpOk=$false
        for($attempt=0;$attempt -lt 3;$attempt++){
            [void]$udp.Send($payload,$payload.Length)
            try {
                $from=[Net.IPEndPoint]::new([Net.IPAddress]::Any,0)
                $reply=$udp.Receive([ref]$from)
                if([Text.Encoding]::UTF8.GetString($reply) -eq [Text.Encoding]::UTF8.GetString($payload)){$udpOk=$true;break}
            } catch { }
        }
        if(-not $udpOk){throw 'UDP transfer failed'}
    } finally {$udp.Dispose()}
    [ordered]@{Component='EasyTier v2.6.4'; Architecture=$Architecture; PackageSha256=[LocalLinkChecks]::OfficialPackageHash($Architecture); OfficialCliParsed=$true; HostCanCreateWithoutPlayers=$hostReady; TwoLocalCoreConnection=$true; TcpPayloadEcho=$true; UdpPayloadEcho=$true; NoTun=$true; ServicesChanged=$false; FirewallChanged=$false; CrossInternetMachinesTested=$false; CompletedUtc=[DateTime]::UtcNow.ToString('o')} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $testRoot 'integration-result.json') -Encoding utf8
    Write-Output 'Official EasyTier double-Core CLI and TCP/UDP echo passed.'
} finally {
    foreach($owned in @($clientProcess,$hostProcess,$echoProcess)) {if($null -ne $owned){try{if(-not $owned.HasExited){$owned.Kill();$owned.WaitForExit(2000)|Out-Null};$owned.Dispose()}catch{}}}
    foreach($name in $logNames){$logPath=Join-Path $testRoot $name;if(Test-Path -LiteralPath $logPath){$content=[IO.File]::ReadAllText($logPath).Replace($secret,'[redacted]').Replace($network,'[room]');[IO.File]::WriteAllText($logPath,$content)}}
}
