using Billiards.Rules;
using NUnit.Framework;

namespace Billiards.Tests
{
    public class RulesEngineTests
    {
        static RulesEngine AfterLegalBreak()
        {
            var rules = new RulesEngine();
            var r = new ShotResult { FirstHitBall = 1 };
            r.Pocketed.Add(3);
            rules.Evaluate(r);
            return rules;
        }

        [Test]
        public void Break_WithPocketedBall_PlayerContinues()
        {
            var rules = new RulesEngine();
            var r = new ShotResult { FirstHitBall = 1 };
            r.Pocketed.Add(10);
            Assert.AreEqual(ShotOutcomeType.Continue, rules.Evaluate(r).Type);
            Assert.AreEqual(0, rules.CurrentPlayer);
            Assert.IsTrue(rules.TableOpen, "Groups are not assigned on the break.");
        }

        [Test]
        public void Break_WithFewerThanFourCushions_IsFoul()
        {
            var rules = new RulesEngine();
            var r = new ShotResult { FirstHitBall = 1 };
            r.BallsTouchingCushion.Add(2);
            Assert.AreEqual(ShotOutcomeType.Foul, rules.Evaluate(r).Type);
            Assert.AreEqual(1, rules.CurrentPlayer);
            Assert.IsTrue(rules.BallInHand);
        }

        [Test]
        public void Scratch_OnBreak_GivesKitchenBallInHand()
        {
            var rules = new RulesEngine();
            var r = new ShotResult { FirstHitBall = 1, CueBallPocketed = true };
            r.Pocketed.Add(0);
            Assert.AreEqual(ShotOutcomeType.Foul, rules.Evaluate(r).Type);
            Assert.IsTrue(rules.BallInHandKitchenOnly);
        }

        [Test]
        public void FirstPocketedBall_AssignsGroups()
        {
            var rules = AfterLegalBreak();
            var r = new ShotResult { FirstHitBall = 12, CushionAfterContact = true };
            r.Pocketed.Add(12);
            Assert.AreEqual(ShotOutcomeType.Continue, rules.Evaluate(r).Type);
            Assert.AreEqual(BallGroup.Stripes, rules.GetGroup(0));
            Assert.AreEqual(BallGroup.Solids, rules.GetGroup(1));
        }

        [Test]
        public void HittingOpponentBallFirst_IsFoul()
        {
            var rules = AfterLegalBreak();
            var assign = new ShotResult { FirstHitBall = 2 };
            assign.Pocketed.Add(2); // player 0 = solids
            rules.Evaluate(assign);

            var r = new ShotResult { FirstHitBall = 11, CushionAfterContact = true };
            Assert.AreEqual(ShotOutcomeType.Foul, rules.Evaluate(r).Type);
        }

        [Test]
        public void NoCushionAfterContact_IsFoul()
        {
            var rules = AfterLegalBreak();
            var r = new ShotResult { FirstHitBall = 5, CushionAfterContact = false };
            Assert.AreEqual(ShotOutcomeType.Foul, rules.Evaluate(r).Type);
        }

        [Test]
        public void EightBallEarly_Loses()
        {
            var rules = AfterLegalBreak();
            var r = new ShotResult { FirstHitBall = 8 };
            r.Pocketed.Add(8);
            Assert.AreEqual(ShotOutcomeType.Lose, rules.Evaluate(r).Type);
            Assert.AreEqual(1, rules.Winner);
        }

        [Test]
        public void EightBallAfterClearingGroup_Wins()
        {
            var rules = AfterLegalBreak();
            var assign = new ShotResult { FirstHitBall = 1 };
            assign.Pocketed.Add(1); // player 0 = solids
            rules.Evaluate(assign);
            for (int b = 2; b <= 7; b++)
            {
                if (!rules.IsOnTable(b)) continue;
                var s = new ShotResult { FirstHitBall = b };
                s.Pocketed.Add(b);
                Assert.AreEqual(ShotOutcomeType.Continue, rules.Evaluate(s).Type);
            }

            Assert.IsTrue(rules.IsLegalTarget(0, 8));
            var win = new ShotResult { FirstHitBall = 8 };
            win.Pocketed.Add(8);
            Assert.AreEqual(ShotOutcomeType.Win, rules.Evaluate(win).Type);
            Assert.AreEqual(0, rules.Winner);
        }

        [Test]
        public void Timeout_GivesBallInHandToOpponent()
        {
            var rules = AfterLegalBreak();
            Assert.AreEqual(ShotOutcomeType.Foul, rules.Timeout().Type);
            Assert.AreEqual(1, rules.CurrentPlayer);
            Assert.IsTrue(rules.BallInHand);
            Assert.IsFalse(rules.BallInHandKitchenOnly);
        }

        [Test]
        public void EightBallWithScratch_Loses()
        {
            var rules = AfterLegalBreak();
            var assign = new ShotResult { FirstHitBall = 1 };
            assign.Pocketed.Add(1);
            rules.Evaluate(assign);
            for (int b = 2; b <= 7; b++)
            {
                if (!rules.IsOnTable(b)) continue;
                var s = new ShotResult { FirstHitBall = b };
                s.Pocketed.Add(b);
                rules.Evaluate(s);
            }

            var r = new ShotResult { FirstHitBall = 8, CueBallPocketed = true };
            r.Pocketed.Add(8);
            r.Pocketed.Add(0);
            Assert.AreEqual(ShotOutcomeType.Lose, rules.Evaluate(r).Type);
        }
    
        static RulesEngine SolidsCleared()
        {
            var rules = AfterLegalBreak();
            var assign = new ShotResult { FirstHitBall = 1 };
            assign.Pocketed.Add(1); // player 0 = solids
            rules.Evaluate(assign);
            for (int b = 2; b <= 7; b++)
            {
                if (!rules.IsOnTable(b)) continue;
                var s = new ShotResult { FirstHitBall = b };
                s.Pocketed.Add(b);
                rules.Evaluate(s);
            }
            return rules;
        }

        [Test]
        public void EightBall_InCalledPocket_Wins()
        {
            var rules = SolidsCleared();
            Assert.IsTrue(rules.IsOnEight(0));
            var r = new ShotResult { FirstHitBall = 8, CalledPocket = 2, EightPocket = 2 };
            r.Pocketed.Add(8);
            Assert.AreEqual(ShotOutcomeType.Win, rules.Evaluate(r).Type);
            Assert.AreEqual(0, rules.Winner);
        }

        [Test]
        public void EightBall_InWrongPocket_Loses()
        {
            var rules = SolidsCleared();
            var r = new ShotResult { FirstHitBall = 8, CalledPocket = 2, EightPocket = 5 };
            r.Pocketed.Add(8);
            Assert.AreEqual(ShotOutcomeType.Lose, rules.Evaluate(r).Type);
            Assert.AreEqual(1, rules.Winner);
        }
    }
}
