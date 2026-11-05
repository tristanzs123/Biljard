using System;
using System.Collections.Generic;
using Billiards.Core;
using Billiards.Gameplay;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Shoots the cue ball. Used by BOTH the human player (mouse input) and the AI (TakeShot / ShowAim),
/// so the AI cannot cheat with the physics.
/// Human: move the mouse to aim, hold the left button and pull back for power, release to shoot.
/// Shift = fine aim, arrow keys / A-D = turn the cue, right button = cancel.
/// Spin (english) is set with the cue ball widget in the HUD, like in 8 Ball Pool.
/// Sensitivities come from GameSettings (menu Nastavitve).
/// </summary>
[RequireComponent(typeof(Ball))]
public class CueController : MonoBehaviour
{
    /// <summary>Fired when the stick hits the cue ball (direction, power 0..1).</summary>
    public event Action<Vector3, float> ShotFired;

    /// <summary>Screen areas (GUI coordinates, top-left origin) covered by HUD widgets: clicks there do not shoot.</summary>
    public static readonly List<Rect> UiBlockers = new List<Rect>();

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
    [Tooltip("Mouse pull-back distance on the table for full power (m), at power sensitivity 1.")]
    [Range(0.1f, 1f)] public float maxDrag = 0.45f;

    [Header("Spin")]
    [Tooltip("Top/back spin at full offset, relative to natural roll (8 Ball Pool ~1.5).")]
    [Range(0f, 3f)] public float topSpinFactor = 1.5f;
    [Tooltip("Side spin at full offset, relative to speed / radius.")]
    [Range(0f, 3f)] public float sideSpinFactor = 1.0f;

    [Header("Aim assist")]
    [Tooltip("Show aim assist lines.")] public bool aimAssist = true;
    [Tooltip("Length multiplier of the predicted ball directions (set by the equipped cue).")]
    [Range(0f, 3f)] public float assistLength = 1f;
    [Tooltip("Base keyboard aim speed (degrees per second).")]
    [Range(1f, 90f)] public float keyboardAimSpeed = 25f;

    /// <summary>Human mouse input is enabled. Disabling it cancels a shot being charged.</summary>
    public bool HumanControl
    {
        get => humanControl;
        set
        {
            if (value && !humanControl) hasMouseAngle = false;
            humanControl = value;
            if (!value) { IsCharging = false; CurrentPower = 0f; }
        }
    }
    bool humanControl;

    /// <summary>Current power 0..1 while charging.</summary>
    public float CurrentPower { get; private set; }

    /// <summary>Player is holding the button and charging a shot.</summary>
    public bool IsCharging { get; private set; }

    /// <summary>Current aim direction (flat, normalized).</summary>
    public Vector3 AimDirection => aimDir;

    /// <summary>Stick animation is playing or a shot is being applied.</summary>
    public bool IsShooting { get; private set; }

    /// <summary>Tip offset on the cue ball: x = side (right +), y = top (+) / back (-). Inside the unit circle.</summary>
    public Vector2 Spin
    {
        get => spin;
        set => spin = Vector2.ClampMagnitude(value, 1f);
    }
    Vector2 spin;

    Ball ball;
    Vector3 aimDir = Vector3.right;
    float aimAngle;          // degrees, 0 = +Z
    float lastMouseAngle;
    bool hasMouseAngle;
    Vector3 pressPoint;
    readonly RaycastHit[] hits = new RaycastHit[8];

    void Awake()
    {
        GameSettings.EnsureLoaded();
        ball = GetComponent<Ball>();
        if (cam == null) cam = Camera.main;
        aimAngle = AngleOf(aimDir);
        HideAssist();
    }

    /// <summary>Losing focus (Alt+Tab) makes the mouse position jump: cancel the charge so it cannot fire a shot.</summary>
    void OnApplicationFocus(bool focused)
    {
        if (focused || !IsCharging) return;
        IsCharging = false;
        CurrentPower = 0f;
        hasMouseAngle = false;
    }

