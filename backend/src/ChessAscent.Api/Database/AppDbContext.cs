using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace ChessAscent.Api.Database;

// LEARN: A DbContext is EF Core's counterpart of a JPA EntityManager: a unit
// of work that tracks the entities it has loaded and writes their changes in
// one SaveChangesAsync() call, the way flush() does at commit. Two
// differences matter.
//
// Lifetime. AddDbContext registers it as Scoped, so each HTTP request gets
// its own instance, disposed when the request ends. That is what Spring's
// transaction-scoped persistence context gives you, but there is no proxy
// in between: the object you inject is the context itself.
//
// Thread safety. A DbContext is not thread-safe, and EF Core throws if a
// second operation starts on it before the first has finished, for example
// two queries awaited together with Task.WhenAll. Spring hides this by
// injecting a thread-safe proxy that finds the right EntityManager for the
// current transaction; here nothing hides it, so never share one instance
// between concurrent operations, and never inject it into a Singleton. In
// the Development environment the DI container refuses to build if a
// Singleton depends on a Scoped service.
//
// The application's own tables arrive as DbSet<T> properties when its first
// entities do; Identity's come from the base class (below). The
// constructor takes the options AddDbContext built (provider and connection
// string), the way an EntityManagerFactory is configured outside the
// EntityManager.
//
// LEARN: Deriving from IdentityUserContext<IdentityUser> instead of plain
// DbContext adds ASP.NET Core Identity's entities to this model: users
// (AspNetUsers) and their claims, external logins and tokens. Its
// OnModelCreating maps them, which is how a library contributes tables to an
// application's own context; with JPA the library would ship @Entity classes
// for the persistence unit to scan. Because they are part of the model,
// `dotnet ef migrations add` generates their tables like any other change,
// and the start-up migration creates them. IdentityDbContext would add role
// tables as well; the API has no roles.
public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityUserContext<IdentityUser>(options);
