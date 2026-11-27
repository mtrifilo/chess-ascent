using System.Runtime.CompilerServices;

// The API tests check CommandWriter directly: the moment it decides between
// a write that never began and one cut off half-way is too brief to reach
// reliably through a real engine process.
[assembly: InternalsVisibleTo("ChessAscent.Api.Tests")]
