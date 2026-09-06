namespace BlastGame.Core
{
    /// <summary>
    /// What occupies a cell.
    /// </summary>
    /// <remarks>
    /// Backed by <c>byte</c> because C# defaults enums to <c>int</c>, which would make
    /// <see cref="Cell"/> six bytes instead of three for the sake of three possible values.
    /// <para>
    /// Empty is deliberately 0: a freshly allocated <c>Cell[]</c> is therefore already a valid
    /// empty board, and <c>Array.Clear</c> is a valid "wipe the board" operation. Neither needs
    /// an initialisation pass.
    /// </para>
    /// </remarks>
    public enum CellType : byte
    {
        Empty = 0,
        Color = 1,
        Box = 2
    }

    /// <summary>
    /// A single board cell. Three bytes, stored by value in the board's flat array, so the whole
    /// 10x10 board is one ~300 byte allocation that fits comfortably in L1.
    /// </summary>
    /// <remarks>
    /// <b>Emptiness is a type, not a sentinel colour.</b> Encoding "empty" as e.g. <c>Color = 255</c>
    /// would put an invalid state in the same field as valid data, and it breaks outright on Box,
    /// which has no colour at all: one byte would have to carry three meanings.
    /// <para>
    /// <b>Copy trap.</b> This is a mutable struct in an array. Reading it into a local gives a copy:
    /// <code>
    /// var c = cells[i]; c.Color = 2;   // WRONG - mutates the copy, the array is unchanged
    /// cells[i].Color = 2;              // RIGHT - mutates the element in place
    /// </code>
    /// Instance methods are exempt: <c>cells[i].Damage()</c> operates on the element itself,
    /// because indexing an array yields a reference rather than a value.
    /// </para>
    /// <para>
    /// Not a <c>readonly struct</c>: gravity, blasting and box damage all mutate cells in place,
    /// and copying three bytes is cheaper than the alternative anyway.
    /// </para>
    /// </remarks>
    public struct Cell
    {
        /// <summary>Health a Box starts with, per the case document.</summary>
        public const byte BoxMaxHealth = 2;

        public CellType Type;

        /// <summary>Colour index in <c>[0, ColorCount)</c>. Meaningful only when <see cref="Type"/> is Color.</summary>
        public byte Color;

        /// <summary>Remaining hit points. Meaningful only when <see cref="Type"/> is Box.</summary>
        public byte Health;

        public bool IsEmpty => Type == CellType.Empty;
        public bool IsColor => Type == CellType.Color;
        public bool IsBox => Type == CellType.Box;

        /// <summary>An empty cell. Identical to <c>default(Cell)</c>; named for readability at call sites.</summary>
        public static readonly Cell Empty = default;

        // Factories rather than a constructor, because a constructor taking all three fields would
        // permit combinations that cannot occur - a Box with a colour, a coloured cell with health.
        // These two are the only shapes a non-empty cell can legally have.

        public static Cell MakeColor(byte color) => new Cell { Type = CellType.Color, Color = color };

        public static Cell MakeBox(byte health = BoxMaxHealth) => new Cell { Type = CellType.Box, Health = health };

        /// <summary>
        /// Compact form for test failure messages and debug dumps: <c>C3</c> a colour, <c>B2</c> a
        /// Box with two health, <c>.</c> empty. Chosen to line up in a grid when a whole board is printed.
        /// </summary>
        public override string ToString()
        {
            switch (Type)
            {
                case CellType.Color: return "C" + Color;
                case CellType.Box: return "B" + Health;
                default: return ".";
            }
        }
    }
}