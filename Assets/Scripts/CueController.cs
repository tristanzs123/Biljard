using System;
using Billiards.Gameplay;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Shoots the cue ball. Used by BOTH the human player (mouse input) and the AI (TakeShot / ShowAim),
/// so the AI cannot cheat with the physics.
/// Human: move the mouse to aim, hold the left button and pull back for power, release to shoot.
/// Shift = fine aim, right button = cancel.
/// </summary>
[RequireComponent(typeof(Ball))]
public class CueController : MonoBehaviour
{
    /// <summary>Fired when the stick hits the cue ball (direction, power 0..1).</summary>
    public event Action<Vector3, float> ShotFired;

    [Header("References")]
    public Camera cam;
    public CueStick stick;
    [Tooltip("Cue ball path line.")] public LineRenderer aimLine;
    [Tooltip("Object ball direction line.")] public LineRenderer objectLine;
    [Tooltip("Cue ball deflection line.")] public LineRenderer deflectLine;
    [Tooltip("Ghost ball at the contact point.")] public Transform ghostBall;

    [Header("Shot")]
    [Tooltip("Maximum cue ball speed (m/s).")]
    [Range(2f, 12f)] public float maxSpeed = 8.5f;
    [Tooltip("Mouse pull-back distance on the table for full power (m).")]
    [Range(0.1f, 1f)] public float maxDrag = 0.45f;

    [Header("Aim assist")]
    [Tooltip("Show aim assist lines.")] public bool aimAssist = true;
    [Tooltip("Length multiplier of the predicted ball directions.")]
    [Range(0f, 2f)] public float assistLength = 1f;
    [Tooltip("Fine aim (Shift) smoothing factor.")]
    [Range(0.01f, 1f)] public float fineAimFactor = 0.08f;

    /// <summary>Human mouse input is enabled.</summary>
    public bool HumanControl { get; set; }

    /// <summary>Current power 0..1 while charging.</summary>
    public float CurrentPower { get; private set; }

    /// <summary>Player is holding the button and charging a shot.</summary>
    public bool IsCharging { get; private set; }

    /// <summary>Current aim direction (flat, normalized).</summary>
    public Vector3 AimDirection => aimDir;

    /// <summary>Stick animation is playing or a shot is being applied.</summary>
    public bool IsShooting { get; private set; }

    Ball ball;
    Vector3 aimDir = Vector3.right;
    Vector3 pressPoint;
    readonly RaycastHit[] hits = new RaycastHit[8];

    void Awake()
    {
        ball = GetComponent<Ball>();
        if (cam == null) cam = Camera.main;
        HideAssist();
    }

    void Update()
    {
        if (!HumanControl || IsShooting || Ball.AnyMoving() || !gameObject.activeInHierarchy)
        {
            if (!IsShooting && stick != null && !HumanControl) { /* AI drives the stick itself */ }
            if (HumanControl && !IsShooting) HideAimVisuals();
            return;
        }

        Mouse mouse = Mouse.current;
        if (mouse == null || cam == null) return;
        Vector3 cuePos = ball.Body.position;
        if (!TryGetMousePoint(mouse.position.ReadValue(), cuePos.y, out Vector3 mousePoint)) return;

        if (!IsCharging)
        {
            Vector3 target = mousePoint - cuePos;
            target.y = 0f;
            if (target.sqrMagnitude > 0.0004f)
            {
                target.Normalize();
                bool fine = Keyboard.current != null && Keyboard.current.shiftKey.isPressed;
                aimDir = fine ? Vector3.Slerp(aimDir, target, fineAimFactor).normalized : target;
            }

            if (mouse.leftButton.wasPressedThisFrame)
            {
                IsCharging = true;
                pressPoint = mousePoint;
            }
            CurrentPower = 0f;
        }
        else
        {
            Vector3 pull = pressPoint - mousePoint;
            CurrentPower = Mathf.Clamp01(Vector3.Dot(pull, aimDir) / maxDrag);

            if (mouse.rightButton.wasPressedThisFrame)
            {
                IsCharging = false;
                CurrentPower = 0f;
            }
            else if (mouse.leftButton.wasReleasedThisFrame)
            {
                IsCharging = false;
                if (CurrentPower > 0.03f)
                {
                    TakeShot(aimDir, CurrentPower);
                    return;
                }
            }
        }

        ShowAim(aimDir, CurrentPower);
    }

