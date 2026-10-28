using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using ThailandHorrorJam.Systems.Gameplay;
using ThailandHorrorJam.Core;

namespace ThailandHorrorJam.Editor.Tests
{
    [TestFixture]
    public class LevelSeamlessTransitionTests
    {
        private GameObject _managerGo;
        private LevelSeamlessManager _manager;
        private List<GameObject> _sectorGos;
        private GameObject _playerGo;

        [SetUp]
        public void SetUp()
        {
            _managerGo = new GameObject("TestLevelSeamlessManager");
            _manager = _managerGo.AddComponent<LevelSeamlessManager>();

            // Create 5 fake sectors
            _sectorGos = new List<GameObject>();
            var sectorsList = new List<LevelSeamlessManager.SectorEntry>();

            for (int i = 0; i < 5; i++)
            {
                var sGo = new GameObject($"TestSector_{i}");
                sGo.transform.position = Vector3.zero;

                // Add a child at x = i * 20
                var child = new GameObject($"Child_{i}");
                child.transform.SetParent(sGo.transform, false);
                child.transform.position = new Vector3(i * 20f, 0f, 0f);

                _sectorGos.Add(sGo);

                var entry = new LevelSeamlessManager.SectorEntry
                {
                    name = $"Sector {i}",
                    rootObject = sGo
                };
                sectorsList.Add(entry);
            }

            // Set private fields via reflection
            typeof(LevelSeamlessManager)
                .GetField("_sectors", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.SetValue(_manager, sectorsList);

            typeof(LevelSeamlessManager)
                .GetField("_activeRadius", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.SetValue(_manager, 1);

            typeof(LevelSeamlessManager)
                .GetField("_initialSectorIndex", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.SetValue(_manager, 0);

            // Invoke Awake
            typeof(LevelSeamlessManager)
                .GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.Invoke(_manager, null);
        }

        [TearDown]
        public void TearDown()
        {
            if (_managerGo != null) Object.DestroyImmediate(_managerGo);
            if (_playerGo != null) Object.DestroyImmediate(_playerGo);
            if (_sectorGos != null)
            {
                foreach (var s in _sectorGos)
                {
                    if (s != null) Object.DestroyImmediate(s);
                }
                _sectorGos.Clear();
            }
        }

        [Test]
        public void InitialSector_Zero_ActivatesOnlySector0And1_CullsSectors2To4()
        {
            _manager.SetCurrentSector(0);

            Assert.IsTrue(_sectorGos[0].activeSelf, "Sector 0 should be active (current)");
            Assert.IsTrue(_sectorGos[1].activeSelf, "Sector 1 should be active (adjacent forward)");
            Assert.IsFalse(_sectorGos[2].activeSelf, "Sector 2 should be culled (distance 2)");
            Assert.IsFalse(_sectorGos[3].activeSelf, "Sector 3 should be culled (distance 3)");
            Assert.IsFalse(_sectorGos[4].activeSelf, "Sector 4 should be culled (distance 4)");
        }

        [Test]
        public void TransitionToSector2_ActivatesSectors123_CullsSectors0And4()
        {
            _manager.SetCurrentSector(0);
            _manager.TransitionToSector(2);

            Assert.IsFalse(_sectorGos[0].activeSelf, "Sector 0 should be culled (distance 2)");
            Assert.IsTrue(_sectorGos[1].activeSelf, "Sector 1 should be active (adjacent backward)");
            Assert.IsTrue(_sectorGos[2].activeSelf, "Sector 2 should be active (current)");
            Assert.IsTrue(_sectorGos[3].activeSelf, "Sector 3 should be active (adjacent forward)");
            Assert.IsFalse(_sectorGos[4].activeSelf, "Sector 4 should be culled (distance 2)");
        }

        [Test]
        public void TransitionToSector4_ActivatesOnlySectors3And4()
        {
            _manager.SetCurrentSector(0);
            _manager.TransitionToSector(4);

            Assert.IsFalse(_sectorGos[0].activeSelf, "Sector 0 culled");
            Assert.IsFalse(_sectorGos[1].activeSelf, "Sector 1 culled");
            Assert.IsFalse(_sectorGos[2].activeSelf, "Sector 2 culled");
            Assert.IsTrue(_sectorGos[3].activeSelf, "Sector 3 active (adjacent)");
            Assert.IsTrue(_sectorGos[4].activeSelf, "Sector 4 active (current)");
        }

        [Test]
        public void TransitionToSectorObject_FindsCorrectIndex()
        {
            _manager.SetCurrentSector(0);
            _manager.TransitionToSector(_sectorGos[3]);

            Assert.IsFalse(_sectorGos[0].activeSelf);
            Assert.IsFalse(_sectorGos[1].activeSelf);
            Assert.IsTrue(_sectorGos[2].activeSelf);
            Assert.IsTrue(_sectorGos[3].activeSelf);
            Assert.IsTrue(_sectorGos[4].activeSelf);
        }

        [Test]
        public void GetSectorCenterX_ComputesCenterFromChildren_WhenRootAtZero()
        {
            var entry = new LevelSeamlessManager.SectorEntry
            {
                name = "Sector 3",
                rootObject = _sectorGos[3] // Child is at x = 60
            };

            float centerX = LevelSeamlessManager.GetSectorCenterX(entry);
            Assert.AreEqual(60f, centerX, 0.01f);
        }
    }
}
