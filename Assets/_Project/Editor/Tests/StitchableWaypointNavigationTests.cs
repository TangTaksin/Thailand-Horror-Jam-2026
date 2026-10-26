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

            // Advance steps until arrival at waypoint 1 triggers reversing
            for (int i = 0; i < 50; i++)
            {
                _traverseMethod.Invoke(_stitchable, null);
                if ((bool)_isReversingField.GetValue(_stitchable))
                {
                    break;
                }
            }

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
                playerObj.transform.position = playerRb.position;
                Physics2D.SyncTransforms();

                typeof(ThailandHorrorJam.Core.PlayerVisuals).GetMethod("UpdateVisualState", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(visuals, null);

                Assert.IsTrue(pc.IsGrounded, $"Player must remain grounded during upward loop (frame {i}).");
                Assert.AreNotEqual("Jump", visualStateField?.GetValue(visuals)?.ToString(), $"Visual state must never enter Jump while riding a rising box (frame {i}).");
            }

            Object.DestroyImmediate(playerObj);
        }

        [Test]
        public void PlayerPushingAgainstVerticalWall_IsNotGrounded_AndFallsUnderGravity()
        {
            // 1. Setup vertical wall on Layer Ground
            GameObject wallObj = new GameObject("TestVerticalWall");
            wallObj.layer = LayerMask.NameToLayer("Ground");
            var wallCol = wallObj.AddComponent<BoxCollider2D>();
            wallCol.size = new Vector2(1f, 8f);
            wallObj.transform.position = new Vector3(1f, 4f, 0f); // X from 0.5 to 1.5, Y from 0 to 8

            // 2. Setup Player in mid-air pressing against the wall at Y = 4 (mid-height)
            GameObject playerObj = new GameObject("TestPlayer");
            var playerRb = playerObj.AddComponent<Rigidbody2D>();
            playerRb.bodyType = RigidbodyType2D.Dynamic;
            playerRb.gravityScale = 0f; // Controlled manually by PlayerController2D

            var playerCol = playerObj.AddComponent<CapsuleCollider2D>();
            playerCol.size = new Vector2(0.42f, 0.84f);

            var pc = playerObj.AddComponent<ThailandHorrorJam.Core.PlayerController2D>();
            typeof(ThailandHorrorJam.Core.PlayerController2D).GetField("_groundLayer", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(pc, (LayerMask)(1 << LayerMask.NameToLayer("Ground")));
            typeof(ThailandHorrorJam.Core.PlayerController2D).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(pc, null);

            // Position player at x = 0.28 (capsule right edge touches wall at x = 0.5) and y = 4 (mid-air)
            playerObj.transform.position = new Vector3(0.28f, 4f, 0f);
            Physics2D.SyncTransforms();

            // Set player horizontal input moving towards wall
            typeof(ThailandHorrorJam.Core.PlayerController2D).GetField("_horizontalInput", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(pc, 1f);

            // 3. Step physics and verify player is NOT grounded and falls with gravity
            for (int i = 0; i < 5; i++)
            {
                typeof(ThailandHorrorJam.Core.PlayerController2D).GetMethod("FixedUpdate", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(pc, null);

                Assert.IsFalse(pc.IsGrounded, $"Player must NOT be detected as grounded while pressing against vertical wall (frame {i}).");
                Assert.IsTrue(playerRb.linearVelocity.y < 0f, $"Player vertical velocity must accelerate downward under gravity (frame {i}, vel={playerRb.linearVelocity.y}).");

                playerRb.position += playerRb.linearVelocity * 0.02f;
                playerObj.transform.position = playerRb.position;
                Physics2D.SyncTransforms();
            }

            Assert.IsTrue(playerObj.transform.position.y < 4f, "Player must have fallen downward along the wall.");

            Object.DestroyImmediate(playerObj);
            Object.DestroyImmediate(wallObj);
        }

        [Test]
        public void PlayerStandingOnRisingLoopingBox_AtApexReversal_RemainsGroundedAndNeverEntersFallVisualState()
        {
            // 1. Setup upward looping box from 0 to 0.28m (arrives in ~4 steps)
            _go.layer = LayerMask.NameToLayer("Stitchable");
            var waypoints = new List<Vector2> { new Vector2(0f, 0f), new Vector2(0f, 0.28f) };
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
            Assert.IsTrue(pc.IsGrounded, "Player must be initially grounded on box.");

            // Advance through rise, apex reversal, dwell, and descending
            for (int i = 0; i < 25; i++)
            {
                _fixedUpdateMethod.Invoke(_stitchable, null);
                typeof(ThailandHorrorJam.Core.PlayerController2D).GetMethod("FixedUpdate", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(pc, null);

                // Simulate physics step (velocity integration)
                playerRb.position += playerRb.linearVelocity * 0.02f;
                playerObj.transform.position = playerRb.position;
                Physics2D.SyncTransforms();

                typeof(ThailandHorrorJam.Core.PlayerVisuals).GetMethod("UpdateVisualState", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(visuals, null);

                string stateName = visualStateField?.GetValue(visuals)?.ToString();
                Assert.IsTrue(pc.IsGrounded, $"Player must remain grounded during loop and apex reversal (frame {i}).");
                Assert.AreEqual("Idle", stateName, $"Visual state must remain Idle when no movement input is given (frame {i}).");
                Assert.AreNotEqual("Fall", stateName, $"Visual state must never be Fall at apex reversal (frame {i}).");
                Assert.AreNotEqual("FallLoop", stateName, $"Visual state must never be FallLoop at apex reversal (frame {i}).");
                Assert.AreNotEqual("Walk", stateName, $"Visual state must never be Walk when player is idle on platform (frame {i}).");
            }

            Object.DestroyImmediate(playerObj);
        }
    }
}
