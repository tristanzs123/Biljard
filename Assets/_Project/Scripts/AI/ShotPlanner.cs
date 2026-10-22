using System;
using System.Collections.Generic;
using UnityEngine;

namespace Billiards.AI
{
    /// <summary>A planned shot.</summary>
    public struct PlannedShot
    {
        public bool valid;
        public Vector3 direction;
        public float power01;
        public float score;
        public Ball target;
        public bool isSafety;
        public Vector3 aimPoint;
    }

    /// <summary>
    /// Finds and scores shots with the ghost-ball method:
    /// ghost = target - dirToPocket * ballDiameter. Paths are checked with SphereCasts.
    /// Plain C# so the math can be tested; uses UnityEngine.Physics for queries.
    /// </summary>
    public sealed class ShotPlanner
    {
        const float R = Ball.Radius;
        const float D = 2f * R;

        /// <summary>Maximum cut angle that is still considered (degrees).</summary>
        public float maxCutAngle = 75f;

        /// <summary>Approximate deceleration used to estimate the needed power (m/s²).</summary>
        public float effectiveDeceleration = 0.45f;

        /// <summary>Speed the object ball should still have at the pocket (m/s).</summary>
        public float pocketEntrySpeed = 0.6f;

        /// <summary>Maximum cue ball speed of the CueController (m/s).</summary>
        public float maxSpeed = 7f;

        /// <summary>Half size of the playing surface (x = length/2, y = width/2).</summary>
        public Vector2 tableHalfSize = new Vector2(1.27f, 0.635f);

        readonly RaycastHit[] hits = new RaycastHit[16];
        readonly List<PlannedShot> candidates = new List<PlannedShot>(64);

        /// <summary>Ghost ball position: where the cue ball must be at contact to send the target toward aimPoint.</summary>
        public static Vector3 GhostBall(Vector3 target, Vector3 aimPoint)
        {
            Vector3 dir = aimPoint - target;
            dir.y = 0f;
            return target - dir.normalized * D;
        }

        /// <summary>Cut angle in degrees between the cue path and the object ball path.</summary>
        public static float CutAngle(Vector3 cuePos, Vector3 ghost, Vector3 target, Vector3 aimPoint)
        {
            Vector3 a = ghost - cuePos; a.y = 0f;
            Vector3 b = aimPoint - target; b.y = 0f;
            return Vector3.Angle(a, b);
        }

        /// <summary>Estimated initial speed needed to cover a distance and still have endSpeed.</summary>
        public float SpeedForDistance(float distance, float endSpeed)
        {
            return Mathf.Sqrt(endSpeed * endSpeed + 2f * effectiveDeceleration * distance);
        }

        /// <summary>Finds the best shot from cuePos. Returns an invalid shot if nothing is possible.</summary>
        public PlannedShot Plan(Vector3 cuePos, Collider cueCollider, IReadOnlyList<Ball> balls, Predicate<int> isLegal,
                                IReadOnlyList<Vector3> pockets, AIDifficultySettings settings, bool isBreak, bool includeBadDecision)
        {
            if (isBreak) return PlanBreak(cuePos, balls);

            candidates.Clear();
            for (int i = 0; i < balls.Count; i++)
            {
                Ball target = balls[i];
                if (target.isCueBall || !target.gameObject.activeInHierarchy || !isLegal(target.number)) continue;

                for (int p = 0; p < pockets.Count; p++)
                {
                    if (TryEvaluate(cuePos, cueCollider, target, pockets[p], p, balls, isLegal, settings, out PlannedShot shot))
                        candidates.Add(shot);
                }
            }

            PlannedShot best = default;
            for (int i = 0; i < candidates.Count; i++)
                if (!best.valid || candidates[i].score > best.score) best = candidates[i];

            if (includeBadDecision && candidates.Count > 1 && UnityEngine.Random.value < settings.badDecisionChance)
                best = candidates[UnityEngine.Random.Range(0, candidates.Count)];

            if (best.valid && (best.score >= settings.minPotScore || !settings.playSafety)) return best;

            PlannedShot safety = PlanSafety(cuePos, cueCollider, balls, isLegal);
            if (safety.valid) return safety;
            return best.valid ? best : PlanAnyContact(cuePos, balls, isLegal);
        }

