using System.Net;

namespace UluBilge.McpServer.Clients;

public sealed class ApiException(string message, HttpStatusCode? statusCode = null) : Exception(message)
{
    public HttpStatusCode? StatusCode { get; } = statusCode;
}