    void Update()
    {
        if (!HumanControl || IsShooting || Ball.AnyMoving() || !gameObject.activeInHierarchy)
        {
            if (HumanControl && !IsShooting) HideAimVisuals();
            return;
        }

        Mouse mouse = Mouse.current;
        if (mouse == null || cam == null) return;
        Vector2 screenPos = mouse.position.ReadValue();
        Vector3 cuePos = ball.Body.position;
        if (!TryGetMousePoint(screenPos, cuePos.y, out Vector3 mousePoint)) return;
        bool overUi = IsOverUi(screenPos);

        if (!IsCharging)
        {
            if (!overUi) UpdateAim(mousePoint - cuePos);
            UpdateKeyboardAim();

            if (mouse.leftButton.wasPressedThisFrame && !overUi)
            {
                IsCharging = true;
                pressPoint = mousePoint;
            }
            CurrentPower = 0f;
        }
        else
        {
            Vector3 pull = pressPoint - mousePoint;
            float drag = maxDrag / Mathf.Max(0.1f, GameSettings.PowerSensitivity);
            CurrentPower = Mathf.Clamp01(Vector3.Dot(pull, aimDir) / drag);

            if (mouse.rightButton.wasPressedThisFrame)
            {
                IsCharging = false;
                CurrentPower = 0f;
                hasMouseAngle = false;
            }
            else if (mouse.leftButton.wasReleasedThisFrame)
            {
                IsCharging = false;
                hasMouseAngle = false;
                if (CurrentPower > 0.03f)
                {
                    TakeShot(aimDir, CurrentPower, spin);
                    return;
                }
            }
        }

        ShowAim(aimDir, CurrentPower);
    }

    /// <summary>
    /// Mouse aim with sensitivity: the cue turns by (change of the mouse angle around the ball) x sensitivity.
    /// At sensitivity 1 without Shift the cue points straight at the mouse.
    /// </summary>
    void UpdateAim(Vector3 toMouse)
    {
        toMouse.y = 0f;
        if (toMouse.sqrMagnitude < 0.0004f) return;
        float mouseAngle = AngleOf(toMouse);
        bool fine = Keyboard.current != null && Keyboard.current.shiftKey.isPressed;
        bool direct = !fine && Mathf.Abs(GameSettings.AimSensitivity - 1f) < 0.01f;

        if (!hasMouseAngle)
        {
            hasMouseAngle = true;
            if (direct) aimAngle = mouseAngle;
        }
        else if (direct)
        {
            if (Mathf.Abs(Mathf.DeltaAngle(lastMouseAngle, mouseAngle)) > 0.001f) aimAngle = mouseAngle;
        }
        else
        {
            float sens = GameSettings.AimSensitivity * (fine ? GameSettings.FineAimSensitivity : 1f);
            aimAngle += Mathf.DeltaAngle(lastMouseAngle, mouseAngle) * sens;
        }
        lastMouseAngle = mouseAngle;
        aimDir = DirOf(aimAngle);
    }

    void UpdateKeyboardAim()
    {
        Keyboard kb = Keyboard.current;
        if (kb == null) return;
        float turn = 0f;
        if (kb.leftArrowKey.isPressed || kb.aKey.isPressed) turn -= 1f;
        if (kb.rightArrowKey.isPressed || kb.dKey.isPressed) turn += 1f;
        if (turn == 0f) return;
        float speed = keyboardAimSpeed * GameSettings.KeyboardAimSpeed * (kb.shiftKey.isPressed ? GameSettings.FineAimSensitivity : 1f);
        aimAngle += turn * speed * Time.unscaledDeltaTime;
        aimDir = DirOf(aimAngle);
    }

    static float AngleOf(Vector3 d) => Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
    static Vector3 DirOf(float angle) => new Vector3(Mathf.Sin(angle * Mathf.Deg2Rad), 0f, Mathf.Cos(angle * Mathf.Deg2Rad));

    static bool IsOverUi(Vector2 screenPos)
    {
        Vector2 gui = new Vector2(screenPos.x, Screen.height - screenPos.y);
        for (int i = 0; i < UiBlockers.Count; i++)
            if (UiBlockers[i].Contains(gui)) return true;
        return false;
    }

    /// <summary>Shows the stick and aim assist for a direction and power (used by the AI while it "thinks").</summary>
    public void ShowAim(Vector3 dir, float power01)
    {
        dir.y = 0f;
        if (dir.sqrMagnitude < 1e-6f) return;
        aimDir = dir.normalized;
        aimAngle = AngleOf(aimDir);
        Vector3 cuePos = ball.Body.position;
        if (stick != null)
        {
            stick.SetVisible(true);
            stick.SetPose(TipPoint(cuePos, aimDir, HumanControl ? spin : Vector2.zero), aimDir, power01);
        }
        DrawAssist(cuePos, aimDir);
    }