        bool TryEvaluate(Vector3 cuePos, Collider cueCollider, Ball target, Vector3 pocket, int pocketIndex,
                         IReadOnlyList<Ball> balls, Predicate<int> isLegal, AIDifficultySettings settings, out PlannedShot shot)
        {
            shot = default;
            Vector3 t = target.Body.position;
            pocket.y = t.y;
            Vector3 ghost = GhostBall(t, pocket);
            ghost.y = cuePos.y;

            float cut = CutAngle(cuePos, ghost, t, pocket);
            if (cut > maxCutAngle) return false;

            Vector3 toPocket = pocket - t; toPocket.y = 0f;
            float dObj = toPocket.magnitude;
            Vector3 objDir = toPocket / dObj;

            // pocket entry angle: side pockets need a fairly straight approach
            bool side = Mathf.Abs(pocket.x) < 0.2f;
            float entryPenalty;
            if (side)
            {
                float straight = Mathf.Abs(objDir.z);
                if (straight < 0.45f) return false;
                entryPenalty = (1f - straight) * 0.4f + 0.05f;
            }
            else
            {
                Vector3 diag = new Vector3(Mathf.Sign(pocket.x), 0f, Mathf.Sign(pocket.z)).normalized;
                float straight = Vector3.Dot(objDir, diag);
                if (straight < 0.35f) return false;
                entryPenalty = (1f - straight) * 0.25f;
            }

            Vector3 cueToGhost = ghost - cuePos; cueToGhost.y = 0f;
            float dCue = cueToGhost.magnitude;
            if (dCue < 0.001f) return false;
            Vector3 cueDir = cueToGhost / dCue;

            if (!PathClear(cuePos, cueDir, dCue - 0.003f, cueCollider, target.Collider)) return false;
            if (!PathClear(t, objDir, Mathf.Max(0f, dObj - 0.06f), target.Collider, null)) return false;

            // power estimate
            float cos = Mathf.Cos(cut * Mathf.Deg2Rad);
            float vObj = SpeedForDistance(dObj, pocketEntrySpeed);
            float vGhost = vObj / Mathf.Max(0.2f, cos * 0.93f);
            float vCue = SpeedForDistance(dCue, vGhost) * 1.12f;
            float power = Mathf.Clamp(vCue / maxSpeed, 0.12f, 1f);

            float score = 1f
                          - 0.55f * (cut / maxCutAngle)
                          - 0.10f * (dCue + dObj)
                          - entryPenalty
                          - (power > 0.9f ? 0.15f : 0f);

            // straight shot into a corner with power = scratch risk
            if (cut < 5f && power > 0.5f) score -= 0.12f * power;

            if (settings.usePosition)
                score += 0.2f * PositionQuality(ghost, cueDir, objDir, vGhost * Mathf.Sin(cut * Mathf.Deg2Rad), target, balls, isLegal);

            shot = new PlannedShot
            {
                valid = true,
                direction = cueDir,
                power01 = power,
                score = score,
                target = target,
                aimPoint = pocket,
                isSafety = false
            };
            return true;
        }

        /// <summary>0..1: how good is the next shot from the estimated cue ball stop position.</summary>
        float PositionQuality(Vector3 ghost, Vector3 cueDir, Vector3 objDir, float cueSpeedAfter, Ball current,
                              IReadOnlyList<Ball> balls, Predicate<int> isLegal)
        {
            // 90-degree rule: the cue ball leaves along the tangent line
            Vector3 tangent = cueDir - objDir * Vector3.Dot(cueDir, objDir);
            Vector3 stop = ghost;
            if (tangent.sqrMagnitude > 1e-4f)
            {
                float dist = cueSpeedAfter * cueSpeedAfter / (2f * effectiveDeceleration);
                stop += tangent.normalized * dist;
            }
            stop.x = Mathf.Clamp(stop.x, -tableHalfSize.x + R, tableHalfSize.x - R);
            stop.z = Mathf.Clamp(stop.z, -tableHalfSize.y + R, tableHalfSize.y - R);

            // count remaining legal balls at a comfortable distance
            int good = 0;
            for (int i = 0; i < balls.Count; i++)
            {
                Ball b = balls[i];
                if (b == current || b.isCueBall || !b.gameObject.activeInHierarchy || !isLegal(b.number)) continue;
                float d = Vector3.Distance(stop, b.Body.position);
                if (d > 0.15f && d < 1.0f) good++;
            }
            return Mathf.Clamp01(good / 2f);
        }

        bool PathClear(Vector3 from, Vector3 dir, float distance, Collider ignoreA, Collider ignoreB)
        {
            if (distance <= 0f) return true;
            int count = Physics.SphereCastNonAlloc(from, R * 0.97f, dir, hits, distance, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Collider c = hits[i].collider;
                if (c == ignoreA || c == ignoreB) continue;
                if (hits[i].distance <= 0f) continue; // overlapping at start (e.g. touching balls)
                return false;
            }
            return true;
        }

        PlannedShot PlanBreak(Vector3 cuePos, IReadOnlyList<Ball> balls)
        {
            // aim at the apex ball (closest object ball to the cue ball)
            Ball apex = null;
            float best = float.MaxValue;
            for (int i = 0; i < balls.Count; i++)
            {
                Ball b = balls[i];
                if (b.isCueBall || !b.gameObject.activeInHierarchy) continue;
                float d = (b.Body.position - cuePos).sqrMagnitude;
                if (d < best) { best = d; apex = b; }
            }
            if (apex == null) return default;
            Vector3 dir = apex.Body.position - cuePos; dir.y = 0f;
            return new PlannedShot { valid = true, direction = dir.normalized, power01 = 1f, score = 1f, target = apex };
        }

