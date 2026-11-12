using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One billiard ball: physics tuning, rolling resistance, collision reporting and pocket animation.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class Ball : MonoBehaviour
{
    /// <summary>All active balls on the table.</summary>
    public static readonly List<Ball> All = new List<Ball>();

    /// <summary>Cue ball touched an object ball (cue, object).</summary>
    public static event Action<Ball, Ball> CueBallHit;
    /// <summary>A ball touched a cushion.</summary>
    public static event Action<Ball> CushionHit;
    /// <summary>A ball dropped into a pocket.</summary>
    public static event Action<Ball> Pocketed;
    /// <summary>A ball left the table.</summary>
    public static event Action<Ball> LeftTable;
    /// <summary>Two balls collided (relative speed in m/s) - for sound/juice.</summary>
    public static event Action<Ball, float> BallCollision;

    public const float Radius = 0.028575f;

    [Tooltip("Is this the cue ball.")]
    public bool isCueBall;

    [Tooltip("Ball number (0 = cue ball). Parsed from the GameObject name when 0.")]
    public int number;

    [Header("Rolling on cloth")]
    [Tooltip("Rolling resistance of the cloth (m/s²). Higher = stops sooner. 8 Ball Pool feel: ~0.2 - 0.3.")]
    [Range(0.05f, 1f)] public float rollingDeceleration = 0.22f;

    [Tooltip("Speed-proportional drag (air/cloth).")]
    [Range(0f, 1f)] public float linearDamping = 0.1f;

    [Tooltip("How fast spin around the vertical axis (side spin) fades, 1/s.")]
    [Range(0f, 5f)] public float spinDecay = 0.6f;

    [Tooltip("Sliding friction coefficient ball-cloth. Turns sliding into rolling and makes draw / follow work.")]
    [Range(0.05f, 0.5f)] public float slidingFriction = 0.2f;

    [Tooltip("Constant braking of side spin by the cloth (rad/s²).")]
    [Range(0f, 30f)] public float sideSpinFriction = 6f;

    [Tooltip("Below this speed (m/s) the ball stops.")]
    [Range(0.001f, 0.05f)] public float stopSpeed = 0.01f;

    public Rigidbody Body { get; private set; }
    public SphereCollider Collider { get; private set; }

    /// <summary>Ball is playing the pocket animation.</summary>
    public bool IsSinking { get; private set; }

    Vector3 spawnPosition;
    Vector3 baseScale;

    void Awake()
    {
        Body = GetComponent<Rigidbody>();
        Collider = GetComponent<SphereCollider>();
        Body.maxAngularVelocity = 1000f;   // strong draw / follow needs > 300 rad/s
        Body.linearDamping = linearDamping;
        Body.angularDamping = 0f;          // spin is handled by the cloth model below
        Body.solverIterations = 12;
        Body.solverVelocityIterations = 8;
        spawnPosition = transform.position;
        baseScale = transform.localScale;
        if (!isCueBall && number == 0) int.TryParse(name, out number);
    }

    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);

    [Tooltip("Spin (rad/s) below which a ball that is not moving counts as stopped.")]
    [Range(0.1f, 5f)] public float restSpin = 1.5f;

    [Tooltip("How fast a ball spinning in place stops (rad/s²).")]
    [Range(1f, 50f)] public float spinBrake = 12f;

    float MaxRestSpin => Mathf.Max(restSpin, stopSpeed / Radius);

    /// <summary>Is the ball still moving (or sinking).</summary>
    public bool IsMoving =>
        IsSinking ||
        (!Body.isKinematic && !Body.IsSleeping() &&
         (Body.linearVelocity.sqrMagnitude > stopSpeed * stopSpeed ||
          Body.angularVelocity.sqrMagnitude > MaxRestSpin * MaxRestSpin));

    /// <summary>Stops every ball immediately (used as a safety net).</summary>
    public static void StopAll()
    {
        for (int i = 0; i < All.Count; i++)
        {
            Ball b = All[i];
            if (b.Body.isKinematic) continue;
            b.Body.linearVelocity = Vector3.zero;
            b.Body.angularVelocity = Vector3.zero;
            b.Body.Sleep();
        }
    }

    /// <summary>Is any ball on the table moving.</summary>
    public static bool AnyMoving()
    {
        for (int i = 0; i < All.Count; i++)
            if (All[i].IsMoving) return true;
        return false;
    }

    void FixedUpdate()
    {
        if (IsSinking || Body.isKinematic) return;

        if (transform.position.y < -0.3f)
        {
            LeftTable?.Invoke(this);
            gameObject.SetActive(false);
            return;
        }

        if (Body.IsSleeping()) return;

        ApplyRollingResistance(Time.fixedDeltaTime);

        if (Body.linearVelocity.sqrMagnitude < stopSpeed * stopSpeed &&
            Body.angularVelocity.sqrMagnitude < MaxRestSpin * MaxRestSpin)
        {
            Body.linearVelocity = Vector3.zero;
            Body.angularVelocity = Vector3.zero;
            Body.Sleep();
        }
    }

    /// <summary>
    /// Ball-cloth model like in 8 Ball Pool / real pool (the cloth PhysicsMaterial has zero friction,
    /// everything happens here):
    ///  - SLIDING: the contact point slips over the cloth. Kinetic friction slows the slip down and changes
    ///    both the speed and the spin -> stun, draw (backspin) and follow (topspin) shots work naturally.
    ///  - ROLLING: no slip, spin = speed / radius, only the small rolling resistance slows the ball.
    ///  - Side spin (around the vertical axis) fades slowly and changes the angle off a cushion.
    /// </summary>
    void ApplyRollingResistance(float dt)
    {
        if (transform.position.y > Radius + 0.01f) return; // airborne

        const float g = 9.81f;
        Vector3 v = Body.linearVelocity;
        Vector3 w = Body.angularVelocity;
        Vector3 vh = new Vector3(v.x, 0f, v.z);

        // pool balls do not jump in 8 Ball Pool: no upward velocity while touching the cloth
        float vy = Mathf.Min(v.y, 0f);

        // velocity of the contact point (bottom of the ball): v + w x (-R * up)
        Vector3 slip = vh + Vector3.Cross(w, Vector3.down * Radius);
        slip.y = 0f;
        float slipSpeed = slip.magnitude;

        if (slipSpeed > 0.02f)
        {
            // sliding: friction acts against the slip; the slip changes 3.5x faster than the speed alone
            Vector3 dirSlip = slip / slipSpeed;
            float dv = Mathf.Min(slidingFriction * g * dt, slipSpeed / 3.5f);
            vh -= dirSlip * dv;
            Vector3 dw = Vector3.Cross(Vector3.up, dirSlip) * (2.5f * dv / Radius);
            w += new Vector3(dw.x, 0f, dw.z);
        }
        else
        {
            // rolling: constant rolling resistance, spin locked to the speed
            float speed = vh.magnitude;
            if (speed > 0f) vh *= Mathf.Max(0f, speed - rollingDeceleration * dt) / speed;
            Vector3 roll = Vector3.Cross(Vector3.up, vh) / Radius;
            w = new Vector3(roll.x, w.y, roll.z);
        }

        // side spin fades out
        w.y = Mathf.MoveTowards(w.y * Mathf.Max(0f, 1f - spinDecay * dt), 0f, sideSpinFriction * dt);

        // a ball that stopped but still turns a little (numerical noise after a cushion) is stopped by the cloth
        if (vh.magnitude < stopSpeed * 3f && slipSpeed <= 0.02f)
        {
            float mag = w.magnitude;
            if (mag > 0f) w *= Mathf.Max(0f, mag - spinBrake * dt) / mag;
        }

        Body.linearVelocity = new Vector3(vh.x, vy, vh.z);
        Body.angularVelocity = w;
    }

    void OnCollisionEnter(Collision collision)
    {
        Ball other = collision.rigidbody != null ? collision.rigidbody.GetComponent<Ball>() : null;
        if (other != null)
        {
            if (GetInstanceID() < other.GetInstanceID())
                BallCollision?.Invoke(this, collision.relativeVelocity.magnitude);
            if (isCueBall) CueBallHit?.Invoke(this, other);
            return;
        }

        if (collision.collider.GetComponent<Billiards.Gameplay.Cushion>() != null)
            CushionHit?.Invoke(this);
    }

    /// <summary>Called by the pocket trigger.</summary>
    public void Pocket(string pocketName) => Pocket(pocketName, transform.position);

    /// <summary>Ball falls into the pocket whose centre (on the cloth) is given.</summary>
    public void Pocket(string pocketName, Vector3 pocketCenter)
    {
        if (IsSinking) return;
        Pocketed?.Invoke(this);
        StartCoroutine(SinkRoutine(pocketCenter));
    }

    /// <summary>
    /// Pocket animation: the ball is pulled to the middle of the hole while it drops and shrinks,
    /// so it always disappears INSIDE the black hole (it never travels on under the table / rail).
    /// </summary>
    IEnumerator SinkRoutine(Vector3 pocketCenter)
    {
        IsSinking = true;
        Body.linearVelocity = Vector3.zero;
        Body.angularVelocity = Vector3.zero;
        Body.isKinematic = true;
        Collider.enabled = false;

        Vector3 start = transform.position;
        Vector3 target = new Vector3(pocketCenter.x, Radius, pocketCenter.z);
        const float duration = 0.3f;
        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            float e = t / duration;
            Vector3 flat = Vector3.Lerp(start, target, 1f - (1f - e) * (1f - e)); // quickly to the hole centre
            transform.position = new Vector3(flat.x, Radius - e * e * 0.05f, flat.z); // and down
            transform.localScale = baseScale * (1f - 0.7f * e);
            yield return null;
        }

        IsSinking = false;
        transform.localScale = baseScale;
        gameObject.SetActive(false);
    }

    /// <summary>Puts the ball back on the table at a position, at rest (used for ball in hand).</summary>
    public void PlaceAt(Vector3 position)
    {
        StopAllCoroutines();
        IsSinking = false;
        transform.localScale = baseScale;
        gameObject.SetActive(true);
        Collider.enabled = true;
        Body.isKinematic = false;
        Body.position = position;
        transform.position = position;
        Body.linearVelocity = Vector3.zero;
        Body.angularVelocity = Vector3.zero;
        Body.Sleep();
    }

    /// <summary>Start position of the ball (head spot for the cue ball).</summary>
    public Vector3 SpawnPosition => spawnPosition;
}
