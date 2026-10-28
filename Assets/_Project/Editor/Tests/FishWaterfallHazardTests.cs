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

        [Test]
        public void FishHazard_Spawn_ResetsScaleToOne_AndInitialFrameToZero()
        {
            var dummyFrames = new Sprite[3];
            dummyFrames[0] = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), Vector2.zero);
            dummyFrames[1] = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), Vector2.zero);
            dummyFrames[2] = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), Vector2.zero);

            _fish.Spawn(Vector3.zero, null, dummyFrames);

            // Simulate distortion & frame change
            _fish.transform.localScale = new Vector3(2.5f, 0.5f, 1f);
            var onHit = _fish.OnHitByNeedle(null, Vector2.zero, Vector2.right);

            // Despawn and re-spawn
            _fish.Despawn();
            _fish.Spawn(new Vector3(5f, 5f, 0f), null, dummyFrames);

            Assert.AreEqual(Vector3.one, _fish.transform.localScale, "Fish localScale must be strictly reset to (1, 1, 1) on spawn.");
            Assert.AreEqual(0, _fish.CurrentFrame, "Fish animation frame must reset to frame 0 on spawn.");
            var sr = _fish.GetComponent<SpriteRenderer>();
            Assert.IsNotNull(sr);
            Assert.AreEqual(Color.white, sr.color, "SpriteRenderer color must reset to pure white.");
            Assert.IsFalse(sr.flipX, "SpriteRenderer flipX must reset to false.");
            Assert.IsFalse(sr.flipY, "SpriteRenderer flipY must reset to false.");
        }

        [Test]
        public void FishHazard_AfterShieldCollectAndDespawn_RestoresPoolParent()
        {
            var poolParentGo = new GameObject("TestPoolRoot");
            var shieldParentGo = new GameObject("TestShieldCrate");

            try
            {
                _fish.transform.SetParent(poolParentGo.transform, false);
                _fish.SetPoolParent(poolParentGo.transform);

                _fish.Spawn(Vector3.zero, null);
                Assert.AreEqual(poolParentGo.transform, _fish.transform.parent, "Spawned fish should start under poolParent.");

                // Collect on shield (attaches to shield crate)
                _fish.CollectOnShield(shieldParentGo.transform, Vector3.up);
                Assert.AreEqual(shieldParentGo.transform, _fish.transform.parent, "Fish should be parented to shield crate.");

                // Despawn
                _fish.Despawn();

                // Must restore pool root
                Assert.AreEqual(poolParentGo.transform, _fish.transform.parent, "Despawned fish must restore parent back to poolRoot.");
            }
            finally
            {
                Object.DestroyImmediate(poolParentGo);
                Object.DestroyImmediate(shieldParentGo);
            }
        }

        [Test]
        public void FishHazard_NumericalSpriteFrames_AreInAscendingSequence()
        {
            var sprites = LevelTemplateCreator.LoadFishSprites();
            Assert.IsNotNull(sprites, "Sprites array must load.");
            Assert.GreaterOrEqual(sprites.Length, 24, "Spritesheet must contain at least 24 frames.");

            // Verify numerical sort: frame 2 must appear before frame 10
            int indexFrame2 = -1;
            int indexFrame10 = -1;
            for (int i = 0; i < sprites.Length; i++)
            {
                if (sprites[i].name == "Dead_Fish_SpriteSheet_2") indexFrame2 = i;
                if (sprites[i].name == "Dead_Fish_SpriteSheet_10") indexFrame10 = i;
            }

            Assert.Greater(indexFrame2, -1, "Frame 2 must exist.");
            Assert.Greater(indexFrame10, -1, "Frame 10 must exist.");
            Assert.Less(indexFrame2, indexFrame10, "Frame 2 must precede Frame 10 in numerical sort order.");
        }
    }
}
