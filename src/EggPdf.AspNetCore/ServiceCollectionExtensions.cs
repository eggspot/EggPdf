using EggPdf.Razor;
using Microsoft.Extensions.DependencyInjection;

namespace EggPdf.AspNetCore;

/// <summary>
/// DI registration extensions for EggPdf in ASP.NET Core.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Register EggPdf Razor-to-PDF converter in the DI container.
    /// Usage: services.AddEggPdfRazor();
    /// </summary>
    public static IServiceCollection AddEggPdfRazor(this IServiceCollection services)
    {
        services.AddSingleton<IRazorToPdfConverter>(sp =>
            new RazorToPdfConverter());
        return services;
    }

    /// <summary>
    /// Register EggPdf Razor-to-PDF converter with custom renderers.
    /// </summary>
    public static IServiceCollection AddEggPdfRazor(this IServiceCollection services,
        System.Func<string, object?, System.Threading.Tasks.Task<string>> viewRenderer,
        System.Func<string, object?, System.Threading.Tasks.Task<string>>? stringRenderer = null)
    {
        services.AddSingleton<IRazorToPdfConverter>(sp =>
            new RazorToPdfConverter(viewRenderer, stringRenderer));
        return services;
    }
}
