using LegoList.Data;
using LegoList.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;

namespace LegoList
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            builder.Services.AddDbContext<LegoListDbContext>(options =>
                options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

            builder.Services.AddCors(options =>
            {
                options.AddPolicy("BlazorClient", policy =>
                    policy.WithOrigins("http://localhost:5004", "https://localhost:7004")
                          .AllowAnyHeader()
                          .AllowAnyMethod());
            });

            var googleClientId = builder.Configuration["Authentication:Google:ClientId"]
                ?? throw new InvalidOperationException("Authentication:Google:ClientId not configured");

            builder.Services
                .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(options =>
                {
                    options.Authority = "https://accounts.google.com";
                    options.Audience = googleClientId;
                    options.TokenValidationParameters.ValidIssuer = "https://accounts.google.com";
                });
            builder.Services.AddAuthorization();
            builder.Services.AddScoped<UserService>();

            builder.Services.AddHttpClient<RebrickableService>(c =>
            {
                c.BaseAddress = new Uri("https://rebrickable.com/");
                var apiKey = builder.Configuration["Rebrickable:ApiKey"] ?? string.Empty;
                if (!string.IsNullOrEmpty(apiKey))
                    c.DefaultRequestHeaders.Add("Authorization", $"key {apiKey}");
            });

            builder.Services.AddControllers();
            builder.Services.AddOpenApi();

            var app = builder.Build();

            if (app.Environment.IsDevelopment())
            {
                app.MapOpenApi();
            }

            app.UseHttpsRedirection();
            app.UseCors("BlazorClient");
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapControllers();

            app.Run();
        }
    }
}