    /// <summary>Where the tip touches the ball for a spin offset (the stick visibly aims off-centre).</summary>
    static Vector3 TipPoint(Vector3 center, Vector3 dir, Vector2 offset)
    {
        Vector3 right = Vector3.Cross(Vector3.up, dir);
        return center + (right * offset.x + Vector3.up * offset.y) * (Ball.Radius * 0.5f);
    }

    /// <summary>Hides stick and aim assist.</summary>
    public void HideAimVisuals()
    {
        if (stick != null && !stick.IsStriking) stick.SetVisible(false);
        HideAssist();
    }

    /// <summary>Plays the stick strike and shoots the cue ball without spin (used by the AI).</summary>
    public void TakeShot(Vector3 dir, float power01) => TakeShot(dir, power01, Vector2.zero);

    /// <summary>Plays the stick strike and shoots the cue ball with a spin offset.</summary>
    public void TakeShot(Vector3 dir, float power01, Vector2 spinOffset)
    {
        if (IsShooting) return;
        dir.y = 0f;
        aimDir = dir.normalized;
        Vector3 shotDir = aimDir;
        float shotPower = Mathf.Clamp01(power01);
        Vector2 shotSpin = Vector2.ClampMagnitude(spinOffset, 1f);

        IsShooting = true;
        HideAssist();
        // The strike coroutine runs on the STICK, not on the cue ball: if the cue ball is pocketed
        // (and deactivated) the animation must not be killed, otherwise IsShooting would stay true forever.
        if (stick != null)
            stick.StartCoroutine(stick.Strike(TipPoint(ball.Body.position, shotDir, shotSpin), shotDir, shotPower,
                () => ApplyShot(shotDir, shotPower, shotSpin)));
        else
            ApplyShot(shotDir, shotPower, shotSpin);
    }

    void ApplyShot(Vector3 dir, float power01, Vector2 shotSpin)
    {
        IsShooting = false;
        spin = Vector2.zero; // like 8 Ball Pool: spin resets after every shot
        if (!gameObject.activeInHierarchy) return;
        float speed = power01 * maxSpeed;
        ball.Body.WakeUp();
        ball.Body.linearVelocity = dir * speed;

        // spin from an off-centre hit: top/back around the horizontal axis, side spin around the vertical axis.
        // Centre hit = no spin: the cloth then turns sliding into rolling (stun / natural roll).
        Vector3 rollAxis = Vector3.Cross(Vector3.up, dir);
        Vector3 w = rollAxis * (shotSpin.y * topSpinFactor * speed / Ball.Radius);
        w.y = -shotSpin.x * sideSpinFactor * speed / Ball.Radius;
        ball.Body.angularVelocity = w;
        ShotFired?.Invoke(dir, power01);
    }

    void OnDisable()
    {
        IsCharging = false;
        CurrentPower = 0f;
    }

    void DrawAssist(Vector3 cuePos, Vector3 dir)
    {
        if (!aimAssist || !GameSettings.ShowGuide || aimLine == null) { HideAssist(); return; }

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

        float length = assistLength * GameSettings.AimLineLength;
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
            SetLine(objectLine, target.Body.position, target.Body.position + objDir * (0.12f + 0.35f * along) * length);
            if (cueDir.sqrMagnitude > 1e-4f)
                SetLine(deflectLine, ghost, ghost + cueDir.normalized * (0.25f * (1f - along) + 0.03f) * length);
            else
                deflectLine.enabled = false;
        }
        else
        {
            // cushion: show the bounce direction (mirror) like the 8 Ball Pool guide
            if (objectLine != null) objectLine.enabled = false;
            if (deflectLine != null)
            {
                Vector3 n = hits[bestIndex].normal;
                n.y = 0f;
                if (n.sqrMagnitude > 1e-4f)
                {
                    Vector3 bounce = Vector3.Reflect(dir, n.normalized);
                    SetLine(deflectLine, ghost, ghost + bounce * 0.2f * length);
                }
                else deflectLine.enabled = false;
            }
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
