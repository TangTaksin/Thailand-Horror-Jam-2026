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

            Debug.Log($"=== Core Stitch & Loop Unit Tests Completed: {passed} Passed, {failed} Failed ===");
            CleanupLeftoverTestObjects();
        }

        public static void CleanupLeftoverTestObjects()
        {
            var allGos = Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include);
            foreach (var go in allGos)
            {
                if (go != null && (go.name.StartsWith("TestNavBox") || go.name.StartsWith("TestPlayer") || go.name.StartsWith("TestVerticalWall") || go.name.StartsWith("TestFloor") || go.name.StartsWith("TestStitchableCrate") || go.name.StartsWith("TestCorpseGasVent") || go.name.StartsWith("TestCrate") || go.name.StartsWith("TestHangingObject")))
                {
                    Object.DestroyImmediate(go);
                }
            }
        }
    }
}
