using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using ThailandHorrorJam.Systems.Needle;

namespace ThailandHorrorJam.Editor.Tests
{
    [TestFixture]
    public class NeedleBudgetAndInteractionTests
    {
        private GameObject _needleObj;
        private NeedleController _needle;
        private MethodInfo _budgetMethod;
        private MethodInfo _primaryDownMethod;
        private FieldInfo _isPinningField;

        [SetUp]
        public void SetUp()
        {
            _needleObj = new GameObject("TestNeedleController");
            _needle = _needleObj.AddComponent<NeedleController>();

            // Setup reflection references
            _budgetMethod = typeof(NeedleController).GetMethod("IsWithinThreadBudget", BindingFlags.NonPublic | BindingFlags.Instance);
            _primaryDownMethod = typeof(NeedleController).GetMethod("OnPrimaryDown", BindingFlags.NonPublic | BindingFlags.Instance);
            _isPinningField = typeof(NeedleController).GetField("_isPinning", BindingFlags.NonPublic | BindingFlags.Instance);
        }

        [TearDown]
        public void TearDown()
        {
            if (_needleObj != null)
            {
                Object.DestroyImmediate(_needleObj);
            }
        }

        [Test]
        public void ThreadBudget_AllowsDistancesWithinConfiguredRange()
        {
            // Default range is typically [0.5m, 9.5m]
            bool validMid = (bool)_budgetMethod.Invoke(_needle, new object[] { 4.0f });
            bool validAtMin = (bool)_budgetMethod.Invoke(_needle, new object[] { 0.5f });
            bool validAtMax = (bool)_budgetMethod.Invoke(_needle, new object[] { 9.5f });

            Assert.IsTrue(validMid, "4.0m should be within thread budget.");
            Assert.IsTrue(validAtMin, "0.5m should be within thread budget.");
            Assert.IsTrue(validAtMax, "9.5m should be within thread budget.");
        }

        [Test]
        public void ThreadBudget_RejectsDistancesOutsideConfiguredRange()
        {
            bool tooShort = (bool)_budgetMethod.Invoke(_needle, new object[] { 0.1f });
            bool tooLong = (bool)_budgetMethod.Invoke(_needle, new object[] { 15.0f });

            Assert.IsFalse(tooShort, "0.1m should be rejected as too short.");
            Assert.IsFalse(tooLong, "15.0m should be rejected as exceeding max thread budget.");
        }

        [Test]
        public void ClickingLoopingBox_IsIgnored_PreservesLoopingState()
        {
            // 1. Create a looping box
            GameObject boxObj = new GameObject("LoopingBox");
            boxObj.transform.position = new Vector3(3f, 0f, 0f);

            var rb = boxObj.AddComponent<Rigidbody2D>();
            var col = boxObj.AddComponent<BoxCollider2D>();
            col.size = new Vector2(1f, 1f);

            var stitchable = boxObj.AddComponent<StitchableObject>();
            typeof(StitchableObject).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(stitchable, null);

            var waypoints = new List<Vector2> { new Vector2(3f, 0f), new Vector2(6f, 0f) };
            stitchable.StartLoopAlongWaypoints(waypoints, LoopPlaybackMode.PingPong);

            Assert.AreEqual(StitchState.Looping, stitchable.CurrentState);

            // 2. Simulate clicking directly on the looping box
            _primaryDownMethod.Invoke(_needle, new object[] { new Vector3(3f, 0f, 0f) });

            // 3. Verify needle did NOT start pinning, and box is still looping
            bool isPinning = (bool)_isPinningField.GetValue(_needle);
            Assert.IsFalse(isPinning, "Clicking a looping box must not start needle pinning.");
            Assert.AreEqual(StitchState.Looping, stitchable.CurrentState, "Box must remain in Looping state.");
            Assert.AreEqual(RigidbodyType2D.Static, rb.bodyType, "Box must remain Static while looping.");

            Object.DestroyImmediate(boxObj);
        }

