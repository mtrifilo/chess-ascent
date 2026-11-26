using ChessAscent.Api.Tests;

// LEARN: An assembly fixture is created once for the whole test project and
// shared by every test class that asks for it in its constructor. Here that
// means one PostgreSQL container per `dotnet test` run, not one per class.
[assembly: AssemblyFixture(typeof(PostgresFixture))]
