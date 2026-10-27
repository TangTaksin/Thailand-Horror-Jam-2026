using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using ThailandHorrorJam.Core;
using ThailandHorrorJam.Systems.Needle;
using ThailandHorrorJam.Systems.Gameplay;

namespace ThailandHorrorJam.Utilities
{
    public enum TeleportModifier
    {
        None,
        LeftShift,
        LeftControl,
        LeftAlt
    }

    /// <summary>
    /// Developer debug and testing utilities for rapid level iteration.
    /// Provides Left-Click teleportation to empty space, checkpoint/needle shortcuts,
    /// and quick scene reloads.
    /// </summary>
    public sealed class DevDebugTools : MonoBehaviour
    {
        public static DevDebugTools Instance { get; private set; }

        [Header("Master Settings")]
        [Tooltip("เปิดใช้งานเครื่องมือ Dev Debug หรือไม่")]
        [SerializeField] private bool _enableDebugTools = true;

        [Tooltip("ปุ่มเปิด/ปิดการทำงานของ Dev Tools ทั้งหมด")]
        [SerializeField] private Key _toggleKey = Key.F1;

        [Tooltip("แสดงคำแนะนำมุมจอด้านซ้ายบนหรือไม่")]
        [SerializeField] private bool _showWatermark = true;

        [Header("Click Teleport")]
        [Tooltip("คลิกซ้ายบนพื้นที่ว่างเพื่อวาปผู้เล่นไปจุดนั้น")]
        [SerializeField] private bool _enableClickTeleport = true;

        [Tooltip("ต้องกดปุ่มคีย์บอร์ดร่วมด้วยหรือไม่ (None = คลิกซ้ายได้เลยทันที)")]
        [SerializeField] private TeleportModifier _teleportModifier = TeleportModifier.None;

        [Tooltip("Layer ของพื้น/กำแพง เพื่อป้องกันไม่ให้คลิกวาปเข้าไปติดในของทึบ")]
        [SerializeField] private LayerMask _solidObstacleMask;

        [Tooltip("Layer ของวัตถุที่ใช้เย็บ/ปฏิสัมพันธ์ เพื่อไม่ให้แย่งจังหวะคลิกเย็บของเกม")]
        [SerializeField] private LayerMask _interactiveMask;

        [Tooltip("Snap ลงบนพื้นด้านล่างอัตโนมัติหรือไม่หลังวาป")]
        [SerializeField] private bool _snapToGround = false;

        [Tooltip("Snap กล้องตามไปยังตำแหน่งใหม่ทันที ไม่ต้องรอแพนกล้อง")]
        [SerializeField] private bool _snapCamera = true;

        [Header("Extra Shortcuts")]
        [Tooltip("ปุ่ม Reload ฉากปัจจุบัน (F5 หรือ Ctrl+R)")]
        [SerializeField] private bool _enableQuickReload = true;

        [Tooltip("กด F2 เพื่อสลับสถานะอมตะ (God Mode)")]
        [SerializeField] private bool _enableGodModeToggle = true;

        [Tooltip("กด F3 เพื่อปลดล็อก/ล็อกเข็มกับด้ายทันที")]
        [SerializeField] private bool _enableNeedleToggle = true;

        private Camera _mainCamera;
        private PlayerController2D _playerController;
        private bool _isGodMode;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            if (_solidObstacleMask == 0)
            {
                _solidObstacleMask = LayerMask.GetMask("Ground");
            }

