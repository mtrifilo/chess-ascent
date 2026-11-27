using ChessAscent.Api.ApiDocument;
using ChessAscent.Api.Database;
using ChessAscent.Api.Features.AppVersion;
using ChessAscent.Api.Features.Health;

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
// check that the database answers. AddApiDocument registers the OpenAPI
// document that describes the endpoints below.
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();
builder.Services.AddDatabase();
builder.Services.AddApiDocument();

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
// as the 404 for an unknown path, gets a problem-details body here, so every
// error the API returns has the same shape.
app.UseStatusCodePages();

// LEARN: Endpoints. WebApplication adds routing to the pipeline itself: it
// matches the route before the middleware above runs, and calls the matched
// endpoint after it. So these Map... calls only declare routes, and the
// handlers they name still run inside the exception handler. Each feature
// maps its own routes through an extension method, which keeps this file a
// table of contents rather than a list of handlers.
app.MapHealthEndpoints();
app.MapAppVersionEndpoints();
app.MapApiDocumentInDevelopment();

await app.RunAsync();
