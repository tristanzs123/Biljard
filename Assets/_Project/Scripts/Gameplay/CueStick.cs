using System;
using System.Collections;
using UnityEngine;

namespace Billiards.Gameplay
{
    /// <summary>
    /// Visual cue stick: follows the aim direction, pulls back with power and plays the strike animation.
    /// The cylinder mesh is a child so it can be scaled independently.
    /// </summary>
    public class CueStick : MonoBehaviour
    {
        [Tooltip("Gap between the tip and the ball at zero power (m).")]
        [SerializeField] float tipGap = 0.012f;

        [Tooltip("Maximum pull-back distance at full power (m).")]
        [SerializeField] float maxPullBack = 0.28f;

        [Tooltip("Stick tilt above the table (degrees).")]
        [SerializeField, Range(0f, 15f)] float elevation = 4f;

        [Tooltip("Duration of the forward strike (s).")]
        [SerializeField] float strikeTime = 0.07f;

        [Tooltip("Length of the stick (m).")]
        [SerializeField] float length = 1.45f;

        /// <summary>Is the strike animation running.</summary>
        public bool IsStriking { get; private set; }

        Renderer[] renderers;

        void Awake()
        {
            renderers = GetComponentsInChildren<Renderer>();
            SetVisible(false);
        }

        /// <summary>Shows or hides the stick.</summary>
        public void SetVisible(bool visible)
        {
            if (renderers == null) return;
            for (int i = 0; i < renderers.Length; i++) renderers[i].enabled = visible;
        }

        /// <summary>Places the stick behind the ball along the aim direction, pulled back by power (0..1).</summary>
        public void SetPose(Vector3 ballPos, Vector3 dir, float power01)
        {
            SetPoseDistance(ballPos, dir, tipGap + power01 * maxPullBack);
        }

        void SetPoseDistance(Vector3 ballPos, Vector3 dir, float tipDistance)
        {
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-6f) return;
            dir.Normalize();
            Quaternion tilt = Quaternion.AngleAxis(elevation, Vector3.Cross(Vector3.up, dir));
            Vector3 stickDir = tilt * dir; // points from butt to tip, slightly downward
            Vector3 tip = ballPos - stickDir * (Ball.Radius + tipDistance);
            transform.position = tip - stickDir * (length * 0.5f);
            transform.rotation = Quaternion.FromToRotation(Vector3.up, stickDir);
        }

        /// <summary>Strike animation: fast forward to the ball, calls onImpact, short follow-through, then hides.</summary>
        public IEnumerator Strike(Vector3 ballPos, Vector3 dir, float power01, Action onImpact)
        {
            IsStriking = true;
            SetVisible(true);
            float from = tipGap + power01 * maxPullBack;
            for (float t = 0f; t < strikeTime; t += Time.deltaTime)
            {
                float e = t / strikeTime;
                SetPoseDistance(ballPos, dir, Mathf.Lerp(from, 0f, e * e));
                yield return null;
            }
            SetPoseDistance(ballPos, dir, 0f);
            onImpact?.Invoke();

            // follow-through
            for (float t = 0f; t < 0.12f; t += Time.deltaTime)
            {
                SetPoseDistance(ballPos, dir, Mathf.Lerp(0f, -0.04f, t / 0.12f));
                yield return null;
            }
            yield return new WaitForSeconds(0.25f);
            SetVisible(false);
            IsStriking = false;
        }
    }
}
