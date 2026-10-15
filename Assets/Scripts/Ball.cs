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

    [Tooltip("Pod to hitrostjo (m/s) krogla obstane. PhysX nima kotalnega trenja, zato jo ustavimo sami.")]
    public float stopSpeed = 0.015f;

    public Rigidbody Body { get; private set; }

    Vector3 spawnPosition;

    void Awake()
    {
        Body = GetComponent<Rigidbody>();
        Body.maxAngularVelocity = 300f; // da se lahko hitra krogla kotali in ne drsi
        spawnPosition = transform.position;
    }

    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);

    /// <summary>Ali se krogla še premika.</summary>
    public bool IsMoving =>
        !Body.IsSleeping() &&
        (Body.linearVelocity.sqrMagnitude > stopSpeed * stopSpeed ||
         Body.angularVelocity.sqrMagnitude > 0.36f);

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

        // skoraj stoji -> ustavi jo do konca
        if (Body.linearVelocity.sqrMagnitude < stopSpeed * stopSpeed &&
            Body.angularVelocity.sqrMagnitude < 0.36f &&
            !Body.IsSleeping())
        {
            Body.linearVelocity = Vector3.zero;
            Body.angularVelocity = Vector3.zero;
            Body.Sleep();
        }
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
