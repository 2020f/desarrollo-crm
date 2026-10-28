[CmdletBinding(SupportsShouldProcess)]
param(
    [switch]$RemoveDatabase,
    [ValidatePattern('^[a-zA-Z0-9-]{1,40}$')][string]$SessionName = 'demo'
)

$ErrorActionPreference = 'Stop'
$repoPath = Split-Path -Parent $PSScriptRoot
$sessionPath = Join-Path $repoPath ".validation\$SessionName-session.json"
if (-not (Test-Path -LiteralPath $sessionPath)) { throw 'No existe una sesión demo registrada.' }
$receipt = Get-Content -LiteralPath $sessionPath -Raw | ConvertFrom-Json
if ($receipt.Kind -ne 'TaskDiagramDemo' -or $receipt.DatabaseName -notmatch '^Desarrollo_DiagramDemo_[0-9a-f]{32}$') {
    throw 'El recibo no identifica una base demo válida. No se detuvo ni eliminó nada.'
}
$process = Get-Process -Id $receipt.ProcessId -ErrorAction SilentlyContinue
if ($process) {
    $expectedDll = Join-Path $repoPath 'src\Desarrollo.Web\bin\Debug\net10.0\Desarrollo.Web.dll'
    $command = (Get-CimInstance Win32_Process -Filter "ProcessId = $($process.Id)").CommandLine
    $sameStart = $process.StartTime.ToUniversalTime().ToString('O') -eq ([DateTime]$receipt.ProcessStartedAtUtc).ToUniversalTime().ToString('O')
    if (-not $sameStart -or -not $command -or $command.IndexOf($expectedDll, [StringComparison]::OrdinalIgnoreCase) -lt 0) {
        throw 'El PID ya no corresponde al host demo registrado. No se detuvo ningún proceso.'
    }
    if ($PSCmdlet.ShouldProcess("PID $($process.Id), $($receipt.BaseUrl)", 'Detener host demo registrado')) {
        Stop-Process -Id $process.Id
        $process.WaitForExit(10000) | Out-Null
    } else { return }
}
$receipt.Status = 'Stopped'
if (-not $WhatIfPreference) { $receipt | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $sessionPath -Encoding utf8 }
if (-not $RemoveDatabase) {
    Write-Output 'Host demo detenido. La base y el acceso local se conservan.'
    return
}

$configured = [Environment]::GetEnvironmentVariable('DESARROLLO_DEMO_CONNECTION_STRING')
if ([string]::IsNullOrWhiteSpace($configured)) { $configured = 'Server=localhost;Trusted_Connection=True;TrustServerCertificate=True' }
$builder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder($configured)
if ($builder.get_DataSource() -ne $receipt.Server -or $builder.get_AttachDBFilename()) { throw 'La instancia no coincide con el recibo; eliminación rechazada.' }
$builder.set_InitialCatalog($receipt.DatabaseName)
$connection = New-Object System.Data.SqlClient.SqlConnection($builder.get_ConnectionString())
try {
    $connection.Open()
    $command = $connection.CreateCommand()
    $command.CommandText = 'SELECT DB_NAME()'
    if ($command.ExecuteScalar() -cne $receipt.DatabaseName) { throw 'La base real no coincide con el recibo; eliminación rechazada.' }
} finally { $connection.Dispose() }
if ($PSCmdlet.ShouldProcess("$($receipt.Server)/$($receipt.DatabaseName)", 'Eliminar exclusivamente la base demo verificada y su recibo local')) {
    $builder.set_InitialCatalog('master')
    $connection = New-Object System.Data.SqlClient.SqlConnection($builder.get_ConnectionString())
    try {
        $connection.Open()
        $command = $connection.CreateCommand()
        $command.CommandText = "ALTER DATABASE [$($receipt.DatabaseName)] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$($receipt.DatabaseName)];"
        $command.ExecuteNonQuery() | Out-Null
    } finally { $connection.Dispose() }
    Remove-Item -LiteralPath $sessionPath
    Write-Output 'Host detenido y base demo verificada eliminada. La base normal se conserva.'
}
