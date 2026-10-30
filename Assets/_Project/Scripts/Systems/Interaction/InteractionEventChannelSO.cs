using System;
using UnityEngine;

namespace ThailandHorrorJam.Systems.Interaction
{
    [CreateAssetMenu(fileName = "InteractionEventChannel", menuName = "Events/Interaction Event Channel")]
    public sealed class InteractionEventChannelSO : ScriptableObject
    {
        private InteractionFocusData _lastFocusData = InteractionFocusData.Hidden;

        public event Action<InteractionFocusData> FocusChanged;
        public InteractionFocusData LastFocusData => _lastFocusData;

        private void OnEnable() => _lastFocusData = InteractionFocusData.Hidden;

        public void RaiseFocusChanged(InteractionFocusData focusData)
        {
            _lastFocusData = focusData;
            FocusChanged?.Invoke(focusData);
        }
    }
}