            if (_interactiveMask == 0)
            {
                _interactiveMask = LayerMask.GetMask("Stitchable");
            }
        }

        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current[_toggleKey].wasPressedThisFrame)
            {
                _enableDebugTools = !_enableDebugTools;
                Debug.Log($"[DevDebug] Debug Tools: {(_enableDebugTools ? "ENABLED" : "DISABLED")}");
            }

            if (!_enableDebugTools) return;

            FindReferences();

            HandleShortcuts();
            HandleClickTeleport();
        }

        private void FindReferences()
        {
            if (_mainCamera == null)
            {
                _mainCamera = Camera.main != null ? Camera.main : FindAnyObjectByType<Camera>();
            }

            if (_playerController == null)
            {
                var player = GameObject.FindWithTag("Player");
                if (player != null)
                {
                    _playerController = player.GetComponent<PlayerController2D>();
                }
            }
        }

        private void HandleClickTeleport()
        {
            if (!_enableClickTeleport || _playerController == null || _mainCamera == null) return;
            if (Mouse.current == null || !Mouse.current.leftButton.wasPressedThisFrame) return;

            // 1. Check modifier key
            if (!IsModifierSatisfied()) return;

            // 2. Ignore UI clicks (buttons, menus)
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

            // 3. Convert mouse position to world space
            Vector2 screenPos = Mouse.current.position.ReadValue();
            Vector3 worldPoint3D = _mainCamera.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, -_mainCamera.transform.position.z));
            Vector2 worldPoint = new Vector2(worldPoint3D.x, worldPoint3D.y);

            // 4. Check if clicking on empty space:
            // Must NOT click on interactive stitchable objects (so we don't block gameplay interactions)
            if (_interactiveMask != 0 && Physics2D.OverlapPoint(worldPoint, _interactiveMask) != null)
            {
                return;
            }

            // Must NOT click inside solid geometry/ground
            if (_solidObstacleMask != 0 && Physics2D.OverlapPoint(worldPoint, _solidObstacleMask) != null)
            {
                // If clicked inside ground, try to find empty space slightly above
                Collider2D solidHit = Physics2D.OverlapPoint(worldPoint, _solidObstacleMask);
                if (solidHit != null)
                {
                    float topY = solidHit.bounds.max.y + 0.6f;
                    Vector2 surfacePoint = new Vector2(worldPoint.x, topY);
                    if (Physics2D.OverlapPoint(surfacePoint, _solidObstacleMask) == null)
                    {
                        worldPoint = surfacePoint;
                    }
                    else
                    {
                        return; // cannot find safe spot inside dense wall
                    }
                }
            }

            // 5. Teleport player!
            bool faceRight = worldPoint.x >= _playerController.transform.position.x;
            _playerController.TeleportTo(worldPoint, faceRight, snapToGround: _snapToGround);

            if (_snapCamera)
            {
                CameraController2D.Instance?.SnapToTarget();
            }

            Debug.Log($"[DevDebug] Teleported player to {worldPoint:F2} (Empty Space)");
        }

        private bool IsModifierSatisfied()
        {
            if (Keyboard.current == null) return true;

            switch (_teleportModifier)
            {
                case TeleportModifier.LeftShift:
                    return Keyboard.current.leftShiftKey.isPressed;
                case TeleportModifier.LeftControl:
                    return Keyboard.current.leftCtrlKey.isPressed;
                case TeleportModifier.LeftAlt:
                    return Keyboard.current.leftAltKey.isPressed;
                case TeleportModifier.None:
                default:
                    return true;
            }
        }

        private void HandleShortcuts()
        {
            if (Keyboard.current == null) return;

            // Quick Reload (F5 or Ctrl+R)
            if (_enableQuickReload)
            {
                bool f5Pressed = Keyboard.current.f5Key.wasPressedThisFrame;
                bool ctrlRPressed = Keyboard.current.leftCtrlKey.isPressed && Keyboard.current.rKey.wasPressedThisFrame;
                if (f5Pressed || ctrlRPressed)
                {
                    Debug.Log("[DevDebug] Reloading current scene...");
                    SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
                    return;
                }
            }

            // Toggle God Mode (F2)
            if (_enableGodModeToggle && Keyboard.current.f2Key.wasPressedThisFrame)
            {
                _isGodMode = !_isGodMode;
                Debug.Log($"[DevDebug] God Mode: {(_isGodMode ? "ON" : "OFF")}");
            }

            if (_isGodMode && _playerController != null)
            {
                _playerController.ResetRespawnImmunity(1.0f);
            }

            // Toggle Needle Controller (F3)
            if (_enableNeedleToggle && Keyboard.current.f3Key.wasPressedThisFrame)
            {
                if (_playerController != null)
                {
                    var nc = _playerController.GetComponent<NeedleController>();
                    if (nc != null)
                    {
                        nc.enabled = !nc.enabled;
                        Debug.Log($"[DevDebug] NeedleController enabled: {nc.enabled}");
                    }
                }
            }
        }

        private void OnGUI()
        {
            if (!_enableDebugTools || !_showWatermark) return;

            string modText = _teleportModifier == TeleportModifier.None ? "Left-Click" : $"{_teleportModifier} + Left-Click";
            string godText = _isGodMode ? " [GOD MODE]" : "";

            GUIStyle style = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold
            };
            style.normal.textColor = new Color(0.3f, 0.95f, 0.4f, 0.85f);

            string info = $"[DEV] {modText} on empty space: Teleport | F1: Hide | F2: God{godText} | F5: Reload";
            GUI.Label(new Rect(10, 10, 600, 25), info, style);
        }
    }
}
