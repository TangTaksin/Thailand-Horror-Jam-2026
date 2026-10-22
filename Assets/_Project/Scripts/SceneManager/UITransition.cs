using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class UITransition : MonoBehaviour
{
    [SerializeField] private float transitionTime;    // The duration of the UI transition.
    [SerializeField] private RectTransform rectTransform; // The RectTransform of the UI element.
    private Vector3 startPosition; // The initial position of the UI element.

    // This method is called when the script is initialized.
    private void Awake()
    {
        // Store the initial position of the UI element.
        startPosition = rectTransform.anchoredPosition;
    }

    // This method can be called to initiate the UI transition (e.g., sliding in).
    public void ShowUI()
    {
        StartCoroutine(ShowUIProgress());
    }

    // Coroutine for animating the UI transition.
    private IEnumerator ShowUIProgress()
    {
        float currentTime = 0;

        // Continue until the current time reaches the specified transition time.
        while (currentTime < transitionTime)
        {
            currentTime += Time.deltaTime;

            // Interpolate the position of the UI element from the initial position to Vector3.zero.
            rectTransform.anchoredPosition = Vector3.Lerp(startPosition, Vector3.zero, Mathf.Clamp01(currentTime / transitionTime));

            // Yielding here allows the coroutine to pause and continue in the next frame.
            yield return null;
        }
    }
}
