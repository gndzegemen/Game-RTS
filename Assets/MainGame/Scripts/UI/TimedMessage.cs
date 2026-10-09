using TMPro;
using UnityEngine;

namespace MainGame.Scripts
{
    /// <summary>
    /// Panellerdeki geri bildirim metni ("Yeterli altın yok" vb.). Belirli süre sonra kendini temizler.
    /// Eskiden bu durumlar yalnızca Debug.Log'a yazılıyordu ve oyuncu neden bir şey olmadığını göremiyordu.
    /// </summary>
    [System.Serializable]
    public class TimedMessage
    {
        [SerializeField] private TMP_Text text;
        [SerializeField] private float duration = 2.5f;

        private float hideTime;

        public void Show(string message)
        {
            if (text == null)
            {
                Debug.Log(message);
                return;
            }

            text.text = message;
            text.gameObject.SetActive(true);
            hideTime = Time.unscaledTime + duration;
        }

        public void Clear()
        {
            hideTime = 0f;
            if (text != null)
            {
                text.text = string.Empty;
            }
        }

        /// <summary>Sahibinin Update'inden çağrılır.</summary>
        public void Tick()
        {
            if (hideTime > 0f && Time.unscaledTime >= hideTime)
            {
                Clear();
            }
        }
    }
}
