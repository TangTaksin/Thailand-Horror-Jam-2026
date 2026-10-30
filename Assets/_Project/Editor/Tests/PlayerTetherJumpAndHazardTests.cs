using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using ThailandHorrorJam.Core;
using ThailandHorrorJam.Systems.Gameplay;
using ThailandHorrorJam.Systems.Needle;

namespace ThailandHorrorJam.Editor.Tests
{
    [TestFixture]
    public sealed class PlayerTetherJumpAndHazardTests
    {
        private GameObject _playerObj;
        private Rigidbody2D _playerRb;
        private CapsuleCollider2D _playerCol;
        private PlayerController2D _playerController;
        private NeedleController _needleController;

        private GameObject _crateObj;
        private Rigidbody2D _crateRb;
        private BoxCollider2D _crateCol;
        private StitchableObject _crate;

        private GameObject _ventObj;
        private CorpseGasVent2D _vent;

        private GameObject _fishObj;
        private FallingFishHazard2D _fish;

        [SetUp]
        public void SetUp()
        {
            // 1. Create and setup player GameObject
            _playerObj = new GameObject("TestPlayer_TetherJump");
            _playerObj.transform.position = Vector3.zero;

            _playerRb = _playerObj.AddComponent<Rigidbody2D>();
            _playerRb.bodyType = RigidbodyType2D.Dynamic;
            _playerRb.gravityScale = 0f;

            _playerCol = _playerObj.AddComponent<CapsuleCollider2D>();
            _playerCol.size = new Vector2(0.4f, 0.8f);

            _playerController = _playerObj.AddComponent<PlayerController2D>();
            _needleController = _playerObj.AddComponent<NeedleController>();

            // Initialize player components via reflection
            InvokePrivateMethod(_playerController, "Awake");
            InvokePrivateMethod(_needleController, "Awake");

            SetPrivateField(_playerController, "_needleController", _needleController);
            SetPrivateField(_needleController, "_playerController", _playerController);
            SetPrivateField(_needleController, "_playerRb", _playerRb);
            SetPrivateField(_needleController, "_playerTransform", _playerObj.transform);

            // 2. Create and setup stitchable crate
            _crateObj = new GameObject("TestStitchableCrate_TetherJump");
            _crateObj.transform.position = new Vector3(2f, 0f, 0f);

            _crateRb = _crateObj.AddComponent<Rigidbody2D>();
            _crateRb.bodyType = RigidbodyType2D.Dynamic;
            _crateRb.mass = 15f;
            _crateRb.gravityScale = 1.75f;

            _crateCol = _crateObj.AddComponent<BoxCollider2D>();
            _crateCol.size = new Vector2(1f, 1f);

            _crate = _crateObj.AddComponent<StitchableObject>();
            InvokePrivateMethod(_crate, "Awake");
        }

        [TearDown]
        public void TearDown()
        {
            if (_fishObj != null) Object.DestroyImmediate(_fishObj);
            if (_ventObj != null) Object.DestroyImmediate(_ventObj);
            if (_crateObj != null) Object.DestroyImmediate(_crateObj);
            if (_playerObj != null) Object.DestroyImmediate(_playerObj);

            var leftovers = Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include);
            foreach (var go in leftovers)
            {
                if (go != null && (go.name.StartsWith("TestPlayer_TetherJump")
                                || go.name.StartsWith("TestStitchableCrate_TetherJump")
                                || go.name.StartsWith("TestVent_TetherJump")
                                || go.name.StartsWith("TestFish_TetherJump")))
                {
                    Object.DestroyImmediate(go);
                }
            }
        }

        [Test]
        public void ExecuteJump_WhenTetheredOnCrate_ReleasesTetherAndAppliesFullUpwardVelocity()
        {
            // Arrange: Place crate at origin and position player on top of the crate
            _crateObj.transform.position = Vector3.zero;
            _playerObj.transform.position = new Vector3(0f, 1.0f, 0f);
            Physics2D.SyncTransforms();

            // Tether crate to player
            _needleController.StartPlayerTether(_crate);

            Assert.IsTrue(_playerController.IsHauling, "Player should be in hauling state when tethered.");
            Assert.IsTrue(_needleController.IsTetheredToPlayer, "NeedleController should report tethered.");
            Assert.AreEqual(StitchState.PlayerHarness, _crate.CurrentState, "Crate should be in PlayerHarness state.");

            // Configure player as standing on crate
            SetPrivateField(_playerController, "_currentStitchable", _crate);
            SetPrivateField(_playerController, "_currentGroundCollider", _crateCol);
            SetPrivateField(_playerController, "_isGrounded", true);
            SetPrivateField(_playerController, "_isJumpButtonHeld", true);
            _playerRb.linearVelocity = Vector2.zero;

            // Act: Execute jump
            InvokePrivateMethod(_playerController, "ExecuteJump");

            // Assert: Tether must be severed, DistanceJoint2D disabled, and full jump force applied
            Assert.IsFalse(_playerController.IsHauling, "Player hauling tether must be released upon jump.");
            Assert.IsFalse(_needleController.IsTetheredToPlayer, "NeedleController tether must be released upon jump.");
            Assert.AreEqual(StitchState.Idle, _crate.CurrentState, "Crate must transition back to Idle state.");

            var joint = _crateObj.GetComponent<DistanceJoint2D>();
            Assert.IsTrue(joint == null || !joint.enabled || joint.connectedBody == null, "DistanceJoint2D must be disabled or unlinked.");

            Assert.GreaterOrEqual(_playerController.Velocity.y, 12.0f, "Player upward velocity must achieve full jump force without joint choking.");
            Assert.IsFalse(_playerController.IsGrounded, "Player must become airborne immediately.");
        }

