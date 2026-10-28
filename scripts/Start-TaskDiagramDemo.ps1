[CmdletBinding()]
param(
    [ValidateRange(1024, 65535)][int]$Port = 5017,
    [SecureString]$LoginPassword,
    [switch]$Resume,
    [ValidatePattern('^[a-zA-Z0-9-]{1,40}$')][string]$SessionName = 'demo'
)

$ErrorActionPreference = 'Stop'
$repoPath = Split-Path -Parent $PSScriptRoot
$webPath = Join-Path $repoPath 'src\Desarrollo.Web'
$dllPath = Join-Path $webPath 'bin\Debug\net10.0\Desarrollo.Web.dll'
$validationPath = Join-Path $repoPath '.validation'
$sessionPath = Join-Path $validationPath "$SessionName-session.json"
$resumeReceipt = $null
if (-not (Test-Path -LiteralPath $dllPath)) {
    throw 'Primero compila la solución y el frontend. Esta demo no compila ni modifica fuentes.'
}
if (Test-Path -LiteralPath $sessionPath) {
    $existing = Get-Content -LiteralPath $sessionPath -Raw | ConvertFrom-Json
    $existingProcess = Get-Process -Id $existing.ProcessId -ErrorAction SilentlyContinue
    if ($existing.Kind -eq 'TaskDiagramDemo' -and $existing.Status -eq 'Ready' -and $existingProcess `
        -and $existingProcess.StartTime.ToUniversalTime().ToString('O') -eq ([DateTime]$existing.ProcessStartedAtUtc).ToUniversalTime().ToString('O')) {
        $existingCommand = (Get-CimInstance Win32_Process -Filter "ProcessId = $($existingProcess.Id)").CommandLine
        if ($existingCommand -and $existingCommand.IndexOf($dllPath, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
            [pscustomobject]@{ DemoUrl = $existing.DemoUrl; LoginUrl = $existing.LoginUrl; LoginEmail = $existing.Email
                SessionFile = $sessionPath; DatabaseName = $existing.DatabaseName; ProcessId = $existing.ProcessId }
            return
        }
    }
    if ($Resume -and $existing.Kind -eq 'TaskDiagramDemo' -and $existing.DatabaseName -match '^Desarrollo_DiagramDemo_[0-9a-f]{32}$' -and -not $existingProcess) {
        $resumeReceipt = $existing
    } else { throw 'Ya existe una sesión registrada. Para reiniciarla, detén su host y usa -Resume; nunca se repite el seed de una sesión activa.' }
}
New-Item -ItemType Directory -Path $validationPath -Force | Out-Null

# Explicit development/demo entry point. This script never selects the normal Desarrollo catalog.
$demoId = if ($resumeReceipt) { $resumeReceipt.DatabaseName.Substring('Desarrollo_DiagramDemo_'.Length) } else { [Guid]::NewGuid().ToString('N') }
$databaseName = "Desarrollo_DiagramDemo_$demoId"
$configuredConnection = [Environment]::GetEnvironmentVariable('DESARROLLO_DEMO_CONNECTION_STRING')
if ([string]::IsNullOrWhiteSpace($configuredConnection)) {
    $configuredConnection = 'Server=localhost;Trusted_Connection=True;TrustServerCertificate=True'
}
$connectionBuilder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder($configuredConnection)
$connectionBuilder.set_InitialCatalog($databaseName)
if ($connectionBuilder.get_AttachDBFilename()) { throw 'La demo no admite adjuntar archivos de bases de datos.' }
if ($resumeReceipt -and $connectionBuilder.get_DataSource() -ne $resumeReceipt.Server) { throw 'La instancia no coincide con la sesión que se quiere reiniciar.' }
if ($databaseName -notmatch '^Desarrollo_DiagramDemo_[0-9a-f]{32}$') { throw 'Nombre de base demo inválido.' }

# Choose a free loopback port; never stop a process that already owns the requested port.
$selectedPort = $null
for ($candidate = $Port; $candidate -le [Math]::Min($Port + 30, 65535); $candidate++) {
    $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, $candidate)
    try { $listener.Start(); $selectedPort = $candidate; break } catch { } finally { $listener.Stop() }
}
if ($null -eq $selectedPort) { throw 'No se encontró un puerto local libre para la demo.' }
$baseUrl = "http://localhost:$selectedPort"
$stdoutPath = Join-Path $validationPath "$SessionName-host.log"
$stderrPath = Join-Path $validationPath "$SessionName-host.err.log"
$email = if ($resumeReceipt) { $resumeReceipt.Email } else { "mapa-$demoId@example.invalid" }
$password = if ($resumeReceipt) { $resumeReceipt.Password }
    elseif ($LoginPassword) { [System.Net.NetworkCredential]::new('', $LoginPassword).Password }
    else { [Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(24)) + 'Aa1!' }

$oldConnection = [Environment]::GetEnvironmentVariable('ConnectionStrings__Default', 'Process')
$oldEnvironment = [Environment]::GetEnvironmentVariable('ASPNETCORE_ENVIRONMENT', 'Process')
$oldMigrationSetting = [Environment]::GetEnvironmentVariable('Database__ApplyMigrationsOnStartup', 'Process')
try {
    [Environment]::SetEnvironmentVariable('ConnectionStrings__Default', $connectionBuilder.get_ConnectionString(), 'Process')
    [Environment]::SetEnvironmentVariable('ASPNETCORE_ENVIRONMENT', 'Development', 'Process')
    # Resume only opens the existing demo; schema updates remain an explicit operation.
    [Environment]::SetEnvironmentVariable('Database__ApplyMigrationsOnStartup', $(if ($resumeReceipt) { 'false' } else { 'true' }), 'Process')
    $hostProcess = Start-Process -FilePath (Get-Command dotnet).Source `
        -ArgumentList @("`"$dllPath`"", '--urls', $baseUrl) -WorkingDirectory $webPath `
        -WindowStyle Hidden -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath -PassThru
} finally {
    [Environment]::SetEnvironmentVariable('ConnectionStrings__Default', $oldConnection, 'Process')
    [Environment]::SetEnvironmentVariable('ASPNETCORE_ENVIRONMENT', $oldEnvironment, 'Process')
    [Environment]::SetEnvironmentVariable('Database__ApplyMigrationsOnStartup', $oldMigrationSetting, 'Process')
}

$receipt = [ordered]@{
    Kind = 'TaskDiagramDemo'; Status = 'Starting'; DatabaseName = $databaseName
    Server = $connectionBuilder.get_DataSource(); ProcessId = $hostProcess.Id
    ProcessStartedAtUtc = $hostProcess.StartTime.ToUniversalTime().ToString('O')
    BaseUrl = $baseUrl; LoginUrl = "$baseUrl/login"; DemoUrl = $null
    Email = $email; Password = $password
    ProjectId = if ($resumeReceipt) { $resumeReceipt.ProjectId } else { $null }
    TaskIds = if ($resumeReceipt) { @($resumeReceipt.TaskIds) } else { @() }
    FailureEndpoint = $null; FailureMessage = $null
}
function Save-DemoReceipt { $receipt | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $sessionPath -Encoding utf8 }
Save-DemoReceipt
$httpSession = New-Object Microsoft.PowerShell.Commands.WebRequestSession
function Send-DemoRequest([string]$Method, [string]$Path, $Body) {
    $script:demoEndpoint = $Path
    $csrf = Invoke-RestMethod -Uri "$baseUrl/api/account/antiforgery" -WebSession $httpSession
    Invoke-RestMethod -Method $Method -Uri "$baseUrl$Path" -WebSession $httpSession `
        -Headers @{ 'X-XSRF-TOKEN' = $csrf.token } -ContentType 'application/json' `
        -Body ($Body | ConvertTo-Json -Depth 12 -Compress)
}

try {
    $ready = $false
    for ($attempt = 0; $attempt -lt 100; $attempt++) {
        $hostProcess.Refresh()
        if ($hostProcess.HasExited) { throw 'El host de demostración terminó durante su inicio.' }
        try {
            Invoke-RestMethod -Uri "$baseUrl/api/account/antiforgery" -WebSession $httpSession -TimeoutSec 2 | Out-Null
            $ready = $true
            break
        } catch { Start-Sleep -Milliseconds 300 }
    }
    if (-not $ready) { throw 'El host de demostración no respondió a tiempo.' }

    if ($resumeReceipt) {
        Send-DemoRequest POST '/api/account/login' @{ username = $email; password = $password; rememberMe = $false } | Out-Null
        if (-not $receipt.ProjectId) { throw 'No hay proyecto guardado para reanudar esta sesión incompleta.' }
        Invoke-RestMethod -Uri "$baseUrl/api/projects/$($receipt.ProjectId)" -WebSession $httpSession | Out-Null
        $project = [pscustomobject]@{ id = $receipt.ProjectId }
    } else {
        Send-DemoRequest POST '/api/account/register' @{
            fullName = 'Equipo de demostración'; email = $email; password = $password
            organizationName = 'Demostración del mapa de tareas'
        } | Out-Null
        $project = Send-DemoRequest POST '/api/projects' @{
            name = 'Tienda demo'; code = 'CMP'; objective = 'Demostración explícita del mapa de tareas'
            priority = 2; productId = $null; leaderId = $null; dueDate = $null
        }
    }
    $receipt.ProjectId = $project.id
    if (-not $resumeReceipt) {
        $states = Invoke-RestMethod -Uri "$baseUrl/api/projects/$($project.id)/states" -WebSession $httpSession
        $pending = ($states | Where-Object name -eq 'Backlog' | Select-Object -First 1).id
        $inProgress = ($states | Where-Object name -eq 'En desarrollo' | Select-Object -First 1).id
        $inReview = ($states | Where-Object name -eq 'En revisión' | Select-Object -First 1).id
        if (-not $pending -or -not $inProgress -or -not $inReview) { throw 'Faltan estados iniciales del proyecto demo.' }
        $existingTasks = (Invoke-RestMethod -Uri "$baseUrl/api/workitems?projectId=$($project.id)&pageSize=1000" -WebSession $httpSession).items

        function New-DemoTask([string]$Title, [string]$Description, [string]$Notes, [int]$Priority, [string]$StateId, [string]$Accent, $ParentId = $null, $Progress = $null) {
            $matches = @($existingTasks | Where-Object title -eq $Title)
            if ($matches.Count -gt 1) { throw 'La sesión tiene tareas duplicadas y necesita revisión explícita.' }
            if ($matches.Count -eq 1) {
                $found = Invoke-RestMethod -Uri "$baseUrl/api/workitems/$($matches[0].id)" -WebSession $httpSession
                if ($found.parentTaskId -ne $ParentId) { throw 'La jerarquía demo existente no coincide; no se modifica automáticamente.' }
                return $found
            }
            $path = if ($ParentId) { "/api/workitems/$ParentId/children" } else { '/api/workitems' }
            Send-DemoRequest POST $path @{
                title = $Title; type = 0; projectId = $project.id; assigneeId = $null; priority = $Priority
                description = $Description; notes = $Notes; dueDate = $null; stateId = $StateId
                accentColor = $Accent; parentTaskId = $ParentId; progressPercent = $Progress
                clientRequestId = [Guid]::NewGuid().ToString()
            }
        }
        # Names are stored independently; the map composes each visible title from its immediate parent.
        $root = New-DemoTask 'Proceso de compra' 'Construir el flujo completo de compra.' 'Coordinar carrito y pago.' 2 $inProgress '#A76BF3' $null 40
        $cart = New-DemoTask 'Carrito' 'Gestionar productos, cantidades y total.' 'Conservar el carrito al cerrar la app.' 2 $inProgress '#4B9EFF' $root.id
        $payment = New-DemoTask 'Pago' 'Seleccionar el método y confirmar la compra.' 'Validar el total antes de cobrar.' 2 $pending '#A76BF3' $root.id
        $products = New-DemoTask 'Productos' 'Agregar, quitar y cambiar cantidades.' 'Comprobar disponibilidad.' 1 $inReview '#3AC5CA' $cart.id
        $totals = New-DemoTask 'Totales' 'Calcular subtotal, envío y descuentos.' 'Actualizar al modificar productos.' 3 $inProgress '#4B9EFF' $cart.id
        $tasks = @($root, $cart, $payment, $products, $totals)
        $receipt.TaskIds = @($tasks | ForEach-Object id)
        $mapPath = "/api/projects/$($project.id)/task-diagram"
        $map = Invoke-RestMethod -Uri "$baseUrl$mapPath" -WebSession $httpSession
        $layout = @(
            @{ Task = $root; X = 0; Y = 155 }, @{ Task = $cart; X = 430; Y = 0 }
            @{ Task = $payment; X = 430; Y = 340 }, @{ Task = $products; X = 860; Y = 0 }
            @{ Task = $totals; X = 860; Y = 340 }
        )
        $positions = @($layout | ForEach-Object {
            $entry = $_
            $node = $map.tasks | Where-Object id -eq $entry.Task.id | Select-Object -First 1
            @{ taskId = $entry.Task.id; x = $entry.X; y = $entry.Y; rowVersion = $node.positionRowVersion }
        })
        Send-DemoRequest PATCH "$mapPath/positions" @{ positions = $positions } | Out-Null
        # No personal camera is seeded: the first browser visit fits the current viewport.
    } else {
        # Titles, hierarchy, layout, camera and collapsed branches may have been edited.
        # Resume verifies access without reapplying demo defaults or recreating tasks.
        Invoke-RestMethod -Uri "$baseUrl/api/projects/$($project.id)/task-diagram?includeArchived=true" -WebSession $httpSession | Out-Null
    }
    $receipt.DemoUrl = "$baseUrl/projects/$($project.id)/diagram"
    $receipt.Status = 'Ready'
    Save-DemoReceipt
    [pscustomobject]@{ DemoUrl = $receipt.DemoUrl; LoginUrl = $receipt.LoginUrl; LoginEmail = $email
        SessionFile = $sessionPath; DatabaseName = $databaseName; ProcessId = $hostProcess.Id }
} catch {
    $receipt.Status = 'Failed'
    $receipt.FailureEndpoint = $script:demoEndpoint
    $receipt.FailureMessage = $_.Exception.Message
    Save-DemoReceipt
    throw 'La demo no pudo prepararse. El recibo y los logs locales en .validation conservan su estado para diagnóstico; no se tocó la base normal.'
}
