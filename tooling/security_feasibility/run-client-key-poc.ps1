$ErrorActionPreference = 'Stop'

$hostDll = Join-Path $PSScriptRoot 'host_poc\bin\Release\net9.0-windows\host_poc.dll'
if (-not (Test-Path -LiteralPath $hostDll -PathType Leaf)) {
    throw 'Build host_poc.csproj in Release before running the Client key POC.'
}

$keyName = 'DovahLink.S2.Feasibility.' + [guid]::NewGuid().ToString('N')
& dotnet $hostDll "--key-name=$keyName" --client-key-proof
if ($LASTEXITCODE -ne 0) {
    throw "Client key POC exited with code $LASTEXITCODE."
}
