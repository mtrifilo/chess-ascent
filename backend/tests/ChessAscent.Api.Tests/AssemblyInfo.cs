using ChessAscent.Api.Tests;

// LEARN: An assembly fixture is created once for the whole test project and
// shared by every test class that asks for it in its constructor. Here that
// means one PostgreSQL container per `dotnet test` run, not one per class.
//
// Every class's ApiFactory uses the same database in that container, and
// xUnit runs test classes in parallel. A test that writes rows must not
// assume empty tables or a fixed row count: use keys of its own, or a
// database of its own. A test that has to stop the server starts its own
// PostgresFixture, as DatabaseReadinessTests does.
[assembly: AssemblyFixture(typeof(PostgresFixture))]
