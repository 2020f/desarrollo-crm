using Desarrollo.Application.Abstractions;
using Desarrollo.Infrastructure.Data;
using Desarrollo.Infrastructure.Identity;
using Desarrollo.Infrastructure.Services;
using Desarrollo.Web.Hubs;
using Desarrollo.Web.Middleware;
using Desarrollo.Web.Services;
using Desarrollo.Web.Worker;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.SqlClient;
using System.Threading.RateLimiting;

var migrateOnly = args.Contains("--migrate-only", StringComparer.Ordinal);
var bootstrapOnly = args.Contains("--bootstrap-admin-only", StringComparer.Ordinal);
if (migrateOnly && bootstrapOnly) throw new InvalidOperationException("Ejecuta migración e inicialización por separado.");
var builder = WebApplication.CreateBuilder(args.Where(arg => arg is not ("--migrate-only" or "--bootstrap-admin-only")).ToArray());
var secureCookies = builder.Configuration.GetValue<bool>("App:RequireSecureCookies");
var hasTrustedReverseProxy = ReverseProxyConfiguration.Configure(builder.Services, builder.Configuration);
var keyRingPath = builder.Configuration["DataProtection:KeyRingPath"];
if (!string.IsNullOrWhiteSpace(keyRingPath))
{
    if (!Path.IsPathFullyQualified(keyRingPath)) throw new InvalidOperationException("DataProtection:KeyRingPath debe ser una ruta absoluta.");
    builder.Services.AddDataProtection().SetApplicationName("Desarrollo.Crm")
        .PersistKeysToFileSystem(new DirectoryInfo(keyRingPath));
}

var conn = migrateOnly
    ? builder.Configuration.GetConnectionString("Maintenance") ?? (builder.Environment.IsDevelopment()
        ? builder.Configuration.GetConnectionString("Default") : throw new InvalidOperationException("Configura ConnectionStrings:Maintenance para las migraciones explícitas."))
    : builder.Configuration.GetConnectionString("Default");
if (string.IsNullOrWhiteSpace(conn)) throw new InvalidOperationException("Cadena de conexión no configurada.");
if (!migrateOnly && !builder.Environment.IsDevelopment() && new SqlConnectionStringBuilder(conn).UserID.Equals("sa", StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("La aplicación debe usar un usuario SQL de ejecución limitado, distinto de sa.");

builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlServer(conn));

builder.Services.AddIdentity<AppUser, IdentityRole<Guid>>(options =>
{
    options.Password.RequiredLength = 8;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequireUppercase = false;
    options.Password.RequireDigit = true;
    options.User.RequireUniqueEmail = true;
    options.SignIn.RequireConfirmedEmail = false;
    options.Lockout.MaxFailedAccessAttempts = 8;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
})
.AddEntityFrameworkStores<AppDbContext>()
.AddClaimsPrincipalFactory<CrmClaimsPrincipalFactory>()
.AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = secureCookies ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
    options.ExpireTimeSpan = TimeSpan.FromDays(7);
    options.SlidingExpiration = true;
    options.LoginPath = "/login";
    var identityValidation = options.Events.OnValidatePrincipal;
    options.Events.OnValidatePrincipal = async context =>
    {
        await identityValidation(context);
        await ActiveAccountSession.ValidateAsync(context);
    };
    options.Events.OnRedirectToLogin = ctx =>
    {
        if (ctx.Request.Path.StartsWithSegments("/api")) { ctx.Response.StatusCode = 401; }
        else { ctx.Response.Redirect("/login"); }
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = ctx =>
    {
        ctx.Response.StatusCode = 403;
        return Task.CompletedTask;
    };
});

builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-XSRF-TOKEN";
    options.Cookie.Name = "XSRF-TOKEN";
    options.Cookie.HttpOnly = false;
    options.Cookie.SecurePolicy = secureCookies ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
});

