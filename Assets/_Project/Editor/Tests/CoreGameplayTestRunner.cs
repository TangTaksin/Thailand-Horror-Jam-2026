using UnityEditor;
using UnityEngine;

namespace ThailandHorrorJam.Editor.Tests
{
    public static class CoreGameplayTestRunner
    {
        [MenuItem("Tools/Tests/Run All Core Stitch & Loop Tests")]
        public static void RunAllCoreTests()
        {
            CleanupLeftoverTestObjects();

            int passed = 0;
            int failed = 0;

            void RunTest(System.Action testAction, string testName)
            {
                try
                {
                    testAction();
                    passed++;
                    Debug.Log($"<color=green>[PASS]</color> {testName}");
                }
                catch (System.Exception ex)
                {
                    failed++;
                    Debug.LogError($"<color=red>[FAIL]</color> {testName}\n{ex}");
                }
                finally
                {
                    CleanupLeftoverTestObjects();
                }
            }

            Debug.Log("=== Starting Core Stitch & Loop Unit Tests ===");

            // 1. StitchableStateTransitionTests
            var t1 = new StitchableStateTransitionTests();

            RunTest(() => { t1.SetUp(); t1.InitialState_IsIdle_AndRigidbodyIsDynamic(); t1.TearDown(); },
                "StitchableState: Initial State Is Idle & Rigidbody Is Dynamic");

            RunTest(() => { t1.SetUp(); t1.StartLoopAlongWaypoints_SwitchesRigidbodyToStatic(); t1.TearDown(); },
                "StitchableState: StartLoop Switches Rigidbody To Static");

            RunTest(() => { t1.SetUp(); t1.CutThread_RestoresRigidbodyToDynamicAndPreservesGravity(); t1.TearDown(); },
                "StitchableState: CutThread Restores Dynamic BodyType & Preserves Gravity");

            RunTest(() => { t1.SetUp(); t1.StartLoopAlongWaypoints_WithLessThanTwoPoints_ImmediatelyCutsThread(); t1.TearDown(); },
                "StitchableState: StartLoop With < 2 Points Immediately Cuts Thread");

            RunTest(() => { t1.SetUp(); t1.StartLoopAlongWaypoints_WhenAlreadyStatic_DoesNotThrow(); t1.TearDown(); },
                "StitchableState: StartLoop When Already Static Does Not Throw");

            RunTest(() => { t1.SetUp(); t1.ResetToInitialState_RestoresTransformPositionAndClearsVelocities(); t1.TearDown(); },
                "StitchableState: ResetToInitialState Restores Position & Velocity");

            RunTest(() => { t1.SetUp(); t1.ResetToInitialState_WhenPreserveLoopOnRespawnTrueAndLooping_PreservesLoopingState(); t1.TearDown(); },
                "StitchableState: Preserve Loop On Respawn Keeps Looping Circuit Active");

            RunTest(() => { t1.SetUp(); t1.ResetToInitialState_WhenForceReset_ResetsEvenIfPreserveLoopTrue(); t1.TearDown(); },
                "StitchableState: Force Reset Resets Crate To Alcove Even If Preserve Loop True");

            // 2. StitchableWaypointNavigationTests
            var t2 = new StitchableWaypointNavigationTests();

            RunTest(() => { t2.SetUp(); t2.TraverseWaypoints_MovesTowardsTarget(); t2.TearDown(); },
                "WaypointNav: TraverseWaypoints Moves Towards Target");

            RunTest(() => { t2.SetUp(); t2.PingPongNavigation_ReversesDirection_WhenReachingEnd(); t2.TearDown(); },
                "WaypointNav: PingPong Loop Reverses At End Of Track");

            RunTest(() => { t2.SetUp(); t2.FixedUpdate_CalculatesCurrentVelocityAndFrameDelta(); t2.TearDown(); },
                "WaypointNav: FixedUpdate Calculates Velocity & FrameDelta");

            RunTest(() => { t2.SetUp(); t2.PlayerLandingOnDescendingLoopingBox_IsDetectedAsGroundedImmediately(); t2.TearDown(); },
                "WaypointNav: Player Landing On Descending Looping Box Is Detected As Grounded Immediately");

            RunTest(() => { t2.SetUp(); t2.PlayerStandingOnRisingLoopingBox_MaintainsGroundedAndDoesNotTriggerJumpVisualState(); t2.TearDown(); },
                "WaypointNav: Player Standing On Rising Looping Box Maintains Grounded & Never Enters Jump");

            RunTest(() => { t2.SetUp(); t2.PlayerPushingAgainstVerticalWall_IsNotGrounded_AndFallsUnderGravity(); t2.TearDown(); },
                "WaypointNav: Player Pressing Against Vertical Wall Never Floats (Falls Under Gravity)");

            RunTest(() => { t2.SetUp(); t2.PlayerPressedAgainstStitchableCrate_WhenJumping_AscendsAndIsNotClampedOrGroundedOnWall(); t2.TearDown(); },
                "WaypointNav: Player Pressed Against Stitchable Crate Jumps Freely Without Sticking To Wall");

            RunTest(() => { t2.SetUp(); t2.PlayerStandingOnRisingLoopingBox_AtApexReversal_RemainsGroundedAndNeverEntersFallVisualState(); t2.TearDown(); },
                "WaypointNav: Player Standing On Rising Box At Apex Reversal Remains Grounded In Idle");

            // 3. NeedleBudgetAndInteractionTests
            var t3 = new NeedleBudgetAndInteractionTests();

            RunTest(() => { t3.SetUp(); t3.ThreadBudget_AllowsDistancesWithinConfiguredRange(); t3.TearDown(); },
                "NeedleBudget: Allows Distances Within Configured Range");

            RunTest(() => { t3.SetUp(); t3.ThreadBudget_RejectsDistancesOutsideConfiguredRange(); t3.TearDown(); },
                "NeedleBudget: Rejects Distances Outside Configured Range");

            RunTest(() => { t3.SetUp(); t3.ClickingLoopingBox_IsIgnored_PreservesLoopingState(); t3.TearDown(); },
                "NeedleInteraction: Clicking Looping Box Is Ignored (Preserves Looping State)");

            RunTest(() => { t3.SetUp(); t3.NeedlePinMarkers_HaveDontSaveFlags_AndAreCleanedUpWithoutLeaking(); t3.TearDown(); },
                "NeedlePinLifecycle: Pin Markers Have HideFlags.DontSave & Cleaned Up Without Leaks");

            // 4. CorpseGasVentTests
            var t4 = new CorpseGasVentTests();
            RunTest(() => { t4.SetUp(); t4.FindBlockingCrate_DetectsCrateOverNozzle(); t4.TearDown(); },
                "CorpseGasVent: Detects Crate Over Nozzle");
            RunTest(() => { t4.SetUp(); t4.VisualHeight_ClampedToZero_WhenCrateRestingOnNozzle(); t4.TearDown(); },
                "CorpseGasVent: Clamps Visual Height To Zero When Resting On Nozzle");
            RunTest(() => { t4.SetUp(); t4.VisualHeight_ExpandsUnderneath_WhenCrateIsLifted(); t4.TearDown(); },
                "CorpseGasVent: Expands Visual Height Underneath Lifted Crate");

            // 5. UniversalHangingObjectTests
            var t5 = new UniversalHangingObjectTests();
            RunTest(() => { t5.SetUp(); t5.HangingObject_StartsSuspendedAndStatic(); t5.TearDown(); },
                "UniversalHanging: Starts Suspended And Static");
            RunTest(() => { t5.SetUp(); t5.SeverHangingThread_DrawbridgeMode_BecomesKinematicRotation(); t5.TearDown(); },
                "UniversalHanging: Severing Drawbridge Thread Becomes Kinematic Rotation");
            RunTest(() => { t5.SetUp(); t5.SeverHangingThread_FreeFallMode_BecomesDynamicGravityDrop(); t5.TearDown(); },
                "UniversalHanging: Severing FreeFall Thread Becomes Dynamic Gravity Drop");
            RunTest(() => { t5.SetUp(); t5.SeverHangingThread_WithStitchableObject_DelegatesToStitchableAfterDrop(); t5.TearDown(); },
                "UniversalHanging: Severing Thread Delegates To Stitchable After Drop");
            RunTest(() => { t5.SetUp(); t5.TetherHangingObject_WhileSuspended_RemainsStaticAndSuspended(); t5.TearDown(); },
                "UniversalHanging: Tether While Suspended Remains Static And Suspended");
            RunTest(() => { t5.SetUp(); t5.SeverHangingThread_WhileAlreadyTethered_DropsAsDynamicAndMaintainsTether(); t5.TearDown(); },
                "UniversalHanging: Severing Thread While Already Tethered Drops As Dynamic And Maintains Tether");

            // 6. LevelSeamlessTransitionTests
            var t6 = new LevelSeamlessTransitionTests();
            RunTest(() => { t6.SetUp(); t6.InitialSector_Zero_ActivatesOnlySector0And1_CullsSectors2To4(); t6.TearDown(); },
                "LevelSeamless: Initial Sector 0 Activates Sector 0 & 1, Culls Sectors 2 to 4");
            RunTest(() => { t6.SetUp(); t6.TransitionToSector2_ActivatesSectors123_CullsSectors0And4(); t6.TearDown(); },
                "LevelSeamless: Transition To Sector 2 Activates 1, 2, 3 and Culls 0, 4");
            RunTest(() => { t6.SetUp(); t6.TransitionToSector4_ActivatesOnlySectors3And4(); t6.TearDown(); },
                "LevelSeamless: Transition To Sector 4 Activates Only 3 and 4");
            RunTest(() => { t6.SetUp(); t6.TransitionToSectorObject_FindsCorrectIndex(); t6.TearDown(); },
                "LevelSeamless: Transition To SectorObject Finds Correct Index");
            RunTest(() => { t6.SetUp(); t6.GetSectorCenterX_ComputesCenterFromChildren_WhenRootAtZero(); t6.TearDown(); },
                "LevelSeamless: GetSectorCenterX Computes Center From Children When Root At Zero");

            // 7. FishWaterfallHazardTests
            var t7 = new FishWaterfallHazardTests();
            RunTest(() => { t7.SetUp(); t7.FishHazard_InitialSpawn_SetsActiveAndNotDeflected(); t7.TearDown(); },
                "FishWaterfall: Initial Spawn Sets Active And Not Deflected");
            RunTest(() => { t7.SetUp(); t7.FishHazard_GentleNeedleHit_DeflectsSidewaysWithoutHarm(); t7.TearDown(); },
                "FishWaterfall: Gentle Needle Hit Deflects Sideways Without Harm");
            RunTest(() => { t7.SetUp(); t7.FishHazard_Despawn_DeactivatesGameObject(); t7.TearDown(); },
                "FishWaterfall: Despawn Deactivates GameObject And Recycles To Pool");
            RunTest(() => { t7.SetUp(); t7.FishHazard_Knockback_PushesPlayerToTheLeft(); t7.TearDown(); },
                "FishWaterfall: Knockback Strictly Pushes Player To The Left (-X)");
            RunTest(() => { t7.SetUp(); t7.FishHazard_SpeedRemainsConstant_AfterNeedleHitAndRespawn(); t7.TearDown(); },
                "FishWaterfall: Fall Speed Remains Constant Across Spawn & Respawn Cycles");
            RunTest(() => { t7.SetUp(); t7.FishHazard_Spawn_ResetsScaleToOne_AndInitialFrameToZero(); t7.TearDown(); },
                "FishWaterfall: Spawn Resets Scale To One And Initial Frame To Zero");
            RunTest(() => { t7.SetUp(); t7.FishHazard_AfterShieldCollectAndDespawn_RestoresPoolParent(); t7.TearDown(); },
                "FishWaterfall: After Shield Collect And Despawn Restores Pool Parent");
            RunTest(() => { t7.SetUp(); t7.FishHazard_NumericalSpriteFrames_AreInAscendingSequence(); t7.TearDown(); },
                "FishWaterfall: Numerical Sprite Frames Are In Ascending Sequence");

            // 8. Sector06GrandClimaxTests
            var t8 = new Sector06GrandClimaxTests();
            RunTest(() => { t8.SetUp(); t8.FishShieldCollector_BlocksFish_AndParentsToShield(); t8.TearDown(); },
                "Sector06: Fish Shield Blocks Fish And Parents To Roof");
            RunTest(() => { t8.SetUp(); t8.FishShieldCollector_ClampsMaxAccumulatedDebris_ToPreventMemoryLeak(); t8.TearDown(); },
                "Sector06: Fish Shield Clamps Max Visual Debris (Zero Memory Leak)");
            RunTest(() => { t8.SetUp(); t8.CrateDropBridge_GeometryMatchesWalkwayLevel(); t8.TearDown(); },
                "Sector06: Crate Drop Bridge Geometry Matches Walkway Level (Flush Bridge)");
            RunTest(() => { t8.SetUp(); t8.LevelExitDoor_LocksInput_AndTriggersTransition(); t8.TearDown(); },
                "Sector06: Level Exit Door Locks Input And Triggers Ending Transition");
            RunTest(() => { t8.SetUp(); t8.EndingCutscene_ContainsAll18Sprites_InCorrectOrder(); t8.TearDown(); },
                "Sector06: Ending Cutscene Contains All 18 Sprites In Correct Numerical Order");
            RunTest(() => { t8.SetUp(); t8.FallingFish_AirborneHit_AppliesDirectionalKnockback_OppositePlayerFacing(); t8.TearDown(); },
                "Sector06: Falling Fish Applies Directional Knockback Opposite Facing");
            RunTest(() => { t8.SetUp(); t8.FallingFish_ConsecutiveHits_RespectsKnockbackCooldownWindow(); t8.TearDown(); },
                "Sector06: Consecutive Hits Respect Knockback Cooldown Window");
            RunTest(() => { t8.SetUp(); t8.FallingFish_GroundLanding_DisablesCollider_AndEntersBouncingFloppingState(); t8.TearDown(); },
                "Sector06: Ground Landing Disables Collider & Enters Bouncing Flopping State");
            RunTest(() => { t8.SetUp(); t8.FishSpawner_ContinuousRain_SpawnsAcrossFullSectorRange_UpToGate(); t8.TearDown(); },
                "Sector06: Fish Spawner Continuous Rain Covers Sector Up To Gate");

            Debug.Log($"=== Core Stitch & Loop Unit Tests Completed: {passed} Passed, {failed} Failed ===");
            CleanupLeftoverTestObjects();
        }

        public static void CleanupLeftoverTestObjects()
        {
            var allGos = Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include);
            foreach (var go in allGos)
            {
                if (go != null && (go.name.StartsWith("TestNavBox") || go.name.StartsWith("TestPlayer") || go.name.StartsWith("TestVerticalWall") || go.name.StartsWith("TestFloor") || go.name.StartsWith("TestStitchableCrate") || go.name.StartsWith("TestCorpseGasVent") || go.name.StartsWith("TestCrate") || go.name.StartsWith("TestHangingObject") || go.name.StartsWith("TestLevelSeamlessManager") || go.name.StartsWith("TestSector_") || go.name.StartsWith("TestShieldCrate") || go.name.StartsWith("TestFish") || go.name.StartsWith("TestExitDoor")))
                {
                    Object.DestroyImmediate(go);
                }
            }
        }
    }
}
