using ChessAscent.Api.ApiDocument;
using ChessAscent.Api.Database;
using ChessAscent.Api.Engine;
using ChessAscent.Api.Features.Accounts;
using ChessAscent.Api.Features.AppVersion;
using ChessAscent.Api.Features.Games;
using ChessAscent.Api.Features.Health;
using ChessAscent.Api.Opponents;
using ChessAscent.Api.Security;

// LEARN: This file has no class and no `static void Main`. C# allows one file
// per application to hold "top-level statements": the compiler wraps them in
// a generated `Program` class with a `Main` method. Execution starts at the
// first line below, the way SpringApplication.run(...) is the whole of a
// Spring Boot main method, except that here the configuration is not spread
// over annotated classes found by scanning. It is all written out in order
// in this file.

// LEARN: The builder is where the application is assembled before it runs.
// It already holds configuration (appsettings.json, then
// appsettings.{Environment}.json, then user secrets in Development, then
// environment variables, then command-line arguments, each overriding the
// last, much like Spring's
// property sources), logging, and an empty dependency-injection container.
var builder = WebApplication.CreateBuilder(args);

// Points the engine pool's Maia kind at where scripts/setup-maia.sh installs
// it (see MaiaInstallation).
builder.Configuration.AddMaiaInstallation(builder.Environment.ContentRootPath);

// LEARN: Service registration. builder.Services is the DI container, the
// counterpart of Spring's ApplicationContext. There is no classpath scanning
// and no @Component: a service exists only if a line here adds it. The Add...
// methods are extension methods that each register a group of related
// services, which is roughly what a Spring Boot starter's auto-configuration
// does for you, except that you call them yourself. Each registration has a
// lifetime: Singleton (one instance), Scoped (one per HTTP request) or
// Transient (new on every resolve).
//
// AddProblemDetails makes errors use the RFC 9457 "problem details" JSON
// shape. AddHealthChecks registers the service that runs health checks.
// AddDatabase registers the EF Core context for PostgreSQL and the readiness
// check that the database answers. AddEnginePool registers the pool of chess
// engine processes, the hosted service that starts and stops them, and the
// readiness check that every configured kind has a working process.
// AddApiDocument registers the OpenAPI document that describes the endpoints
// below. AddValidation checks request bodies against their validation
// attributes (see AccountContracts). AddAccounts registers ASP.NET Core
// Identity, the session cookie and authorization. AddGames registers the
// store that saves and loads games. AddOpponent registers the computer
// opponent, which plays through the engine pool.
builder.Services.AddProblemDetails();
builder.Services.AddValidation();
builder.Services.AddHealthChecks();
builder.Services.AddDatabase();
builder.Services.AddEnginePool();
builder.Services.AddApiDocument();
builder.Services.AddAccounts();
builder.Services.AddGames();
builder.Services.AddOpponent();

// LEARN: Build() freezes the container: no services can be added after this
// line. What comes next configures how requests are handled.
var app = builder.Build();

// LEARN: `await` at the top level makes the generated Main method return a
// Task (`static async Task Main`), so start-up code can await async methods.
// It does not free the main thread: the compiler's real entry point waits on
// that Task until the application stops, as app.Run() would. The schema is
// brought up to date before the API accepts a request, and if the database
// cannot be reached the exception ends the process here. The build-time
// OpenAPI generator also runs this file, and must not need a database.
if (!ApiDocumentServices.IsBuildTimeGeneration)
{
    await app.ApplyPendingMigrationsAsync();
}

// LEARN: The middleware pipeline. Each Use... call adds a step that a request
// passes through on the way in, and the response passes back through in
// reverse order on the way out, like a chain of servlet filters. Order is
// exactly the order of these lines (there is no @Order annotation), and it
// matters: a step only sees what happens in the steps added after it.
//
// The exception handler goes first so that it wraps everything else. An
// unhandled exception anywhere later becomes a 500 problem-details response
// with no stack trace in the body.
app.UseExceptionHandler();

// A response that leaves the pipeline with an error status and no body, such
// as the 404 for an unknown path, gets a body here: problem details when the
// request accepts JSON, as every request from the front end does, and plain
// text when its Accept header rules JSON out. The steps below rely on it for
// their 401 and 403 answers.
app.UseStatusCodePages();

// A state-changing request that a page on another origin started is refused
// before anything reads its cookie (see CrossSiteRequests).
app.UseCrossSiteRequestRefusal();

// LEARN: Authentication and authorization are two middleware steps, and they
// do different jobs. Authentication reads the request's credentials (here,
// the session cookie) and sets HttpContext.User to the signed-in user, or to
// an anonymous user if there is no valid cookie. It never refuses a request.
// Authorization then reads the policy on the endpoint that routing matched
// (RequireAuthorization, in AccountEndpoints) and, if HttpContext.User does
// not satisfy it, stops the request: a challenge (401) for an anonymous user,
// a forbid (403) for a signed-in user who lacks the permission. Spring
// Security does both inside its one filter chain, in an order it fixes for
// you; here each is a line in the pipeline, so the order is yours. Swapped,
// authorization would see every caller as anonymous. Both must also come
// after routing, which WebApplication runs before the first step above.
// Leaving the two lines out does not leave them out: WebApplication then adds
// both by itself, ahead of the exception handler, where a 401 would get no
// problem-details body.
app.UseAuthentication();
app.UseAuthorization();

// LEARN: Endpoints. WebApplication adds routing to the pipeline itself: it
// matches the route before the middleware above runs, and calls the matched
// endpoint after it. So these Map... calls only declare routes, and the
// handlers they name still run inside the exception handler. Each feature
// maps its own routes through an extension method, which keeps this file a
// table of contents rather than a list of handlers.
app.MapHealthEndpoints();
app.MapAppVersionEndpoints();
app.MapAccountEndpoints();
app.MapApiDocumentInDevelopment();

await app.RunAsync();
