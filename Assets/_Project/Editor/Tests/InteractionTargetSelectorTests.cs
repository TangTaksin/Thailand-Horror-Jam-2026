using NUnit.Framework;
using UnityEngine;
using ThailandHorrorJam.Systems.Interaction;

namespace ThailandHorrorJam.Editor.Tests
{
    [TestFixture]
    public sealed class InteractionTargetSelectorTests
    {
        private InteractionEventChannelSO _channel;

        [SetUp]
        public void SetUp() => _channel = ScriptableObject.CreateInstance<InteractionEventChannelSO>();

        [TearDown]
        public void TearDown()
        {
            if (_channel != null) Object.DestroyImmediate(_channel);
        }

        [Test]
        public void DeterminePromptStage_ReturnsExpectedStageForDistanceThresholds()
        {
            Assert.AreEqual(PromptStage.InteractIcon, PlayerInteractor.DeterminePromptStage(2.0f, 3.5f, 5.5f));
            Assert.AreEqual(PromptStage.AwareDot, PlayerInteractor.DeterminePromptStage(4.2f, 3.5f, 5.5f));
            Assert.AreEqual(PromptStage.Hidden, PlayerInteractor.DeterminePromptStage(6.0f, 3.5f, 5.5f));
        }

        [Test]
        public void EvaluateCandidateScore_InnerRadiusTarget_AlwaysOutranksOuterRadiusTarget()
        {
            float innerScore = PlayerInteractor.EvaluateCandidateScore(
                Vector2.zero, Vector2.right, new Vector2(-2.0f, 0f), 2.0f, 3.5f, 5.5f, 0.35f, 0.12f, false);

            float outerScore = PlayerInteractor.EvaluateCandidateScore(
                Vector2.zero, Vector2.right, new Vector2(4.0f, 0f), 4.0f, 3.5f, 5.5f, 0.35f, 0.12f, true);

            Assert.Greater(innerScore, outerScore);
        }

        [Test]
        public void EvaluateCandidateScore_EquidistantTargets_PrefersTargetInFacingDirection()
        {
            float frontScore = PlayerInteractor.EvaluateCandidateScore(
                Vector2.zero, Vector2.right, new Vector2(2.0f, 0f), 2.0f, 3.5f, 5.5f, 0.35f, 0.12f, false);

            float behindScore = PlayerInteractor.EvaluateCandidateScore(
                Vector2.zero, Vector2.right, new Vector2(-2.0f, 0f), 2.0f, 3.5f, 5.5f, 0.35f, 0.12f, false);

            Assert.Greater(frontScore, behindScore);
        }

        [Test]
        public void InteractionEventChannel_BroadcastsEvents()
        {
            InteractionFocusData received = InteractionFocusData.Hidden;
            _channel.FocusChanged += data => received = data;

            _channel.RaiseFocusChanged(new InteractionFocusData(null, PromptStage.InteractIcon));

            Assert.AreEqual(PromptStage.InteractIcon, received.Stage);
        }
    }
}
