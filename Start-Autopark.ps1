[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Test-ApiPort([int]$Port) {
    $connection = [System.Net.Sockets.TcpClient]::new()
    try {
        return $connection.ConnectAsync('127.0.0.1', $Port).Wait(250)
    }
    catch [System.AggregateException] {
        return $false
    }
    finally {
        $connection.Dispose()
    }
}

$solution = Join-Path $PSScriptRoot 'BD_RGR.slnx'
$apiDirectory = Join-Path $PSScriptRoot 'BD_RGR.Api'
$clientDirectory = Join-Path $PSScriptRoot 'BD_RGR'
$settings = Get-Content -Raw -LiteralPath (Join-Path $clientDirectory 'clientsettings.json') | ConvertFrom-Json
$apiAddress = [uri]$settings.ApiBaseUrl
if (!$apiAddress.IsAbsoluteUri -or $apiAddress.Scheme -ne 'http' -or $apiAddress.Host -notin @('127.0.0.1', 'localhost')) {
    throw 'В clientsettings.json укажите локальный HTTP-адрес API, например http://127.0.0.1:5080.'
}

$apiPort = $apiAddress.Port
if (Test-ApiPort $apiPort) {
    throw "Порт ${apiPort} уже занят. Если API запущен вручную, остановите его и повторите запуск скрипта."
}

dotnet build $solution --configuration Debug
if ($LASTEXITCODE -ne 0) {
    throw "Сборка завершилась с кодом $LASTEXITCODE."
}

$apiExe = Join-Path $apiDirectory 'bin\Debug\net8.0\BD_RGR.Api.exe'
$clientExe = Join-Path $clientDirectory 'bin\Debug\net8.0-windows\BD_RGR.exe'
$api = Start-Process -FilePath $apiExe -ArgumentList @('--ApiPort', "$apiPort") `
    -WorkingDirectory $apiDirectory -NoNewWindow -PassThru

try {
    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    while (!(Test-ApiPort $apiPort)) {
        if ($api.HasExited) {
            throw "API завершился с кодом $($api.ExitCode). Проверьте сообщения выше."
        }
        if ([DateTime]::UtcNow -ge $deadline) {
            throw "API не начал слушать порт $apiPort за 20 секунд. Проверьте сообщения выше."
        }
        Start-Sleep -Milliseconds 200
    }

    $client = Start-Process -FilePath $clientExe -WorkingDirectory $clientDirectory -PassThru
    while (!$client.HasExited) {
        Start-Sleep -Milliseconds 250
    }
}
finally {
    if (!$api.HasExited) {
        Stop-Process -InputObject $api
    }
}
