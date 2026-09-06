using System;

namespace BlastGame.Core
{
    /// <summary>How a level ended, or that it has not.</summary>
    public enum GameState : byte
    {
        Playing = 0,
        Won = 1,
        Lost = 2
    }

    /// <summary>What one tap did, in the form the presentation layer needs.</summary>
    /// <remarks>
    /// A value rather than an event argument: the caller reads it on the same line it asked for it, so
    /// there is nothing to keep and nothing to go stale.
    /// </remarks>
    public readonly struct TurnResult
    {
        /// <summary>False when the tap hit no blastable group. Nothing changed; there is nothing to draw.</summary>
        public readonly bool Played;

        /// <summary>True when the board had no legal move left and was shuffled at the end of this turn.</summary>
        public readonly bool Shuffled;

        /// <summary>State after the turn.</summary>
        public readonly GameState State;

        public TurnResult(bool played, bool shuffled, GameState state)
        {
            Played = played;
            Shuffled = shuffled;
            State = state;
        }

        public static TurnResult Rejected(GameState state) => new TurnResult(false, false, state);
    }

    /// <summary>
    /// One playthrough of a level: the move counter, the score, the objective, and the order the checks
    /// run in after a blast.
    /// </summary>
    /// <remarks>
    /// <b>Engine-free like the rest of Core, and that is the point.</b> Whether the player has won is a
    /// rule, not a rendering concern, so it lives where it can be asserted without opening Unity - which
    /// is exactly what the "win before lose" test needs (test plan, item 8).
    /// <para>
    /// <see cref="Board"/> stays the owner of the cells; this owns everything that is true about the
    /// <i>playthrough</i> rather than about the board. Splitting them that way is why restarting is a
    /// method here and not a special case everywhere else.
    /// </para>
    /// </remarks>
    public sealed class GameSession
    {
        private readonly Board board;

        /// <summary>Zero means unlimited. Only consulted when there is an objective to run out of moves against.</summary>
        private readonly int moveLimit;

        /// <summary>
        /// Boxes the level started with, counted from the board rather than read from the config.
        /// </summary>
        /// <remarks>
        /// <c>BoxCount</c> is a request that generation clamps to what the board can hold, so a level
        /// asking for 200 Boxes on a 10x10 board would otherwise have an objective it can never meet
        /// (DECISIONS.md, Karar 20).
        /// </remarks>
        private int objectiveBoxes;

        public GameSession(Board board, int moveLimit)
        {
            this.board = board ?? throw new ArgumentNullException(nameof(board));

            if (moveLimit < 0)
                throw new ArgumentOutOfRangeException(nameof(moveLimit), moveLimit, "Move limit cannot be negative.");

            this.moveLimit = moveLimit;

            Begin();
        }

        public Board Board => board;

        public int Moves { get; private set; }
        public int Score { get; private set; }
        public GameState State { get; private set; }

        /// <summary>Boxes still standing. What the objective is measured against.</summary>
        public int RemainingBoxes => board.RemainingBoxes();

        /// <summary>
        /// Whether this level can be won or lost at all.
        /// </summary>
        /// <remarks>
        /// A board generated without Boxes has nothing to break, so it has no objective, and a move limit
        /// on a level that cannot be completed would only be a timer with no finish line. Both of the
        /// case document's own examples have no Boxes, so this is a legitimate level rather than a
        /// degenerate one - and it is <b>not a second mode</b>: the turn below runs the same either way,
        /// the objective is simply data that is absent (Karar 7a).
        /// </remarks>
        public bool HasObjective => objectiveBoxes > 0;

        public bool HasMoveLimit => HasObjective && moveLimit > 0;

        /// <summary>Moves still available, or -1 when they are unlimited.</summary>
        public int MovesLeft => HasMoveLimit ? Math.Max(moveLimit - Moves, 0) : -1;

        /// <summary>Regenerates the board and starts the playthrough over on it.</summary>
        public void Restart()
        {
            board.Generate();
            Begin();
        }

        /// <summary>
        /// Adopts the board as it currently stands and resets the counters.
        /// </summary>
        /// <remarks>
        /// <b>This does not generate a board.</b> Producing a layout belongs to <see cref="Board"/>;
        /// this class only plays one. Keeping the two apart is what lets a test state an exact board and
        /// then ask what the rules do with it - the win-before-lose case cannot be arranged at all on a
        /// randomly generated board.
        /// <para>
        /// Resolving a board that is born without a legal move happens here rather than in the turn: the
        /// same two calls handle it, so the opening needs no guarantee of its own and the shuffle path is
        /// exercised by ordinary play instead of being code that only runs on a rare board (Karar 16).
        /// </para>
        /// </remarks>
        private void Begin()
        {
            if (board.IsDeadlocked) board.TryResolveDeadlock();

            objectiveBoxes = board.RemainingBoxes();

            Moves = 0;
            Score = 0;
            State = GameState.Playing;
        }

        /// <summary>
        /// Plays one tap: blasts, then runs the end-of-turn checks in the order they have to run in.
        /// </summary>
        /// <remarks>
        /// <b>Winning is checked before losing.</b> On the last move the player can break the last Box,
        /// and at that instant "no Boxes left" and "no moves left" are both true. Asking about the loss
        /// first would take the win away on the move that earned it.
        /// <para>
        /// <b>The deadlock check runs last.</b> A board that has just been won or lost is not going to be
        /// played again, so shuffling it would be an animation with nothing behind it.
        /// </para>
        /// <para>
        /// <b>A shuffle does not consume a move.</b> The player did not cause the deadlock and cannot
        /// avoid it, so charging them for it would be a penalty for the generator's luck (Karar 7b).
        /// </para>
        /// </remarks>
        public TurnResult Play(int cellIndex)
        {
            if (State != GameState.Playing) return TurnResult.Rejected(State);

            // Steps 1-4 of the move flow happen inside here: blast, damage adjacent Boxes, gravity and
            // refill, rebuild groups. The board is in its final state the moment this returns.
            if (!board.TryBlast(cellIndex)) return TurnResult.Rejected(State);

            Score += ScoreFor(board.LastBlast.BlastedGroupSize);
            Moves++;

            if (HasObjective && board.RemainingBoxes() == 0) return Finish(GameState.Won);
            if (HasMoveLimit && Moves >= moveLimit) return Finish(GameState.Lost);

            if (!board.IsDeadlocked) return new TurnResult(true, false, State);

            // Unsolvable is a precise condition, not a guess: no colour occurs twice, or no two coloured
            // cells are adjacent. Either way no rearrangement can produce a group (Karar 8/9).
            if (!board.TryResolveDeadlock()) return Finish(GameState.Lost);

            return new TurnResult(true, true, State);
        }

        private TurnResult Finish(GameState state)
        {
            State = state;
            return new TurnResult(true, false, state);
        }

        /// <summary>
        /// Points for a blasted group. Quadratic, so one group of eight is worth four groups of two.
        /// </summary>
        /// <remarks>
        /// A linear score would pay the same for careful play and for clearing pairs at random, which
        /// would make the icon tiers decoration. This is the only place the game says that bigger groups
        /// are the point.
        /// </remarks>
        private static int ScoreFor(int groupSize) => groupSize * groupSize;
    }
}
