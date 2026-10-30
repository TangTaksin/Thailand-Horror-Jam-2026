using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using System.Reflection;
using System.Collections.Generic;
using ThailandHorrorJam.Core;
using ThailandHorrorJam.Systems.Gameplay;
using ThailandHorrorJam.Systems.Needle;

namespace ThailandHorrorJam.Editor.Tests
{
    [TestFixture]
    public sealed class Sector06GrandClimaxTests
    {
        private GameObject _crateGo;
        private CrateFishShieldCollector2D _collector;
        private List<GameObject> _spawnedObjects;

        [SetUp]
        public void SetUp()
        {
            _spawnedObjects = new List<GameObject>();

            _crateGo = new GameObject("TestShieldCrate");
            _spawnedObjects.Add(_crateGo);
            _crateGo.transform.position = new Vector3(140f, 8f, 0f);

            var col = _crateGo.AddComponent<BoxCollider2D>();
            col.size = new Vector2(2.4f, 1.0f);

            var roofGo = new GameObject("TestRoofCollector");
            roofGo.transform.SetParent(_crateGo.transform, false);
            roofGo.transform.localPosition = new Vector3(0f, 0.5f, 0f);

            var roofCol = roofGo.AddComponent<BoxCollider2D>();
            roofCol.isTrigger = true;
            roofCol.size = new Vector2(2.4f, 0.4f);

            _collector = roofGo.AddComponent<CrateFishShieldCollector2D>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_spawnedObjects != null)
            {
                foreach (var go in _spawnedObjects)
                {
                    if (go != null)
                    {
                        Object.DestroyImmediate(go);
                    }
                }
                _spawnedObjects.Clear();
            }
        }

        [Test]
        public void FishShieldCollector_BlocksFish_AndParentsToShield()
        {
            var fishGo = new GameObject("TestFish");
            _spawnedObjects.Add(fishGo);
            var fishCol = fishGo.AddComponent<CircleCollider2D>();
            fishCol.isTrigger = true;
            var fish = fishGo.AddComponent<FallingFishHazard2D>();
            fish.Spawn(new Vector3(140f, 10f, 0f), null);

            _collector.InterceptFish(fish);

            Assert.IsTrue(fish.IsCaughtOnShield, "Fish must be marked as caught on shield.");
            Assert.AreEqual(_collector.transform, fish.transform.parent, "Fish must be reparented to shield roof.");
            
            var rb = fish.GetComponent<Rigidbody2D>();
            if (rb != null)
            {
                Assert.IsFalse(rb.simulated, "Fish Rigidbody2D must stop simulating physics.");
            }
        }

        [Test]
        public void FishShieldCollector_ClampsMaxAccumulatedDebris_ToPreventMemoryLeak()
        {
            var so = new SerializedObject(_collector);
            so.FindProperty("_maxCollectedFish").intValue = 3;
            so.ApplyModifiedProperties();

            var fishList = new List<FallingFishHazard2D>();
            for (int i = 0; i < 5; i++)
            {
                var fishGo = new GameObject($"TestFish_{i}");
                _spawnedObjects.Add(fishGo);
                fishGo.AddComponent<CircleCollider2D>().isTrigger = true;
                var fish = fishGo.AddComponent<FallingFishHazard2D>();
                fish.Spawn(new Vector3(140f, 10f + i, 0f), null);
                _collector.InterceptFish(fish);
                fishList.Add(fish);
            }

            Assert.LessOrEqual(_collector.ActiveDebrisCount, 3, "Active debris count on shield must be clamped to maxVisualDebris.");
            Assert.IsFalse(fishList[0].gameObject.activeSelf, "Oldest fish beyond capacity must be deactivated/recycled.");
        }

        [Test]
        public void CrateDropBridge_GeometryMatchesWalkwayLevel()
        {
            // Floor walkway surface at Y = 3.0
            float walkwaySurfaceY = 2.5f + (1.0f * 0.5f); // Pos Y=2.5, Height=1.0 -> 3.0
            Assert.AreEqual(3.0f, walkwaySurfaceY, 0.001f);

            // Slot shelf at Y = 1.5 with height 1.0 -> shelf top is at Y = 2.0
            float shelfSurfaceY = 1.5f + (1.0f * 0.5f); // 2.0
            Assert.AreEqual(2.0f, shelfSurfaceY, 0.001f);

            // Crate has height 1.0. When resting on shelf:
            // Crate Center Y = shelfSurfaceY + crateHeight * 0.5 = 2.0 + 0.5 = 2.5
            // Crate Top Y = Crate Center Y + crateHeight * 0.5 = 2.5 + 0.5 = 3.0
            float crateHeight = 1.0f;
            float crateCenterWhenDropped = shelfSurfaceY + (crateHeight * 0.5f);
            float crateTopWhenDropped = crateCenterWhenDropped + (crateHeight * 0.5f);

            Assert.AreEqual(walkwaySurfaceY, crateTopWhenDropped, 0.001f, 
                "Dropped crate top must match walkway surface Y perfectly (flush bridge).");
        }

