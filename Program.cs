using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MiniMarket.Web.Data;

var builder = WebApplication.CreateBuilder(args);

// Mantiene la sesión local durante los reinicios de dotnet watch.
if (builder.Environment.IsDevelopment() || builder.Environment.EnvironmentName == "QA")
{
    var localKeys = Path.Combine(builder.Environment.ContentRootPath, ".local-data-protection-keys");
    Directory.CreateDirectory(localKeys);
    builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(localKeys));
}

// 1. Base de Datos
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));

// 2. Identidad (Usuarios y Roles)
builder.Services.AddDefaultIdentity<IdentityUser>(options => 
{
    options.SignIn.RequireConfirmedAccount = false;
    options.Password.RequireDigit = true;
    options.Password.RequireNonAlphanumeric = true;
    options.Password.RequiredLength = 12;
    options.Lockout.AllowedForNewUsers = true;
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
})
.AddRoles<IdentityRole>()
.AddEntityFrameworkStores<ApplicationDbContext>();
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsProduction()
        ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
});
// Rechaza inmediatamente sesiones antiguas cuando cambia el correo/credenciales.
builder.Services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = TimeSpan.Zero);

// 3. Servicios Web
builder.Services.AddControllersWithViews(options =>
    options.Filters.Add(new Microsoft.AspNetCore.Mvc.AutoValidateAntiforgeryTokenAttribute()));
builder.Services.AddRazorPages(); // ¡IMPORTANTE PARA EL LOGIN!
builder.Services.AddHttpClient<MiniMarket.Web.Services.FactilizaService>()
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddScoped<MiniMarket.Web.Services.IAAsistenteService>();

var app = builder.Build();

if (args.Contains("--apply-commercial-upgrade", StringComparer.OrdinalIgnoreCase))
{
    using var scope = app.Services.CreateScope();
    await ActualizacionComercial.Aplicar(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    scope.ServiceProvider.GetRequiredService<ILogger<Program>>()
        .LogInformation("Upgrade comercial/fiscal completado. La aplicación no se iniciará en este proceso.");
    return;
}

// 4. Inicializar Datos (solo desarrollo)
// En produccion no se modifica la base de datos durante el arranque.
if (!app.Environment.IsProduction() &&
    (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("StartupDatabaseTasks:Enabled")))
{
    using (var scope = app.Services.CreateScope())
    {
        var services = scope.ServiceProvider;
        await ActualizacionComercial.Aplicar(services.GetRequiredService<ApplicationDbContext>());
        try
        {
            await SembradorDatos.Inicializar(services);
        }
        catch (Exception ex)
        {
            var logger = services.GetRequiredService<ILogger<Program>>();
            logger.LogError(ex, "Error al sembrar datos.");
        }
    }
}

// 5. Pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");
app.MapRazorPages(); // ¡IMPORTANTE!

app.Run();
