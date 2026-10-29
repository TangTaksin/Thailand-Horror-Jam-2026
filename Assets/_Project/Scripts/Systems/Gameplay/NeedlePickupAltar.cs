using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using DG.Tweening;
using ThailandHorrorJam.Core;
using ThailandHorrorJam.Systems.Needle;

namespace ThailandHorrorJam.Systems.Gameplay
{
    /// <summary>
    /// Coordinates the Needle and Thread acquisition cinematic sequence at the Dead Fish Altar in Sector_02.
    /// Handles proximity detection, Right-Click input, horror stinger tug animation,
    /// camera zoom out, and game logo title reveal before permanently unlocking NeedleController.
    /// </summary>
    public sealed class NeedlePickupAltar : MonoBehaviour
    {
        [Header("Detection & UI Prompt")]
        [SerializeField] private float _interactionRadius = 2.4f;
        [SerializeField] private GameObject _promptCanvasGo;
        [Tooltip("ระยะความสูงของ Prompt ที่ลอยอยู่เหนือ Needle_In_Pile")]
        [SerializeField] private float _promptAboveOffset = 0.9f;
        [Tooltip("แสดงข้อความ Action Text กำกับหรือไม่ (ปิดเพื่อให้แสดงเฉพาะ icon ปุ่มคลิกขวา)")]
        [SerializeField] private bool _showActionText = false;
        [SerializeField] private string _actionHintText = "Take Needle";

        [Header("Altar Props")]
        [SerializeField] private Transform _needleInPile;
        [SerializeField] private Transform _fishMound;
        [SerializeField] private UnityEngine.Rendering.Universal.Light2D _altarLight;

        [Header("Needle Extraction Juice")]
        [Tooltip("ความสูงที่เข็มเด้งพุ่งขึ้นไปบนอากาศก่อนตกลงมา (เมตร)")]
        [SerializeField] private float _needlePopHeight = 2.0f;
        [Tooltip("เวลาที่เข็มพุ่งขึ้นไปถึงจุดสูงสุด (วินาที)")]
        [SerializeField] private float _needleRiseDuration = 0.30f;
        [Tooltip("เวลาที่เข็มลอยค้างบนอากาศที่จุดสูงสุดก่อนตก (วินาที)")]
        [SerializeField] private float _needleApexHangDuration = 0.10f;
        [Tooltip("เวลาที่เข็มร่วงตกลงมาเข้ามือผู้เล่น (วินาที)")]
        [SerializeField] private float _needleFallDuration = 0.26f;
        [Tooltip("องศาการหมุนควงสว่านของเข็มกลางอากาศ (องศา)")]
        [SerializeField] private float _needleSpinTumbleAngle = 360f;
        [Tooltip("Sorting Layer ของเข็มขณะร่วงตกลงมาเข้ามือผู้เล่น (เปลี่ยนเป็น Player หลังจากลอยค้างกลางอากาศ)")]
        [SerializeField] private string _needleFallSortingLayer = "Player";
        [SerializeField] private int _needleFallSortingOrder = 5;

        [Header("Title Reveal & Camera Zoom")]
        [Tooltip("Close-up camera ortho size during the tense needle extraction struggle.")]
        [SerializeField] private float _pullZoomInOrthoSize = 3.8f;
        [SerializeField] private float _pullZoomInDuration = 0.55f;

        [Tooltip("Wide camera ortho size revealing the surrounding courtyard as logo appears.")]
        [SerializeField] private float _zoomOutOrthoSize = 8.0f;
        [Tooltip("Duration of the slow, atmospheric zoom-out reveal.")]
        [SerializeField] private float _zoomOutDuration = 3.0f;
        [Tooltip("Target camera Y offset while zooming out (เลื่อนกล้องขึ้นด้านบนตอนซูมออก). ค่าปกติกล้องอยู่ที่ 1.2")]
        [SerializeField] private float _zoomOutOffsetY = 3.2f;

