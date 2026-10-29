using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using UluBilge.Core.Exceptions;

namespace UluBilge.Api.Exceptions;

public sealed class ApiExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, title, detail) = exception switch
        {
            ValidationException validation =>
                (StatusCodes.Status400BadRequest, "Geçersiz istek", validation.Message),
            NotFoundException notFound =>
                (StatusCodes.Status404NotFound, "Kayıt bulunamadı", notFound.Message),
            SqlException { Number: 2601 or 2627 } =>
                (StatusCodes.Status409Conflict, "Kayıt zaten var", "Bu bölge numarasıyla bir kayıt zaten bulunuyor."),
            SqlException { Number: 547 } =>
                (StatusCodes.Status409Conflict, "İlişkili kayıt var", "Bölge kaydı, ilişkili Territories kayıtları nedeniyle silinemiyor."),
            _ =>
                (StatusCodes.Status500InternalServerError, "İşlem tamamlanamadı", "Beklenmeyen bir hata oluştu.")
        };

        if (status == StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "İstek işlenirken hata oluştu. İzleme numarası: {TraceId}", httpContext.TraceIdentifier);
        }

        httpContext.Response.StatusCode = status;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = title,
                Detail = detail,
                Extensions = { ["traceId"] = httpContext.TraceIdentifier }
            }
        });
    }
}
