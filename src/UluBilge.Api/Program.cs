using Scalar.AspNetCore;
using UluBilge.Api.Authentication;
using UluBilge.Api.Exceptions;
using UluBilge.Api.Localization;
using UluBilge.Api.OpenApi;
using UluBilge.App;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers(TurkishApiMessages.ConfigureMvc)
    .AddJsonOptions(options => options.AllowInputFormatterExceptionMessages = false)
    .ConfigureApiBehaviorOptions(TurkishApiMessages.ConfigureApiBehavior);
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
    options.AddOperationTransformer<BearerSecuritySchemeTransformer>();
});
builder.Services.AddProblemDetails(options =>
    options.CustomizeProblemDetails = TurkishApiMessages.CustomizeProblemDetails);
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddApplication(builder.Configuration);
builder.Services.AddApiAuthentication(builder.Configuration, builder.Environment);

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages(context => Results.Problem(
    statusCode: context.HttpContext.Response.StatusCode,
    title: TurkishApiMessages.GetStatusTitle(context.HttpContext.Response.StatusCode))
    .ExecuteAsync(context.HttpContext));

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.Use(async (context, next) =>
    {
        if (!context.Request.IsHttps)
        {
            await Results.Problem(statusCode: StatusCodes.Status400BadRequest,
                title: "HTTPS gerekli").ExecuteAsync(context);
            return;
        }

        await next(context);
    });
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
    app.MapScalarApiReference("/docs", options => options.WithTitle("UluBilge API")
        .AddPreferredSecuritySchemes("Bearer")).AllowAnonymous();
}

app.UseHttpsRedirection();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

public partial class Program;
