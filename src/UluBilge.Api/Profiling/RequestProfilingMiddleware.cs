using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.Options;
using StackExchange.Profiling;
using MiniProfilerExtensions = UluBilge.Api.Extentions.MiniProfilerExtensions;

namespace UluBilge.Api.Profiling
{
    public sealed class RequestProfilingMiddleware(RequestDelegate next, IOptions<MiniProfilerOptions> options)
    {
        private const int BodyLimit = 16 * 1024;
        private static readonly JsonSerializerOptions Json = new()
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
        };

        public async Task InvokeAsync(HttpContext context)
        {
            if (context.Request.Path.StartsWithSegments(MiniProfilerExtensions.Route))
            {
                await next(context);
                return;
            }

            var request = context.Request;
            var name = $"{request.Method} {request.Path}";
            var profiler = options.Value.StartProfiler(name);
            if (profiler is null)
            {
                await next(context);
                return;
            }
            string? body = null;
            // Yalnızca küçük, uzunluğu bilinen gövdeleri okuyup başa sar.
            if (request.ContentLength is > 0 and <= BodyLimit)
            {
                request.EnableBuffering();
                using var reader = new StreamReader(request.Body, Encoding.UTF8, leaveOpen: true);
                body = await reader.ReadToEndAsync(context.RequestAborted);
                request.Body.Position = 0;
            }
            using (var timing = profiler.CustomTiming("http", JsonSerializer.Serialize(new
            {
                request.Method, Url = request.GetEncodedUrl(), request.Protocol, request.ContentType,
                request.ContentLength, request.Headers, Body = FormatBody(body)
            }, Json), "Request", includeStackTrace: false)) { }

            var originalBody = context.Response.Body;
            // MCP yanıtları akış halinde gider; bunları bellekte bekletme.
            using var responseBody = request.Path.StartsWithSegments("/mcp") ? null : new MemoryStream();
            if (responseBody is not null)
                context.Response.Body = responseBody;

            var status = 0;
            try
            {
                await next(context);
            }
            catch
            {
                status = context.RequestAborted.IsCancellationRequested ? 499 : 500;
                throw;
            }
            finally
            {
                context.Response.Body = originalBody;
                try
                {
                    if (status == 0) status = context.Response.StatusCode;
                    profiler.Name = $"{name} [{status}]";
                    profiler.Root.Name = profiler.Name;
                    using (var timing = profiler.CustomTiming("http", JsonSerializer.Serialize(new
                    {
                        StatusCode = status, context.Response.ContentType, context.Response.ContentLength,
                        context.Response.Headers,
                        Body = responseBody is null ? null : FormatBody(Encoding.UTF8.GetString(responseBody.GetBuffer(), 0, (int)Math.Min(responseBody.Length, BodyLimit)))
                    }, Json), "Response", includeStackTrace: false)) { }

                    if (responseBody is not null)
                    {
                        responseBody.Position = 0;
                        await responseBody.CopyToAsync(originalBody, context.RequestAborted);
                    }
                }
                finally
                {
                    await profiler.StopAsync();
                }
            }
        }

        private static object? FormatBody(string? body)
        {
            if (string.IsNullOrWhiteSpace(body)) return body;
            try
            {
                using var document = JsonDocument.Parse(body);
                return document.RootElement.Clone();
            }
            catch (JsonException)
            {
                return body;
            }
        }
    }
}
