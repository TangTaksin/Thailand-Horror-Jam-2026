using UnityEngine;
using UnityEngine.Events;

[CreateAssetMenu(fileName = "TransitionEventChannel", menuName = "Events/Transition Event Channel")]
public class TransitionEventChannelSO : ScriptableObject
{
    public UnityAction OnTransitionRequested;

    public bool isTransitioning { get; set; } = false;

    public void RaiseEvent()
    {
        OnTransitionRequested?.Invoke();
    }

    private void OnEnable()
    {
        isTransitioning = false;
    }
}