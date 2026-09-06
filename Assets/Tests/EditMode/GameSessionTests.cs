using BlastGame.Core;
using NUnit.Framework;

namespace BlastGame.Tests
{
    /// <summary>
    /// The end-of-turn rules: what counts as a move, when the level is won, when it is lost, and in
    /// which order those two questions are asked.
    /// </summary>
    /// <remarks>
    /// These run without Unity, which is the whole reason the turn flow lives in Core. The ordering
    /// below is a one-line difference in the source and a completely different game to the player, and
    /// nothing on screen would look wrong if it were reversed - exactly the kind of silent breakage the
    /// test plan is for.
    /// </remarks>
    [TestFixture]
    public class GameSessionTests
    {
        // --- Test 8: winning is checked before losing -------------------------------------------

        [Test]
        public void LastMoveBreaksLastBox_WinsRatherThanRunningOutOfMoves()
        {
            //  . . .     the four 1s are one group; the Box already took a hit, so this blast breaks it
            //  1 x .
            //  1 1 1
            var board = BoardBuilder.Parse(
                "...",
                "1x.",
                "111").ToBoard(colorCount: 1);

            // One move allowed, and this is it: after the blast "no Boxes left" and "no moves left" are
            // both true at the same instant.
            var session = new GameSession(board, moveLimit: 1);
            Assert.IsTrue(session.HasObjective, "a Box on the board is an objective");

            TurnResult turn = session.Play(0);

            Assert.IsTrue(turn.Played);
            Assert.AreEqual(0, session.RemainingBoxes, "the last Box broke");
            Assert.AreEqual(0, session.MovesLeft, "and the last move was spent doing it");
            Assert.AreEqual(GameState.Won, turn.State,
                "the player must not lose on the move that completed the objective");
        }

        [Test]
        public void RunningOutOfMovesWithABoxStillStanding_Loses()
        {
            //  Same board, but the Box is at full health: one blast only cracks it.
            var board = BoardBuilder.Parse(
                "...",
                "1#.",
                "111").ToBoard(colorCount: 1);

            var session = new GameSession(board, moveLimit: 1);

            TurnResult turn = session.Play(0);

            Assert.AreEqual(1, session.RemainingBoxes, "the Box survived");
            Assert.AreEqual(GameState.Lost, turn.State);
        }

        // --- The objective is data, not a second mode (Karar 7a) --------------------------------

        [Test]
        public void BoardWithoutBoxes_HasNoObjectiveAndNoMoveLimit()
        {
            var board = BoardBuilder.Parse(
                "111",
                "111",
                "111").ToBoard(colorCount: 1);

            // A move limit is configured and still must not apply: with nothing to break there is no
            // finish line for it to measure against. Both examples in the case document look like this.
            var session = new GameSession(board, moveLimit: 2);

            Assert.IsFalse(session.HasObjective);
            Assert.IsFalse(session.HasMoveLimit);
            Assert.AreEqual(-1, session.MovesLeft, "unlimited reports as -1, not as 0");

            for (int i = 0; i < 5; i++) session.Play(0);

            Assert.AreEqual(GameState.Playing, session.State, "a level with no objective cannot end");
            Assert.AreEqual(5, session.Moves, "moves are still counted, they just do not run out");
        }

        // --- What counts as a move --------------------------------------------------------------

        [Test]
        public void TapOnSomethingUnblastable_IsNotAMove()
        {
            //  . . .     the 2 in the bottom-right corner has no neighbour of its own colour
            //  1 1 #
            //  1 1 2
            var board = BoardBuilder.Parse(
                "...",
                "11#",
                "112").ToBoard(colorCount: 3);

            var session = new GameSession(board, moveLimit: 5);

            TurnResult turn = session.Play(board.Index(0, 2));

            Assert.IsFalse(turn.Played, "nothing happened");
            Assert.AreEqual(0, session.Moves, "so nothing was charged for it");
            Assert.AreEqual(0, session.Score);
        }

        [Test]
        public void TapsAfterTheLevelEnded_AreRejected()
        {
            var board = BoardBuilder.Parse(
                "...",
                "1x.",
                "111").ToBoard(colorCount: 1);

            var session = new GameSession(board, moveLimit: 1);
            Assert.AreEqual(GameState.Won, session.Play(0).State);

            int scoreAtWin = session.Score;
            TurnResult turn = session.Play(0);

            Assert.IsFalse(turn.Played);
            Assert.AreEqual(GameState.Won, turn.State, "a finished level stays finished");
            Assert.AreEqual(scoreAtWin, session.Score, "and cannot be farmed for points");
            Assert.AreEqual(1, session.Moves);
        }

        // --- Scoring ----------------------------------------------------------------------------

        [Test]
        public void BiggerGroupsPayMoreThanTheSameBlocksInSmallOnes()
        {
            //  One group of six.
            var wide = BoardBuilder.Parse(
                "...",
                "111",
                "111").ToBoard(colorCount: 1);

            var wideSession = new GameSession(wide, moveLimit: 0);
            wideSession.Play(wide.Index(0, 0));

            //  The same six blocks, split into three pairs by the empty column.
            var split = BoardBuilder.Parse(
                "1.2",
                "1.2",
                "33.").ToBoard(colorCount: 4);

            var splitSession = new GameSession(split, moveLimit: 0);
            splitSession.Play(split.Index(1, 0));

            Assert.AreEqual(36, wideSession.Score, "six in one group");
            Assert.AreEqual(4, splitSession.Score, "two in one group");
            Assert.Greater(wideSession.Score, splitSession.Score * 3,
                "clearing six blocks at once has to beat clearing them two at a time");
        }
    }
}
