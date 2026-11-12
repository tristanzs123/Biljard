using UnityEngine;
using UnityEngine.InputSystem;

namespace Billiards.Gameplay
{
    /// <summary>
    /// Camera: always looks straight down at the table (top view, like 8 Ball Pool).
    /// The height is fitted to the screen aspect so the whole table is visible, the mouse wheel zooms,
    /// Shake() adds a short camera shake (break shot).
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class CameraViews : MonoBehaviour
    {
        [Tooltip("Size of the area that must be visible (table with rails), metres.")]
        [SerializeField] Vector2 viewArea = new Vector2(3.0f, 1.75f);
        [Tooltip("Extra space for the HUD at the top and bottom of the screen (fraction).")]
        [SerializeField, Range(0f, 0.5f)] float hudMargin = 0.18f;
        [Tooltip("Field of view of the camera.")]
        [SerializeField, Range(20f, 70f)] float fieldOfView = 40f;
        [Tooltip("Zoom limits (multiplier of the fitted height).")]
        [SerializeField] Vector2 zoomRange = new Vector2(0.6f, 1.15f);
        [Tooltip("Smoothing speed.")]
        [SerializeField, Range(1f, 20f)] float blendSpeed = 8f;

        Camera cam;
        float zoom = 1f;
        float shakeTime;
        float shakeStrength;

        void Awake()
        {
            cam = GetComponent<Camera>();
            cam.fieldOfView = fieldOfView;
            transform.SetPositionAndRotation(new Vector3(0f, FitHeight(), 0f), Quaternion.Euler(90f, 0f, 0f));
        }

        /// <summary>Short camera shake.</summary>
        public void Shake(float duration, float strength)
        {
            shakeTime = duration;
            shakeStrength = strength;
        }

        /// <summary>Camera height at which the whole table fits on the screen.</summary>
        float FitHeight()
        {
            float tanV = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float tanH = tanV * Mathf.Max(0.1f, cam.aspect);
            float hForDepth = viewArea.y * (1f + hudMargin) * 0.5f / tanV;
            float hForWidth = viewArea.x * 0.5f / tanH;
            return Mathf.Max(hForDepth, hForWidth);
        }

        void LateUpdate()
        {
            Mouse mouse = Mouse.current;
            if (mouse != null && Time.timeScale > 0f)
            {
                float scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f)
                    zoom = Mathf.Clamp(zoom - Mathf.Sign(scroll) * 0.06f, zoomRange.x, zoomRange.y);
            }

            Vector3 pos = new Vector3(0f, FitHeight() * zoom, 0f);
            float k = 1f - Mathf.Exp(-blendSpeed * Time.unscaledDeltaTime);
            transform.position = Vector3.Lerp(transform.position, pos, k);
            transform.rotation = Quaternion.Euler(90f, 0f, 0f);

            if (shakeTime > 0f)
            {
                shakeTime -= Time.deltaTime;
                transform.position += Random.insideUnitSphere * shakeStrength;
            }
        }
    }
}
