using System;
using System.Collections.Generic;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Wasil.Api.Middleware;

public class GlobalExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;

    public GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var correlationId = Guid.NewGuid().ToString();
        var statusCode = HttpStatusCode.InternalServerError;
        string message = "An internal server error occurred. Please contact support.";

        switch (exception)
        {
            case ArgumentException:
                statusCode = HttpStatusCode.BadRequest;
                message = exception.Message;
                break;
            case KeyNotFoundException:
                statusCode = HttpStatusCode.NotFound;
                message = exception.Message;
                break;
        }

        if (statusCode == HttpStatusCode.InternalServerError)
        {
            _logger.LogError(exception, "Unhandled Exception details. CorrelationId: {CorrelationId}. Message: {Message}", correlationId, exception.Message);
        }
        else
        {
            _logger.LogWarning("Business rule violation / Bad request handled by middleware: {Message}. Status Code: {StatusCode}", exception.Message, statusCode);
        }

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)statusCode;

        object responseBody;
        if (statusCode == HttpStatusCode.InternalServerError)
        {
            responseBody = new { error = message, correlationId };
        }
        else
        {
            responseBody = new { error = message };
        }

        var json = JsonSerializer.Serialize(responseBody);
        return context.Response.WriteAsync(json);
    }
}
