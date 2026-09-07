using System;

namespace BlastGame.Core
{
    public enum GameState : byte
    {
        Playing = 0,
        Won = 1,
        Lost = 2
    }

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

    // One playthrough: the move counter, the score, the objective, and the order the checks run in
    // after a blast. Board owns the cells; this owns what is true about the playthrough.
    public sealed class GameSession
    {
        private readonly Board board;

        private readonly int moveLimit;   // zero means unlimited

        // Counted from the board rather than read from the config. BoxCount is a request that
        // generation clamps, so a level asking for 200 Boxes would have an unreachable objective.
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

        // A board without Boxes has no objective, which is what both case examples look like.
        public bool HasObjective => objectiveBoxes > 0;

        public bool HasMoveLimit => HasObjective && moveLimit > 0;

        public int MovesLeft => HasMoveLimit ? Math.Max(moveLimit - Moves, 0) : -1;

        public void Restart()
        {
            board.Generate();
            Begin();
        }

        // Adopts the board as it stands. Producing a layout belongs to Board, which is what lets a test
        // state an exact one - win-before-lose cannot be arranged on a rolled board.
        private void Begin()
        {
            objectiveBoxes = board.RemainingBoxes();

            Moves = 0;
            Score = 0;

            // A generated board always has a legal move, guaranteed by Board.Generate. A board stated
            // through LoadState need not, and the failure has to be read rather than dropped -
            // reporting Playing leaves the player tapping a board that can never answer.
            State = board.IsDeadlocked && !board.TryResolveDeadlock()
                ? GameState.Lost
                : GameState.Playing;
        }

        public TurnResult Play(int cellIndex)
        {
            if (State != GameState.Playing) return TurnResult.Rejected(State);

            if (!board.TryBlast(cellIndex)) return TurnResult.Rejected(State);

            Score += ScoreFor(board.LastBlast.BlastedGroupSize);
            Moves++;

            // Win before loss. On the last move both "no Boxes left" and "no moves left" can be true,
            // and asking about the loss first would take the win away on the move that earned it.
            if (HasObjective && board.RemainingBoxes() == 0) return Finish(GameState.Won);
            if (HasMoveLimit && Moves >= moveLimit) return Finish(GameState.Lost);

            // Deadlock last, because shuffling a finished board is an animation with nothing behind it.
            // A shuffle costs no move: the player neither caused it nor could avoid it.
            if (!board.IsDeadlocked) return new TurnResult(true, false, State);

            // Only reachable on a hand-authored board with no two adjacent coloured cells. Generated
            // boards always have a full top row, so the resolver always has somewhere to place a group.
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
