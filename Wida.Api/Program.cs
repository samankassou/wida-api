using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;
using Scalar.AspNetCore;
using Wida.Dal;
using Wida.Bll;
using Wida.Api.Authentication;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "Configure ConnectionStrings:DefaultConnection using user secrets or ConnectionStrings__DefaultConnection.");
}

builder.Services.AddWidaAuthentication(builder.Configuration, builder.Environment);
builder.Services.AddControllers(options => options.Filters.Add<ValidateSessionAntiforgeryFilter>())
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(
            new JsonStringEnumConverter());
    });

// Add services to the container.
builder.Services
    .AddDal(builder.Configuration)
    .AddBll();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

// The frontend is the public HTTPS origin. The private API may use HTTP behind it.
// Authentication callback URLs and cookie security use the configured public origin.
var publicScheme = new Uri(app.Services.GetRequiredService<PilotAccess>().PublicOrigin).Scheme;
app.Use(async (context, next) =>
{
    // Use server configuration, never caller-supplied forwarded headers.
    context.Request.Scheme = publicScheme;
    context.Response.Headers.CacheControl = "no-store";
    await next(context);
});
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

public partial class Program { }