        [Tooltip("Delay before logo begins fading in after camera starts zooming out.")]
        [SerializeField] private float _logoFadeInDelay = 0.4f;
        [Tooltip("Duration for the logo to smoothly fade in. ยิ่งปรับมาก โลโก้จะยิ่งค่อยๆ ปรากฏขึ้นช้าลง")]
        [SerializeField] private float _logoFadeInDuration = 2.5f;
        [Tooltip("How long the logo stays fully visible at full opacity before fading out.")]
        [SerializeField] private float _titleLingerDuration = 2.0f;
        [Tooltip("Duration for the logo to fade out.")]
        [SerializeField] private float _logoFadeOutDuration = 1.2f;

        [SerializeField] private SpriteRenderer _logoSpriteRenderer;

        [Header("Starting Suture Tether Visual & Physics")]
        [Tooltip("Prefab ของ SutureThread ที่เชื่อมต่อระหว่างตัวผู้เล่นกับ Needle_In_Pile ตั้งแต่เริ่มเกม")]
        [SerializeField] private SutureThread _sutureThreadPrefab;

        [Tooltip("จำนวน Verlet Nodes (ยิ่งเยอะยิ่งละเอียด โค้งมนนุ่มนวล ไม่เป็นท่อนเหลี่ยม แนะนำ 48 - 64)")]
        [SerializeField] private int _startingThreadNodes = 52;

        [Tooltip("โหมดการคำนวณความหย่อน: PathContour (แนะนำ) อิงตามความยาวแนวภูมิประเทศจริง, StraightLine อิงระยะขจัดเส้นตรง")]
        [SerializeField] private SutureThread.SlackMode _slackCalculationMode = SutureThread.SlackMode.PathContour;

        [Tooltip("ความหย่อนของด้าย (Slack factor): 1.0 = แนบพอดีตามแนวผิว, 1.03 - 1.08 = หย่อนทิ้งตัวพาดพื้นนุ่มนวล")]
        [SerializeField] private float _startingThreadSlack = 1.04f;

        [Tooltip("ความหย่อนส่วนเกินคงที่ (เมตร)")]
        [SerializeField] private float _extraSlackMeters = 0.45f;

        [Tooltip("แรงต้านการดัดโค้ง (Bending Stiffness): ยิ่งน้อย ยิ่งอ่อนยวบเหมือนเส้นไหม ไม่กระด้าง (แนะนำ 0.02 - 0.05)")]
        [SerializeField] private float _startingBendingStiffness = 0.03f;

        [Tooltip("แรงโน้มถ่วงดึงด้าย (Gravity): ปรับให้ด้ายทิ้งตัวลงพื้นอย่างเป็นธรรมชาติ (แนะนำ 9.8 - 14.0)")]
        [SerializeField] private float _startingGravity = 12.0f;

        [Tooltip("แรงต้านอากาศ (Air Damping): ค่าน้อยจะพลิ้วไหวตามการเคลื่อนไหว ไม่หนืดช้า (แนะนำ 1.5 - 2.2)")]
        [SerializeField] private float _startingAirDamping = 1.8f;

        [Tooltip("ความสูงของคลื่นลมที่ทำให้เส้นด้ายลอยพริ้วไหวขณะ Gravity ติดลบ (เมตร)")]
        [SerializeField] private float _startingFloatingWaveAmplitude = 0.12f;

        [Tooltip("ความเร็วในการกระเพื่อมคลื่นลมลอย")]
        [SerializeField] private float _startingFloatingWaveSpeed = 1.6f;

        [Tooltip("เปิดใช้งาน Pixel Snapping หรือไม่ (หากปิด จะได้เส้นด้ายโค้งมนสมูทแบบเวกเตอร์ ไหลลื่นไม่กระตุกเป็นขั้นบันไดพิกเซล)")]
        [SerializeField] private bool _enablePixelSnapping = false;

        [Tooltip("ระดับความสูงของจุดเชื่อมต่อบนตัวผู้เล่น (เทียบจาก transform.position ผู้เล่นมีจุดศูนย์กลางตัวอยู่ที่ 0.0, หน้าอก/ลำตัวอยู่ที่ -0.05 ถึง 0.05)")]
        [SerializeField] private float _playerTetherHeightOffset = -0.05f;

