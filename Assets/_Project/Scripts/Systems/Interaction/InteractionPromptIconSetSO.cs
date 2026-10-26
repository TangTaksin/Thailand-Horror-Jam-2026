using UnityEngine;

namespace ThailandHorrorJam.Systems.Interaction
{
    [CreateAssetMenu(fileName = "DefaultInteractionIcons", menuName = "Data/Interaction Prompt Icon Set")]
    public sealed class InteractionPromptIconSetSO : ScriptableObject
    {
        [SerializeField] private Sprite _dotSprite;
        [SerializeField] private Sprite _leftClickSprite;
        [SerializeField] private Sprite _rightClickSprite;

        public Sprite DotSprite => _dotSprite;
        public Sprite LeftClickSprite => _leftClickSprite;
        public Sprite RightClickSprite => _rightClickSprite;
    }
}
