using UnityEngine;

/// <summary>
/// Luknja na mizi. Ima trigger collider - ko krogla pride vanj, "pade" v luknjo.
/// </summary>
[RequireComponent(typeof(Collider))]
public class Pocket : MonoBehaviour
{
    void Reset()
    {
        GetComponent<Collider>().isTrigger = true;
    }

    void OnTriggerEnter(Collider other)
    {
        Ball ball = other.attachedRigidbody != null ? other.attachedRigidbody.GetComponent<Ball>() : null;
        if (ball != null)
            ball.Pocket(name);
    }
}
