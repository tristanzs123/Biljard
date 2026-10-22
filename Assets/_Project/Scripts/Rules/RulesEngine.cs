using System.Collections.Generic;

namespace Billiards.Rules
{
    /// <summary>Ball group a player shoots at.</summary>
    public enum BallGroup { None, Solids, Stripes }

    /// <summary>What happens after a shot.</summary>
    public enum ShotOutcomeType { Continue, SwitchTurn, Foul, Win, Lose }

    /// <summary>Everything that happened during one shot. Filled by the GameManager, read by the RulesEngine.</summary>
    public sealed class ShotResult
    {
        /// <summary>Number of the first object ball the cue ball touched, or -1 if none.</summary>
        public int FirstHitBall = -1;

        /// <summary>Numbers of pocketed balls in the order they dropped (0 = cue ball).</summary>
        public readonly List<int> Pocketed = new List<int>(16);

        /// <summary>Distinct object balls that touched a cushion (used for the break rule).</summary>
        public readonly HashSet<int> BallsTouchingCushion = new HashSet<int>();

        /// <summary>Any ball touched a cushion after the cue ball hit the first object ball.</summary>
        public bool CushionAfterContact;

        /// <summary>Cue ball dropped into a pocket.</summary>
        public bool CueBallPocketed;

        /// <summary>Numbers of balls that left the table (0 = cue ball).</summary>
        public readonly List<int> OffTable = new List<int>(4);

        /// <summary>Resets the result for a new shot.</summary>
        public void Clear()
        {
            FirstHitBall = -1;
            Pocketed.Clear();
            BallsTouchingCushion.Clear();
            CushionAfterContact = false;
            CueBallPocketed = false;
            OffTable.Clear();
        }
    }

    /// <summary>Result of rule evaluation.</summary>
    public readonly struct ShotOutcome
    {
        public readonly ShotOutcomeType Type;
        public readonly string Message;

        public ShotOutcome(ShotOutcomeType type, string message)
        {
            Type = type;
            Message = message;
        }
    }

    /// <summary>
    /// 8-ball rules (WPA, slightly simplified). Pure C#, no Unity dependencies, so it can be unit tested.
    /// Player 0 = human, player 1 = AI.
    /// </summary>
    public sealed class RulesEngine
    {
        public const int EightBall = 8;

        readonly BallGroup[] groups = new BallGroup[2];
        readonly HashSet<int> onTable = new HashSet<int>();

        /// <summary>Player whose turn it is (0 or 1).</summary>
        public int CurrentPlayer { get; private set; }

        /// <summary>True until the break shot has been evaluated.</summary>
        public bool IsBreak { get; private set; } = true;

        /// <summary>Incoming player may place the cue ball anywhere.</summary>
        public bool BallInHand { get; private set; }

        /// <summary>Ball in hand is restricted behind the head string (scratch on the break).</summary>
        public bool BallInHandKitchenOnly { get; private set; }

        /// <summary>Table is open (groups not yet assigned).</summary>
        public bool TableOpen => groups[0] == BallGroup.None;

        /// <summary>Winner (0/1) when the game is over, otherwise -1.</summary>
        public int Winner { get; private set; } = -1;

        /// <summary>Creates a new game with all 15 balls on the table.</summary>
        public RulesEngine(int startingPlayer = 0)
        {
            CurrentPlayer = startingPlayer;
            for (int i = 1; i <= 15; i++) onTable.Add(i);
        }

        /// <summary>Group of a ball number.</summary>
        public static BallGroup GroupOf(int ball)
        {
            if (ball >= 1 && ball <= 7) return BallGroup.Solids;
            if (ball >= 9 && ball <= 15) return BallGroup.Stripes;
            return BallGroup.None;
        }

        /// <summary>Group assigned to a player.</summary>
        public BallGroup GetGroup(int player) => groups[player];

        /// <summary>Is the ball still on the table.</summary>
        public bool IsOnTable(int ball) => onTable.Contains(ball);

        /// <summary>How many balls of the player's group are still on the table (7 while table is open).</summary>
        public int RemainingInGroup(int player)
        {
            BallGroup g = groups[player];
            if (g == BallGroup.None) return 7;
            int count = 0;
            foreach (int b in onTable)
                if (GroupOf(b) == g) count++;
            return count;
        }

