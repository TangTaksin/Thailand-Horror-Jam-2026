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
    private Coroutine _activeCoroutine;

    private void Awake()
    {
        transitionShaderID = Shader.PropertyToID(transitionName);

        if (transitionImage != null && transitionImage.material != null)
        {
            internalMaterial = new Material(transitionImage.material);
            transitionImage.material = internalMaterial;
        }
    }

    private void OnDestroy()
    {
        if (internalMaterial != null)
        {
            Destroy(internalMaterial);
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
            if (eventChannel != null) eventChannel.isTransitioning = true;
            UpdateShaderValue(0f);
            if (_activeCoroutine != null) StopCoroutine(_activeCoroutine);
            _activeCoroutine = StartCoroutine(DoTransition(playForward: true));
        }
        else
        {
            UpdateShaderValue(1f);
            if (eventChannel != null) eventChannel.isTransitioning = false;
        }
    }

    public void PlayTransition()
    {
        if (_activeCoroutine != null) StopCoroutine(_activeCoroutine);
        _activeCoroutine = StartCoroutine(DoTransition(playForward: false));
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
        _activeCoroutine = null;
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
