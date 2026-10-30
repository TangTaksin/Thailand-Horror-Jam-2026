using ThailandHorrorJam.Systems.Needle;

namespace ThailandHorrorJam.Systems.Interaction
{
    public enum PromptStage
    {
        Hidden = 0,
        AwareDot = 1,
        InteractIcon = 2,
        AimingPrompt = 3 // คลิกซ้าย=ยิงเข็ม, คลิกขวา=ยกเลิกเล็ง
    }

    public readonly struct InteractionFocusData
    {
        public StitchableObject Target { get; }
        public PromptStage Stage { get; }

        public InteractionFocusData(StitchableObject target, PromptStage stage)
        {
            Target = target;
            Stage = stage;
        }

        public static InteractionFocusData Hidden => new InteractionFocusData(null, PromptStage.Hidden);
    }
}
