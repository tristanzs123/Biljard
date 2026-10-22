using System;
using System.Collections;
using System.Collections.Generic;
using Billiards.Rules;
using UnityEngine;

namespace Billiards.AI
{
    /// <summary>
    /// AI opponent. Plans a shot with the ShotPlanner and plays it through the SAME CueController as the player
    /// (visible aiming, thinking, pull-back, strike). No physics cheating.
    /// </summary>
    public class AIPlayer : MonoBehaviour
    {
        [Tooltip("Cue controller on the cue ball (shared with the player).")]
        [SerializeField] CueController cue;

        [Tooltip("Time to rotate the stick toward the target (s).")]
        [SerializeField, Range(0.1f, 2f)] float aimTurnTime = 0.7f;

        [Tooltip("Time of the visible pull-back (s).")]
        [SerializeField, Range(0.1f, 2f)] float pullBackTime = 0.5f;

        /// <summary>Text describing what the AI is doing (for the HUD).</summary>
        public string Status { get; private set; } = "";

        /// <summary>Difficulty in use.</summary>
        public AIDifficultySettings Difficulty { get; set; }

        readonly ShotPlanner planner = new ShotPlanner();
        Ball cueBall;

        void Awake()
        {
            if (cue != null) cueBall = cue.GetComponent<Ball>();
            if (cue != null) planner.maxSpeed = cue.maxSpeed;
        }

        /// <summary>Sets the cue controller (used by the scene builder).</summary>
        public void SetCue(CueController controller) => cue = controller;

        /// <summary>Plays one AI turn.</summary>
        public void TakeTurn(RulesEngine rules, IReadOnlyList<Vector3> pockets, Action<Vector3> placeCueBall)
        {
            StopAllCoroutines();
            StartCoroutine(TurnRoutine(rules, pockets, placeCueBall));
        }

        IEnumerator TurnRoutine(RulesEngine rules, IReadOnlyList<Vector3> pockets, Action<Vector3> placeCueBall)
        {
            if (cueBall == null) cueBall = cue.GetComponent<Ball>();
            planner.maxSpeed = cue.maxSpeed;
            AIDifficultySettings s = Difficulty;
            Predicate<int> legal = n => rules.IsBreak || rules.IsLegalTarget(1, n);

            Status = "AI razmišlja...";
            yield return new WaitForSeconds(0.6f);

            if (rules.BallInHand)
            {
                Status = "AI postavlja belo kroglo";
                bool wasActive = cueBall.gameObject.activeSelf;
                if (wasActive) cueBall.Collider.enabled = false;
                Vector3 pos = planner.FindBestCuePosition(cueBall, Ball.All, legal, pockets, s, rules.BallInHandKitchenOnly);
                if (wasActive) cueBall.Collider.enabled = true;
                placeCueBall(pos);
                yield return new WaitForSeconds(0.5f);
            }

            PlannedShot shot = planner.Plan(cueBall.Body.position, cueBall.Collider, Ball.All, legal, pockets, s, rules.IsBreak, true);
            if (!shot.valid)
            {
                // nothing found at all: hit toward the table centre
                shot = new PlannedShot { valid = true, direction = (Vector3.zero - cueBall.Body.position).normalized, power01 = 0.5f };
            }

            // human error
            float angle = ShotPlanner.Gaussian(s.angleErrorDeg);
            Vector3 dir = Quaternion.AngleAxis(angle, Vector3.up) * shot.direction;
            float power = Mathf.Clamp01(shot.power01 * (1f + UnityEngine.Random.Range(-s.powerErrorPercent, s.powerErrorPercent) / 100f));

            Status = shot.isSafety ? "AI igra obrambno" : shot.target != null ? $"AI cilja kroglo {shot.target.number}" : "AI cilja";

            // turn the stick toward the target
            Vector3 startDir = cue.AimDirection;
            for (float t = 0f; t < aimTurnTime; t += Time.deltaTime)
            {
                float e = Mathf.SmoothStep(0f, 1f, t / aimTurnTime);
                cue.ShowAim(Vector3.Slerp(startDir, dir, e), 0f);
                yield return null;
            }
            cue.ShowAim(dir, 0f);

            // think
            yield return new WaitForSeconds(UnityEngine.Random.Range(s.thinkTime.x, s.thinkTime.y));

            // pull back
            for (float t = 0f; t < pullBackTime; t += Time.deltaTime)
            {
                cue.ShowAim(dir, Mathf.Lerp(0f, power, t / pullBackTime));
                yield return null;
            }

            Status = "";
            Debug.Log($"[Biljard] AI strel: cilj={(shot.target != null ? shot.target.number : -1)}, moč={power:0.00}, safety={shot.isSafety}, ocena={shot.score:0.00}");
            cue.TakeShot(dir, power);
        }
    }
}
