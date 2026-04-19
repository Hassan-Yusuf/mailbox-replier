Write-Host "== Proxy environment ==" -ForegroundColor Cyan
Get-ChildItem Env:HTTP_PROXY,Env:HTTPS_PROXY,Env:ALL_PROXY,Env:NO_PROXY -ErrorAction SilentlyContinue |
    Format-Table Name, Value -AutoSize

Write-Host "`n== WinHTTP proxy ==" -ForegroundColor Cyan
netsh winhttp show proxy

Write-Host "`n== Internet Settings proxy ==" -ForegroundColor Cyan
Get-ItemProperty "HKCU:\Software\Microsoft\Windows\CurrentVersion\Internet Settings" |
    Select-Object ProxyEnable, ProxyServer, AutoConfigURL |
    Format-Table -AutoSize

Write-Host "`n== Network reachability ==" -ForegroundColor Cyan
Test-NetConnection outlook.office365.com -Port 993 |
    Format-List ComputerName, RemotePort, TcpTestSucceeded

Write-Host "`n== Core crypto services ==" -ForegroundColor Cyan
Get-Service CryptSvc, KeyIso, EventLog |
    Select-Object Name, Status, StartType |
    Format-Table -AutoSize

function Test-TlsHandshake {
    param(
        [Parameter(Mandatory = $true)]
        [string] $HostName,

        [Parameter(Mandatory = $true)]
        [int] $Port
    )

    try {
        $client = New-Object System.Net.Sockets.TcpClient($HostName, $Port)
        $ssl = New-Object System.Net.Security.SslStream($client.GetStream(), $false, ({ $true }))
        $ssl.AuthenticateAsClient($HostName)

        [pscustomobject]@{
            Host     = $HostName
            Port     = $Port
            Success  = $true
            Protocol = [string]$ssl.SslProtocol
            Error    = ""
        }

        $ssl.Dispose()
        $client.Dispose()
    }
    catch {
        $errorMessage = if ($_.Exception.InnerException -and $_.Exception.InnerException.Message) {
            $_.Exception.InnerException.Message
        }
        else {
            $_.Exception.Message
        }

        [pscustomobject]@{
            Host     = $HostName
            Port     = $Port
            Success  = $false
            Protocol = ""
            Error    = $errorMessage
        }
    }
}

Write-Host "`n== Direct .NET TLS handshake probe ==" -ForegroundColor Cyan
@(
    Test-TlsHandshake -HostName "outlook.office365.com" -Port 993
    Test-TlsHandshake -HostName "www.microsoft.com" -Port 443
    Test-TlsHandshake -HostName "api.openai.com" -Port 443
) | Format-Table -AutoSize
