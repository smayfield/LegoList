using Microsoft.AspNetCore.Components.Server.Circuits;

namespace LegoList.Blazor.Services;

// IHttpClientFactory creates message handlers (like ApiAuthHandler) in their own DI
// scope, so they can't see services scoped to the user's Blazor circuit. This is
// Microsoft's documented pattern for reaching the current circuit's services:
// https://learn.microsoft.com/aspnet/core/blazor/fundamentals/dependency-injection#access-server-side-blazor-services-from-a-different-di-scope

public class CircuitServicesAccessor
{
    private static readonly AsyncLocal<IServiceProvider?> BlazorServices = new();

    public IServiceProvider? Services
    {
        get => BlazorServices.Value;
        set => BlazorServices.Value = value;
    }
}

public class ServicesAccessorCircuitHandler(IServiceProvider services, CircuitServicesAccessor accessor)
    : CircuitHandler
{
    public override Func<CircuitInboundActivityContext, Task> CreateInboundActivityHandler(
        Func<CircuitInboundActivityContext, Task> next) =>
        async context =>
        {
            accessor.Services = services;
            await next(context);
            accessor.Services = null;
        };
}

public static class CircuitServicesServiceCollectionExtensions
{
    public static IServiceCollection AddCircuitServicesAccessor(this IServiceCollection services)
    {
        services.AddScoped<CircuitServicesAccessor>();
        services.AddScoped<CircuitHandler, ServicesAccessorCircuitHandler>();
        return services;
    }
}
