using System.Diagnostics.CodeAnalysis;

namespace ChessAscent.Chess;

// LEARN: A `record struct` is a record that is a value type. Like a Java
// record it gets value equality, a readable ToString and an immutable shape,
// but it is copied on assignment and lives inline (on the stack, or inside
// the object that holds it) instead of being a heap object behind a
// reference. Java has no user-defined value types, so the nearest thing is a
// primitive. `readonly` makes every field immutable, which matters because
// mutating a copy of a struct silently changes nothing. Keep such types small:
// this one is two ints.
//
// LEARN: Every struct has a parameterless default value with all fields zero,
// which callers can create with `default(Square)` without calling any
// constructor. Here that is a1, a real square, which is why the type stores a
// file and rank from zero rather than the name.
/// <summary>A square on the board, such as <c>e4</c>.</summary>
public readonly record struct Square
{
    private Square(int file, int rank)
    {
        File = file;
        Rank = rank;
    }

    /// <summary>The file from 0 (the a-file) to 7 (the h-file).</summary>
    public int File { get; }

    /// <summary>The rank from 0 (White's back rank) to 7 (Black's back rank).</summary>
    public int Rank { get; }

    // LEARN: `out` hands a second value back through a parameter that the
    // method must assign before it returns; Java has no equivalent and would
    // return an Optional or a holder object. The Try pattern (bool plus an
    // `out` result) is the standard .NET way to report an expected failure
    // without an exception, as in int.TryParse.
    //
    // LEARN: `string?` says the argument may be null. With nullable reference
    // types on, a plain `string` is a promise of non-null that the compiler
    // checks at every call site in this solution, though not at run time.
    // [NotNullWhen(true)] tells the compiler that after a `true` return the
    // argument was not null, so a caller can use it without another check.
    /// <summary>Reads a lowercase square name from <c>a1</c> to <c>h8</c>.</summary>
    /// <returns><c>false</c> for anything else, including upper case and surrounding spaces.</returns>
    public static bool TryParse([NotNullWhen(true)] string? text, out Square square)
    {
        if (text is [var file and >= 'a' and <= 'h', var rank and >= '1' and <= '8'])
        {
            square = new Square(file - 'a', rank - '1');
            return true;
        }

        square = default;
        return false;
    }

    /// <summary>The square's name, such as <c>e4</c>.</summary>
    public override string ToString() => $"{(char)('a' + File)}{(char)('1' + Rank)}";
}
