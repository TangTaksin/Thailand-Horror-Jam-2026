using System.Collections.Generic;
using UnityEngine;

namespace ThailandHorrorJam.Systems.Needle
{
    public enum StitchState
    {
        Idle,
        Tethered,
        Looping,
        PlayerHarness
    }

    public enum LoopPlaybackMode
    {
        PingPong,         // Slides back and forth along the rail (e.g. A -> B -> C -> B -> A)
        RestartFromStart  // Teleports back to start once reaching the end
    }

    /// <summary>
    /// Contract for any world entity that can be tethered and scripted into a movement loop via the needle.
    /// </summary>
    public interface IStitchable
    {
        StitchState CurrentState { get; }
        Transform ObjectTransform { get; }
        Vector2 CurrentPosition { get; }
        void AttachThread();
        void UpdatePinnedWaypoints(IReadOnlyList<Vector2> waypoints);
        void UpdateInFlightNeedle(Vector2 needlePos);
        void StartLoopAlongWaypoints(IReadOnlyList<Vector2> waypoints, LoopPlaybackMode mode);
        void StartPlayerTether(Transform playerTransform, Rigidbody2D playerRb);
        void ReleasePlayerTether();
        void CutThread();
    }
}
