namespace ThailandHorrorJam.Systems.Gameplay
{
    /// <summary>
    /// Interface for any puzzle or objective object that can permanently dismiss an associated tutorial hint.
    /// </summary>
    public interface IHintDismissCondition
    {
        /// <summary>
        /// When true, the associated hint trigger is permanently dismissed and will not show again.
        /// </summary>
        bool IsHintDismissed { get; }
    }
}
