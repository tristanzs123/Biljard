using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// Udarjanje bele krogle z miško (kot frača):
/// pritisni levi gumb, povleci miško NAZAJ od bele krogle in spusti.
/// Dlje kot povlečeš, močnejši je udarec. Desni gumb prekliče, R ponastavi igro.
/// </summary>
[RequireComponent(typeof(Ball))]
public class CueController : MonoBehaviour
{
    public Camera cam;
    public LineRenderer aimLine;

    [Tooltip("Največja hitrost bele krogle (m/s).")]
    public float maxSpeed = 6f;

    [Tooltip("Koliko daleč (m) moraš povleči miško za polno moč.")]
    public float maxDrag = 0.5f;

    Ball ball;
    bool aiming;
    Vector3 shotDir = Vector3.right;
    float power; // 0..1

    void Awake()
    {
        ball = GetComponent<Ball>();
        if (cam == null) cam = Camera.main;
        if (aimLine != null) aimLine.enabled = false;
    }

    void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.rKey.wasPressedThisFrame)
        {
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
            return;
        }

        Mouse mouse = Mouse.current;
        if (mouse == null || cam == null) return;

        Vector3 cuePos = ball.Body.position;
        if (!TryGetMousePoint(mouse.position.ReadValue(), cuePos.y, out Vector3 mousePoint)) return;

        // začni meriti samo, ko vse krogle mirujejo
        if (!aiming && mouse.leftButton.wasPressedThisFrame && !Ball.AnyMoving())
            aiming = true;

        if (!aiming) return;

        // smer udarca je NASPROTNA od smeri, kamor vlečeš miško
        Vector3 pull = cuePos - mousePoint;
        pull.y = 0f;
        if (pull.sqrMagnitude > 0.0001f) shotDir = pull.normalized;
        power = Mathf.Clamp01(pull.magnitude / maxDrag);
        DrawAim(cuePos);

        if (mouse.rightButton.wasPressedThisFrame)
        {
            StopAiming();
        }
        else if (mouse.leftButton.wasReleasedThisFrame)
        {
            StopAiming();
            if (power > 0.02f) Shoot();
        }
    }

    void Shoot()
    {
        // hitrost nastavimo direktno (AddForce bi se uporabil šele v naslednjem fizikalnem koraku)
        ball.Body.WakeUp();
        ball.Body.linearVelocity = shotDir * (power * maxSpeed);
    }

    void StopAiming()
    {
        aiming = false;
        if (aimLine != null) aimLine.enabled = false;
    }

    void DrawAim(Vector3 cuePos)
    {
        if (aimLine == null) return;
        aimLine.enabled = true;
        aimLine.positionCount = 2;
        aimLine.SetPosition(0, cuePos);
        aimLine.SetPosition(1, cuePos + shotDir * (0.15f + power * 0.85f));
        Color c = Color.Lerp(Color.white, Color.red, power);
        aimLine.startColor = c;
        aimLine.endColor = new Color(c.r, c.g, c.b, 0.2f);
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

    void OnGUI()
    {
        GUI.Label(new Rect(12, 10, 700, 24),
            "Levi gumb: povleci nazaj od bele krogle in spusti  |  Desni gumb: prekliči  |  R: znova");
        if (aiming)
            GUI.Label(new Rect(12, 32, 300, 24), $"Moč: {Mathf.RoundToInt(power * 100)} %");
    }
}
