using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ThailandHorrorJam.Systems.Needle;

namespace ThailandHorrorJam.Editor.Tests
{
    [TestFixture]
    public class StitchableStateTransitionTests
    {
        private GameObject _go;
        private Rigidbody2D _rb;
        private BoxCollider2D _col;
        private StitchableObject _stitchable;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("TestStitchableBox");
            _go.transform.position = new Vector3(2f, 3f, 0f);

            _rb = _go.AddComponent<Rigidbody2D>();
            _rb.bodyType = RigidbodyType2D.Dynamic;
            _rb.mass = 2.5f;
            _rb.gravityScale = 1.75f;

            _col = _go.AddComponent<BoxCollider2D>();
            _col.size = new Vector2(1f, 1f);

            _stitchable = _go.AddComponent<StitchableObject>();

            // Trigger Awake to capture initial physics state
            typeof(StitchableObject).GetMethod("Awake", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.Invoke(_stitchable, null);
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
        public void InitialState_IsIdle_AndRigidbodyIsDynamic()
        {
            Assert.AreEqual(StitchState.Idle, _stitchable.CurrentState);
            Assert.AreEqual(RigidbodyType2D.Dynamic, _rb.bodyType);
            Assert.AreEqual(1.75f, _rb.gravityScale);
            Assert.AreEqual(2.5f, _rb.mass);
        }

        [Test]
        public void StartLoopAlongWaypoints_SwitchesRigidbodyToStatic()
        {
            var waypoints = new List<Vector2> { new Vector2(2f, 3f), new Vector2(6f, 3f) };
            _stitchable.StartLoopAlongWaypoints(waypoints, LoopPlaybackMode.PingPong);

            Assert.AreEqual(StitchState.Looping, _stitchable.CurrentState);
            Assert.AreEqual(RigidbodyType2D.Static, _rb.bodyType, "Looping object must be Static so external physics cannot knock it off rail.");
        }

        [Test]
        public void CutThread_RestoresRigidbodyToDynamicAndPreservesGravity()
        {
            var waypoints = new List<Vector2> { new Vector2(2f, 3f), new Vector2(6f, 3f) };
            _stitchable.StartLoopAlongWaypoints(waypoints, LoopPlaybackMode.PingPong);
            Assert.AreEqual(RigidbodyType2D.Static, _rb.bodyType);

            _stitchable.CutThread();

            Assert.AreEqual(StitchState.Idle, _stitchable.CurrentState);
            Assert.AreEqual(RigidbodyType2D.Dynamic, _rb.bodyType, "CutThread must restore Dynamic bodyType.");
            Assert.AreEqual(1.75f, _rb.gravityScale, "Original gravityScale must be preserved after cutting.");
            Assert.AreEqual(2.5f, _rb.mass, "Original mass must be preserved after cutting.");
        }

        [Test]
        public void StartLoopAlongWaypoints_WithLessThanTwoPoints_ImmediatelyCutsThread()
        {
            var singlePoint = new List<Vector2> { new Vector2(2f, 3f) };
            _stitchable.StartLoopAlongWaypoints(singlePoint, LoopPlaybackMode.PingPong);

            Assert.AreEqual(StitchState.Idle, _stitchable.CurrentState);
            Assert.AreEqual(RigidbodyType2D.Dynamic, _rb.bodyType);
        }

        [Test]
        public void StartLoopAlongWaypoints_WhenAlreadyStatic_DoesNotThrow()
        {
            _rb.bodyType = RigidbodyType2D.Static;

            var waypoints = new List<Vector2> { new Vector2(2f, 3f), new Vector2(6f, 3f) };
            Assert.DoesNotThrow(() =>
            {
                _stitchable.StartLoopAlongWaypoints(waypoints, LoopPlaybackMode.PingPong);
            }, "Calling StartLoop on a body that is already Static must not throw Cannot use linearVelocity on a static body exception.");

            Assert.AreEqual(StitchState.Looping, _stitchable.CurrentState);
            Assert.AreEqual(RigidbodyType2D.Static, _rb.bodyType);
        }

        [Test]
        public void ResetToInitialState_RestoresTransformPositionAndClearsVelocities()
        {
            _go.transform.position = new Vector3(15f, 20f, 0f);
            _rb.position = new Vector2(15f, 20f);
            _rb.linearVelocity = new Vector2(5f, -8f);

            _stitchable.ResetToInitialState();

            Assert.AreEqual(new Vector2(2f, 3f), (Vector2)_go.transform.position, "Position must reset to initial position.");
            Assert.AreEqual(Vector2.zero, _rb.linearVelocity, "Linear velocity must reset to zero.");
            Assert.AreEqual(StitchState.Idle, _stitchable.CurrentState);
            Assert.AreEqual(RigidbodyType2D.Dynamic, _rb.bodyType);
        }

        [Test]
        public void ResetToInitialState_WhenPreserveLoopOnRespawnTrueAndLooping_PreservesLoopingState()
        {
            _stitchable.PreserveLoopOnRespawn = true;
            var waypoints = new List<Vector2> { new Vector2(2f, 3f), new Vector2(6f, 3f) };
            _stitchable.StartLoopAlongWaypoints(waypoints, LoopPlaybackMode.PingPong);
            Assert.AreEqual(StitchState.Looping, _stitchable.CurrentState);

            _stitchable.ResetToInitialState(forceReset: false);

            Assert.AreEqual(StitchState.Looping, _stitchable.CurrentState, "When PreserveLoopOnRespawn is true, ResetToInitialState must not terminate loop.");
            Assert.AreEqual(RigidbodyType2D.Static, _rb.bodyType);
        }

        [Test]
        public void ResetToInitialState_WhenForceReset_ResetsEvenIfPreserveLoopTrue()
        {
            _stitchable.PreserveLoopOnRespawn = true;
            var waypoints = new List<Vector2> { new Vector2(2f, 3f), new Vector2(6f, 3f) };
            _stitchable.StartLoopAlongWaypoints(waypoints, LoopPlaybackMode.PingPong);
            Assert.AreEqual(StitchState.Looping, _stitchable.CurrentState);

            _stitchable.ResetToInitialState(forceReset: true);

            Assert.AreEqual(StitchState.Idle, _stitchable.CurrentState, "ForceReset must reset to Idle even when PreserveLoopOnRespawn is true.");
            Assert.AreEqual(RigidbodyType2D.Dynamic, _rb.bodyType);
            Assert.AreEqual(new Vector2(2f, 3f), (Vector2)_go.transform.position);
        }

        [Test]
        public void WhenBoxPushedOffLedgeDuringHauling_TetherSnapsAndReleasesAutomatically()
        {
            GameObject playerObj = new GameObject("TestHaulPlayer");
            var playerRb = playerObj.AddComponent<Rigidbody2D>();
            playerObj.transform.position = new Vector3(0f, 2f, 0f);

            // Tether box at player position level
            _stitchable.StartPlayerTether(playerObj.transform, playerRb);
            Assert.AreEqual(StitchState.PlayerHarness, _stitchable.CurrentState);
            Assert.IsTrue(_stitchable.IsTetheredToPlayer);

            // Simulate box pushed off ledge, dropping below player into mid-air (vertical drop > 0.85m)
            _go.transform.position = new Vector3(1f, 0.9f, 0f);
            _rb.position = new Vector2(1f, 0.9f);
            _rb.linearVelocity = new Vector2(0f, -2.5f);
            Physics2D.SyncTransforms();

            // Run FixedUpdate step
            typeof(StitchableObject).GetMethod("FixedUpdate", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.Invoke(_stitchable, null);

            Assert.AreEqual(StitchState.Idle, _stitchable.CurrentState, "Tether must snap and release when box falls off platform.");
            Assert.IsFalse(_stitchable.IsTetheredToPlayer, "IsTetheredToPlayer must be false after snapping.");

            Object.DestroyImmediate(playerObj);
        }

        [Test]
        public void WhenBoxAirborneBeneathPlayerForDuration_TetherSnapsAndReleasesAutomatically()
        {
            GameObject playerObj = new GameObject("TestHaulPlayer");
            var playerRb = playerObj.AddComponent<Rigidbody2D>();
            playerObj.transform.position = new Vector3(0f, 2f, 0f);

            _stitchable.StartPlayerTether(playerObj.transform, playerRb);
            Assert.AreEqual(StitchState.PlayerHarness, _stitchable.CurrentState);

            // Box suspended in mid-air 0.6m below player (verticalDrop > 0.45m)
            _go.transform.position = new Vector3(0.8f, 1.4f, 0f);
            _rb.position = new Vector2(0.8f, 1.4f);
            _rb.linearVelocity = Vector2.zero;
            Physics2D.SyncTransforms();

            var fixedUpdateMethod = typeof(StitchableObject).GetMethod("FixedUpdate", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            // Advance multiple fixed updates (> 0.2s = 10 frames of 0.02s)
            for (int i = 0; i < 15; i++)
            {
                fixedUpdateMethod?.Invoke(_stitchable, null);
                if (_stitchable.CurrentState == StitchState.Idle) break;
            }

            Assert.AreEqual(StitchState.Idle, _stitchable.CurrentState, "Tether must snap after box hangs in mid-air below platform for duration.");
            Assert.IsFalse(_stitchable.IsTetheredToPlayer);

            Object.DestroyImmediate(playerObj);
        }
    }
}
