using UnityEngine;
using UnityEngine.InputSystem;

namespace Billiards.Gameplay
{
    /// <summary>
    /// Simple camera rig: C switches between the angled view and the top view (smooth blend),
    /// mouse wheel zooms, Shake() adds a short camera shake (break shot).
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class CameraViews : MonoBehaviour
    {
        [Tooltip("Angled view position.")]
        [SerializeField] Vector3 angledPosition = new Vector3(0f, 2.3f, -1.45f);
        [Tooltip("Point the angled view looks at.")]
        [SerializeField] Vector3 angledLookAt = new Vector3(0f, 0f, 0.05f);
        [Tooltip("Top view height.")]
        [SerializeField] float topHeight = 3.3f;
        [Tooltip("Blend speed between views.")]
        [SerializeField, Range(1f, 20f)] float blendSpeed = 5f;
        [Tooltip("Field of view limits for the mouse wheel zoom.")]
        [SerializeField] Vector2 fovRange = new Vector2(28f, 62f);

        Camera cam;
        bool topView;
        float targetFov = 50f;
        float shakeTime;
        float shakeStrength;

        void Awake()
        {
            cam = GetComponent<Camera>();
            targetFov = cam.fieldOfView;
        }

        /// <summary>Short camera shake.</summary>
        public void Shake(float duration, float strength)
        {
            shakeTime = duration;
            shakeStrength = strength;
        }

        /// <summary>Switches between angled and top view.</summary>
        public void ToggleView() => topView = !topView;

        void LateUpdate()
        {
            Keyboard kb = Keyboard.current;
            if (kb != null && kb.cKey.wasPressedThisFrame && Time.timeScale > 0f) ToggleView();

            Mouse mouse = Mouse.current;
            if (mouse != null && Time.timeScale > 0f)
            {
                float scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f)
                    targetFov = Mathf.Clamp(targetFov - Mathf.Sign(scroll) * 3f, fovRange.x, fovRange.y);
            }

            Vector3 pos = topView ? new Vector3(0f, topHeight, 0f) : angledPosition;
            Quaternion rot = topView
                ? Quaternion.Euler(90f, 0f, 0f)
                : Quaternion.LookRotation(angledLookAt - angledPosition, Vector3.up);

            float k = 1f - Mathf.Exp(-blendSpeed * Time.unscaledDeltaTime);
            transform.position = Vector3.Lerp(transform.position, pos, k);
            transform.rotation = Quaternion.Slerp(transform.rotation, rot, k);
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, targetFov, k);

            if (shakeTime > 0f)
            {
                shakeTime -= Time.deltaTime;
                transform.position += Random.insideUnitSphere * shakeStrength;
            }
        }
    }
}
