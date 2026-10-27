using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using ThailandHorrorJam.Systems.Needle;

namespace ThailandHorrorJam.Editor.Tests
{
    [TestFixture]
    public class UniversalHangingObjectTests
    {
        private GameObject _hangingObj;
        private HangingObject2D _hanging;
        private Rigidbody2D _rb;

        [SetUp]
        public void SetUp()
        {
            _hangingObj = new GameObject("TestHangingObject");
            _hangingObj.AddComponent<BoxCollider2D>();
            _rb = _hangingObj.AddComponent<Rigidbody2D>();
            _hanging = _hangingObj.AddComponent<HangingObject2D>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_hangingObj != null)
            {
                Object.DestroyImmediate(_hangingObj);
            }
        }

        [Test]
        public void HangingObject_StartsSuspendedAndStatic()
        {
            InvokePrivateMethod(_hanging, "Awake");

            Assert.IsTrue(_hanging.IsSuspended, "HangingObject must start in suspended state.");
            Assert.AreEqual(RigidbodyType2D.Static, _rb.bodyType, "Rigidbody must be Static while suspended to hang firmly.");
            Assert.AreEqual(StitchState.Tethered, _hanging.CurrentState, "CurrentState must report Tethered while suspended for cut detection.");
        }

        [Test]
        public void SeverHangingThread_DrawbridgeMode_BecomesKinematicRotation()
        {
            SetPrivateField(_hanging, "_mode", HangingMode.Drawbridge);
            InvokePrivateMethod(_hanging, "Awake");

            _hanging.SeverHangingThread();

            Assert.IsFalse(_hanging.IsSuspended, "Must no longer be suspended after severing thread.");
            Assert.AreEqual(RigidbodyType2D.Kinematic, _rb.bodyType, "Drawbridge mode must rotate down as Kinematic.");
        }

        [Test]
        public void SeverHangingThread_FreeFallMode_BecomesDynamicGravityDrop()
        {
            SetPrivateField(_hanging, "_mode", HangingMode.FreeFall);
            SetPrivateField(_hanging, "_freeFallGravityScale", 2.0f);
            InvokePrivateMethod(_hanging, "Awake");

            _hanging.SeverHangingThread();

            Assert.IsFalse(_hanging.IsSuspended, "Must no longer be suspended after severing thread.");
            Assert.AreEqual(RigidbodyType2D.Dynamic, _rb.bodyType, "FreeFall mode must become Dynamic with gravity.");
            Assert.AreEqual(2.0f, _rb.gravityScale, "FreeFall mode must apply configured gravity scale.");
        }

        [Test]
        public void SeverHangingThread_WithStitchableObject_DelegatesToStitchableAfterDrop()
        {
            SetPrivateField(_hanging, "_mode", HangingMode.FreeFall);
            StitchableObject stitchable = _hangingObj.AddComponent<StitchableObject>();
            InvokePrivateMethod(_hanging, "Awake");

            // While suspended: CurrentState is Tethered (HangingObject rope)
            Assert.AreEqual(StitchState.Tethered, _hanging.CurrentState);

            // Cut the thread
            _hanging.CutThread();

            // After cutting: CurrentState delegates to the StitchableObject (Idle)
            Assert.IsFalse(_hanging.IsSuspended);
            Assert.AreEqual(StitchState.Idle, _hanging.CurrentState);

            // Verify delegation of player tethering
            GameObject playerObj = new GameObject("TestPlayer");
            Rigidbody2D playerRb = playerObj.AddComponent<Rigidbody2D>();
            _hanging.StartPlayerTether(playerObj.transform, playerRb);

            Assert.AreEqual(StitchState.PlayerHarness, stitchable.CurrentState, "Tethering hanging object after drop must delegate to StitchableObject.");

            Object.DestroyImmediate(playerObj);
        }

        [Test]
        public void TetherHangingObject_WhileSuspended_RemainsStaticAndSuspended()
        {
            SetPrivateField(_hanging, "_mode", HangingMode.FreeFall);
            StitchableObject stitchable = _hangingObj.AddComponent<StitchableObject>();
            InvokePrivateMethod(_hanging, "Awake");

            GameObject playerObj = new GameObject("TestPlayer");
            Rigidbody2D playerRb = playerObj.AddComponent<Rigidbody2D>();

            // Tether while suspended
            _hanging.StartPlayerTether(playerObj.transform, playerRb);

            Assert.IsTrue(_hanging.IsSuspended, "Hanging object must still be suspended from ceiling.");
            Assert.AreEqual(RigidbodyType2D.Static, _rb.bodyType, "Hanging object must remain Static while suspended so it doesn't fall before being cut.");
            Assert.IsTrue(stitchable.IsTetheredToPlayer, "StitchableObject must register as tethered to player.");
            Assert.AreEqual(StitchState.PlayerHarness, _hanging.CurrentState, "CurrentState must report PlayerHarness while tethered.");

            // Release tether while still suspended
            _hanging.ReleasePlayerTether();
            Assert.IsFalse(stitchable.IsTetheredToPlayer);
            Assert.IsTrue(_hanging.IsSuspended);
            Assert.AreEqual(RigidbodyType2D.Static, _rb.bodyType, "Releasing tether must keep hanging object Static.");

            Object.DestroyImmediate(playerObj);
        }

        [Test]
        public void SeverHangingThread_WhileAlreadyTethered_DropsAsDynamicAndMaintainsTether()
        {
            SetPrivateField(_hanging, "_mode", HangingMode.FreeFall);
            StitchableObject stitchable = _hangingObj.AddComponent<StitchableObject>();
            InvokePrivateMethod(_hanging, "Awake");

            GameObject playerObj = new GameObject("TestPlayer");
            Rigidbody2D playerRb = playerObj.AddComponent<Rigidbody2D>();

            // Tether while suspended
            _hanging.StartPlayerTether(playerObj.transform, playerRb);
            Assert.IsTrue(stitchable.IsTetheredToPlayer);
            Assert.AreEqual(RigidbodyType2D.Static, _rb.bodyType);

            // Cut the hanging thread while tethered
            _hanging.CutThread();

            Assert.IsFalse(_hanging.IsSuspended, "Must no longer be suspended after cutting ceiling thread.");
            Assert.AreEqual(RigidbodyType2D.Dynamic, _rb.bodyType, "Must transition to Dynamic to fall under gravity.");
            Assert.IsTrue(stitchable.IsTetheredToPlayer, "Tether to player must be preserved after cutting ceiling thread.");

            Object.DestroyImmediate(playerObj);
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo fi = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            fi?.SetValue(target, value);
        }

        private static object InvokePrivateMethod(object target, string methodName, params object[] parameters)
        {
            MethodInfo mi = target.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance);
            return mi?.Invoke(target, parameters);
        }
    }
}
