$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$hostDll = Join-Path $PSScriptRoot 'host_poc\bin\Release\net9.0-windows\host_poc.dll'
$keyName = 'DovahLink.S2.Feasibility.' + [guid]::NewGuid().ToString('N')
$clientDirectory = Join-Path $PSScriptRoot 'client_poc'
$stdoutPath = Join-Path ([System.IO.Path]::GetTempPath()) ('dovahlink-s2-' + [guid]::NewGuid().ToString('N') + '.out')
$stderrPath = Join-Path ([System.IO.Path]::GetTempPath()) ('dovahlink-s2-' + [guid]::NewGuid().ToString('N') + '.err')
$hostProcess = $null
$runFailed = $false

try {
    $quotedHostDll = '"' + $hostDll + '"'
    $hostProcess = Start-Process -FilePath 'dotnet' -ArgumentList @($quotedHostDll, "--key-name=$keyName") -WorkingDirectory $repoRoot `
        -PassThru -WindowStyle Hidden -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath

    $readyLine = $null
    for ($attempt = 0; $attempt -lt 150; $attempt++) {
        Start-Sleep -Milliseconds 200
        if (Test-Path -LiteralPath $stdoutPath) {
            $readyLine = Get-Content -LiteralPath $stdoutPath | Where-Object { $_ -like 'READY *' } | Select-Object -First 1
            if ($readyLine) { break }
        }
        if ($hostProcess.HasExited) { break }
    }

    if (-not $readyLine) {
        if (Test-Path -LiteralPath $stderrPath) { Get-Content -LiteralPath $stderrPath }
        throw 'The WSS Host did not start within 30 seconds.'
    }

    $pinLine = Get-Content -LiteralPath $stdoutPath | Where-Object { $_ -like 'SPKI_SHA256_BASE64URL=*' } | Select-Object -First 1
    if (-not $pinLine) { throw 'The WSS Host did not report its SPKI pin.' }

    Write-Output $readyLine
    Get-Content -LiteralPath $stdoutPath | Where-Object { $_ -like 'CNG_*' -or $_ -like 'CERTIFICATE_*' }
    $url = $readyLine.Substring('READY '.Length)
    $pin = $pinLine.Substring('SPKI_SHA256_BASE64URL='.Length)
    Push-Location $clientDirectory
    try {
        & dart run lib/client.dart "--url=$url" "--pin=$pin"
        if ($LASTEXITCODE -ne 0) { throw "Dart POC exited with code $LASTEXITCODE." }
    }
    finally {
        Pop-Location
    }
}
catch {
    $runFailed = $true
    throw
}
finally {
    if ($hostProcess -and -not $hostProcess.HasExited) {
        Stop-Process -Id $hostProcess.Id -Force
        $hostProcess.WaitForExit()
    }
    Remove-Item -LiteralPath $stdoutPath, $stderrPath -Force -ErrorAction SilentlyContinue
    & dotnet $hostDll "--key-name=$keyName" --delete-key
    if ($LASTEXITCODE -ne 0) {
        $cleanupError = "POC key cleanup for $keyName exited with code $LASTEXITCODE."
        if ($runFailed) {
            Write-Warning $cleanupError
        }
        else {
            throw $cleanupError
        }
    }
}
