using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using ThailandHorrorJam.Systems.Needle;

namespace ThailandHorrorJam.Editor.Tests
{
    [TestFixture]
    public class StitchableWaypointNavigationTests
    {
        private GameObject _go;
        private Rigidbody2D _rb;
        private BoxCollider2D _col;
        private StitchableObject _stitchable;
        private MethodInfo _traverseMethod;
        private MethodInfo _fixedUpdateMethod;
        private FieldInfo _isReversingField;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("TestNavBox");
            _go.transform.position = Vector3.zero;

            _rb = _go.AddComponent<Rigidbody2D>();
            _rb.bodyType = RigidbodyType2D.Dynamic;

            _col = _go.AddComponent<BoxCollider2D>();
            _col.size = new Vector2(0.5f, 0.5f);

            _stitchable = _go.AddComponent<StitchableObject>();

            // Invoke Awake via reflection
            typeof(StitchableObject).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.Invoke(_stitchable, null);

            _traverseMethod = typeof(StitchableObject).GetMethod("TraverseWaypoints", BindingFlags.NonPublic | BindingFlags.Instance);
            _fixedUpdateMethod = typeof(StitchableObject).GetMethod("FixedUpdate", BindingFlags.NonPublic | BindingFlags.Instance);
            _isReversingField = typeof(StitchableObject).GetField("_isReversing", BindingFlags.NonPublic | BindingFlags.Instance);
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null)
            {
                Object.DestroyImmediate(_go);
            }
        }

        [Test]
        public void TraverseWaypoints_MovesTowardsTarget()
        {
            var waypoints = new List<Vector2> { Vector2.zero, new Vector2(2f, 0f) };
            _stitchable.StartLoopAlongWaypoints(waypoints, LoopPlaybackMode.PingPong);

            Vector2 initialPos = _stitchable.CurrentPosition;

            // Execute one step of waypoint traversal
            _traverseMethod.Invoke(_stitchable, null);

            Vector2 newPos = _stitchable.CurrentPosition;
            Assert.Greater(newPos.x, initialPos.x, "Box position should advance along the rail towards target waypoint.");
            Assert.AreEqual(0f, newPos.y, 0.001f, "Box Y position should remain on track.");
        }

        [Test]
        public void PingPongNavigation_ReversesDirection_WhenReachingEnd()
        {
            var waypoints = new List<Vector2> { Vector2.zero, new Vector2(0.5f, 0f) };
            _stitchable.StartLoopAlongWaypoints(waypoints, LoopPlaybackMode.PingPong);

            // Advance multiple steps until arrival at waypoint 1
            for (int i = 0; i < 30; i++)
            {
                _traverseMethod.Invoke(_stitchable, null);
            }

            // Once reached, the next traversal step should trigger reversing
            _traverseMethod.Invoke(_stitchable, null);

            bool isReversing = (bool)_isReversingField.GetValue(_stitchable);
            Assert.IsTrue(isReversing, "PingPong loop must reverse direction upon reaching end of track.");
        }

        [Test]
        public void FixedUpdate_CalculatesCurrentVelocityAndFrameDelta()
        {
            var waypoints = new List<Vector2> { Vector2.zero, new Vector2(5f, 0f) };
            _stitchable.StartLoopAlongWaypoints(waypoints, LoopPlaybackMode.PingPong);

            _fixedUpdateMethod.Invoke(_stitchable, null);

            Assert.Greater(_stitchable.FrameDelta.x, 0f, "FrameDelta.x should be positive when moving right.");
            Assert.Greater(_stitchable.CurrentVelocity.x, 0f, "CurrentVelocity.x should be positive when moving right.");
        }

        [Test]
        public void PlayerLandingOnDescendingLoopingBox_IsDetectedAsGroundedImmediately()
        {
            // 1. Setup downward looping box
            _go.layer = LayerMask.NameToLayer("Stitchable");
            var waypoints = new List<Vector2> { new Vector2(0f, 2f), new Vector2(0f, 0f) };
            _stitchable.StartLoopAlongWaypoints(waypoints, LoopPlaybackMode.PingPong);
            _fixedUpdateMethod.Invoke(_stitchable, null);

            // 2. Setup player landing on top
            GameObject playerObj = new GameObject("TestPlayer");
            var playerRb = playerObj.AddComponent<Rigidbody2D>();
            var playerCol = playerObj.AddComponent<CapsuleCollider2D>();
            playerCol.size = new Vector2(0.4f, 0.8f);
            var pc = playerObj.AddComponent<ThailandHorrorJam.Core.PlayerController2D>();
            typeof(ThailandHorrorJam.Core.PlayerController2D).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(pc, null);

            float boxTopY = _col.bounds.max.y;
            playerObj.transform.position = new Vector3(0f, boxTopY + 0.4f, 0f);
            playerRb.linearVelocity = Vector2.zero;
            Physics2D.SyncTransforms();

            // 3. Run CheckGround
            typeof(ThailandHorrorJam.Core.PlayerController2D).GetMethod("CheckGround", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(pc, null);

            Assert.IsTrue(pc.IsGrounded, "Player must be recognized as grounded when landing on descending looping box.");
            Assert.IsNotNull(pc.CurrentStitchable, "CurrentStitchable must be cached on landing.");

            Object.DestroyImmediate(playerObj);
        }

        [Test]
        public void PlayerStandingOnRisingLoopingBox_MaintainsGroundedAndDoesNotTriggerJumpVisualState()
        {
            // 1. Setup upward looping box
            _go.layer = LayerMask.NameToLayer("Stitchable");
            var waypoints = new List<Vector2> { new Vector2(0f, 0f), new Vector2(0f, 5f) };
            _stitchable.StartLoopAlongWaypoints(waypoints, LoopPlaybackMode.PingPong);

            // 2. Setup grounded player with PlayerVisuals
            GameObject playerObj = new GameObject("TestPlayer");
            var playerRb = playerObj.AddComponent<Rigidbody2D>();
            var playerCol = playerObj.AddComponent<CapsuleCollider2D>();
            playerCol.size = new Vector2(0.4f, 0.8f);
            var pc = playerObj.AddComponent<ThailandHorrorJam.Core.PlayerController2D>();
            typeof(ThailandHorrorJam.Core.PlayerController2D).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(pc, null);

            var spriteHolder = new GameObject("VisualHolder");
            spriteHolder.transform.SetParent(playerObj.transform, false);
            var sr = spriteHolder.AddComponent<SpriteRenderer>();

            var visuals = playerObj.AddComponent<ThailandHorrorJam.Core.PlayerVisuals>();
            typeof(ThailandHorrorJam.Core.PlayerVisuals).GetField("_controller", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(visuals, pc);
            typeof(ThailandHorrorJam.Core.PlayerVisuals).GetField("_spriteRenderer", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(visuals, sr);
            typeof(ThailandHorrorJam.Core.PlayerVisuals).GetField("_visualHolder", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(visuals, spriteHolder.transform);

            // Place player resting on box
            float boxTopY = _col.bounds.max.y;
            playerObj.transform.position = new Vector3(0f, boxTopY + 0.4f, 0f);
            playerRb.linearVelocity = Vector2.zero;
            Physics2D.SyncTransforms();

            // Run initial check
            typeof(ThailandHorrorJam.Core.PlayerController2D).GetMethod("CheckGround", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(pc, null);
            typeof(ThailandHorrorJam.Core.PlayerVisuals).GetMethod("UpdateVisualState", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(visuals, null);

            var visualStateField = typeof(ThailandHorrorJam.Core.PlayerVisuals).GetField("_currentState", BindingFlags.NonPublic | BindingFlags.Instance);
            var state = visualStateField?.GetValue(visuals);

            Assert.AreEqual("Idle", state?.ToString(), "Player resting on rising box should be in Idle state.");

            // Step box and player for multiple upward frames
            for (int i = 0; i < 5; i++)
            {
                _fixedUpdateMethod.Invoke(_stitchable, null);
                typeof(ThailandHorrorJam.Core.PlayerController2D).GetMethod("FixedUpdate", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(pc, null);

                // Simulate physics step (velocity integration)
                playerRb.position += playerRb.linearVelocity * 0.02f;
                Physics2D.SyncTransforms();

                typeof(ThailandHorrorJam.Core.PlayerVisuals).GetMethod("UpdateVisualState", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(visuals, null);

                Assert.IsTrue(pc.IsGrounded, $"Player must remain grounded during upward loop (frame {i}).");
                Assert.AreNotEqual("Jump", visualStateField?.GetValue(visuals)?.ToString(), $"Visual state must never enter Jump while riding a rising box (frame {i}).");
            }

            Object.DestroyImmediate(playerObj);
        }
    }
}