        [Test]
        public void ExecuteJump_WhenHaulingFromGround_ReleasesTetherAndAppliesFullUpwardVelocity()
        {
            // Arrange: Player hauling crate while standing on ground (not standing on the crate)
            _playerObj.transform.position = Vector3.zero;
            _crateObj.transform.position = new Vector3(3f, 0f, 0f);
            Physics2D.SyncTransforms();

            _needleController.StartPlayerTether(_crate);
            Assert.IsTrue(_playerController.IsHauling);

            // Grounded on normal floor
            SetPrivateField(_playerController, "_currentStitchable", null);
            SetPrivateField(_playerController, "_currentGroundCollider", null);
            SetPrivateField(_playerController, "_isGrounded", true);
            SetPrivateField(_playerController, "_isJumpButtonHeld", true);
            _playerRb.linearVelocity = Vector2.zero;

            // Act: Execute jump
            InvokePrivateMethod(_playerController, "ExecuteJump");

            // Assert: Hauling tether released, crate returns to Idle, jump force preserved
            Assert.IsFalse(_playerController.IsHauling, "Player hauling tether must be released when jumping from ground.");
            Assert.IsFalse(_needleController.IsTetheredToPlayer, "Needle tether must be released.");
            Assert.AreEqual(StitchState.Idle, _crate.CurrentState, "Crate must return to Idle.");
            Assert.GreaterOrEqual(_playerController.Velocity.y, 12.0f, "Player upward jump velocity must be fully delivered.");
        }

        [Test]
        public void OnPlayerJumpedOff_WhenTetheredToCrate_ImmediatelyReleasesTetherAndJoint()
        {
            // Arrange: Crate tethered in PlayerHarness mode
            _crateObj.transform.position = Vector3.zero;
            _playerObj.transform.position = new Vector3(0f, 1f, 0f);
            _needleController.StartPlayerTether(_crate);

            Assert.AreEqual(StitchState.PlayerHarness, _crate.CurrentState);

            // Act: Player jumps off the crate surface
            _crate.OnPlayerJumpedOff(_playerObj.transform.position);

            // Assert: Crate must transition to Idle and tether joint disabled
            Assert.AreEqual(StitchState.Idle, _crate.CurrentState, "Crate must return to Idle upon player jump off.");
            var joint = _crateObj.GetComponent<DistanceJoint2D>();
            Assert.IsTrue(joint == null || !joint.enabled || joint.connectedBody == null, "DistanceJoint2D must be disabled on jump off.");
            Assert.IsFalse(_playerController.IsHauling, "Player hauling flag must be cleared.");
            Assert.IsFalse(_needleController.IsTetheredToPlayer, "NeedleController tether must be released.");
        }

        [Test]
        public void ApplyKnockback_WhenHaulingTetheredCrate_ReleasesTetherImmediately()
        {
            // Arrange: Player hauling crate
            _playerObj.transform.position = Vector3.zero;
            _crateObj.transform.position = new Vector3(3f, 0f, 0f);
            _needleController.StartPlayerTether(_crate);

            Assert.IsTrue(_playerController.IsHauling);
            Assert.AreEqual(StitchState.PlayerHarness, _crate.CurrentState);

            // Act: Player is struck by hazard knockback
            Vector2 knockbackImpulse = new Vector2(-6f, 4f);
            _playerController.ApplyKnockback(knockbackImpulse, 0.25f);

            // Assert: Tether severed instantly and knockback velocity applied
            Assert.IsFalse(_playerController.IsHauling, "Hauling tether must be released immediately upon receiving hazard knockback.");
            Assert.IsFalse(_needleController.IsTetheredToPlayer, "Needle tether must be severed upon knockback.");
            Assert.AreEqual(StitchState.Idle, _crate.CurrentState, "Crate must return to Idle upon player knockback.");
            Assert.AreEqual(knockbackImpulse.x, _playerController.Velocity.x, 0.01f, "Horizontal knockback velocity must be applied.");
            Assert.AreEqual(knockbackImpulse.y, _playerController.Velocity.y, 0.01f, "Vertical knockback velocity must be applied.");
        }