    /// <summary>Shows the stick and aim assist for a direction and power (used by the AI while it "thinks").</summary>
    public void ShowAim(Vector3 dir, float power01)
    {
        dir.y = 0f;
        if (dir.sqrMagnitude < 1e-6f) return;
        aimDir = dir.normalized;
        Vector3 cuePos = ball.Body.position;
        if (stick != null)
        {
            stick.SetVisible(true);
            stick.SetPose(cuePos, aimDir, power01);
        }
        DrawAssist(cuePos, aimDir);
    }

    /// <summary>Hides stick and aim assist.</summary>
    public void HideAimVisuals()
    {
        if (stick != null && !stick.IsStriking) stick.SetVisible(false);
        HideAssist();
    }

    /// <summary>Plays the stick strike and shoots the cue ball. Used by the player and the AI.</summary>
    public void TakeShot(Vector3 dir, float power01)
    {
        if (IsShooting) return;
        dir.y = 0f;
        aimDir = dir.normalized;
        Vector3 shotDir = aimDir;
        float shotPower = Mathf.Clamp01(power01);

        IsShooting = true;
        HideAssist();
        // The strike coroutine runs on the STICK, not on the cue ball: if the cue ball is pocketed
        // (and deactivated) the animation must not be killed, otherwise IsShooting would stay true forever.
        if (stick != null)
            stick.StartCoroutine(stick.Strike(ball.Body.position, shotDir, shotPower, () => ApplyShot(shotDir, shotPower)));
        else
            ApplyShot(shotDir, shotPower);
    }

    void ApplyShot(Vector3 dir, float power01)
    {
        IsShooting = false;
        if (!gameObject.activeInHierarchy) return;
        ball.Body.WakeUp();
        ball.Body.linearVelocity = dir * (power01 * maxSpeed);
        // start without spin: cloth friction turns sliding into natural rolling
        ball.Body.angularVelocity = Vector3.zero;
        ShotFired?.Invoke(dir, power01);
    }

    void OnDisable()
    {
        IsCharging = false;
        CurrentPower = 0f;
    }

    void DrawAssist(Vector3 cuePos, Vector3 dir)
    {
        if (!aimAssist || aimLine == null) { HideAssist(); return; }

        int count = Physics.SphereCastNonAlloc(cuePos, Ball.Radius * 0.98f, dir, hits, 4f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        int bestIndex = -1;
        for (int i = 0; i < count; i++)
        {
            if (hits[i].collider == ball.Collider) continue;
            if (hits[i].distance <= 0f) continue;
            if (hits[i].distance < best) { best = hits[i].distance; bestIndex = i; }
        }
        if (bestIndex < 0) { HideAssist(); return; }

        Vector3 ghost = cuePos + dir * best;
        SetLine(aimLine, cuePos, ghost);
        if (ghostBall != null)
        {
            ghostBall.gameObject.SetActive(true);
            ghostBall.position = ghost;
        }

        Ball target = hits[bestIndex].rigidbody != null ? hits[bestIndex].rigidbody.GetComponent<Ball>() : null;
        if (target != null && objectLine != null && deflectLine != null)
        {
            Vector3 objDir = target.Body.position - ghost;
            objDir.y = 0f;
            objDir.Normalize();
            float along = Vector3.Dot(dir, objDir);
            Vector3 cueDir = dir - objDir * along;
            SetLine(objectLine, target.Body.position, target.Body.position + objDir * (0.12f + 0.35f * along) * assistLength);
            if (cueDir.sqrMagnitude > 1e-4f)
                SetLine(deflectLine, ghost, ghost + cueDir.normalized * (0.25f * (1f - along) + 0.03f) * assistLength);
            else
                deflectLine.enabled = false;
        }
        else
        {
            if (objectLine != null) objectLine.enabled = false;
            if (deflectLine != null) deflectLine.enabled = false;
        }
    }

    static void SetLine(LineRenderer line, Vector3 a, Vector3 b)
    {
        line.enabled = true;
        line.positionCount = 2;
        line.SetPosition(0, a);
        line.SetPosition(1, b);
    }

    void HideAssist()
    {
        if (aimLine != null) aimLine.enabled = false;
        if (objectLine != null) objectLine.enabled = false;
        if (deflectLine != null) deflectLine.enabled = false;
        if (ghostBall != null) ghostBall.gameObject.SetActive(false);
    }

    bool TryGetMousePoint(Vector2 screenPos, float height, out Vector3 point)
    {
        Ray ray = cam.ScreenPointToRay(screenPos);
        Plane plane = new Plane(Vector3.up, new Vector3(0f, height, 0f));
        if (plane.Raycast(ray, out float distance))
        {
            point = ray.GetPoint(distance);
            return true;
        }
        point = Vector3.zero;
        return false;
    }
}