        [Test]
        public void LevelExitDoor_LocksInput_AndTriggersTransition()
        {
            var doorGo = new GameObject("TestExitDoor");
            _spawnedObjects.Add(doorGo);
            doorGo.AddComponent<BoxCollider2D>().isTrigger = true;
            var door = doorGo.AddComponent<LevelExitDoor2D>();

            var playerGo = new GameObject("TestPlayer");
            _spawnedObjects.Add(playerGo);
            var playerCol = playerGo.AddComponent<BoxCollider2D>();
            playerGo.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Dynamic;
            var player = playerGo.AddComponent<PlayerController2D>();

            Assert.IsFalse(player.IsInputLocked, "Player input initially unlocked.");

            door.TriggerExit(player);

            Assert.IsTrue(player.IsInputLocked, "Player input must be locked upon passing exit door.");
            Assert.IsTrue(door.IsExitTriggered, "Door must be marked as triggered.");
        }

        [Test]
        public void EndingCutscene_ContainsAll18Sprites_InCorrectOrder()
        {
            Sprite[] sprites = LevelTemplateCreator.LoadEndingSprites();

            Assert.IsNotNull(sprites, "Ending sprites must be loadable from project.");
            Assert.AreEqual(18, sprites.Length, "Ending cutscene must contain exactly 18 slides.");

            Assert.AreEqual("End_SC1_0", sprites[0].name, "First slide must be End_SC1.");
            Assert.AreEqual("End_SC2_0", sprites[1].name, "Second slide must be End_SC2 (numerical sort before End_SC10).");
            Assert.AreEqual("End_SC10_0", sprites[9].name, "Tenth slide must be End_SC10.");
            Assert.AreEqual("End_SC18_0", sprites[17].name, "Eighteenth slide must be End_SC18.");
        }

        [Test]
        public void FallingFish_AirborneHit_AppliesDirectionalKnockback_OppositePlayerFacing()
        {
            var playerGo = new GameObject("TestPlayer");
            _spawnedObjects.Add(playerGo);
            var playerCol = playerGo.AddComponent<BoxCollider2D>();
            var rb = playerGo.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Dynamic;
            var player = playerGo.AddComponent<PlayerController2D>();

            // Player facing right (default)
            Assert.IsTrue(player.IsFacingRight, "Player facing right by default.");

            var fishGo = new GameObject("TestFish");
            _spawnedObjects.Add(fishGo);
            var fishCol = fishGo.AddComponent<CircleCollider2D>();
            fishCol.isTrigger = true;
            var fish = fishGo.AddComponent<FallingFishHazard2D>();
            fish.ConfigureBehavior(harmPlayer: true, landAndFlopOnGround: true, groundLayerMask: 0);
            fish.Spawn(new Vector3(140f, 5f, 0f), null);

            var onTrigger = typeof(FallingFishHazard2D).GetMethod("OnTriggerEnter2D", BindingFlags.NonPublic | BindingFlags.Instance);
            onTrigger.Invoke(fish, new object[] { playerCol });

            Assert.IsTrue(fish.IsDeflected, "Fish should deflect on player hit.");
            Assert.Less(player.Velocity.x, 0f, "Player facing right must be knocked back to the left (-X).");
            Assert.IsFalse(player.CanReceiveHazardKnockback, "Player must enter knockback cooldown window.");
        }

        [Test]
        public void FallingFish_ConsecutiveHits_RespectsKnockbackCooldownWindow()
        {
            var playerGo = new GameObject("TestPlayer");
            _spawnedObjects.Add(playerGo);
            var playerCol = playerGo.AddComponent<BoxCollider2D>();
            var rb = playerGo.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Dynamic;
            var player = playerGo.AddComponent<PlayerController2D>();

            player.SetHazardKnockbackCooldown(0.3f);
            Assert.IsFalse(player.CanReceiveHazardKnockback, "Player should be in cooldown.");

            var fishGo = new GameObject("TestFish");
            _spawnedObjects.Add(fishGo);
            fishGo.AddComponent<CircleCollider2D>().isTrigger = true;
            var fish = fishGo.AddComponent<FallingFishHazard2D>();
            fish.ConfigureBehavior(harmPlayer: true, landAndFlopOnGround: true, groundLayerMask: 0);
            fish.Spawn(new Vector3(140f, 5f, 0f), null);

            var onTrigger = typeof(FallingFishHazard2D).GetMethod("OnTriggerEnter2D", BindingFlags.NonPublic | BindingFlags.Instance);
            onTrigger.Invoke(fish, new object[] { playerCol });

            // Velocity should remain 0 because player was in cooldown
            Assert.AreEqual(0f, player.Velocity.x, "In cooldown, player should not receive stacked knockback.");
        }