        [Tooltip("จุด Transform ของรูเข็ม (ถ้ามี จะยึดตำแหน่งจากจุดนี้โดยตรง ทำให้ลากจัดตำแหน่งใน Scene ได้ทันที)")]
        [SerializeField] private Transform _needleThreadAnchor;

        [Tooltip("ตำแหน่ง Offset รูเข็มบน Needle_In_Pile (โดยปกติจุด Pivot (0,0,0) คือรูเข็มพอดี)")]
        [SerializeField] private Vector3 _needleEyeLocalOffset = Vector3.zero;

        [Tooltip("แสดงจุดปมด้ายสีแดง (NeedlePin_2D) ที่รูเข็มหรือไม่ (ถ้า false จะซ่อนปม ให้เห็นเฉพาะเข็มปัก)")]
        [SerializeField] private bool _showNeedlePinDot = true;

        [Header("Checkpoint")]
        [SerializeField] private Checkpoint _altarCheckpoint;

        [Header("Audio (Optional)")]
        [SerializeField] private AudioSource _audioSource;
        [SerializeField] private AudioClip _fleshTugSfx;
        [SerializeField] private AudioClip _needleExtractSfx;
        [SerializeField] private AudioClip _titleRevealSfx;

        private Transform _playerTransform;
        private PlayerController2D _playerController;
        private PlayerVisuals _playerVisuals;
        private NeedleController _needleController;
        private Camera _mainCamera;

        private CanvasGroup _promptCanvasGroup;
        private RectTransform _promptRect;
        private SutureThread _startingThreadInstance;

        private bool _isPlayerInRange;
        private bool _isSequenceActive;
        private bool _isClaimed;
        private string _originalNeedleSortingLayer = "Default";
        private int _originalNeedleSortingOrder = 3;

        public bool IsClaimed => _isClaimed;

        /// <summary>
        /// Public trigger for testing or cutscene orchestration.
        /// </summary>
        public void TriggerPickup()
        {
            if (!_isClaimed && !_isSequenceActive)
            {
                StartCoroutine(ExecutePickupSequence());
            }
        }

        private void Start()
        {
            FindPlayerReferences();

            if (_needleInPile != null && _needleInPile.TryGetComponent(out SpriteRenderer nsr))
            {
                _originalNeedleSortingLayer = nsr.sortingLayerName;
                _originalNeedleSortingOrder = nsr.sortingOrder;
            }

            if (_logoSpriteRenderer != null)
            {
                Color c = _logoSpriteRenderer.color;
                c.a = 0f;
                _logoSpriteRenderer.color = c;
            }

            SetupPromptCanvas();

            // Check if player already owns the needle (e.g. after respawn)
            if (_isClaimed || (_needleController != null && _needleController.enabled && _isClaimed))
            {
                HideAltarPropsPermanently();
            }
            else
            {
                InitializeStartingTether();
            }
        }

        private void InitializeStartingTether()
        {
            if (_isClaimed || _startingThreadInstance != null) return;
            if (_sutureThreadPrefab == null)
            {
                Debug.LogWarning("[NeedlePickupAltar] _sutureThreadPrefab is not assigned on DeadFishAltar! Starting suture tether will not spawn.", this);
                return;
            }

            _startingThreadInstance = Instantiate(_sutureThreadPrefab, transform);
            _startingThreadInstance.name = "StartingSutureTether";
            _startingThreadInstance.NodesPerSegment = _startingThreadNodes;
            _startingThreadInstance.SlackCalculationMode = _slackCalculationMode;
            _startingThreadInstance.SlackFactor = _startingThreadSlack;
            _startingThreadInstance.ExtraSlackMeters = _extraSlackMeters;
            _startingThreadInstance.BendingStiffness = _startingBendingStiffness;
            _startingThreadInstance.Gravity = _startingGravity;
            _startingThreadInstance.AirDamping = _startingAirDamping;
            _startingThreadInstance.FloatingWaveAmplitude = _startingFloatingWaveAmplitude;
            _startingThreadInstance.FloatingWaveSpeed = _startingFloatingWaveSpeed;
            _startingThreadInstance.IsCuttable = false;

            UpdateStartingTether();
        }

