using Unity.Netcode;
using UnityEngine;

namespace MainGame.Scripts
{
    /// <summary>
    /// Oyuncu kamera kökü (Camera Parent). Her oyuncu için sunucu tarafından player object olarak spawn edilir.
    /// Sahibi değilsek kamerayı, ses dinleyicisini ve HUD'u kapatır; böylece diğer oyuncunun arayüzü
    /// bizim tıklamalarımızı yakalamaz (eski GraphicSelection hack'ine gerek kalmaz).
    /// Kamera hareketi yalnızca yerelde uygulanır, ağ üzerinden senkronize edilmez.
    /// </summary>
    public class RTSCameraController : NetworkBehaviour
    {
        [SerializeField] private float normalSpeed = 35f;
        [SerializeField] private float fastSpeed = 60f;
        [SerializeField] private float normalZoomSpeed = 1000f;
        [SerializeField] private float fastZoomSpeed = 2000f;
        [SerializeField] private float rotateSpeed = 1f;
        [SerializeField] private float maxHeight = 55f;
        [Tooltip("Dünya yüksekliği. Zemin y=10 civarında; bunun altına inmemeli.")]
        [SerializeField] private float minHeight = 16f;

        [Header("Harita sınırı (kamera kökünün x/z aralığı)")]
        [SerializeField] private Vector2 mapMin = new Vector2(-45f, -15f);
        [SerializeField] private Vector2 mapMax = new Vector2(115f, 105f);

        [Header("Kenar kaydırma ve klavyeyle döndürme")]
        [SerializeField] private bool edgePan = true;
        [SerializeField] private float edgePanBorder = 12f;
        [SerializeField] private float keyRotateSpeed = 90f;

        [Tooltip("Sahibi olmayan oyuncularda kapatılacak objeler (HUD canvas'ları vb.).")]
        [SerializeField] private GameObject[] ownerOnlyObjects;

        private Vector2 lastMousePosition;

        public override void OnNetworkSpawn()
        {
            // Kök Rigidbody kinematik değildi; kamera transform ile sürüldüğü için fizik müdahale etmemeli.
            if (TryGetComponent(out Rigidbody body))
            {
                body.isKinematic = true;
            }

            if (IsOwner)
            {
                return;
            }

            foreach (var cam in GetComponentsInChildren<Camera>(true))
            {
                cam.enabled = false;
            }

            foreach (var listener in GetComponentsInChildren<AudioListener>(true))
            {
                listener.enabled = false;
            }

            foreach (var canvas in GetComponentsInChildren<Canvas>(true))
            {
                canvas.gameObject.SetActive(false);
            }

            if (ownerOnlyObjects != null)
            {
                foreach (var ownerOnly in ownerOnlyObjects)
                {
                    if (ownerOnly != null)
                    {
                        ownerOnly.SetActive(false);
                    }
                }
            }

            foreach (var commandController in GetComponentsInChildren<UnitCommandController>(true))
            {
                commandController.enabled = false;
            }

            foreach (var uiManager in GetComponentsInChildren<UIManager>(true))
            {
                uiManager.enabled = false;
            }

            enabled = false;
        }

        private void Update()
        {
            if (!IsSpawned || !IsOwner)
            {
                return;
            }

            bool fast = Input.GetKey(KeyCode.LeftShift);
            float speed = fast ? fastSpeed : normalSpeed;
            float zoomSpeed = fast ? fastZoomSpeed : normalZoomSpeed;

            Vector2 edge = GetEdgePanInput();
            float horizontal = speed * Mathf.Clamp(Input.GetAxis("Horizontal") + edge.x, -1f, 1f);
            float vertical = speed * Mathf.Clamp(Input.GetAxis("Vertical") + edge.y, -1f, 1f);

            float height = transform.position.y;
            float scroll = Mathf.Log(Mathf.Max(height, 1f)) * -zoomSpeed * Input.GetAxis("Mouse ScrollWheel");

            Vector3 forward = transform.forward;
            forward.y = 0f;
            forward.Normalize();

            Vector3 move = new Vector3(0f, scroll, 0f) + horizontal * transform.right + vertical * forward;
            Vector3 position = transform.position + move * Time.deltaTime;

            // Eski sürümde minHeight -4 idi ve zemin y=10'da olduğu için kamera zeminin altına inebiliyordu;
            // harita sınırı da yoktu. Yükseklik ve konum artık sınırlanıyor.
            position.y = Mathf.Clamp(position.y, minHeight, maxHeight);
            position.x = Mathf.Clamp(position.x, mapMin.x, mapMax.x);
            position.z = Mathf.Clamp(position.z, mapMin.y, mapMax.y);
            transform.position = position;

            UpdateRotation();
        }

        private Vector2 GetEdgePanInput()
        {
            if (!edgePan || !Application.isFocused)
            {
                return Vector2.zero;
            }

            Vector3 mouse = Input.mousePosition;
            if (mouse.x < 0f || mouse.y < 0f || mouse.x > Screen.width || mouse.y > Screen.height)
            {
                return Vector2.zero;
            }

            Vector2 input = Vector2.zero;
            if (mouse.x <= edgePanBorder) input.x = -1f;
            else if (mouse.x >= Screen.width - edgePanBorder) input.x = 1f;
            if (mouse.y <= edgePanBorder) input.y = -1f;
            else if (mouse.y >= Screen.height - edgePanBorder) input.y = 1f;
            return input;
        }

        private void UpdateRotation()
        {
            float keyRotation = 0f;
            if (Input.GetKey(KeyCode.Q)) keyRotation -= keyRotateSpeed * Time.deltaTime;
            if (Input.GetKey(KeyCode.E)) keyRotation += keyRotateSpeed * Time.deltaTime;
            if (keyRotation != 0f)
            {
                transform.rotation *= Quaternion.Euler(0f, keyRotation, 0f);
            }

            if (Input.GetMouseButtonDown(2))
            {
                lastMousePosition = Input.mousePosition;
            }

            if (Input.GetMouseButton(2))
            {
                Vector2 mousePosition = Input.mousePosition;
                float deltaX = (mousePosition - lastMousePosition).x * rotateSpeed;
                transform.rotation *= Quaternion.Euler(0f, deltaX, 0f);
                lastMousePosition = mousePosition;
            }
        }
    }
}
