using Microsoft.Extensions.DependencyInjection;
using Wida.Bll.Services.Implementations;
using Wida.Bll.Services.Interfaces;

namespace Wida.Bll;

public static class DependencyInjection
{
    public static IServiceCollection AddBll(
        this IServiceCollection services)
    {
        services.AddScoped<IDocumentService, DocumentService>();
        services.AddScoped<IInvoiceService, InvoiceService>();
        services.AddScoped<IProcessingService, ProcessingService>();

        return services;
    }
}