        private void UpdateStartingTether()
        {
            if (_startingThreadInstance == null || _isClaimed) return;

            FindPlayerReferences();
            if (_playerTransform == null) return;

            // Allow live Inspector tuning during Play Mode
            _startingThreadInstance.SlackCalculationMode = _slackCalculationMode;
            _startingThreadInstance.SlackFactor = _startingThreadSlack;
            _startingThreadInstance.ExtraSlackMeters = _extraSlackMeters;
            _startingThreadInstance.BendingStiffness = _startingBendingStiffness;
            _startingThreadInstance.Gravity = _startingGravity;
            _startingThreadInstance.AirDamping = _startingAirDamping;
            _startingThreadInstance.FloatingWaveAmplitude = _startingFloatingWaveAmplitude;
            _startingThreadInstance.FloatingWaveSpeed = _startingFloatingWaveSpeed;
            _startingThreadInstance.EnablePixelSnapping = _enablePixelSnapping;
            _startingThreadInstance.HideAllPins = !_showNeedlePinDot;

            Vector2 playerTetherPos = (Vector2)_playerTransform.position + new Vector2(0f, _playerTetherHeightOffset);
            Vector2 needleEyePos;
            if (_needleThreadAnchor != null)
            {
                needleEyePos = _needleThreadAnchor.position;
            }
            else if (_needleInPile != null)
            {
                needleEyePos = _needleInPile.TransformPoint(_needleEyeLocalOffset);
            }
            else
            {
                needleEyePos = transform.position;
            }

            _startingThreadInstance.UpdateDynamicEndpoints(needleEyePos, playerTetherPos, hideEndPin: true);
        }

        private void SetupPromptCanvas()
        {
            if (_promptCanvasGo == null)
            {
                var childCanvas = GetComponentInChildren<Canvas>(true);
                if (childCanvas != null) _promptCanvasGo = childCanvas.gameObject;
            }

            if (_promptCanvasGo != null)
            {
                // Disable InteractionPromptUI script on this dedicated Altar canvas so it doesn't fight our custom prompt
                var promptUI = _promptCanvasGo.GetComponentInChildren<ThailandHorrorJam.Systems.Interaction.InteractionPromptUI>(true);
                if (promptUI != null) promptUI.enabled = false;

                _promptCanvasGroup = _promptCanvasGo.GetComponentInChildren<CanvasGroup>(true);
                if (_promptCanvasGroup != null) _promptCanvasGroup.alpha = 0f;

                var promptRootT = _promptCanvasGo.transform.Find("PromptRoot");
                if (promptRootT != null)
                {
                    _promptRect = promptRootT as RectTransform;

                    var dot = promptRootT.Find("DotImage");
                    if (dot != null) dot.gameObject.SetActive(false);

                    var left = promptRootT.Find("LeftClickImage");
                    if (left != null) left.gameObject.SetActive(false);

                    var right = promptRootT.Find("RightClickImage");
                    if (right != null)
                    {
                        right.gameObject.SetActive(true);
                        var rtRight = right as RectTransform;
                        if (rtRight != null && !_showActionText)
                        {
                            rtRight.anchorMin = new Vector2(0.5f, 0.5f);
                            rtRight.anchorMax = new Vector2(0.5f, 0.5f);
                            rtRight.pivot = new Vector2(0.5f, 0.5f);
                            rtRight.anchoredPosition = Vector2.zero;
                        }

                        var rightImg = right.GetComponent<UnityEngine.UI.Image>();
                        if (rightImg != null)
                        {
                            Color rc = rightImg.color;
                            rc.a = 1f;
                            rightImg.color = rc;
                        }
                    }

                    var textObj = promptRootT.Find("ActionHintText");
                    if (textObj != null)
                    {
                        textObj.gameObject.SetActive(_showActionText);
                        if (_showActionText)
                        {
                            var tmp = textObj.GetComponent<TMPro.TextMeshProUGUI>();
                            if (tmp != null)
                            {
                                tmp.text = _actionHintText;
                                tmp.enabled = true;
                                Color tc = tmp.color;
                                tc.a = 1f;
                                tmp.color = tc;
                            }
                        }
                    }
                }
            }
        }

