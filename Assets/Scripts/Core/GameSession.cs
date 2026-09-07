using System;

namespace BlastGame.Core
{
    public enum GameState : byte
    {
        Playing = 0,
        Won = 1,
        Lost = 2
    }

    // What one tap did. A value rather than an event argument: the caller reads it on the same line it
    // asked for it, so there is nothing to keep and nothing to go stale.
    public readonly struct TurnResult
    {
        public readonly bool Played;      // false when the tap hit no blastable group
        public readonly bool Shuffled;    // the board had no legal move left and was rearranged
        public readonly GameState State;

        public TurnResult(bool played, bool shuffled, GameState state)
        {
            Played = played;
            Shuffled = shuffled;
            State = state;
        }

        public static TurnResult Rejected(GameState state) => new TurnResult(false, false, state);
    }

    // One playthrough of a level: the move counter, the score, the objective, and the order the checks
    // run in after a blast. Board stays the owner of the cells; this owns everything that is true about
    // the playthrough rather than about the board.
    public sealed class GameSession
    {
        private readonly Board board;

        private readonly int moveLimit;   // zero means unlimited

        // Counted from the board, not read from the config: BoxCount is a request that generation
        // clamps, so a level asking for 200 Boxes on a 10x10 board would have an unreachable objective.
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

        public int RemainingBoxes => board.RemainingBoxes();

        // A board without Boxes has no objective, which both of the case document's examples look like.
        // Not a second mode: the turn below runs the same either way, the objective is simply absent.
        public bool HasObjective => objectiveBoxes > 0;

        public bool HasMoveLimit => HasObjective && moveLimit > 0;

        public int MovesLeft => HasMoveLimit ? Math.Max(moveLimit - Moves, 0) : -1;

        public void Restart()
        {
            board.Generate();
            Begin();
        }

        // Adopts the board as it stands; producing a layout belongs to Board. That split is what lets
        // a test state an exact board - win-before-lose cannot be arranged on a rolled one.
        private void Begin()
        {
            // Handles a board born deadlocked too, so the opening needs no guarantee of its own.
            if (board.IsDeadlocked) board.TryResolveDeadlock();

            objectiveBoxes = board.RemainingBoxes();

            Moves = 0;
            Score = 0;
            State = GameState.Playing;
        }

        public TurnResult Play(int cellIndex)
        {
            if (State != GameState.Playing) return TurnResult.Rejected(State);

            // Blast, damage adjacent Boxes, gravity and refill, rebuild groups. The board is in its
            // final state the moment this returns.
            if (!board.TryBlast(cellIndex)) return TurnResult.Rejected(State);

            Score += ScoreFor(board.LastBlast.BlastedGroupSize);
            Moves++;

            // Win before loss: on the last move both "no Boxes left" and "no moves left" can be true,
            // and asking about the loss first would take the win away on the move that earned it.
            if (HasObjective && board.RemainingBoxes() == 0) return Finish(GameState.Won);
            if (HasMoveLimit && Moves >= moveLimit) return Finish(GameState.Lost);

            // Deadlock last: shuffling a finished board is an animation with nothing behind it.
            // A shuffle costs no move - the player neither caused it nor could avoid it.
            if (!board.IsDeadlocked) return new TurnResult(true, false, State);

            // Unsolvable is a precise condition, not a guess: no colour occurs twice, or no two
            // coloured cells are adjacent.
            if (!board.TryResolveDeadlock()) return Finish(GameState.Lost);

            return new TurnResult(true, true, State);
        }

        private TurnResult Finish(GameState state)
        {
            State = state;
            return new TurnResult(true, false, state);
        }

        // Quadratic, so one group of eight beats four groups of two. A linear score would pay the same
        // for careful play and for clearing pairs at random.
        private static int ScoreFor(int groupSize) => groupSize * groupSize;
    }
}
