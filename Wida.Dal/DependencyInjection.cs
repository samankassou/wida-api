using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Wida.Dal.Persistence;
using Wida.Dal.Repositories.Implementations;
using Wida.Dal.Repositories.Interfaces;
using Wida.Dal.Services;
using Wida.Dal.Services.Interfaces;

namespace Wida.Dal;

public static class DependencyInjection
{
    public static IServiceCollection AddDal(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<WidaDbContext>(options =>
        {
            options.UseNpgsql(
                configuration.GetConnectionString("DefaultConnection"));
        });

        services.AddScoped<IDocumentRepository, DocumentRepository>();
        services.AddScoped<IInvoiceRepository, InvoiceRepository>();
        services.AddScoped<IProcessingRunRepository, ProcessingRunRepository>();
        services.AddScoped<IDocumentAnalyzer, AzureDocumentAnalyzer>();

        return services;
    }
}
