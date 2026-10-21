using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Routify.Web.Data;
using Routify.Web.Hubs;
using Routify.Web.Services.Archivos;
using Routify.Web.Services.Indicadores;
using Routify.Web.Services.Notificaciones;
using Routify.Web.Services.Parametros;
using Routify.Web.Services.Pedidos;
using Routify.Web.Services.Ruteo;
using Routify.Web.Services.Seguridad;
using Routify.Web.Services.Telemetria;

var builder = WebApplication.CreateBuilder(args);
var cfg = builder.Configuration;

// ------------------------------------------------------------------ Base de datos
// SQL Server + tipos espaciales (geography) mediante NetTopologySuite.
builder.Services.AddDbContext<RoutifyDbContext>(opciones =>
    opciones.UseSqlServer(
        cfg.GetConnectionString("RoutifyDB"),
        sql => sql.UseNetTopologySuite()));

// ------------------------------------------------------------------ MVC, seguridad y tiempo real
builder.Services.AddControllersWithViews(opciones =>
{
    // Protección CSRF en todas las peticiones que modifican datos (POST/PUT/DELETE).
    opciones.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
    // La validación se declara explícitamente con [Required]; los textos no anulables no se exigen de forma implícita.
    opciones.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
});

builder.Services.AddAntiforgery(opciones =>
{
    opciones.HeaderName = "RequestVerificationToken";
    opciones.Cookie.Name = "routify.antiforgery";
    opciones.Cookie.SameSite = SameSiteMode.Strict;
    opciones.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
});

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(opciones =>
    {
        opciones.Cookie.Name = "routify.sesion";
        opciones.Cookie.HttpOnly = true;
        opciones.Cookie.SameSite = SameSiteMode.Lax;
        opciones.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        opciones.LoginPath = "/Account/Login";
        opciones.AccessDeniedPath = "/Account/AccesoDenegado";
        opciones.SlidingExpiration = true;
        opciones.ExpireTimeSpan = TimeSpan.FromMinutes(cfg.GetValue("Seguridad:InactividadMinutos", 20));

        // Las llamadas AJAX/SignalR reciben 401/403 en vez de una redirección HTML.
        opciones.Events.OnRedirectToLogin = ctx =>
        {
            if (EsPeticionDeApi(ctx.Request)) { ctx.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; }
            ctx.Response.Redirect(ctx.RedirectUri);
            return Task.CompletedTask;
        };
        opciones.Events.OnRedirectToAccessDenied = ctx =>
        {
            if (EsPeticionDeApi(ctx.Request)) { ctx.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; }
            ctx.Response.Redirect(ctx.RedirectUri);
            return Task.CompletedTask;
        };

        // Si un administrador desactiva una cuenta, su sesión deja de ser válida (RF-03).
        opciones.Events.OnValidatePrincipal = async ctx =>
        {
            var idTexto = ctx.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(idTexto, out int id))
            {
                ctx.RejectPrincipal();
                await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return;
            }

            var cache = ctx.HttpContext.RequestServices.GetRequiredService<IMemoryCache>();
            if (!cache.TryGetValue($"usuario-activo:{id}", out bool activo))
            {
                var db = ctx.HttpContext.RequestServices.GetRequiredService<RoutifyDbContext>();
                activo = await db.Usuarios.AsNoTracking().AnyAsync(u => u.Id == id && u.Estado == "Activo");
                cache.Set($"usuario-activo:{id}", activo, TimeSpan.FromSeconds(15));
            }
            if (!activo)
            {
                ctx.RejectPrincipal();
                await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            }
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddSignalR();
builder.Services.AddMemoryCache();

builder.Services.AddRateLimiter(opciones =>
{
    opciones.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    opciones.AddPolicy("login", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "anonimo",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
    opciones.AddPolicy("seguimiento", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "anonimo",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 90, Window = TimeSpan.FromMinutes(1) }));
});

// ------------------------------------------------------------------ Servicios de la aplicación
builder.Services.Configure<CorreoOpciones>(cfg.GetSection("Correo"));
builder.Services.Configure<WhatsAppOpciones>(cfg.GetSection("WhatsApp"));

builder.Services.AddSingleton<IPasswordService, PasswordService>();
builder.Services.AddSingleton<IArchivoService, ArchivoService>();
builder.Services.AddSingleton<NotificacionCola>();

builder.Services.AddScoped<IConfiguracionService, ConfiguracionService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<RutaService>();
builder.Services.AddScoped<INotificacionService, NotificacionService>();
builder.Services.AddScoped<PedidoService>();
builder.Services.AddScoped<TelemetriaService>();
builder.Services.AddScoped<IndicadoresService>();
builder.Services.AddScoped<ICorreoSender, SmtpCorreoSender>();
builder.Services.AddScoped<IWhatsAppSender, MetaWhatsAppSender>();

builder.Services.AddHostedService<NotificacionWorker>();
builder.Services.AddHostedService<DepuracionWorker>();

builder.Services.AddHttpClient("whatsapp", c =>
{
    c.BaseAddress = new Uri("https://graph.facebook.com/");
    c.Timeout = TimeSpan.FromSeconds(20);
});
builder.Services.AddHttpClient("nominatim", c =>
{
    c.BaseAddress = new Uri(cfg["Geocodificacion:UrlBase"] ?? "https://nominatim.openstreetmap.org/");
    // La política de uso de Nominatim exige identificar la aplicación.
    c.DefaultRequestHeaders.UserAgent.ParseAdd(cfg["Geocodificacion:UserAgent"] ?? "Routify/1.0");
    c.DefaultRequestHeaders.AcceptLanguage.ParseAdd("es");
    c.Timeout = TimeSpan.FromSeconds(10);
});

var app = builder.Build();

// ------------------------------------------------------------------ Cultura (español de Perú)
var cultura = new CultureInfo("es-PE");
CultureInfo.DefaultThreadCurrentCulture = cultura;
CultureInfo.DefaultThreadCurrentUICulture = cultura;

// ------------------------------------------------------------------ Pipeline HTTP
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}
app.UseHttpsRedirection();

app.Use(async (ctx, next) =>
{
    var h = ctx.Response.Headers;
    h["X-Content-Type-Options"] = "nosniff";
    h["X-Frame-Options"] = "DENY";
    h["Referrer-Policy"] = "strict-origin-when-cross-origin";
    // La PWA necesita geolocalización y cámara; el resto se deniega.
    h["Permissions-Policy"] = "geolocation=(self), camera=(self), microphone=()";
    await next();
});

app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        // El Service Worker y el manifiesto nunca deben quedar en caché del navegador.
        var nombre = ctx.File.Name;
        if (nombre == "sw.js" || nombre == "manifest.webmanifest")
            ctx.Context.Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
    }
});

app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapHub<TrackingHub>("/hubs/tracking");
app.MapControllerRoute(name: "default", pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();

static bool EsPeticionDeApi(HttpRequest req) =>
    req.Path.StartsWithSegments("/api") || req.Path.StartsWithSegments("/hubs");