        [Test]
        public void NeedlePinMarkers_HaveDontSaveFlags_AndAreCleanedUpWithoutLeaking()
        {
            // 1. Create SutureThread
            GameObject threadObj = new GameObject("TestSutureThread");
            SutureThread thread = threadObj.AddComponent<SutureThread>();

            // 2. Render a path with 3 waypoints
            var waypoints = new List<Vector2> { new Vector2(0f, 0f), new Vector2(2f, 0f), new Vector2(4f, 0f) };
            thread.RenderPath(waypoints);

            // 3. Find pin markers in scene
            var container = GameObject.Find("[NeedlePins_Runtime]");
            Assert.IsNotNull(container, "Pin container [NeedlePins_Runtime] must exist.");

            var pinField = typeof(SutureThread).GetField("_pinMarkers", BindingFlags.NonPublic | BindingFlags.Instance);
            var pinList = pinField?.GetValue(thread) as List<GameObject>;
            Assert.IsNotNull(pinList, "_pinMarkers field must exist.");
            Assert.IsTrue(pinList.Count > 0, "Pin markers should be spawned for path waypoints.");

            // Verify each pin has DontSave flag and parented to container
            foreach (var pin in pinList)
            {
                Assert.IsNotNull(pin, "Pin marker must not be null.");
                Assert.IsTrue((pin.hideFlags & HideFlags.DontSave) != 0, $"Pin marker {pin.name} must have HideFlags.DontSave set.");
                Assert.IsNotNull(pin.transform.parent, "Pin marker should be organized under runtime container.");
                Assert.AreEqual("[NeedlePins_Runtime]", pin.transform.parent.name, "Pin marker parent should be [NeedlePins_Runtime].");
            }

            // 4. Destroy SutureThread and verify pins are destroyed cleanly
            var onDestroy = typeof(SutureThread).GetMethod("OnDestroy", BindingFlags.NonPublic | BindingFlags.Instance);
            onDestroy?.Invoke(thread, null);
            Object.DestroyImmediate(threadObj);

            foreach (var pin in pinList)
            {
                Assert.IsTrue(pin == null, "All pin markers belonging to this SutureThread must be destroyed without leaking.");
            }
        }

