using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;

namespace MainGame.Scripts
{
    /// <summary>
    /// Yerel oyuncunun unit seçimi ve komutları (eski UnitClick + her unit'teki PlayerController/WoodCutterController).
    /// RTS oyunlarındaki alışılmış düzen kullanılır:
    /// Sol tık: seç (Shift ile seçime ekle / çıkar). Sol tıkla sürükle: kutu içindeki unit'leri seç. Esc: seçimi bırak.
    /// Sağ tık: seçili unit'lere komut ver (düşmana saldır, ağaç kes ya da hedefe yürü).
    /// Yürüme komutunda unit'ler aynı noktaya yığılmasın diye hedefin etrafına dizilir.
    /// Yalnızca kendi unit'lerimiz seçilebilir.
    /// </summary>
    public class UnitCommandController : MonoBehaviour
    {
        private const float DragThreshold = 8f;

        [SerializeField] private GameObject cameraParent;
        [SerializeField] private LayerMask clickable;
        [SerializeField] private LayerMask ground;

        [Tooltip("Yürüme komutunda unit'ler arasındaki mesafe.")]
        [SerializeField] private float formationSpacing = 1.2f;

        [SerializeField] private Color selectionBoxColor = new Color(0.4f, 0.8f, 1f, 0.9f);

        private readonly List<Unit> selectedUnits = new List<Unit>();
        private Camera playerCamera;
        private NetworkObject owner;
        private bool selecting;
        private Vector2 selectionStart;
        private static Texture2D whiteTexture;

        public IReadOnlyList<Unit> SelectedUnits => selectedUnits;

        private void Awake()
        {
            owner = GetComponentInParent<NetworkObject>();
            var cameraRoot = cameraParent != null ? cameraParent : transform.root.gameObject;
            playerCamera = cameraRoot.GetComponentInChildren<Camera>(true);
        }

        private void OnDisable()
        {
            selecting = false;
        }

        private void Update()
        {
            if (owner == null || !owner.IsSpawned || !owner.IsOwner || playerCamera == null)
            {
                return;
            }

            selectedUnits.RemoveAll(unit => unit == null || !unit.IsSpawned);

            if (selecting)
            {
                if (Input.GetMouseButtonUp(0))
                {
                    FinishSelection(Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift));
                }

                return;
            }

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                DeselectAll();
            }

            if (IsPointerOverUi())
            {
                return;
            }

