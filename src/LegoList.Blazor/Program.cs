using LegoList.Blazor.Components;
using LegoList.Blazor.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

// In production the app runs behind a reverse proxy (Caddy) that terminates TLS.
// Trust its X-Forwarded-* headers so Google OAuth builds https:// redirect URIs.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

// Persist Data Protection keys so auth cookies survive container restarts/deploys.
var keysPath = builder.Configuration["DataProtection:KeysPath"];
if (!string.IsNullOrEmpty(keysPath))
{
    builder.Services.AddDataProtection()
        .SetApplicationName("LegoList")
        .PersistKeysToFileSystem(new DirectoryInfo(keysPath));
}

builder.Services
    .AddAuthentication(o =>
    {
        o.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        o.DefaultChallengeScheme = GoogleDefaults.AuthenticationScheme;
    })
    .AddCookie(o =>
    {
        o.SlidingExpiration = true;
        o.ExpireTimeSpan = TimeSpan.FromDays(14);
        // The API accepts the Google ID token, which expires after about an hour.
        // Once it has, drop the cookie so the next page load signs in again
        // (Google usually does this silently, without a prompt).
        o.Events.OnValidatePrincipal = async ctx =>
        {
            var token = IdToken.From(ctx.Principal);
            var expiry = token is null ? null : IdToken.GetExpiry(token);
            if (expiry is null || expiry <= DateTimeOffset.UtcNow.AddMinutes(1))
            {
                ctx.RejectPrincipal();
                await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            }
        };
    })
    .AddGoogle(o =>
    {
        o.ClientId = builder.Configuration["Authentication:Google:ClientId"]
            ?? throw new InvalidOperationException("Authentication:Google:ClientId not configured");
        o.ClientSecret = builder.Configuration["Authentication:Google:ClientSecret"]
            ?? throw new InvalidOperationException("Authentication:Google:ClientSecret not configured");
        // Keep the ID token on the user itself (a claim in the auth cookie) so it can
        // be read inside interactive circuits, where there is no HttpContext.
        o.Events.OnCreatingTicket = ctx =>
        {
            if (ctx.TokenResponse.Response?.RootElement.TryGetProperty("id_token", out var idToken) == true
                && idToken.GetString() is { Length: > 0 } token)
            {
                ctx.Identity?.AddClaim(new System.Security.Claims.Claim(IdToken.ClaimType, token));
            }
            return Task.CompletedTask;
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddHttpContextAccessor();
builder.Services.AddCircuitServicesAccessor();
builder.Services.AddTransient<ApiAuthHandler>();

builder.Services.AddHttpClient("LegoListApi", c =>
    c.BaseAddress = new Uri(builder.Configuration["ApiBaseUrl"] ?? "http://localhost:5003/"))
    .AddHttpMessageHandler<ApiAuthHandler>();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/auth/login", async (HttpContext ctx, string? returnUrl) =>
    await ctx.ChallengeAsync(GoogleDefaults.AuthenticationScheme,
        new AuthenticationProperties { RedirectUri = returnUrl ?? "/" }))
    .AllowAnonymous();

app.MapGet("/auth/logout", async (HttpContext ctx) =>
{
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/");
}).RequireAuthorization();

app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
