using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

namespace UluBilge.Api.Localization
{
    internal static class TurkishApiMessages
    {
        public static void ConfigureMvc(MvcOptions options)
        {
            var messages = options.ModelBindingMessageProvider;
            messages.SetMissingBindRequiredValueAccessor(field => $"{field} alanı zorunludur.");
            messages.SetMissingKeyOrValueAccessor(() => "Bir değer girilmesi zorunludur.");
            messages.SetMissingRequestBodyRequiredValueAccessor(() => "İstek gövdesi boş olamaz.");
            messages.SetValueMustNotBeNullAccessor(_ => "Bu alan boş olamaz.");
            messages.SetAttemptedValueIsInvalidAccessor((_, field) => $"{field} alanı için geçerli bir değer gönderin.");
            messages.SetNonPropertyAttemptedValueIsInvalidAccessor(_ => "Gönderilen değer geçersizdir.");
            messages.SetUnknownValueIsInvalidAccessor(field => $"{field} alanı için gönderilen değer geçersizdir.");
            messages.SetNonPropertyUnknownValueIsInvalidAccessor(() => "Gönderilen değer geçersizdir.");
            messages.SetValueIsInvalidAccessor(_ => "Gönderilen değer geçersizdir.");
            messages.SetValueMustBeANumberAccessor(field => $"{field} alanı sayısal bir değer olmalıdır.");
            messages.SetNonPropertyValueMustBeANumberAccessor(() => "Sayısal bir değer gönderilmelidir.");
        }

        public static void ConfigureApiBehavior(ApiBehaviorOptions options)
        {
            foreach (var (statusCode, error) in options.ClientErrorMapping)
            {
                error.Title = GetStatusTitle(statusCode);
            }

            options.InvalidModelStateResponseFactory = context =>
            {
                var errors = context.ModelState
                    .Where(entry => entry.Value is { Errors.Count: > 0 })
                    .ToDictionary(entry => entry.Key, entry => entry.Value!.Errors
                        .Select(error => error.Exception is not null || string.IsNullOrWhiteSpace(error.ErrorMessage)
                            ? "Gönderilen değer beklenen biçimde değildir. İstek gövdesini ve alan türlerini kontrol edin."
                            : error.ErrorMessage)
                        .ToArray());
                var problem = new ValidationProblemDetails(errors)
                {
                    Status = StatusCodes.Status400BadRequest,
                    Title = "Bir veya daha fazla doğrulama hatası oluştu.",
                    Type = options.ClientErrorMapping[StatusCodes.Status400BadRequest].Link,
                    Extensions = { ["traceId"] = context.HttpContext.TraceIdentifier }
                };

                return new BadRequestObjectResult(problem)
                {
                    ContentTypes = { "application/problem+json" }
                };
            };
        }

        public static void CustomizeProblemDetails(ProblemDetailsContext context)
        {
            var problem = context.ProblemDetails;
            var statusCode = problem.Status ?? context.HttpContext.Response.StatusCode;
            if (string.IsNullOrWhiteSpace(problem.Title)
                || problem.Title == ReasonPhrases.GetReasonPhrase(statusCode))
            {
                problem.Title = GetStatusTitle(statusCode);
            }
        }

        public static string GetStatusTitle(int statusCode) => statusCode switch
        {
            200 => "İşlem başarılı",
            201 => "Kayıt oluşturuldu",
            202 => "İstek kabul edildi",
            204 => "İşlem tamamlandı",
            400 => "Geçersiz istek",
            401 => "Kimlik doğrulama gerekli",
            403 => "Erişim reddedildi",
            404 => "İstenen kaynak bulunamadı",
            405 => "HTTP yöntemine izin verilmiyor",
            406 => "İstenen yanıt biçimi desteklenmiyor",
            408 => "İstek zaman aşımına uğradı",
            409 => "İstek mevcut durumla çakışıyor",
            410 => "İstenen kaynak artık kullanılamıyor",
            411 => "İstek gövdesinin uzunluğu belirtilmelidir",
            412 => "İstek önkoşulu sağlanamadı",
            413 => "İstek gövdesi çok büyük",
            414 => "İstek adresi çok uzun",
            415 => "İstek içeriğinin türü desteklenmiyor",
            416 => "İstenen veri aralığı sağlanamıyor",
            417 => "İstek beklentisi karşılanamadı",
            422 => "İstek içeriği işlenemedi",
            428 => "İstek için önkoşul gerekli",
            429 => "Çok fazla istek gönderildi",
            431 => "İstek başlıkları çok büyük",
            500 => "Beklenmeyen bir sunucu hatası oluştu",
            501 => "İstenen işlem desteklenmiyor",
            502 => "Ağ geçidi geçersiz yanıt aldı",
            503 => "Hizmet şu anda kullanılamıyor",
            504 => "Ağ geçidi zaman aşımına uğradı",
            505 => "HTTP sürümü desteklenmiyor",
            _ => "İstek işlenemedi"
        };
    }
}