        [Test]
        public void EmptySpaceRightClick_TogglesCutAimMode_AndSecondRightClickCancels()
        {
            var secondaryClick = typeof(NeedleController).GetMethod("OnSecondaryClick", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(secondaryClick, "OnSecondaryClick must exist on NeedleController.");

            // 1. Right click on empty space toggles Cut Aim ON
            secondaryClick.Invoke(_needle, new object[] { new Vector3(50f, 50f, 0f) });

            Assert.IsTrue(_needle.IsCutAiming, "Right clicking empty space must toggle Cut Aim mode ON.");
            Assert.IsTrue(_needle.IsAiming, "IsAiming must be true while in Cut Aim.");
            Assert.IsFalse(_needle.IsPinning, "IsPinning must remain false in Cut Aim.");

            // 2. Right click again toggles Cut Aim OFF
            secondaryClick.Invoke(_needle, new object[] { new Vector3(50f, 50f, 0f) });

            Assert.IsFalse(_needle.IsCutAiming, "Right click while in Cut Aim must toggle it OFF.");
            Assert.IsFalse(_needle.IsAiming, "IsAiming must be false after toggling off.");
        }

        [Test]
        public void CutAim_PuddlesThreadAtFeet_WhileAiming()
        {
            var secondaryClick = typeof(NeedleController).GetMethod("OnSecondaryClick", BindingFlags.NonPublic | BindingFlags.Instance);
            secondaryClick?.Invoke(_needle, new object[] { new Vector3(50f, 50f, 0f) });

            Assert.IsTrue(_needle.IsCutAiming, "Must be in Cut Aim mode.");

            var updateThreadMethod = typeof(NeedleController).GetMethod("UpdateCutNeedleThread", BindingFlags.NonPublic | BindingFlags.Instance);
            updateThreadMethod?.Invoke(_needle, new object[] { new Vector3(10f, 0f, 0f) });

            var threadField = typeof(NeedleController).GetField("_cutNeedleThread", BindingFlags.NonPublic | BindingFlags.Instance);
            var thread = threadField?.GetValue(_needle) as SutureThread;

            Assert.IsNotNull(thread, "Cut needle must create SutureThread while aiming.");
            Assert.IsFalse(thread.IsCuttable, "Thread pooled at feet must not be cuttable.");

            // Verify FixedUpdate does not throw ArgumentOutOfRangeException while in pooled mode
            var fixedUpdateMethod = typeof(SutureThread).GetMethod("FixedUpdate", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.DoesNotThrow(() => fixedUpdateMethod?.Invoke(thread, null), "FixedUpdate must not throw ArgumentOutOfRangeException while thread is pooled.");

            // Cancel aim
            secondaryClick?.Invoke(_needle, new object[] { new Vector3(50f, 50f, 0f) });
            Assert.IsFalse(_needle.IsCutAiming, "Must exit Cut Aim mode.");
        }

        [Test]
        public void CutNeedle_TrailingThread_ExistsAndIsNotCuttable()
        {
            var secondaryClick = typeof(NeedleController).GetMethod("OnSecondaryClick", BindingFlags.NonPublic | BindingFlags.Instance);
            secondaryClick?.Invoke(_needle, new object[] { new Vector3(50f, 50f, 0f) });

            // Fire cut needle
            _primaryDownMethod.Invoke(_needle, new object[] { new Vector3(10f, 0f, 0f) });

            var threadField = typeof(NeedleController).GetField("_cutNeedleThread", BindingFlags.NonPublic | BindingFlags.Instance);
            var thread = threadField?.GetValue(_needle) as SutureThread;

            Assert.IsNotNull(thread, "Cut needle must spawn a trailing SutureThread dragging from the player.");
            Assert.IsFalse(thread.IsCuttable, "Cut needle's own trailing thread must not be cuttable.");
            Assert.IsFalse(thread.HideAllPins, "Cut needle thread should show pin at player.");

            _needle.ResetAllNeedleStates();
        }

        [Test]
        public void CutAim_TrySeverAlongSegment_CutsThreadAndClearsLoop()
        {
            // 1. Create looping box with SutureThread
            GameObject boxObj = new GameObject("LoopingBox_CutTest");
            boxObj.transform.position = new Vector3(0f, 0f, 0f);
            var rb = boxObj.AddComponent<Rigidbody2D>();
            var col = boxObj.AddComponent<BoxCollider2D>();

            // Attach SutureThread as child to emulate the box prefab hierarchy
            GameObject threadObj = new GameObject("Thread");
            threadObj.transform.SetParent(boxObj.transform);
            var sutureThread = threadObj.AddComponent<SutureThread>();
            typeof(SutureThread).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(sutureThread, null);
            typeof(SutureThread).GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(sutureThread, null);

            var stitchable = boxObj.AddComponent<StitchableObject>();
            typeof(StitchableObject).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(stitchable, null);

            var waypoints = new List<Vector2> { new Vector2(0f, 0f), new Vector2(4f, 0f) };
            stitchable.StartLoopAlongWaypoints(waypoints, LoopPlaybackMode.PingPong);
            Assert.AreEqual(StitchState.Looping, stitchable.CurrentState);

            // 2. Test TrySeverAlongSegment crossing the thread (from (2, 2) to (2, -2))
            var trySeverMethod = typeof(NeedleController).GetMethod("TrySeverAlongSegment", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(trySeverMethod, "TrySeverAlongSegment method should exist on NeedleController.");

            bool severed = (bool)trySeverMethod.Invoke(_needle, new object[] { new Vector2(2f, 2f), new Vector2(2f, -2f) });
            Assert.IsTrue(severed, "Segment crossing the suture thread must sever it.");
            Assert.AreEqual(StitchState.Idle, stitchable.CurrentState, "Box must return to Idle state after thread is severed.");

            Object.DestroyImmediate(boxObj);
        }

        [Test]
        public void CutAim_TrySeverAlongSegment_MissReturnsFalse()
        {
            var trySeverMethod = typeof(NeedleController).GetMethod("TrySeverAlongSegment", BindingFlags.NonPublic | BindingFlags.Instance);
            bool severed = (bool)trySeverMethod.Invoke(_needle, new object[] { new Vector2(100f, 100f), new Vector2(100f, 110f) });
            Assert.IsFalse(severed, "Segment far from threads must return false.");
        }
    }
}