        /// <summary>May the player legally hit this ball first.</summary>
        public bool IsLegalTarget(int player, int ball)
        {
            if (!onTable.Contains(ball)) return false;
            if (ball == EightBall) return groups[player] != BallGroup.None && RemainingInGroup(player) == 0;
            if (TableOpen) return true;
            return GroupOf(ball) == groups[player];
        }

        /// <summary>Evaluates a finished shot, updates the game state and returns the outcome.</summary>
        public ShotOutcome Evaluate(ShotResult r)
        {
            int player = CurrentPlayer;
            int opponent = 1 - player;
            bool wasBreak = IsBreak;
            bool groupClearedBefore = groups[player] != BallGroup.None && RemainingInGroup(player) == 0;
            bool tableOpenBefore = TableOpen;

            bool eightPocketed = r.Pocketed.Contains(EightBall);
            bool eightOff = r.OffTable.Contains(EightBall);

            string foul = FindFoul(r, player, wasBreak, groupClearedBefore, tableOpenBefore);

            foreach (int b in r.Pocketed) onTable.Remove(b);
            foreach (int b in r.OffTable) if (b != 0) onTable.Remove(b);

            IsBreak = false;
            BallInHand = false;
            BallInHandKitchenOnly = false;

            // --- black ball ---
            if (eightPocketed || eightOff)
            {
                bool win = !eightOff && foul == null && (wasBreak || groupClearedBefore);
                Winner = win ? player : opponent;
                return win
                    ? new ShotOutcome(ShotOutcomeType.Win, wasBreak ? "Črna na razbijanju - zmaga!" : "Črna v luknji - zmaga!")
                    : new ShotOutcome(ShotOutcomeType.Lose, foul != null ? "Črna s prekrškom - poraz!" : "Črna prezgodaj - poraz!");
            }

            // --- foul ---
            if (foul != null)
            {
                CurrentPlayer = opponent;
                BallInHand = true;
                BallInHandKitchenOnly = wasBreak && r.CueBallPocketed;
                return new ShotOutcome(ShotOutcomeType.Foul, "PREKRŠEK: " + foul);
            }

            // --- group assignment (not on the break) ---
            if (!wasBreak && tableOpenBefore)
            {
                foreach (int b in r.Pocketed)
                {
                    BallGroup g = GroupOf(b);
                    if (g == BallGroup.None) continue;
                    groups[player] = g;
                    groups[opponent] = g == BallGroup.Solids ? BallGroup.Stripes : BallGroup.Solids;
                    break;
                }
            }

            // --- continue? ---
            bool pocketedOwn = false;
            foreach (int b in r.Pocketed)
            {
                if (b == 0) continue;
                if (wasBreak || groups[player] == BallGroup.None || GroupOf(b) == groups[player])
                {
                    pocketedOwn = true;
                    break;
                }
            }

            if (pocketedOwn) return new ShotOutcome(ShotOutcomeType.Continue, "Nadaljuj");

            CurrentPlayer = opponent;
            return new ShotOutcome(ShotOutcomeType.SwitchTurn, "Menjava poteze");
        }

        /// <summary>The shot clock ran out: foul, the opponent gets ball in hand.</summary>
        public ShotOutcome Timeout()
        {
            CurrentPlayer = 1 - CurrentPlayer;
            BallInHand = true;
            BallInHandKitchenOnly = IsBreak;
            return new ShotOutcome(ShotOutcomeType.Foul, "PREKRŠEK: čas je potekel");
        }

        string FindFoul(ShotResult r, int player, bool isBreak, bool groupCleared, bool tableOpen)
        {
            if (r.CueBallPocketed) return "bela v luknji";
            if (r.OffTable.Count > 0) return "krogla je skočila z mize";
            if (r.FirstHitBall < 0) return "bela ni zadela nobene krogle";

            if (isBreak)
            {
                int objectPocketed = 0;
                foreach (int b in r.Pocketed) if (b != 0) objectPocketed++;
                if (objectPocketed == 0 && r.BallsTouchingCushion.Count < 4)
                    return "nelegalno razbijanje (manj kot 4 krogle na blazini)";
                return null;
            }

            int first = r.FirstHitBall;
            if (first == EightBall && !groupCleared) return "prva zadeta je črna";
            if (!tableOpen && first != EightBall && GroupOf(first) != groups[player]) return "prva zadeta ni tvoja krogla";
            if (groupCleared && first != EightBall) return "najprej moraš zadeti črno";

            if (r.Pocketed.Count == 0 && !r.CushionAfterContact) return "nobena krogla ni zadela blazine";
            return null;
        }
    }
}
