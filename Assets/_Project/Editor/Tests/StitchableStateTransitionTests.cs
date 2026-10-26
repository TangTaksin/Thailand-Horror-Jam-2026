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
    }
}