            if (Input.GetMouseButtonDown(0))
            {
                selecting = true;
                selectionStart = Input.mousePosition;
            }
            else if (Input.GetMouseButtonDown(1))
            {
                HandleCommandClick();
            }
        }

        private void OnGUI()
        {
            if (!selecting || !IsDragging())
            {
                return;
            }

            if (whiteTexture == null)
            {
                whiteTexture = Texture2D.whiteTexture;
            }

            Rect rect = GetScreenRect(selectionStart, Input.mousePosition);
            // GUI koordinatlarında y aşağı doğru artar.
            rect.y = Screen.height - rect.y - rect.height;

            var previousColor = GUI.color;
            GUI.color = new Color(selectionBoxColor.r, selectionBoxColor.g, selectionBoxColor.b, 0.15f);
            GUI.DrawTexture(rect, whiteTexture);
            GUI.color = selectionBoxColor;
            GUI.DrawTexture(new Rect(rect.xMin, rect.yMin, rect.width, 2f), whiteTexture);
            GUI.DrawTexture(new Rect(rect.xMin, rect.yMax - 2f, rect.width, 2f), whiteTexture);
            GUI.DrawTexture(new Rect(rect.xMin, rect.yMin, 2f, rect.height), whiteTexture);
            GUI.DrawTexture(new Rect(rect.xMax - 2f, rect.yMin, 2f, rect.height), whiteTexture);
            GUI.color = previousColor;
        }

        public void DeselectAll()
        {
            foreach (var unit in selectedUnits)
            {
                if (unit != null)
                {
                    unit.SetSelected(false);
                }
            }

            selectedUnits.Clear();
        }

        private void FinishSelection(bool additive)
        {
            selecting = false;

            if (IsDragging())
            {
                HandleBoxSelection(GetScreenRect(selectionStart, Input.mousePosition), additive);
            }
            else
            {
                HandleSelectionClick(additive);
            }
        }

        private bool IsDragging()
        {
            return ((Vector2)Input.mousePosition - selectionStart).sqrMagnitude > DragThreshold * DragThreshold;
        }

        private void HandleSelectionClick(bool additive)
        {
            Unit clickedUnit = null;
            if (Physics.Raycast(GetMouseRay(), out var hit, Mathf.Infinity, clickable))
            {
                clickedUnit = hit.collider.GetComponentInParent<Unit>();
            }

            if (clickedUnit == null || !clickedUnit.IsOwner)
            {
                if (!additive)
                {
                    DeselectAll();
                }

                return;
            }

            if (additive)
            {
                ToggleSelection(clickedUnit);
            }
            else
            {
                DeselectAll();
                Select(clickedUnit);
            }
        }

        private void HandleBoxSelection(Rect screenRect, bool additive)
        {
            if (!additive)
            {
                DeselectAll();
            }

            foreach (var unit in Unit.All)
            {
                if (unit == null || !unit.IsOwner)
                {
                    continue;
                }

                Vector3 screenPoint = playerCamera.WorldToScreenPoint(unit.transform.position);
                if (screenPoint.z > 0f && screenRect.Contains(screenPoint))
                {
                    Select(unit);
                }
            }
        }

        private void HandleCommandClick()
        {
            if (selectedUnits.Count == 0)
            {
                return;
            }

            var hits = Physics.RaycastAll(GetMouseRay());
            if (hits.Length == 0)
            {
                return;
            }

            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            // Önce saldırılabilir bir hedef var mı bak (her unit kendi kuralına göre: savaşçı düşmana, oduncu ağaca).
            bool anyAttackIssued = false;
            foreach (var hit in hits)
            {
                var target = hit.collider.GetComponentInParent<NetworkObject>();
                if (target == null)
                {
                    continue;
                }

                foreach (var unit in selectedUnits)
                {
                    if (unit.Attack != null && unit.Attack.CanAttack(target.gameObject))
                    {
                        unit.CommandAttack(target);
                        anyAttackIssued = true;
                    }
                }

                if (anyAttackIssued)
                {
                    return;
                }
            }

            // Saldırı hedefi yoksa en yakın zemin noktasına yürü.
            Vector3 destination = hits[0].point;
            foreach (var hit in hits)
            {
                if (ground.value == 0 || (ground.value & (1 << hit.collider.gameObject.layer)) != 0)
                {
                    destination = hit.point;
                    break;
                }
            }

            IssueFormationMove(destination);
        }

        /// <summary>Unit'leri hedefin etrafında kare bir diziliş içinde dağıtır.</summary>
        private void IssueFormationMove(Vector3 destination)
        {
            int count = selectedUnits.Count;
            int columns = Mathf.CeilToInt(Mathf.Sqrt(count));
            int rows = Mathf.CeilToInt(count / (float)columns);

            Vector3 right = playerCamera.transform.right;
            right.y = 0f;
            right = right.sqrMagnitude > 0.0001f ? right.normalized : Vector3.right;
            Vector3 forward = Vector3.Cross(right, Vector3.up);

            for (int i = 0; i < count; i++)
            {
                int row = i / columns;
                int column = i % columns;
                Vector3 offset = right * ((column - (columns - 1) * 0.5f) * formationSpacing)
                                 + forward * ((row - (rows - 1) * 0.5f) * formationSpacing);
                selectedUnits[i].CommandMove(destination + offset);
            }
        }

        private void Select(Unit unit)
        {
            if (!selectedUnits.Contains(unit))
            {
                selectedUnits.Add(unit);
                unit.SetSelected(true);
            }
        }

        private void ToggleSelection(Unit unit)
        {
            if (selectedUnits.Remove(unit))
            {
                unit.SetSelected(false);
            }
            else
            {
                Select(unit);
            }
        }

        private Ray GetMouseRay()
        {
            return playerCamera.ScreenPointToRay(Input.mousePosition);
        }

        private static Rect GetScreenRect(Vector2 a, Vector2 b)
        {
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        }

        private static bool IsPointerOverUi()
        {
            return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        }
    }
}
