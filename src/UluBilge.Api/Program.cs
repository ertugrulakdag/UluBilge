using Scalar.AspNetCore;
using UluBilge.Api.Exceptions;
using UluBilge.Api.Extentions;
using UluBilge.Api.Localization;
using UluBilge.Api.MCP.Tools;
using UluBilge.App.Authentication;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers(TurkishApiMessages.ConfigureMvc)
    .AddJsonOptions(options => options.AllowInputFormatterExceptionMessages = false)
    .ConfigureApiBehaviorOptions(TurkishApiMessages.ConfigureApiBehavior);

builder.Services.AddApiOpenApi(builder.Environment);
builder.Services.AddRequestProfiling();
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = TurkishApiMessages.CustomizeProblemDetails);
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddUluBilgeServices(builder.Configuration);
builder.Services.AddApiAuthentication(builder.Configuration, builder.Environment);
builder.Services.AddMcpOAuth(builder.Configuration, builder.Environment);
builder.Services.AddMcpServer().WithHttpTransport().WithToolsFromAssembly(typeof(ProductSalesReportTool).Assembly);
//AddMcpServer(): MCP servislerini kaydeder.WithHttpTransport(): HTTP üzerinden iletişimi etkinleştirir.WithToolsFromAssembly: İlgili assembly’deki bütün MCP toollarını bulup kaydeder.

var app = builder.Build();

app.UseRequestProfiling();
app.UseExceptionHandler();
app.UseStatusCodePages(context => Results.Problem(statusCode: context.HttpContext.Response.StatusCode, title: TurkishApiMessages.GetStatusTitle(context.HttpContext.Response.StatusCode)).ExecuteAsync(context.HttpContext));

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.Use(async (context, next) =>
    {
        if (!context.Request.IsHttps)
        {
            await Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "HTTPS gerekli").ExecuteAsync(context);
            return;
        }

        await next(context);
    });
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
    app.MapScalarApiReference("/docs", options => options.WithTitle("UluBilge API").AddPreferredSecuritySchemes("Bearer")).AllowAnonymous();
}

app.UseHttpsRedirection();
app.UseMiniProfiler();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapMcpOAuth();

app.Run(); 
