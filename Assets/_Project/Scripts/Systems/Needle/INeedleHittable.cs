using UnityEngine;

namespace ThailandHorrorJam.Systems.Needle
{
    /// <summary>
    /// Decoupled interface for entities that can be struck by the Needle projectile
    /// (e.g. falling fish hazards, bell chime bells, interactive targets).
    /// </summary>
    public interface INeedleHittable
    {
        /// <summary>
        /// Called when a flying NeedleProjectile strikes this entity.
        /// </summary>
        /// <param name="projectile">The projectile instance that struck this object.</param>
        /// <param name="hitPoint">The world position of the impact point.</param>
        /// <param name="hitDirection">The normalized velocity direction of the needle.</param>
        /// <returns>True if the hit was accepted and absorbed; false to ignore.</returns>
        bool OnHitByNeedle(NeedleProjectile projectile, Vector2 hitPoint, Vector2 hitDirection);
    }
}
