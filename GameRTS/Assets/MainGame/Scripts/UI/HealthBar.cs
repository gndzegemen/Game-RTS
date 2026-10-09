using UnityEngine;
using UnityEngine.UI;

namespace MainGame.Scripts
{
    /// <summary>
    /// Unit/bina üzerindeki can barı. Bağlı olduğu <see cref="Health"/>'i dinler (Observer);
    /// Health artık UI'ı bilmek zorunda değil.
    /// Bar her karede kameraya döner (billboard) ve tıklamaları yakalamaz: world-space canvas'taki
    /// GraphicRaycaster, imleç barın üstündeyken EventSystem'i "UI üzerinde" sandırıp komutları engelliyordu.
    /// </summary>
    public class HealthBar : MonoBehaviour
    {
        [SerializeField] private Image _healthbarSprite;

        private Health health;
        private Camera cachedCamera;

        private void Awake()
        {
            health = GetComponentInParent<Health>();

            foreach (var raycaster in GetComponentsInChildren<GraphicRaycaster>(true))
            {
                raycaster.enabled = false;
            }

            foreach (var graphic in GetComponentsInChildren<Graphic>(true))
            {
                graphic.raycastTarget = false;
            }
        }

        private void OnEnable()
        {
            if (health != null)
            {
                health.HealthChanged += UpdateHealthBar;
                UpdateHealthBar(health.CurrentHealth, health.MaxHealth);
            }
        }

        private void OnDisable()
        {
            if (health != null)
            {
                health.HealthChanged -= UpdateHealthBar;
            }
        }

        private void LateUpdate()
        {
            if (cachedCamera == null || !cachedCamera.isActiveAndEnabled)
            {
                cachedCamera = Camera.main;
            }

            if (cachedCamera != null)
            {
                transform.rotation = cachedCamera.transform.rotation;
            }
        }

        public void UpdateHealthBar(float currentHealth, float maxHealth)
        {
            if (_healthbarSprite != null && maxHealth > 0f)
            {
                _healthbarSprite.fillAmount = Mathf.Clamp01(currentHealth / maxHealth);
            }
        }
    }
}
