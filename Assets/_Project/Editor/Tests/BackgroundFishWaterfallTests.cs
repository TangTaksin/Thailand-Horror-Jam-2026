using NUnit.Framework;
using UnityEngine;
using ThailandHorrorJam.Systems.Gameplay;

namespace ThailandHorrorJam.Editor.Tests
{
    [TestFixture]
    public sealed class BackgroundFishWaterfallTests
    {
        private GameObject _go;
        private BackgroundFishWaterfall2D _bg;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("BGFishTest");
            _go.SetActive(false);
            _bg = _go.AddComponent<BackgroundFishWaterfall2D>();
            _go.SetActive(true); // triggers Awake (maxFish default = 60)
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
        }

        [Test]
        public void SpawnOne_ActivatesFish_AndNoCollidersExist()
        {
            Assert.IsTrue(_bg.SpawnOne());
            Assert.AreEqual(1, _bg.ActiveCount);
            Assert.AreEqual(0, _go.GetComponentsInChildren<Collider2D>(true).Length);
        }

        [Test]
        public void SpawnOne_WhenPoolFull_ReturnsFalse()
        {
            for (int i = 0; i < 60; i++) Assert.IsTrue(_bg.SpawnOne());
            Assert.IsFalse(_bg.SpawnOne());
        }
    }
}