        /// <summary>Defensive shot: legally hit an own ball with enough power to reach a cushion.</summary>
        PlannedShot PlanSafety(Vector3 cuePos, Collider cueCollider, IReadOnlyList<Ball> balls, Predicate<int> isLegal)
        {
            PlannedShot best = default;
            for (int i = 0; i < balls.Count; i++)
            {
                Ball b = balls[i];
                if (b.isCueBall || !b.gameObject.activeInHierarchy || !isLegal(b.number)) continue;
                Vector3 p = b.Body.position;
                Vector3 dir = p - cuePos; dir.y = 0f;
                float d = dir.magnitude;
                if (d < 0.001f) continue;
                dir /= d;
                if (!PathClear(cuePos, dir, d - D - 0.003f, cueCollider, b.Collider)) continue;

                float toCushion = DistanceToCushion(p, dir);
                float vObj = SpeedForDistance(toCushion + 0.3f, 0.3f);
                float vCue = SpeedForDistance(d, vObj / 0.93f) * 1.1f;
                float power = Mathf.Clamp(vCue / maxSpeed, 0.12f, 0.6f);

                // prefer far-away targets and leaving the cue ball far from the object balls
                float score = 0.1f + 0.05f * d;
                if (!best.valid || score > best.score)
                    best = new PlannedShot { valid = true, direction = dir, power01 = power, score = score, target = b, isSafety = true };
            }
            return best;
        }

        PlannedShot PlanAnyContact(Vector3 cuePos, IReadOnlyList<Ball> balls, Predicate<int> isLegal)
        {
            Ball nearest = null;
            float best = float.MaxValue;
            for (int i = 0; i < balls.Count; i++)
            {
                Ball b = balls[i];
                if (b.isCueBall || !b.gameObject.activeInHierarchy || !isLegal(b.number)) continue;
                float d = (b.Body.position - cuePos).sqrMagnitude;
                if (d < best) { best = d; nearest = b; }
            }
            if (nearest == null) return default;
            Vector3 dir = nearest.Body.position - cuePos; dir.y = 0f;
            return new PlannedShot { valid = true, direction = dir.normalized, power01 = 0.45f, score = 0f, target = nearest, isSafety = true };
        }

        float DistanceToCushion(Vector3 p, Vector3 dir)
        {
            float tx = Mathf.Abs(dir.x) > 1e-4f ? ((Mathf.Sign(dir.x) * (tableHalfSize.x - R)) - p.x) / dir.x : float.MaxValue;
            float tz = Mathf.Abs(dir.z) > 1e-4f ? ((Mathf.Sign(dir.z) * (tableHalfSize.y - R)) - p.z) / dir.z : float.MaxValue;
            return Mathf.Max(0f, Mathf.Min(tx, tz));
        }

        /// <summary>Ball in hand: tries a grid of positions and returns the one with the best shot.</summary>
        public Vector3 FindBestCuePosition(Ball cueBall, IReadOnlyList<Ball> balls, Predicate<int> isLegal,
                                           IReadOnlyList<Vector3> pockets, AIDifficultySettings settings, bool kitchenOnly)
        {
            float maxX = kitchenOnly ? -tableHalfSize.x * 0.5f : tableHalfSize.x - R - 0.02f;
            float minX = -tableHalfSize.x + R + 0.02f;
            float minZ = -tableHalfSize.y + R + 0.02f;
            float maxZ = tableHalfSize.y - R - 0.02f;

            Vector3 bestPos = cueBall.SpawnPosition;
            float bestScore = float.MinValue;
            const int nx = 10, nz = 5;
            for (int ix = 0; ix < nx; ix++)
            for (int iz = 0; iz < nz; iz++)
            {
                var pos = new Vector3(Mathf.Lerp(minX, maxX, ix / (nx - 1f)), R, Mathf.Lerp(minZ, maxZ, iz / (nz - 1f)));
                if (Physics.CheckSphere(pos + Vector3.up * 0.004f, R, ~0, QueryTriggerInteraction.Ignore)) continue; // lifted so the cloth does not count

                PlannedShot shot = Plan(pos, cueBall.Collider, balls, isLegal, pockets, settings, false, false);
                if (shot.valid && !shot.isSafety && shot.score > bestScore)
                {
                    bestScore = shot.score;
                    bestPos = pos;
                }
            }
            return bestPos;
        }

        /// <summary>Gaussian random number (Box-Muller).</summary>
        public static float Gaussian(float stdDev)
        {
            float u1 = Mathf.Max(1e-6f, UnityEngine.Random.value);
            float u2 = UnityEngine.Random.value;
            return stdDev * Mathf.Sqrt(-2f * Mathf.Log(u1)) * Mathf.Cos(2f * Mathf.PI * u2);
        }
    }
}