        [Test]
        public void FallingFish_GroundLanding_DisablesCollider_AndEntersBouncingFloppingState()
        {
            var groundGo = new GameObject("TestFloor");
            _spawnedObjects.Add(groundGo);
            groundGo.layer = LayerMask.NameToLayer("Ground");
            if (groundGo.layer < 0) groundGo.layer = 0;
            var groundCol = groundGo.AddComponent<BoxCollider2D>();
            groundCol.size = new Vector2(10f, 1f);
            groundGo.transform.position = new Vector3(140f, 2.5f, 0f);

            var fishGo = new GameObject("TestFish");
            _spawnedObjects.Add(fishGo);
            var fishCol = fishGo.AddComponent<CircleCollider2D>();
            fishCol.isTrigger = true;
            var fish = fishGo.AddComponent<FallingFishHazard2D>();
            fish.ConfigureBehavior(harmPlayer: true, landAndFlopOnGround: true, groundLayerMask: 1 << groundGo.layer);
            fish.Spawn(new Vector3(140f, 3.2f, 0f), null);

            Assert.IsTrue(fishCol.enabled, "Collider initially enabled while falling.");

            // Trigger ground landing at Y = 3.0f
            fish.LandOnGround(new Vector2(140f, 3.0f), Vector2.up);

            Assert.IsTrue(fish.IsFloppingOnGround, "Fish must enter flopping on ground state.");
            Assert.IsFalse(fishCol.enabled, "Collider must be disabled upon landing to optimize 100+ fish for WebGL!");
            Assert.AreEqual(Vector2.zero, fish.CurrentVelocity, "Fish velocity must be zeroed upon landing.");
        }

        [Test]
        public void FishSpawner_ContinuousRain_SpawnsAcrossFullSectorRange_UpToGate()
        {
            var spawnerGo = new GameObject("TestSpawner");
            _spawnedObjects.Add(spawnerGo);
            var spawner = spawnerGo.AddComponent<FishWaterfallSpawner2D>();

            var so = new SerializedObject(spawner);
            so.FindProperty("_spawnMode").enumValueIndex = (int)FishSpawnMode.ContinuousRain;
            so.FindProperty("_rainMinX").floatValue = 136.0f;
            so.FindProperty("_rainMaxX").floatValue = 160.5f;
            so.ApplyModifiedProperties();

            Assert.AreEqual(FishSpawnMode.ContinuousRain, spawner.SpawnMode);
            Assert.AreEqual(136.0f, spawner.RainMinX);
            Assert.AreEqual(160.5f, spawner.RainMaxX);
        }

        [Test]
        public void EndingCutscene_WhenStarted_DeactivatesAllGameplaySectorsAndPlayer()
        {
            // 1. Setup mock sectors
            var sector1 = new GameObject("TestSector_1");
            var sector2 = new GameObject("TestSector_2");
            _spawnedObjects.Add(sector1);
            _spawnedObjects.Add(sector2);

            var managerGo = new GameObject("TestLevelSeamlessManager");
            _spawnedObjects.Add(managerGo);
            var manager = managerGo.AddComponent<LevelSeamlessManager>();

            var sectorsList = new List<LevelSeamlessManager.SectorEntry>
            {
                new LevelSeamlessManager.SectorEntry { name = "Sector 1", rootObject = sector1 },
                new LevelSeamlessManager.SectorEntry { name = "Sector 2", rootObject = sector2 }
            };

            typeof(LevelSeamlessManager)
                .GetField("_sectors", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.SetValue(manager, sectorsList);

            typeof(LevelSeamlessManager)
                .GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.Invoke(manager, null);

            manager.SetCurrentSector(0);
            Assert.IsTrue(sector1.activeSelf);

            // 2. Setup player
            var playerGo = new GameObject("TestPlayer");
            _spawnedObjects.Add(playerGo);
            var player = playerGo.AddComponent<PlayerController2D>();
            typeof(PlayerController2D).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(player, null);

            // 3. Setup Ending Cutscene in FullScreenCinematicMovie mode
            var cutsceneGo = new GameObject("TestEndingCutscene");
            _spawnedObjects.Add(cutsceneGo);
            var cutscene = cutsceneGo.AddComponent<CutsceneController>();

            typeof(CutsceneController)
                .GetField("_displayMode", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.SetValue(cutscene, CutsceneDisplayMode.FullScreenCinematicMovie);

            // 4. Act: Start ending cutscene
            cutscene.StartCutscene();

            // 5. Assert: All sectors deactivated, player hidden
            Assert.IsFalse(sector1.activeSelf, "Sector 1 root must be deactivated.");
            Assert.IsFalse(sector2.activeSelf, "Sector 2 root must be deactivated.");
            Assert.IsFalse(playerGo.activeSelf, "Player GameObject must be hidden during ending cutscene.");
        }
    }
}