        private void FindPlayerReferences()
        {
            if (_playerTransform == null)
            {
                var player = GameObject.FindWithTag("Player");
                if (player != null)
                {
                    _playerTransform = player.transform;
                    _playerController = player.GetComponent<PlayerController2D>();
                    _playerVisuals = player.GetComponentInChildren<PlayerVisuals>();
                    _needleController = player.GetComponent<NeedleController>();
                }
            }

            if (_mainCamera == null)
            {
                _mainCamera = Camera.main != null ? Camera.main : FindAnyObjectByType<Camera>();
            }
        }

        private void Update()
        {
            if (_isClaimed || _isSequenceActive) return;

            FindPlayerReferences();
            if (_playerTransform == null) return;

            float dist = Vector2.Distance(transform.position, _playerTransform.position);
            _isPlayerInRange = dist <= _interactionRadius;

            float targetAlpha = (_isPlayerInRange && !_isSequenceActive && !_isClaimed) ? 1f : 0f;
            if (_promptCanvasGroup != null)
            {
                _promptCanvasGroup.alpha = Mathf.MoveTowards(_promptCanvasGroup.alpha, targetAlpha, Time.unscaledDeltaTime * 6f);
            }

            if (_isPlayerInRange && Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame)
            {
                StartCoroutine(ExecutePickupSequence());
            }
        }

        private void LateUpdate()
        {
            if (!_isClaimed)
            {
                UpdateStartingTether();
            }

            if (_promptCanvasGroup == null || _promptCanvasGroup.alpha <= 0.001f || _promptRect == null) return;

            if (_mainCamera == null)
            {
                _mainCamera = Camera.main != null ? Camera.main : FindAnyObjectByType<Camera>();
                if (_mainCamera == null) return;
            }

            Vector3 worldTarget = _needleInPile != null ? _needleInPile.position : transform.position;
            Vector3 screenPos = _mainCamera.WorldToScreenPoint(worldTarget + Vector3.up * _promptAboveOffset);
            _promptRect.position = screenPos;
        }

