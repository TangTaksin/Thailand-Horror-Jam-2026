using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Events;

public class SceneSwitcher : MonoBehaviour
{
    [Header("Event Channel")]
    [SerializeField] private TransitionEventChannelSO eventChannel;

    [Header("Timing Control")]
    [SerializeField] private bool useAnimationDuration = true;
    [SerializeField] private float customWaitTime = 1f;

    public void LoadNextScene()
    {
        if (eventChannel != null && eventChannel.isTransitioning) return;
        StartCoroutine(WaitAndChangeScene(SceneManager.GetActiveScene().buildIndex + 1));
    }

    public void ToPreviousScene()
    {
        if (eventChannel != null && eventChannel.isTransitioning) return;
        StartCoroutine(WaitAndChangeScene(SceneManager.GetActiveScene().buildIndex - 1));
    }

    private IEnumerator WaitAndChangeScene(int targetBuildIndex)
    {
        if (targetBuildIndex < 0 || targetBuildIndex >= SceneManager.sceneCountInBuildSettings)
        {
            Debug.LogWarning($"ไม่สามารถย้ายไปฉาก Index ที่ {targetBuildIndex} ได้");
            yield break;
        }

        if (eventChannel != null)
        {
            eventChannel.isTransitioning = true;
            eventChannel.RaiseEvent();
        }

        if (useAnimationDuration)
        {
            SceneTransition transition = FindAnyObjectByType<SceneTransition>();

            if (transition != null)
            {
                bool isAnimationDone = false;
                UnityAction action = null;

                action = () =>
                {
                    isAnimationDone = true;
                    transition.OnTransitionDone.RemoveListener(action);
                };

                transition.OnTransitionDone.AddListener(action);
                yield return new WaitUntil(() => isAnimationDone);
            }
            else
            {
                yield return null;
            }
        }
        else
        {
            yield return new WaitForSeconds(customWaitTime);
        }

#if UNITY_EDITOR
        UnityEditor.Selection.activeObject = null;
#endif
        SceneManager.LoadScene(targetBuildIndex);
    }
}