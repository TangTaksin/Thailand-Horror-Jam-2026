using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using ThailandHorrorJam.Systems.Gameplay;
using ThailandHorrorJam.Systems.Needle;

namespace ThailandHorrorJam.Editor.Tests
{
    [TestFixture]
    public class CorpseGasVentTests
    {
        private GameObject _ventObj;
        private CorpseGasVent2D _vent;
        private GameObject _crateObj;
        private StitchableObject _crate;
        private GameObject _eruptionVisualObj;
        private SpriteRenderer _eruptionSprite;

        [SetUp]
        public void SetUp()
        {
            _ventObj = new GameObject("TestCorpseGasVent");
            _ventObj.transform.position = new Vector3(87.5f, 1.25f, 0f);

            BoxCollider2D ventCol = _ventObj.AddComponent<BoxCollider2D>();
            ventCol.size = new Vector2(1.6f, 2.5f);
            ventCol.isTrigger = true;

            _eruptionVisualObj = new GameObject("Visual_Eruption");
            _eruptionVisualObj.transform.SetParent(_ventObj.transform, false);
            _eruptionVisualObj.transform.localPosition = Vector3.zero;
            _eruptionSprite = _eruptionVisualObj.AddComponent<SpriteRenderer>();
            _eruptionSprite.drawMode = SpriteDrawMode.Tiled;
            _eruptionSprite.size = new Vector2(1.2f, 2.4f);

            _vent = _ventObj.AddComponent<CorpseGasVent2D>();

            // Assign private visual fields via reflection for test isolation
            SetPrivateField(_vent, "_eruptionVisual", _eruptionVisualObj);

            // Initialize Awake on vent
            InvokePrivateMethod(_vent, "Awake");

            // Setup test crate
            _crateObj = new GameObject("TestCrate");
            _crateObj.transform.position = new Vector3(87.5f, 0.5f, 0f); // Resting on floor (bottom at Y=0.0)
            BoxCollider2D crateCol = _crateObj.AddComponent<BoxCollider2D>();
            crateCol.size = new Vector2(1f, 1f);
            Rigidbody2D crateRb = _crateObj.AddComponent<Rigidbody2D>();
            crateRb.bodyType = RigidbodyType2D.Dynamic;
            crateRb.mass = 20f;
            _crate = _crateObj.AddComponent<StitchableObject>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_crateObj != null) Object.DestroyImmediate(_crateObj);
            if (_ventObj != null) Object.DestroyImmediate(_ventObj);
        }

        [Test]
        public void FindBlockingCrate_DetectsCrateOverNozzle()
        {
            // Simulate crate entering vent trigger
            InvokePrivateMethod(_vent, "OnTriggerEnter2D", _crate.GetComponent<Collider2D>());

            object[] args = new object[] { false };
            MethodInfo findMethod = typeof(CorpseGasVent2D).GetMethod("FindBlockingCrate", BindingFlags.NonPublic | BindingFlags.Instance);
            StitchableObject found = (StitchableObject)findMethod.Invoke(_vent, args);
            bool isTethered = (bool)args[0];

            Assert.IsNotNull(found, "Crate placed directly over the vent nozzle must be detected.");
            Assert.IsFalse(isTethered, "Untethered crate must report isTetheredToPlayer = false.");
        }

        [Test]
        public void VisualHeight_ClampedToZero_WhenCrateRestingOnNozzle()
        {
            // Simulate crate entering vent trigger
            InvokePrivateMethod(_vent, "OnTriggerEnter2D", _crate.GetComponent<Collider2D>());

            // Set state to Erupting
            SetPrivateField(_vent, "_currentState", GasVentState.Erupting);

            // Run UpdateVisuals
            InvokePrivateMethod(_vent, "UpdateVisuals", 0.02f);

            float effectiveHeight = !_eruptionVisualObj.activeSelf ? 0f : _eruptionSprite.size.y;
            Assert.LessOrEqual(effectiveHeight, 0.08f, "Gas eruption height must be clamped to 0 when crate is resting on the nozzle.");
        }

        [Test]
        public void VisualHeight_ExpandsUnderneath_WhenCrateIsLifted()
        {
            // Simulate crate lifted to Y = 1.5 (bottom at Y = 1.0)
            _crateObj.transform.position = new Vector3(87.5f, 1.5f, 0f);
            Physics2D.SyncTransforms();

            // Simulate crate in vent trigger
            InvokePrivateMethod(_vent, "OnTriggerEnter2D", _crate.GetComponent<Collider2D>());
            SetPrivateField(_vent, "_currentState", GasVentState.Erupting);

            InvokePrivateMethod(_vent, "UpdateVisuals", 0.02f);

            // Bottom of vent nozzle is at Y = 1.25 - 1.2 = 0.05. Bottom of crate is at Y = 1.0. Expected height ~ 0.95m
            float effectiveHeight = _eruptionSprite.size.y;
            Assert.Greater(effectiveHeight, 0.8f, "Gas eruption height must expand to fill the space under the lifted crate.");
            Assert.LessOrEqual(effectiveHeight, 1.05f, "Gas eruption top must not penetrate above the crate's bottom surface.");
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
