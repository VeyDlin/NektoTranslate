using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using NektoTranslate.Common.Contracts;


namespace NektoTranslate.Common.Http;


// Every exception that escapes a controller leaves the server as a status, in the same
// `{ status }` envelope a deliberate refusal uses.
//
// Without this, a service saying "there is no translation between chapters 1 and 20 to learn from"
// reached the interface as a bare 500 with an empty body, and the interface had nothing to show but
// that a button did not work. The sentence the service wrote for a reader is the one thing worth
// sending, and sending it as a status means the interface handles it exactly as it handles every
// other refusal - one code path, one translation table.
//
// The status code tells the two apart. An InvalidOperationException is how the services refuse a
// request that is well-formed but wrong for the moment, so it goes out as a 409; anything else is a
// failure of ours and goes out as a 500, with its message, because this is one person's own machine
// and a sentence they can search for beats a number they cannot.
public sealed class StatusExceptionHandler(ILogger<StatusExceptionHandler> logger) : IExceptionHandler {

    public sealed record StatusEnvelope(Status status);


    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken
    ) {
        (int statusCode, Status status) = Describe(exception);

        if (statusCode >= StatusCodes.Status500InternalServerError) {
            logger.LogError(exception, "{Method} {Path} failed", httpContext.Request.Method, httpContext.Request.Path);
        } else {
            logger.LogInformation("{Method} {Path} was refused: {Reason}", httpContext.Request.Method, httpContext.Request.Path, exception.Message);
        }

        httpContext.Response.StatusCode = statusCode;
        await httpContext.Response.WriteAsJsonAsync(new StatusEnvelope(status), cancellationToken);

        return true;
    }


    public static (int statusCode, Status status) Describe(Exception exception) {
        return exception switch {
            InvalidOperationException refusal => (
                StatusCodes.Status409Conflict,
                Statuses.RequestRefused.With(("reason", refusal.Message))
            ),

            _ => (
                StatusCodes.Status500InternalServerError,
                Statuses.ServerFailed.With(("reason", exception.Message))
            )
        };
    }
}
