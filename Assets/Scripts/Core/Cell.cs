namespace BlastGame.Core
{
    // byte-backed: an int enum would make Cell six bytes instead of three.
    // Empty = 0, so a fresh Cell[] is already a valid empty board.
    public enum CellType : byte
    {
        Empty = 0,
        Color = 1,
        Box = 2
    }

    // Mutable struct in an array. Copy trap:
    //   var c = cells[i]; c.Color = 2;   // wrong, mutates the copy
    //   cells[i].Color = 2;              // right
    public struct Cell
    {
        public const byte BoxMaxHealth = 2;

        public CellType Type;
        public byte Color;    // only meaningful when Type is Color
        public byte Health;   // only meaningful when Type is Box

        public bool IsEmpty => Type == CellType.Empty;
        public bool IsColor => Type == CellType.Color;
        public bool IsBox => Type == CellType.Box;

        public static readonly Cell Empty = default;

        // Factories, not a constructor: a three-field constructor would allow a coloured Box.
        public static Cell MakeColor(byte color) => new Cell { Type = CellType.Color, Color = color };

        public static Cell MakeBox(byte health = BoxMaxHealth) => new Cell { Type = CellType.Box, Health = health };

        // C3 a colour, B2 a Box with two health, . empty. Debug dumps only.
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
