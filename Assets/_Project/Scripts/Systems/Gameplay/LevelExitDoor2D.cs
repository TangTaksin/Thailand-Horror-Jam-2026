using System;
using System.Collections;
using UnityEngine;
using ThailandHorrorJam.Core;

namespace ThailandHorrorJam.Systems.Gameplay
{
    /// <summary>
    /// The final exit portal (The Suture Gate) at the climax of Sector 06.
    /// When the player crosses the crate bridge and walks through this gate, it locks
    /// input, triggers the screen pixel transition, and activates the automatic ending cutscene.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public sealed class LevelExitDoor2D : MonoBehaviour
    {
        [Header("Transition & Ending")]
        [SerializeField] private TransitionEventChannelSO _transitionEventChannel;
        [SerializeField] private EndingCutsceneController _endingCutscene;

        [Header("Timing")]
        [Tooltip("Delay in seconds after entering doorway before the ending cutscene starts.")]
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
            yield return new WaitForSeconds(_transitionDelay);

            if (_endingCutscene != null)
            {
                _endingCutscene.StartCutscene();
            }
            else
            {
                // Fallback: locate EndingCutsceneController in scene if not directly wired
                EndingCutsceneController found = FindAnyObjectByType<EndingCutsceneController>();
                found?.StartCutscene();
            }
        }

        public void SetEndingCutscene(EndingCutsceneController cutscene)
        {
            _endingCutscene = cutscene;
        }

        public void SetTransitionChannel(TransitionEventChannelSO channel)
        {
            _transitionEventChannel = channel;
        }
    }
}
