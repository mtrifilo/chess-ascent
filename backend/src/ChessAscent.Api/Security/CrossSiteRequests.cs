using Microsoft.AspNetCore.Antiforgery;

namespace ChessAscent.Api.Security;

// The session is a cookie, and a browser attaches cookies to a request
// whichever site's page started it. So a page on another site could submit a
// hidden form to POST /auth/sign-out, or to a future game endpoint, and the
// request would arrive signed in as the player: cross-site request forgery.
//
// The defence is to refuse any request that could change state (every method
// except GET, HEAD, OPTIONS, TRACE and QUERY) when the browser says it was started by
// a page from another origin. Browsers say so in the Sec-Fetch-Site header
// (Chrome since version 76, Firefox since 90, Safari since 16.4 in 2023),
// which a page's own script cannot set or change. The
// front end calls the API through its own origin (the Next.js rewrite of
// /api), so its requests say "same-origin" and pass. Without that header, an
// Origin header that differs from the API's own address is refused too, for
// older browsers. Behind the development rewrite that also refuses those
// older browsers' own requests: the rewrite sends the API's address as the
// Host, so it never matches the web app's Origin. A request with neither
// header, such as curl, is not from a browser and carries no cookie the
// browser chose to attach, so it passes.
//
// That decision is ASP.NET Core's own ICsrfProtection, which the default host
// registers. The framework applies it by itself only to endpoints that carry
// antiforgery metadata, such as those that bind a form, and there it only
// records a verdict for the endpoint to act on, so the
// middleware below applies it to every request and answers a refusal with
// 403. It is not the token-based antiforgery system (AddAntiforgery and
// UseAntiforgery): a token needs a way to hand it to the front end and a
// header to send it back in, and the header check gives the same protection
// to a same-origin front end without either.
//
// SameSite=Lax on the cookie (see AccountServices) already keeps browsers
// from sending it on most cross-site POSTs, but not from a sibling subdomain,
// which counts as the same site. If the front end and the API ever sit on
// different origins, their requests will say "same-site" or "cross-site" and
// be refused here, until the front end's origin is trusted through a CORS
// policy that allows credentials, which ICsrfProtection honours.
public static partial class CrossSiteRequests
{
    /// <summary>The detail of the problem a refused request gets, which tells it apart from any other 403.</summary>
    public const string RefusedDetail = "A page on another origin started this request.";

    /// <summary>
    /// Answers 403 to a request that could change state when the browser
    /// reports that a page from another origin started it.
    /// </summary>
    public static IApplicationBuilder UseCrossSiteRequestRefusal(this IApplicationBuilder app)
    {
        // The default host registers ICsrfProtection as a Singleton, so it is
        // resolved once, while the pipeline is built, rather than per request.
        var protection = app.ApplicationServices.GetRequiredService<ICsrfProtection>();
        var problems = app.ApplicationServices.GetRequiredService<IProblemDetailsService>();
        var logger = app.ApplicationServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(CrossSiteRequests).FullName!);

        // LEARN: app.Use with a lambda adds a middleware without writing a
        // class: it receives the request and `next`, the rest of the pipeline,
        // and either calls it or answers by itself, like a servlet Filter's
        // doFilter that may skip chain.doFilter. A Singleton it needs can be
        // taken from app.ApplicationServices once, as above; a Scoped service
        // would have to come from context.RequestServices, the request's own
        // DI scope, on every request.
        return app.Use(async (context, next) =>
        {
            var verdict = await protection.ValidateAsync(context);
            if (!verdict.IsAllowed)
            {
                if (logger.IsEnabled(LogLevel.Information))
                {
                    var secFetchSite = DescribeSecFetchSite(context.Request.Headers["Sec-Fetch-Site"].ToString());
                    var origin = DescribeOrigin(context.Request.Headers.Origin.ToString());
                    LogRefused(logger, context.Request.Method, context.Request.Path, secFetchSite, origin);
                }
                context.Response.StatusCode = StatusCodes.Status403Forbidden;

                // TryWriteAsync, not WriteAsync: when the request's Accept
                // header rules out JSON, no problem-details writer applies,
                // and WriteAsync would throw and turn the refusal into a 500
                // that any page could trigger. Then the body stays empty, and
                // UseStatusCodePages writes a plain-text 403 on the way out.
                await problems.TryWriteAsync(new ProblemDetailsContext
                {
                    HttpContext = context,
                    ProblemDetails = { Status = StatusCodes.Status403Forbidden, Detail = RefusedDetail },
                });
                return;
            }

            await next(context);
        });
    }

    // LEARN: [LoggerMessage] on a partial method has a source generator
    // write the method's body at build time. The generated method parses its
    // template once into a static field, does nothing at all when the level
    // is switched off, and passes the arguments as typed values until a
    // logging provider asks for the text or the named fields. A call to
    // logger.LogInformation(template, args) finds the template in a shared
    // cache but builds its params array every time, switched off or not. The placeholders become named fields in
    // structured logs, as with SLF4J's {} placeholders and a structured
    // appender. "partial" lets one part of a class or method be written by
    // hand and the rest by a generator, which Java has no syntax for.
    // Information rather than Warning: any page, or any client, can cause one.
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Refused {Method} {Path}: Sec-Fetch-Site '{SecFetchSite}', Origin '{Origin}'.")]
    private static partial void LogRefused(
        ILogger logger, string method, PathString path, string secFetchSite, string origin);

    // Both headers come from the client, and Kestrel lets control characters
    // other than CR, LF and NUL through, which could rewrite a terminal's
    // display of the log. So only known values are logged: a Sec-Fetch-Site
    // value the Fetch Metadata specification defines, and an Origin's scheme,
    // host (in its ASCII form) and port.
    private static string DescribeSecFetchSite(string value) => value switch
    {
        "cross-site" or "same-site" or "same-origin" or "none" => value,
        "" => "(none)",
        _ => "(other)",
    };

    private static string DescribeOrigin(string value) =>
        value.Length == 0 ? "(none)"
        : Uri.TryCreate(value, UriKind.Absolute, out var origin) && origin.Scheme is "http" or "https"
            ? $"{origin.Scheme}://{origin.IdnHost}:{origin.Port}"
            : "(other)";
}
