using System;
using System.Collections;
using UnityEngine;
using ThailandHorrorJam.Core;
using ThailandHorrorJam.Systems.UI;

namespace ThailandHorrorJam.Systems.Gameplay
{
    /// <summary>
    /// The final exit portal (The Suture Gate) at the climax of Sector 06.
    /// When the player crosses the crate bridge and walks through this gate, it locks
    /// input, triggers a blinding white fade transition, and activates the automatic ending cutscene.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public sealed class LevelExitDoor2D : MonoBehaviour
    {
        [Header("Transition & Ending")]
        [SerializeField] private TransitionEventChannelSO _transitionEventChannel;
        [SerializeField] private CutsceneController _endingCutscene;

        [Header("White Fade Transition")]
        [Tooltip("If true, performs a white screen fade transition before the ending cutscene begins.")]
        [SerializeField] private bool _fadeWhiteBeforeCutscene = true;

        [Tooltip("Duration of the fade to solid white (seconds).")]
        [SerializeField] private float _whiteFadeDuration = 1.0f;

        [Tooltip("Hold duration in pure white screen before cutscene begins (seconds).")]
        [SerializeField] private float _whiteHoldDuration = 0.35f;

        [Tooltip("Duration to fade in from white into the ending cutscene (seconds).")]
        [SerializeField] private float _whiteFadeInDuration = 0.75f;

        [Header("Timing")]
        [Tooltip("Delay in seconds after entering doorway before the transition sequence starts.")]
        [SerializeField] private float _transitionDelay = 0.85f;

        private Collider2D _doorCollider;
        private bool _isTriggered;

        public bool IsTriggered => _isTriggered;
        public bool IsExitTriggered => _isTriggered;

        public event Action OnDoorEntered;

        private void Awake()
        {
            _doorCollider = GetComponent<Collider2D>();
            if (_doorCollider != null)
            {
                _doorCollider.isTrigger = true;
            }
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (_isTriggered || other == null) return;

            if (PlayerController2D.IsPlayer(other))
            {
                PlayerController2D player = other.GetComponentInParent<PlayerController2D>();
                TriggerDoor(player);
            }
        }

        /// <summary>
        /// Triggers the exit door sequence, locking player input and initiating the ending cutscene.
        /// </summary>
        public void TriggerExit(PlayerController2D player) => TriggerDoor(player);

        /// <summary>
        /// Triggers the exit door sequence, locking player input and initiating the ending cutscene.
        /// </summary>
        public void TriggerDoor(PlayerController2D player)
        {
            if (_isTriggered) return;
            _isTriggered = true;

            if (_doorCollider != null)
            {
                _doorCollider.enabled = false;
            }

            if (player != null)
            {
                player.SetInputLocked(true);
            }

            if (_transitionEventChannel != null)
            {
                _transitionEventChannel.RaiseEvent();
            }

            OnDoorEntered?.Invoke();

            StartCoroutine(StartEndingSequenceRoutine());
        }

        private IEnumerator StartEndingSequenceRoutine()
        {
            if (_transitionDelay > 0f)
            {
                yield return new WaitForSeconds(_transitionDelay);
            }

            // 1. Fade to blinding white before cutscene
            if (_fadeWhiteBeforeCutscene && ScreenFader.Instance != null)
            {
                bool fadeOutDone = false;
                ScreenFader.Instance.FadeOutWhite(_whiteFadeDuration, () => fadeOutDone = true);
                while (!fadeOutDone)
                {
                    yield return null;
                }

                if (_whiteHoldDuration > 0f)
                {
                    yield return new WaitForSeconds(_whiteHoldDuration);
                }
            }

            // 2. Deactivate all gameplay sectors when entering the ending sequence
            LevelSeamlessManager.Instance?.DeactivateAllSectors();

            // 3. Start ending cutscene while screen is fully white
            CutsceneController cutscene = _endingCutscene != null
                ? _endingCutscene
                : FindAnyObjectByType<CutsceneController>();

            if (cutscene != null)
            {
                cutscene.StartCutscene();
            }

            // 4. Fade smoothly from white to reveal the ending cutscene
            if (_fadeWhiteBeforeCutscene && ScreenFader.Instance != null)
            {
                ScreenFader.Instance.FadeIn(_whiteFadeInDuration);
            }
        }

        public void SetEndingCutscene(CutsceneController cutscene)
        {
            _endingCutscene = cutscene;
        }

        public void SetTransitionChannel(TransitionEventChannelSO channel)
        {
            _transitionEventChannel = channel;
        }
    }
}
