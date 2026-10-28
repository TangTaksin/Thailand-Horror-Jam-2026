using NUnit.Framework;
using UnityEngine;
using ThailandHorrorJam.Core;
using ThailandHorrorJam.Systems.Gameplay;
using ThailandHorrorJam.Systems.Needle;

namespace ThailandHorrorJam.Editor.Tests
{
    [TestFixture]
    public sealed class FishWaterfallHazardTests
    {
        private GameObject _fishGo;
        private FallingFishHazard2D _fish;

        [SetUp]
        public void SetUp()
        {
            _fishGo = new GameObject("TestFish");
            var col = _fishGo.AddComponent<CircleCollider2D>();
            col.isTrigger = true;
            col.radius = 0.28f;
            _fish = _fishGo.AddComponent<FallingFishHazard2D>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_fishGo != null)
            {
                Object.DestroyImmediate(_fishGo);
            }
        }

        [Test]
        public void FishHazard_InitialSpawn_SetsActiveAndNotDeflected()
        {
            Vector3 spawnPos = new Vector3(109.5f, 7.5f, 0f);
            _fish.Spawn(spawnPos, null);

            Assert.IsTrue(_fish.gameObject.activeSelf);
            Assert.IsFalse(_fish.IsDeflected);
            Assert.AreEqual(spawnPos, _fish.transform.position);
        }

        [Test]
        public void FishHazard_GentleNeedleHit_DeflectsSidewaysWithoutHarm()
        {
            _fish.Spawn(new Vector3(109.5f, 7.5f, 0f), null);

            // Needle shot moving to the right
            Vector2 needleHitDir = Vector2.right;
            bool hitAccepted = _fish.OnHitByNeedle(null, _fish.transform.position, needleHitDir);

            Assert.IsTrue(hitAccepted, "First needle hit should be accepted.");
            Assert.IsTrue(_fish.IsDeflected, "Fish should enter deflected state.");

            // Subsequent hits while deflected should be ignored
            bool secondHit = _fish.OnHitByNeedle(null, _fish.transform.position, needleHitDir);
            Assert.IsFalse(secondHit, "Already deflected fish should ignore repeated hits.");
        }

        [Test]
        public void FishHazard_Despawn_DeactivatesGameObject()
        {
            bool returnedToPool = false;
            _fish.Spawn(new Vector3(109.5f, 7.5f, 0f), f => returnedToPool = true);

            _fish.Despawn();

            Assert.IsFalse(_fish.gameObject.activeSelf);
            Assert.IsTrue(returnedToPool);
        }

        [Test]
        public void FishHazard_Knockback_PushesPlayerToTheLeft()
        {
            var playerGo = new GameObject("TestPlayer");
            playerGo.tag = "Player";
            var rb = playerGo.AddComponent<Rigidbody2D>();
            var col = playerGo.AddComponent<BoxCollider2D>();
            var player = playerGo.AddComponent<PlayerController2D>();
            typeof(PlayerController2D).GetMethod("Awake", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.Invoke(player, null);

            try
            {
                // Position player to the left of the fish
                playerGo.transform.position = new Vector3(108.0f, 2.0f, 0f);
                _fish.Spawn(new Vector3(109.0f, 2.0f, 0f), null);

                // Simulate trigger hit
                var method = typeof(FallingFishHazard2D).GetMethod("OnTriggerEnter2D", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                method.Invoke(_fish, new object[] { col });

                // Velocity should be pushing left (-X)
                Assert.Less(player.Velocity.x, 0f, "Knockback velocity must push player to the left (-X).");
            }
            finally
            {
                Object.DestroyImmediate(playerGo);
            }
        }

        [Test]
        public void FishHazard_SpeedRemainsConstant_AfterNeedleHitAndRespawn()
        {
            // 1. Initial spawn: velocity must be strictly (0, -_fallSpeed)
            _fish.Spawn(new Vector3(109.5f, 7.5f, 0f), null);
            Assert.AreEqual(0f, _fish.CurrentVelocity.x, 0.001f, "Horizontal velocity should be 0 on normal fall.");
            Assert.AreEqual(-_fish.FallSpeed, _fish.CurrentVelocity.y, 0.001f, "Vertical fall speed must strictly equal -_fallSpeed.");
            Assert.IsFalse(_fish.IsDeflected);

            // 2. Hit by needle: deflects with positive upward and lateral speed
            bool hit = _fish.OnHitByNeedle(null, _fish.transform.position, Vector2.right);
            Assert.IsTrue(hit);
            Assert.IsTrue(_fish.IsDeflected);
            Assert.Greater(_fish.CurrentVelocity.x, 0f, "Deflected fish should have positive lateral velocity.");
            Assert.Greater(_fish.CurrentVelocity.y, 0f, "Deflected fish should have positive upward velocity.");

            // 3. Despawn back to pool
            _fish.Despawn();
            Assert.IsFalse(_fish.gameObject.activeSelf);

            // 4. Respawn from pool: velocity must be completely reset back to constant (0, -_fallSpeed)
            _fish.Spawn(new Vector3(109.5f, 7.5f, 0f), null);
            Assert.IsFalse(_fish.IsDeflected, "Fish must not be deflected after respawning.");
            Assert.AreEqual(0f, _fish.CurrentVelocity.x, 0.001f, "Horizontal velocity must reset to 0 after respawn.");
            Assert.AreEqual(-_fish.FallSpeed, _fish.CurrentVelocity.y, 0.001f, "Fall speed must strictly reset to -_fallSpeed after respawn.");
        }
    }
}
