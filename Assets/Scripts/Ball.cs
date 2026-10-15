using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Ena biljardna krogla. Skrbi za fiziko krogle, ustavljanje in kaj se zgodi, ko pade v luknjo.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class Ball : MonoBehaviour
{
    /// <summary>Vse krogle, ki so trenutno na mizi.</summary>
    public static readonly List<Ball> All = new List<Ball>();

    [Tooltip("Ali je to bela krogla (ki jo udarjaš).")]
    public bool isCueBall;

    [Header("Kotaljenje po suknu")]
    [Tooltip("Kotalni upor sukna (m/s²). Večje = krogla se prej ustavi. Realno okoli 0.3 - 0.6.")]
    public float rollingDeceleration = 0.45f;

    [Tooltip("Upor zraka/sukna, ki je sorazmeren s hitrostjo (hitre krogle hitreje izgubijo hitrost).")]
    public float linearDamping = 0.08f;

    [Tooltip("Kako hitro izgine vrtenje okoli navpične osi (stranski efekt), v 1/s.")]
    public float spinDecay = 1.5f;

    [Tooltip("Pod to hitrostjo (m/s) krogla obstane.")]
    public float stopSpeed = 0.02f;

    public Rigidbody Body { get; private set; }

    const float Radius = 0.028575f;
    Vector3 spawnPosition;

    void Awake()
    {
        Body = GetComponent<Rigidbody>();
        Body.maxAngularVelocity = 300f;     // da se lahko hitra krogla kotali in ne drsi
        Body.linearDamping = linearDamping;
        Body.angularDamping = 0.05f;        // vrtenje ustavlja kotalni upor spodaj, ne damping
        spawnPosition = transform.position;
    }

    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);

    /// <summary>Ali se krogla še premika.</summary>
    public bool IsMoving =>
        !Body.IsSleeping() &&
        (Body.linearVelocity.sqrMagnitude > stopSpeed * stopSpeed ||
         Body.angularVelocity.sqrMagnitude > MaxRestSpin * MaxRestSpin);

    float MaxRestSpin => stopSpeed / Radius;

    /// <summary>Ali se katerakoli krogla na mizi še premika.</summary>
    public static bool AnyMoving()
    {
        foreach (var ball in All)
            if (ball.IsMoving) return true;
        return false;
    }

    void FixedUpdate()
    {
        // padla z mize -> enako kot luknja
        if (transform.position.y < -0.5f)
        {
            Pocket("tla");
            return;
        }

        if (Body.IsSleeping()) return;

        ApplyRollingResistance(Time.fixedDeltaTime);

        // skoraj stoji -> ustavi jo do konca
        if (Body.linearVelocity.sqrMagnitude < stopSpeed * stopSpeed &&
            Body.angularVelocity.sqrMagnitude < MaxRestSpin * MaxRestSpin)
        {
            Body.linearVelocity = Vector3.zero;
            Body.angularVelocity = Vector3.zero;
            Body.Sleep();
        }
    }

    /// <summary>
    /// PhysX ne pozna kotalnega upora, zato bi se krogla kotalila skoraj v nedogled.
    /// Tu jo zaviramo s konstantnim pojemkom (kot pravo sukno): hitrost pada enakomerno
    /// in krogla se lepo ustavi, namesto da dolgo "leze" s skoraj ničelno hitrostjo.
    /// </summary>
    void ApplyRollingResistance(float dt)
    {
        // samo ko je krogla na mizi (ne med skokom)
        if (transform.position.y > Radius + 0.01f) return;

        Vector3 v = Body.linearVelocity;
        Vector3 vFlat = new Vector3(v.x, 0f, v.z);
        float speed = vFlat.magnitude;

        if (speed > 0.0001f)
        {
            float newSpeed = Mathf.Max(0f, speed - rollingDeceleration * dt);
            float k = newSpeed / speed;

            // zmanjšamo hitrost IN vodoravno vrtenje v enakem razmerju,
            // da se krogla še naprej kotali (ne začne drseti ali je vrtenje ne potiska naprej)
            Body.linearVelocity = new Vector3(v.x * k, v.y, v.z * k);
            Vector3 w = Body.angularVelocity;
            Body.angularVelocity = new Vector3(w.x * k, w.y, w.z * k);
        }

        // stranski efekt (vrtenje okoli navpične osi) počasi izgine
        Vector3 spin = Body.angularVelocity;
        spin.y *= Mathf.Max(0f, 1f - spinDecay * dt);
        Body.angularVelocity = spin;
    }

    /// <summary>Pokliče jo luknja (Pocket), ko krogla pade vanjo.</summary>
    public void Pocket(string pocketName)
    {
        if (isCueBall)
        {
            Debug.Log("Bela krogla je padla v luknjo - vrnem jo na začetno mesto.");
            ResetTo(spawnPosition);
        }
        else
        {
            Debug.Log($"Krogla {name} je padla v luknjo ({pocketName}).");
            gameObject.SetActive(false);
        }
    }

    void ResetTo(Vector3 position)
    {
        Body.linearVelocity = Vector3.zero;
        Body.angularVelocity = Vector3.zero;
        Body.position = position;
        transform.position = position;
    }
}