builder.Services.AddControllersWithViews();
builder.Services.AddSignalR();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton(new RegistrationPolicy(builder.Configuration.GetValue<bool?>("App:AllowRegistration") ?? builder.Environment.IsDevelopment()));
builder.Services.AddScoped<AccountProvisioningService>();
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("account-login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "local", _ => new FixedWindowRateLimiterOptions
        { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true }));
    options.AddPolicy("account-register", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "local", _ => new FixedWindowRateLimiterOptions
        { PermitLimit = 3, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true }));
    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.StatusCode = 429;
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retry))
            context.HttpContext.Response.Headers.RetryAfter = Math.Ceiling(retry.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
        await context.HttpContext.Response.WriteAsJsonAsync(new { code = "RATE_LIMIT", message = "Demasiados intentos. Espera un minuto e inténtalo de nuevo." }, cancellationToken: token);
    };
});

builder.Services.AddScoped<ICurrentUser, CurrentUserService>();
builder.Services.AddScoped<IPermissionService, PermissionService>();
builder.Services.AddScoped<IOrganizationService, OrganizationService>();
builder.Services.AddScoped<IProjectService, ProjectService>();
builder.Services.AddScoped<IWorkItemService, WorkItemService>();
builder.Services.AddScoped<ITaskDiagramService, TaskDiagramService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<ITicketService, TicketService>();
builder.Services.AddScoped<IChannelService, ChannelService>();
builder.Services.AddScoped<IDocumentService, DocumentService>();
builder.Services.AddScoped<IFileService, FileService>();
builder.Services.AddSingleton<IPrivateFileStorage>(_ =>
{
    var storageRoot = builder.Configuration["Storage:RootPath"];
    if (string.IsNullOrWhiteSpace(storageRoot)) storageRoot = Path.Combine(builder.Environment.ContentRootPath, "App_Data", "files");
    if (!Path.IsPathFullyQualified(storageRoot)) throw new InvalidOperationException("Storage:RootPath debe ser una ruta absoluta.");
    var full = Path.GetFullPath(storageRoot).TrimEnd(Path.DirectorySeparatorChar);
    var web = Path.GetFullPath(builder.Environment.WebRootPath ?? Path.Combine(builder.Environment.ContentRootPath, "wwwroot")).TrimEnd(Path.DirectorySeparatorChar);
    var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    if (full.Equals(web, comparison) || full.StartsWith(web + Path.DirectorySeparatorChar, comparison))
        throw new InvalidOperationException("El almacenamiento de archivos debe estar fuera de wwwroot.");
    return new LocalPrivateFileStorage(full);
});
builder.Services.AddScoped<ICalendarService, CalendarService>();
builder.Services.AddScoped<IReportService, ReportService>();

builder.Services.AddHostedService<BackgroundWorker>();

var app = builder.Build();

// Explicit maintenance command for Docker/Ubuntu; never an implicit production startup migration.
if (migrateOnly)
{
    using var migrationScope = app.Services.CreateScope();
    await migrationScope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    return;
}
if (bootstrapOnly)
{
    using var bootstrapScope = app.Services.CreateScope();
    var config = app.Configuration;
    await bootstrapScope.ServiceProvider.GetRequiredService<AccountProvisioningService>().CreateTeamAsync(new(
        config["Bootstrap:FullName"] ?? "", config["Bootstrap:Email"] ?? "",
        config["Bootstrap:Password"] ?? "", config["Bootstrap:OrganizationName"] ?? ""), onlyIfEmpty: true);
    app.Logger.LogInformation("Cuenta administrativa inicial creada. No se modificaron cuentas existentes.");
    return;
}

app.UseMiddleware<ApiExceptionMiddleware>();
if (hasTrustedReverseProxy) app.UseForwardedHeaders();

if (builder.Configuration.GetValue<bool>("Database:ApplyMigrationsOnStartup"))
{
    if (!app.Environment.IsDevelopment())
        throw new InvalidOperationException("Las migraciones al arrancar solo se permiten mediante configuración explícita en desarrollo.");
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}

app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = context =>
    {
        // Fixed Vite asset names must revalidate after a build. A query string on
        // just the entry would load a second React instance through lazy imports.
        if (context.Context.Request.Path.StartsWithSegments("/dist"))
            context.Context.Response.Headers.CacheControl = "no-cache";
    },
});

app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<AppHub>("/hubs/app");
app.MapGet("/api/{**path}", () => Results.NotFound(new { code = "NOT_FOUND", message = "Ruta no encontrada" }));
app.MapFallbackToController("Index", "Home");

app.Run();