        private IEnumerator ExecutePickupSequence()
        {
            _isSequenceActive = true;
            if (_promptCanvasGroup != null) _promptCanvasGroup.alpha = 0f;

            // 1. Lock player & align facing
            if (_playerController != null)
            {
                _playerController.SetInputLocked(true);
                _playerController.StopHorizontalMovement();
            }

            bool faceRight = transform.position.x >= _playerTransform.position.x;
            if (_playerVisuals != null)
            {
                _playerVisuals.SetFacing(faceRight);
            }

            // =========================================================
            // PHASE 1: Zoom In Close & Horror Stinger Tug (ซูมเข้าตอนกำลังดึง)
            // =========================================================
            // 1.1 Zoom in close to focus on the grotesque struggle
            CameraController2D.Instance?.ZoomTo(_pullZoomInOrthoSize, _pullZoomInDuration, Ease.OutQuad);

            if (_audioSource != null && _fleshTugSfx != null)
            {
                _audioSource.PlayOneShot(_fleshTugSfx);
            }

            // Player crouches / leans slightly forward towards the altar
            Vector3 originalPlayerScale = _playerTransform.localScale;
            _playerTransform.DOScale(new Vector3(originalPlayerScale.x * 1.12f, originalPlayerScale.y * 0.88f, originalPlayerScale.z), 0.25f);
            
            // Initial stuck tug: tremor in needle & camera micro shake
            if (_needleInPile != null)
            {
                _needleInPile.DOPunchRotation(new Vector3(0f, 0f, 15f), 0.25f, 12, 1f);
            }
            CameraController2D.Instance?.Shake(0.12f, 0.25f);
            yield return new WaitForSeconds(0.32f);

            // 1.2 Violent yank: needle wrenches free into an arc towards player's hand position
            if (_audioSource != null && _needleExtractSfx != null)
            {
                _audioSource.PlayOneShot(_needleExtractSfx);
            }

            CameraController2D.Instance?.Shake(0.28f, 0.35f);
            CameraController2D.Instance?.Punch(faceRight ? Vector2.left : Vector2.right, 0.18f);

            // Player recoils backward and stands tall
            _playerTransform.DOScale(new Vector3(originalPlayerScale.x * 0.92f, originalPlayerScale.y * 1.15f, originalPlayerScale.z), 0.18f)
                .OnComplete(() => _playerTransform.DOScale(originalPlayerScale, 0.25f));

            if (_needleInPile != null)
            {
                Vector3 startPos = _needleInPile.position;
                Vector3 targetHandPos = _playerTransform.position + new Vector3(faceRight ? 0.35f : -0.35f, 0.5f, 0f);
                Vector3 apexPos = new Vector3(
                    Mathf.Lerp(startPos.x, targetHandPos.x, 0.35f),
                    startPos.y + _needlePopHeight,
                    startPos.z
                );

                float spinDir = faceRight ? -_needleSpinTumbleAngle : _needleSpinTumbleAngle;

                Sequence extractSeq = DOTween.Sequence();

                // 1. Burst up violently into apex with rapid tumble
                extractSeq.Append(_needleInPile.DOMove(apexPos, _needleRiseDuration).SetEase(Ease.OutQuad));
                extractSeq.Join(_needleInPile.DORotate(new Vector3(0f, 0f, spinDir * 0.5f), _needleRiseDuration, RotateMode.LocalAxisAdd).SetEase(Ease.OutQuad));

                // 2. Micro hang / float at apex
                if (_needleApexHangDuration > 0.001f)
                {
                    extractSeq.AppendInterval(_needleApexHangDuration);
                }

                // 2.1 After apex hang, switch sorting layer to "Player" so it renders in front of player
                extractSeq.AppendCallback(() =>
                {
                    if (_needleInPile != null && _needleInPile.TryGetComponent(out SpriteRenderer sr))
                    {
                        sr.sortingLayerName = _needleFallSortingLayer;
                        sr.sortingOrder = _needleFallSortingOrder;
                    }
                });

                // 3. Accelerate and plummet down into player's hands
                extractSeq.Append(_needleInPile.DOMove(targetHandPos, _needleFallDuration).SetEase(Ease.InQuad));
                extractSeq.Join(_needleInPile.DORotate(new Vector3(0f, 0f, spinDir * 0.5f), _needleFallDuration, RotateMode.LocalAxisAdd).SetEase(Ease.InQuad));

                extractSeq.OnComplete(() =>
                {
                    _needleInPile.gameObject.SetActive(false);

                    // Tactile impact recoil on player catch
                    if (_playerTransform != null)
                    {
                        _playerTransform.DOPunchScale(new Vector3(0.12f, -0.12f, 0f), 0.22f, 8, 0.5f);
                    }
                    CameraController2D.Instance?.Shake(0.16f, 0.2f);

                    if (_startingThreadInstance != null)
                    {
                        var tetherToClean = _startingThreadInstance;
                        _startingThreadInstance = null;
                        tetherToClean.ReelInTo(targetHandPos, 0.18f, () =>
                        {
                            if (tetherToClean != null)
                            {
                                Destroy(tetherToClean.gameObject);
                            }
                        });
                    }
                });
            }

            float totalAirTime = _needleRiseDuration + _needleApexHangDuration + _needleFallDuration + 0.12f;
            yield return new WaitForSeconds(totalAirTime);

            // =========================================================
            // PHASE 2: Slow Zoom Out & Title Reveal (ได้เข็มแล้ว ค่อยๆ ซูมออกช้าๆ แสดงโลโก้)
            // =========================================================
            if (_audioSource != null && _titleRevealSfx != null)
            {
                _audioSource.PlayOneShot(_titleRevealSfx);
            }

            // Camera slowly zooms out to wide perspective and shifts upwards in Y
            CameraController2D.Instance?.ZoomTo(_zoomOutOrthoSize, _zoomOutDuration, Ease.InOutCubic);
            CameraController2D.Instance?.ShiftOffsetY(_zoomOutOffsetY, _zoomOutDuration, Ease.InOutCubic);

            // Game Logo fades in above player's head while camera is smoothly zooming out
            if (_logoSpriteRenderer != null)
            {
                _logoSpriteRenderer.DOFade(1f, _logoFadeInDuration).SetDelay(_logoFadeInDelay).SetEase(Ease.InOutSine);
                _logoSpriteRenderer.transform.DOPunchScale(new Vector3(0.08f, 0.08f, 0f), _logoFadeInDuration, 2, 0.5f);
            }

            // Dramatic linger pause while wide (รอดีเลย์ + เวลาที่โลโก้ค่อยๆ ขึ้น + เวลาค้างโลโก้)
            yield return new WaitForSeconds(_logoFadeInDelay + _logoFadeInDuration + _titleLingerDuration);

            // Game Logo fades out
            if (_logoSpriteRenderer != null)
            {
                _logoSpriteRenderer.DOFade(0f, _logoFadeOutDuration).SetEase(Ease.InQuad);
            }

            // Camera smoothly returns back to standard gameplay distance and Y offset
            CameraController2D.Instance?.ResetZoom(1.8f, Ease.InOutQuad);
            CameraController2D.Instance?.ResetOffset(1.8f, Ease.InOutQuad);
            yield return new WaitForSeconds(1.3f);

            // =========================================================
            // PHASE 3: Equip Needle & Save Checkpoint
            // =========================================================
            if (_needleController != null)
            {
                _needleController.enabled = true;
            }

            _isClaimed = true;
            RestorePromptCanvas();

            if (_altarCheckpoint != null && CheckpointManager.Instance != null)
            {
                // Register checkpoint so dying here respawns directly with needle
                _altarCheckpoint.transform.position = _playerTransform.position;
            }

            if (_playerController != null)
            {
                _playerController.SetInputLocked(false);
            }

            _isSequenceActive = false;
        }

