using ChessAscent.Api.Database;
using Microsoft.AspNetCore.Identity;

namespace ChessAscent.Api.Features.Accounts;

public static class AccountServices
{
    /// <summary>
    /// Registers ASP.NET Core Identity for email-and-password accounts stored
    /// in <see cref="AppDbContext"/>, the session cookie that keeps a player
    /// signed in, and authorization.
    /// </summary>
    public static IServiceCollection AddAccounts(this IServiceCollection services)
    {
        // LEARN: ASP.NET Core Identity is the membership library: it stores
        // accounts, hashes and checks passwords, and counts failed sign-ins
        // towards a lockout. Spring Security has no single counterpart. The
        // nearest are a UserDetailsManager (accounts in a store), a
        // PasswordEncoder (hashing) and the AuthenticationManager (checking a
        // sign-in), which Spring wires for you. Here the work is split between
        // two services the endpoints inject:
        //   - UserManager<TUser> creates accounts, validates passwords and
        //     emails, hashes passwords (PBKDF2 with HMAC-SHA512) and finds
        //     users. It talks to storage only through an IUserStore, which
        //     AddEntityFrameworkStores implements on AppDbContext.
        //   - SignInManager<TUser> checks a password against the stored hash,
        //     applies the lockout rules, and on success issues the
        //     authentication cookie described below.
        // AddIdentityCore registers only those parts. AddIdentity would also
        // add roles, external-login cookies and the Razor Pages redirects a
        // server-rendered site needs; this is a JSON API.
        //
        // Every option left out below keeps Identity's default. For
        // passwords: at least 6 characters, at least 1 distinct character, and
        // at least one each of a lower-case ASCII letter, an upper-case ASCII
        // letter, a digit and a character that is none of those. For lockout:
        // on for new accounts, after 5 failed sign-ins in a row, for 5
        // minutes. For user names, which here are emails: only the characters
        // a-z, A-Z, 0-9 and - . _ @ +, so an address such as o'neill@... or
        // one with a non-ASCII letter cannot register. Change them here, on
        // options.Password, options.Lockout and
        // options.User.AllowedUserNameCharacters.
        //
        // The lockout limits guessing one attempt after another, not guessing
        // in parallel. Each failed attempt saves the new count with
        // Identity's concurrency check, so of many wrong passwords sent at the
        // same moment only one is counted (the rest fail their save and are
        // answered as wrong), and a right password among them still signs in.
        // A cap on concurrent sign-ins per account would close that; it is
        // rate limiting, which this API does not have yet.
        //
        // It also cuts both ways: anyone who knows an account's email can
        // keep it locked by sending five wrong passwords every five minutes,
        // and the player then sees the same "incorrect" answer as for a wrong
        // password (see AccountEndpoints for why).
        services.AddIdentityCore<IdentityUser>(options =>
            {
                // The email is also the user name (see AccountEndpoints), and
                // Identity compares both through their normalized (upper-case)
                // forms, so "Ann@Example.com" and "ann@example.com" are the
                // same account. This line makes Identity validate the email
                // and refuse a second account with it.
                options.User.RequireUniqueEmail = true;

                // No email is sent yet, so no account could ever confirm its
                // address. These are Identity's defaults, set here so that
                // nobody turns them on before confirmation exists.
                options.SignIn.RequireConfirmedEmail = false;
                options.SignIn.RequireConfirmedAccount = false;
            })
            .AddEntityFrameworkStores<AppDbContext>()
            .AddSignInManager();

        // LEARN: An authentication scheme is a name paired with a handler
        // that knows one way of proving who the caller is: a cookie, a bearer
        // token, a client certificate. An application can register several
        // and choose per endpoint; the one passed to AddAuthentication is the
        // default, used to authenticate every request and to answer when an
        // endpoint refuses one. Spring Security's closest idea is the
        // authentication mechanism a SecurityFilterChain is configured with
        // (formLogin, httpBasic, oauth2ResourceServer).
        //
        // AddApplicationCookie adds the cookie handler under Identity's
        // scheme name, which is what SignInManager signs in to. Unlike a
        // JSESSIONID, the cookie is not a key to server-side session state:
        // it carries the signed-in user's claims themselves, encrypted and
        // signed with ASP.NET Core Data Protection keys, so no session table
        // exists. It also re-reads the account every 30 minutes and rejects
        // the cookie if the account's security stamp has changed.
        //
        // Two consequences follow. Nothing on the server ends a session, so
        // signing out deletes the cookie from the browser that signs out, and
        // a copy of it taken earlier stays valid until it expires (see
        // SignOutAsync in AccountEndpoints). And the cookie can be read only
        // with the key ring it was written with. Data Protection's defaults
        // keep that ring in the user profile of the machine that runs the
        // API (on macOS and Linux, ~/.aspnet/DataProtection-Keys), which
        // persists in development. A deployment has to keep the ring durable,
        // private and shared between instances, or every restart signs every
        // player out, and anyone who reads it can forge a session.
        //
        // LEARN: The options pattern. AddApplicationCookie returns an
        // OptionsBuilder<CookieAuthenticationOptions>, and Configure adds a
        // callback that the container runs the first time the options are
        // read, after the callbacks registered before it (here, Identity's
        // own). The type parameter asks the container for a service to pass
        // in, the way a Spring customizer bean receives its dependencies.
        // Code that needs the settings injects IOptions<T>, or here
        // IOptionsMonitor<T>.Get(scheme), because each scheme's options are
        // a named instance. It is close to @ConfigurationProperties plus
        // customizer beans, except that the values are built on demand
        // rather than bound at start-up.
        services.AddAuthentication(IdentityConstants.ApplicationScheme)
            .AddApplicationCookie()
            .Configure<IHostEnvironment>((options, environment) =>
            {
                // Already the cookie handler's defaults; written out because
                // the session's safety depends on them: the page's script
                // cannot read the cookie, and browsers hold it back from
                // most requests that other sites start.
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;

                // Development serves plain HTTP on localhost, where a Secure
                // cookie would never be sent back. Everywhere else the cookie
                // is marked Secure whatever the request's scheme, so a TLS
                // proxy in front of the API cannot leave it off.
                options.Cookie.SecurePolicy = environment.IsDevelopment()
                    ? CookieSecurePolicy.SameAsRequest
                    : CookieSecurePolicy.Always;

                // Cookie.Domain stays unset, so the browser sends the cookie
                // only to the host that set it. ExpireTimeSpan (14 days) and
                // SlidingExpiration (on) keep their defaults: the sign-in
                // endpoints issue a persistent cookie, which a browser keeps
                // across restarts, and every request in the second half of
                // the 14 days renews it for another 14.

                // The handler's defaults suit a server-rendered site: it
                // answers an unauthenticated request with a redirect to a
                // sign-in page, and a forbidden one with a redirect to an
                // access-denied page. An API answers with the status code
                // instead, and UseStatusCodePages in Program.cs gives the
                // empty response a problem-details body.
                options.Events.OnRedirectToLogin = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                };
                options.Events.OnRedirectToAccessDenied = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };
            });

        services.AddAuthorization();

        return services;
    }
}
