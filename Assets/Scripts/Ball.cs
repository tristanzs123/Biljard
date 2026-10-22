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
    [Range(0f, 1f)] public float linearDamping = 0.12f;

    [Tooltip("How fast spin around the vertical axis (side spin) fades, 1/s.")]
    [Range(0f, 5f)] public float spinDecay = 1.5f;

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
        Body.maxAngularVelocity = 300f;
        Body.linearDamping = linearDamping;
        Body.angularDamping = 0.05f;
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
    /// PhysX has no rolling resistance. Sliding turns into rolling through cloth friction (PhysicsMaterial);
    /// here we add the constant rolling resistance of the cloth so the ball decelerates evenly and stops cleanly.
    /// </summary>
    void ApplyRollingResistance(float dt)
    {
        if (transform.position.y > Radius + 0.01f) return; // airborne

        Vector3 v = Body.linearVelocity;
        Vector3 w = Body.angularVelocity;
        float speed = Mathf.Sqrt(v.x * v.x + v.z * v.z);
        if (speed > 0.0001f)
        {
            float k = Mathf.Max(0f, speed - rollingDeceleration * dt) / speed;
            Body.linearVelocity = new Vector3(v.x * k, v.y, v.z * k);
            w = new Vector3(w.x * k, w.y, w.z * k);
        }

        // side spin fades out
        w.y *= Mathf.Max(0f, 1f - spinDecay * dt);

        // a ball spinning in place (e.g. after hitting a cushion) is braked by the cloth until it stops
        if (speed < stopSpeed * 3f)
        {
            float mag = w.magnitude;
            if (mag > 0f) w *= Mathf.Max(0f, mag - spinBrake * dt) / mag;
        }
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
    public void Pocket(string pocketName)
    {
        if (IsSinking) return;
        Pocketed?.Invoke(this);
        StartCoroutine(SinkRoutine());
    }

    IEnumerator SinkRoutine()
    {
        IsSinking = true;
        Vector3 v = Body.linearVelocity;
        Body.isKinematic = true;
        Collider.enabled = false;

        Vector3 start = transform.position;
        Vector3 drift = new Vector3(v.x, 0f, v.z) * 0.06f;
        const float duration = 0.45f;
        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            float e = t / duration;
            transform.position = start + drift * e + Vector3.down * (e * e * 0.12f);
            transform.localScale = baseScale * (1f - 0.5f * e);
            yield return null;
        }

        IsSinking = false;
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