        private void HideAltarPropsPermanently()
        {
            if (_startingThreadInstance != null)
            {
                _startingThreadInstance.Clear();
                Destroy(_startingThreadInstance.gameObject);
                _startingThreadInstance = null;
            }
            if (_needleInPile != null)
            {
                if (_needleInPile.TryGetComponent(out SpriteRenderer sr))
                {
                    sr.sortingLayerName = _originalNeedleSortingLayer;
                    sr.sortingOrder = _originalNeedleSortingOrder;
                }
                _needleInPile.gameObject.SetActive(false);
            }
            RestorePromptCanvas();
            if (_logoSpriteRenderer != null)
            {
                _logoSpriteRenderer.gameObject.SetActive(false);
            }
        }

        private void RestorePromptCanvas()
        {
            if (_promptCanvasGo == null) return;

            var promptUI = _promptCanvasGo.GetComponentInChildren<ThailandHorrorJam.Systems.Interaction.InteractionPromptUI>(true);
            if (promptUI != null)
            {
                _promptCanvasGo.SetActive(true);
                promptUI.enabled = true;
                promptUI.ResetDefaultLayout();
            }
            else
            {
                _promptCanvasGo.SetActive(false);
            }
        }

        private void OnDestroy()
        {
            if (_startingThreadInstance != null)
            {
                _startingThreadInstance.Clear();
                Destroy(_startingThreadInstance.gameObject);
                _startingThreadInstance = null;
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.9f, 0.3f, 0.4f, 0.5f);
            Gizmos.DrawWireSphere(transform.position, _interactionRadius);
        }
    }
}
