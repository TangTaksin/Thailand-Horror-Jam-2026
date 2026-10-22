using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;

public class SceneTransition : MonoBehaviour
{
    [Header("Event Channel")]
    [SerializeField] private TransitionEventChannelSO eventChannel;

    [Header("UI Settings")]
    [SerializeField] private Image transitionImage;

    [Header("Shader Settings")]
    [SerializeField] private float transitionDuration = 1f;
    [SerializeField] private string transitionName = "_TransValuePixel";

    [Header("Automation")]
    [SerializeField] private bool enableAutoPlay = true;

    [Header("Events")]
    public UnityEvent OnTransitionDone;

    private int transitionShaderID;
    private Material internalMaterial;

    private void Awake()
    {
        transitionShaderID = Shader.PropertyToID(transitionName);

        if (transitionImage != null)
        {
            internalMaterial = transitionImage.material;
        }
    }

    private void OnEnable()
    {
        if (eventChannel != null) eventChannel.OnTransitionRequested += PlayTransition;
    }

    private void OnDisable()
    {
        if (eventChannel != null) eventChannel.OnTransitionRequested -= PlayTransition;
    }

    private void Start()
    {
        if (enableAutoPlay)
        {
            UpdateShaderValue(0f);
            StartCoroutine(DoTransition(playForward: true));
        }
        else
        {
            UpdateShaderValue(1f);
            if (eventChannel != null) eventChannel.isTransitioning = false;
        }
    }

    public void PlayTransition()
    {
        StartCoroutine(DoTransition(playForward: false));
    }

    private IEnumerator DoTransition(bool playForward)
    {
        float elapsedTime = 0f;

        while (elapsedTime < transitionDuration)
        {
            elapsedTime += Time.deltaTime;
            float progress = Mathf.Clamp01(elapsedTime / transitionDuration);
            float finalValue = playForward ? progress : (1f - progress);

            UpdateShaderValue(finalValue);
            yield return null;
        }

        UpdateShaderValue(playForward ? 1f : 0f);
        OnTransitionDone?.Invoke();
        if (playForward && eventChannel != null)
        {
            eventChannel.isTransitioning = false;
        }
    }

    private void UpdateShaderValue(float value)
    {
        if (internalMaterial != null)
        {
            internalMaterial.SetFloat(transitionShaderID, value);
        }
    }
}