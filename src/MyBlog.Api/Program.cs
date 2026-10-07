using MyBlog.Api;
using MyBlog.Api.Endpoints;
using MyBlog.Api.Infrastructure;
using MyBlog.Application;
using MyBlog.Infrastructure;
using MyBlog.Infrastructure.Persistence;
using Scalar.AspNetCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSerilog((services, logger) => logger
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());

builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration, builder.Environment)
    .AddPresentation(builder.Configuration);

var app = builder.Build();

await app.Services.InitializeDatabaseAsync();

// Birinchi middleware: keyingilari (log, HSTS, rate limiter) mijozning haqiqiy IP/sxemasini ko'radi.
app.UseReverseProxySupport();
app.UseExceptionHandler();

if (!app.Environment.IsDevelopment())
    app.UseHsts();

app.UseSerilogRequestLogging();
app.UseRequestLocalization();
app.UseCors(MyBlog.Api.DependencyInjection.CorsPolicy);

// Autentifikatsiya rate limiter'dan oldin: Comments/Upload policy'lari foydalanuvchi bo'yicha bo'linadi.
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapControllers();
app.MapMediaEndpoints();
app.MapHealthChecks("/health").DisableRateLimiting();

await app.RunAsync();

public partial class Program;