        [Test]
        public void CheckTetherSlice_WhenFallingFishCrossesTether_CutsTetherAndDeflectsFish()
        {
            // Arrange: Player at (0, 0), Crate at (4, 0)
            _playerObj.transform.position = Vector3.zero;
            _crateObj.transform.position = new Vector3(4f, 0f, 0f);
            _needleController.StartPlayerTether(_crate);

            Assert.IsTrue(_playerController.IsHauling);
            Assert.IsTrue(_needleController.IsTetheredToPlayer);

            // Spawn falling fish above tether midpoint
            _fishObj = new GameObject("TestFish_TetherJump");
            var fishCol = _fishObj.AddComponent<CircleCollider2D>();
            fishCol.isTrigger = true;
            _fish = _fishObj.AddComponent<FallingFishHazard2D>();
            _fish.Spawn(new Vector3(2f, 2f, 0f), null);

            Assert.IsFalse(_fish.IsDeflected);

            // Trajectory crossing the tether line (0,0) -> (4,0) at (2,0)
            Vector2 prevPos = new Vector2(2f, 0.5f);
            Vector2 nextPos = new Vector2(2f, -0.5f);

            // Act: Run tether slice detection
            InvokePrivateMethod(_fish, "CheckTetherSlice", prevPos, nextPos);

            // Assert: Fish deflected and tether cleanly severed
            Assert.IsTrue(_fish.IsDeflected, "Fish must enter deflected state upon cutting tether.");
            Assert.IsFalse(_playerController.IsHauling, "Player hauling tether must be released when fish cuts line.");
            Assert.IsFalse(_needleController.IsTetheredToPlayer, "NeedleController tether must be released.");
            Assert.AreEqual(StitchState.Idle, _crate.CurrentState, "Crate must return to Idle state after tether slice.");
        }

        [Test]
        public void CorpseGasVentSuperJump_WhenTetheredOnCrateOverVent_GrantsFullSuperJumpBoostAndReleasesTether()
        {
            // Arrange: Setup vent at (10, 1) in Erupting state
            _ventObj = new GameObject("TestVent_TetherJump");
            _ventObj.transform.position = new Vector3(10f, 1f, 0f);
            var ventCol = _ventObj.AddComponent<BoxCollider2D>();
            ventCol.size = new Vector2(2f, 3f);
            ventCol.isTrigger = true;
            _vent = _ventObj.AddComponent<CorpseGasVent2D>();
            InvokePrivateMethod(_vent, "Awake");
            SetPrivateField(_vent, "_currentState", GasVentState.Erupting);
            SetPrivateField(_vent, "_ventCollider", ventCol);

            // Place crate directly inside vent trigger
            _crateObj.transform.position = new Vector3(10f, 0.5f, 0f);
            Physics2D.SyncTransforms();
            InvokePrivateMethod(_vent, "OnTriggerEnter2D", _crateCol);

            Assert.IsTrue(_vent.IsErupting, "Vent must be in Erupting state.");
            Assert.IsTrue(_vent.IsObjectInVent(_crateObj), "Crate must be recognized within the vent.");

            // Place player on top of crate and tether
            _playerObj.transform.position = new Vector3(10f, 1.5f, 0f);
            _needleController.StartPlayerTether(_crate);

            SetPrivateField(_playerController, "_currentStitchable", _crate);
            SetPrivateField(_playerController, "_currentGroundCollider", _crateCol);
            SetPrivateField(_playerController, "_isGrounded", true);
            SetPrivateField(_playerController, "_isJumpButtonHeld", true);
            _playerRb.linearVelocity = Vector2.zero;

            // Act: Execute super jump
            InvokePrivateMethod(_playerController, "ExecuteJump");

            // Assert: Full Super Jump velocity applied (12 + 7 = 19 m/s) and tether completely released
            Assert.IsTrue(_playerController.IsVentSuperJumping, "Player must enter vent super jump state.");
            float expectedSuperJumpVelocity = 12.0f + 7.0f; // _jumpForce (12) + _ventSuperJumpBoost (7)
            Assert.GreaterOrEqual(_playerController.Velocity.y, expectedSuperJumpVelocity, "Player must receive full super jump boost without tether choking.");

            Assert.IsFalse(_playerController.IsHauling, "Tether must be severed during vent super jump.");
            Assert.IsFalse(_needleController.IsTetheredToPlayer, "Needle tether must be released during vent super jump.");
            Assert.AreEqual(StitchState.Idle, _crate.CurrentState, "Crate must return to Idle.");

            var joint = _crateObj.GetComponent<DistanceJoint2D>();
            Assert.IsTrue(joint == null || !joint.enabled || joint.connectedBody == null, "DistanceJoint2D must be deactivated so it cannot anchor the player down.");
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
