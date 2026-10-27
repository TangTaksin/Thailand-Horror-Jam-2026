using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering.Universal;
using TMPro;
using ThailandHorrorJam.Core;
using ThailandHorrorJam.Systems.Gameplay;
using ThailandHorrorJam.Systems.Needle;

namespace ThailandHorrorJam.Editor
{
    public static class LevelTemplateCreator
    {
        private const string k_PrefabsOutputDir = "Assets/_Project/Prefabs/LevelDesign";
        private const string k_SceneOutputPath = "Assets/_Project/Scenes/LevelTemplate.unity";

        // Asset paths
        private const string k_SquareSpriteGuid = "311925a002f4447b3a28927169b83ea6";
        private const string k_LogoSpriteGuid = "0be16a128256941419aba22d3a94a9a9";
        private const string k_NeedleSpritePath = "Assets/_Project/Art/Sprites/Needle_Sheet.png";
        private const string k_PhysicsMaterialPath = "Assets/_Project/Art/Materials/Physic Materials/OnFriction.physicsMaterial2D";
        private const string k_FontAssetPath = "Assets/_Project/Art/Fonts/monogram SDF.asset";

        private const string k_PlayerPrefabPath = "Assets/_Project/Prefabs/Player.prefab";
        private const string k_CheckpointManagerPrefabPath = "Assets/_Project/Prefabs/Gameplay/CheckpointManager.prefab";
        private const string k_CheckpointPrefabPath = "Assets/_Project/Prefabs/Gameplay/Checkpoint.prefab";
        private const string k_KillZonePrefabPath = "Assets/_Project/Prefabs/Gameplay/KillZone.prefab";
        private const string k_InteractionPromptCanvasPrefabPath = "Assets/_Project/Prefabs/UI/InteractionPromptCanvas.prefab";
        private const string k_SutureThreadPrefabPath = "Assets/_Project/Prefabs/SutureThread.prefab";
        private const string k_NeedleAnchorPrefabPath = "Assets/_Project/Prefabs/NeedleAnchor.prefab";
        private const string k_StitchableCratePrefabPath = "Assets/_Project/Prefabs/StitchableCrate.prefab";

        [MenuItem("Tools/Horror Jam/Generate Graybox Prefab Library", false, 10)]
        public static void GenerateGrayboxPrefabs()
        {
            if (!AssetDatabase.IsValidFolder(k_PrefabsOutputDir))
            {
                Directory.CreateDirectory(k_PrefabsOutputDir);
                AssetDatabase.Refresh();
            }

            Sprite squareSprite = AssetDatabase.LoadAssetAtPath<Sprite>(AssetDatabase.GUIDToAssetPath(k_SquareSpriteGuid));
            PhysicsMaterial2D onFrictionMat = AssetDatabase.LoadAssetAtPath<PhysicsMaterial2D>(k_PhysicsMaterialPath);
            int groundLayer = LayerMask.NameToLayer("Ground");
            if (groundLayer < 0) groundLayer = 0;

            Color floorColor = new Color(0.24f, 0.26f, 0.28f, 1f);
            Color wallColor = new Color(0.18f, 0.20f, 0.22f, 1f);

            CreateGrayboxPrefab("Ground_Block_1x1", 1f, 1f, floorColor, squareSprite, onFrictionMat, groundLayer);
            CreateGrayboxPrefab("Ground_Platform_4x1", 4f, 1f, floorColor, squareSprite, onFrictionMat, groundLayer);
            CreateGrayboxPrefab("Ground_Platform_8x1", 8f, 1f, floorColor, squareSprite, onFrictionMat, groundLayer);
            CreateGrayboxPrefab("Ground_Platform_16x2", 16f, 2f, floorColor, squareSprite, onFrictionMat, groundLayer);
            CreateGrayboxPrefab("Wall_1x4", 1f, 4f, wallColor, squareSprite, onFrictionMat, groundLayer);
            CreateGrayboxPrefab("Wall_1x8", 1f, 8f, wallColor, squareSprite, onFrictionMat, groundLayer);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[LevelTemplateCreator] Successfully created Graybox Prefabs in: {k_PrefabsOutputDir}");
        }

        private static void CreateGrayboxPrefab(string name, float width, float height, Color color, Sprite sprite, PhysicsMaterial2D mat, int layer)
        {
            string prefabPath = $"{k_PrefabsOutputDir}/{name}.prefab";

            GameObject go = new GameObject(name);
            go.layer = layer;
            go.transform.position = Vector3.zero;
            go.transform.localScale = Vector3.one;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.drawMode = SpriteDrawMode.Tiled;
            sr.tileMode = SpriteTileMode.Continuous;
            sr.size = new Vector2(width, height);
            sr.color = color;
            sr.sortingLayerName = "Default";
            sr.sortingOrder = 0;

            var col = go.AddComponent<BoxCollider2D>();
            col.size = new Vector2(width, height);
            col.offset = Vector2.zero;
            col.sharedMaterial = mat;

            PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
            Object.DestroyImmediate(go);
        }

        [MenuItem("Tools/Horror Jam/Create Level Template Scene", false, 11)]
        public static void CreateLevelTemplateScene()
        {
            GenerateGrayboxPrefabs();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            Sprite squareSprite = AssetDatabase.LoadAssetAtPath<Sprite>(AssetDatabase.GUIDToAssetPath(k_SquareSpriteGuid));
            Sprite logoSprite = AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(k_LogoSpriteGuid)).OfType<Sprite>().FirstOrDefault();
            Sprite needleSprite = AssetDatabase.LoadAllAssetsAtPath(k_NeedleSpritePath).OfType<Sprite>().FirstOrDefault(s => s.name == "Needle_Sheet_0");
            PhysicsMaterial2D onFrictionMat = AssetDatabase.LoadAssetAtPath<PhysicsMaterial2D>(k_PhysicsMaterialPath);
            TMP_FontAsset fontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(k_FontAssetPath);
            int groundLayer = LayerMask.NameToLayer("Ground");
            if (groundLayer < 0) groundLayer = 0;

            Color floorColor = new Color(0.24f, 0.26f, 0.28f, 1f);
            Color wallColor = new Color(0.18f, 0.20f, 0.22f, 1f);

            // 1. --- SYSTEMS ROOT -----------------------------------------
            GameObject systemsRoot = new GameObject("--- SYSTEMS -----------------------------------");
            systemsRoot.transform.position = Vector3.zero;

            GameObject cpMgrPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(k_CheckpointManagerPrefabPath);
            if (cpMgrPrefab != null)
            {
                var cpMgr = (GameObject)PrefabUtility.InstantiatePrefab(cpMgrPrefab, scene);
                cpMgr.transform.SetParent(systemsRoot.transform, false);
            }

            GameObject promptCanvasPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(k_InteractionPromptCanvasPrefabPath);
            if (promptCanvasPrefab != null)
            {
                var promptCanvas = (GameObject)PrefabUtility.InstantiatePrefab(promptCanvasPrefab, scene);
                promptCanvas.transform.SetParent(systemsRoot.transform, false);
            }

            GameObject eventSystemGo = new GameObject("EventSystem");
            eventSystemGo.transform.SetParent(systemsRoot.transform, false);
            eventSystemGo.AddComponent<UnityEngine.EventSystems.EventSystem>();
            eventSystemGo.AddComponent<InputSystemUIInputModule>();

            GameObject levelMgrGo = new GameObject("LevelSeamlessManager");
            levelMgrGo.transform.SetParent(systemsRoot.transform, false);
            var seamlessMgr = levelMgrGo.AddComponent<LevelSeamlessManager>();

            GameObject debugToolsGo = new GameObject("DevDebugTools");
            debugToolsGo.transform.SetParent(systemsRoot.transform, false);
            debugToolsGo.AddComponent<ThailandHorrorJam.Utilities.DevDebugTools>();

            // 2. --- CAMERAS & LIGHTING ------------------------------------
            GameObject camLightRoot = new GameObject("--- CAMERAS & LIGHTING -------------------------");

            GameObject camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(camLightRoot.transform, false);
            camGo.transform.position = new Vector3(-12f, 1.7f, -10f);

            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 5.5f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.11f, 0.11f, 0.13f, 1f);
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 1000f;

            camGo.AddComponent<AudioListener>();
            camGo.AddComponent<UniversalAdditionalCameraData>();
            var camCtrl = camGo.AddComponent<CameraController2D>();

            // Global 2D Light
            GameObject lightGo = new GameObject("Global Light 2D");
            lightGo.transform.SetParent(camLightRoot.transform, false);
            var light2D = lightGo.AddComponent<Light2D>();
            light2D.lightType = Light2D.LightType.Global;
            light2D.color = new Color(0.95f, 0.95f, 0.95f, 1f);
            light2D.intensity = 0.85f;

            // 3. --- CHARACTERS --------------------------------------------
            GameObject charRoot = new GameObject("--- CHARACTERS --------------------------------");
            GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(k_PlayerPrefabPath);
            GameObject playerInstance = null;
            if (playerPrefab != null)
            {
                playerInstance = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab, scene);
                playerInstance.transform.SetParent(charRoot.transform, false);
                playerInstance.transform.position = new Vector3(-12f, 0.5f, 0f);

                // Disable NeedleController initially (must be unlocked at Altar in Sector_02)
                var needleCtrl = playerInstance.GetComponent<NeedleController>();
                if (needleCtrl != null)
                {
                    needleCtrl.enabled = false;
                }
            }

            // Hook up Camera to Player
            if (playerInstance != null)
            {
                var camSerialized = new SerializedObject(camCtrl);
                var targetProp = camSerialized.FindProperty("_target");
                if (targetProp != null)
                {
                    targetProp.objectReferenceValue = playerInstance.transform;
                    camSerialized.ApplyModifiedProperties();
                }
            }

            // 4. --- ENVIRONMENT ------------------------------------------
            GameObject envRoot = new GameObject("--- ENVIRONMENT ------------------------------");

            // =============================================================
            // SECTOR 1: TUTORIAL (Movement A/D, Slope, 1x1 Hop, Hold Jump)
            // Calibrated for Player Prefab: moveSpeed = 3.5, jumpForce = 6.0
            // Tap Jump Distance = ~2.0m | Hold Jump Distance = ~3.5m
            // =============================================================
            GameObject sector1Root = new GameObject("Sector_01 (Intro)");
            sector1Root.transform.SetParent(envRoot.transform, false);

            // ZONE 1: Basic Movement (A / D)
            // Start platform: X from -14 to -6 (top surface at Y = 0)
            CreatePlatform(sector1Root.transform, "Wall_Left_Boundary", new Vector3(-14.5f, 3.5f, 0f), new Vector2(1f, 8f), wallColor, squareSprite, onFrictionMat, groundLayer);
            CreatePlatform(sector1Root.transform, "Floor_Movement_Start", new Vector3(-10f, -1f, 0f), new Vector2(8f, 2f), floorColor, squareSprite, onFrictionMat, groundLayer);
            CreateWorldHint(sector1Root.transform, "Hint_Movement", new Vector3(-10f, 1.8f, 0f), "[A]   [D]\nMove", fontAsset);

            // Checkpoint 1 (Start)
            GameObject cpPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(k_CheckpointPrefabPath);
            Checkpoint cp1 = null;
            if (cpPrefab != null)
            {
                var cpGo = (GameObject)PrefabUtility.InstantiatePrefab(cpPrefab, scene);
                cpGo.name = "Checkpoint_Sector1_Start";
                cpGo.transform.SetParent(sector1Root.transform, false);
                cpGo.transform.position = new Vector3(-12f, 0.5f, 0f);
                cp1 = cpGo.GetComponent<Checkpoint>();
            }

            // ZONE 2: Slope (ทางลาดขึ้น)
            // Slope from (-6, 0) up to (-1.5, 2.0) (Angle: 23.96°, Length: ~4.92)
            CreateSlope(sector1Root.transform, "Slope_Ramp", new Vector2(-6f, 0f), new Vector2(-1.5f, 2f), 1f, floorColor, squareSprite, onFrictionMat, groundLayer);
            CreatePlatform(sector1Root.transform, "Slope_Support", new Vector3(-3.75f, 0.5f, 0f), new Vector2(4.5f, 1f), wallColor, squareSprite, onFrictionMat, groundLayer);
            CreateWorldHint(sector1Root.transform, "Hint_Slope", new Vector3(-3.75f, 2.3f, 0f), "Walk up Slope", fontAsset);

            // ZONE 3: Mid Platform & Ground 1x1 Stepping Stone (Short Jump)
            // Mid ledge: X from -1.5 to 1.0 (top at Y = 2.0)
            CreatePlatform(sector1Root.transform, "Floor_MidLedge", new Vector3(-0.25f, 1f, 0f), new Vector2(2.5f, 2f), floorColor, squareSprite, onFrictionMat, groundLayer);
            
            // Ground 1x1 block at X = 2.5 (top at Y = 2.0, gap = 1.0 unit on each side)
            CreatePlatform(sector1Root.transform, "Ground_Block_1x1", new Vector3(2.5f, 1.5f, 0f), new Vector2(1f, 1f), floorColor, squareSprite, onFrictionMat, groundLayer);
            CreateWorldHint(sector1Root.transform, "Hint_Jump", new Vector3(2.5f, 3.2f, 0f), "[Space]\nTap Jump", fontAsset);

            // ZONE 4: Launch Runway & The Large Gap (Hold Jump required)
            // Launch runway: X from 4.0 to 8.0 (length 4.0, top at Y = 2.0)
            CreatePlatform(sector1Root.transform, "Floor_LaunchRunway", new Vector3(6f, 1f, 0f), new Vector2(4f, 2f), floorColor, squareSprite, onFrictionMat, groundLayer);
            CreateWorldHint(sector1Root.transform, "Hint_HoldJump", new Vector3(6.5f, 3.6f, 0f), "[Hold Space]\nLong Jump", fontAsset);

            // CALIBRATED GAP: from X = 8.0 to X = 10.8 (Width = 2.8 units)
            // Tap jump reach = 2.03m (fails and drops into pit)
            // Hold jump reach = 3.50m (comfortably clears 2.8m and lands safely)
            CreatePlatform(sector1Root.transform, "Floor_Landing", new Vector3(13.9f, 1f, 0f), new Vector2(6.2f, 2f), floorColor, squareSprite, onFrictionMat, groundLayer);

            // ZONE 5: Safety Pit (No Death! / ไม่มีอุปสรรคให้ตาย)
            // Pit floor at Y = -0.5 (surface at Y = 0.0, only 2 units below launch runway)
            CreatePlatform(sector1Root.transform, "Pit_SafetyFloor", new Vector3(9.4f, -0.5f, 0f), new Vector2(4f, 1f), floorColor, squareSprite, onFrictionMat, groundLayer);
            CreatePlatform(sector1Root.transform, "Pit_RightWall", new Vector3(10.8f, 1.25f, 0f), new Vector2(0.5f, 2.5f), wallColor, squareSprite, onFrictionMat, groundLayer);

            // Return Step: 1x1 block at X = 8.8, Y = 0.5 (top at Y = 1.0)
            CreatePlatform(sector1Root.transform, "Pit_ReturnStep_1x1", new Vector3(8.8f, 0.5f, 0f), new Vector2(1f, 1f), floorColor, squareSprite, onFrictionMat, groundLayer);
            CreateWorldHint(sector1Root.transform, "Hint_Retry", new Vector3(9.5f, 2.2f, 0f), "Fell down?\nJump up <--", fontAsset);

            // SECTOR BOUNDARY TRIGGER (Bidirectional)
            GameObject triggerGo = new GameObject("Trigger_Sector1_to_Sector2");
            triggerGo.transform.SetParent(envRoot.transform, false);
            triggerGo.transform.position = new Vector3(17f, 3.5f, 0f);

            var triggerCol = triggerGo.AddComponent<BoxCollider2D>();
            triggerCol.isTrigger = true;
            triggerCol.size = new Vector2(2f, 6f);

            // =============================================================
            // SECTOR 2: COURTYARD / DEAD FISH ALTAR (NEEDLE ACQUISITION)
            // =============================================================
            GameObject sector2Root = new GameObject("Sector_02 (Courtyard)");
            sector2Root.transform.SetParent(envRoot.transform, false);

            // Floor from X = 18 to X = 36 (length 18, center at 27, top at Y = 2.0)
            CreatePlatform(sector2Root.transform, "Floor_Sector2", new Vector3(27f, 1f, 0f), new Vector2(18f, 2f), floorColor, squareSprite, onFrictionMat, groundLayer);
            CreatePlatform(sector2Root.transform, "Wall_Right_Boundary", new Vector3(36.5f, 6f, 0f), new Vector2(1f, 10f), wallColor, squareSprite, onFrictionMat, groundLayer);

            // Checkpoint 2 (At Altar)
            Checkpoint cp2 = null;
            if (cpPrefab != null)
            {
                var cpGo2 = (GameObject)PrefabUtility.InstantiatePrefab(cpPrefab, scene);
                cpGo2.name = "Checkpoint_Sector2";
                cpGo2.transform.SetParent(sector2Root.transform, false);
                cpGo2.transform.position = new Vector3(22.5f, 2.5f, 0f);
                cp2 = cpGo2.GetComponent<Checkpoint>();
            }

            // --- DEAD FISH PILE ALTAR ---
            GameObject altarRoot = new GameObject("DeadFishAltar");
            altarRoot.transform.SetParent(sector2Root.transform, false);
            altarRoot.transform.position = new Vector3(24.5f, 2.0f, 0f);

            // 1. Organic Mound
            GameObject fishMound = new GameObject("FishMound");
            fishMound.transform.SetParent(altarRoot.transform, false);
            CreateSpriteRendererObject(fishMound.transform, "Mound_Base", new Vector3(0f, 0.35f, 0f), new Vector2(2.4f, 0.7f), new Color(0.20f, 0.16f, 0.19f, 1f), squareSprite);
            CreateSpriteRendererObject(fishMound.transform, "Mound_Mid", new Vector3(0.15f, 0.65f, 0f), new Vector2(1.6f, 0.5f), new Color(0.28f, 0.16f, 0.20f, 1f), squareSprite);
            CreateSpriteRendererObject(fishMound.transform, "Mound_Gore", new Vector3(-0.25f, 0.45f, 0f), new Vector2(0.9f, 0.35f), new Color(0.40f, 0.11f, 0.14f, 1f), squareSprite);

            // 2. Needle sticking into the pile
            GameObject needleInPile = new GameObject("Needle_In_Pile");
            needleInPile.transform.SetParent(altarRoot.transform, false);
            needleInPile.transform.localPosition = new Vector3(0.08f, 0.95f, 0f);
            needleInPile.transform.localRotation = Quaternion.Euler(0f, 0f, -28f);
            needleInPile.transform.localScale = new Vector3(1.3f, 1.3f, 1f);
            var needleSr = needleInPile.AddComponent<SpriteRenderer>();
            needleSr.sprite = needleSprite;
            needleSr.sortingLayerName = "Default";
            needleSr.sortingOrder = 3;

            // 3. Eerie Point Light 2D spotlighting the needle
            GameObject light2DGo = new GameObject("Altar_SpotLight");
            light2DGo.transform.SetParent(altarRoot.transform, false);
            light2DGo.transform.localPosition = new Vector3(0.1f, 1.15f, 0f);
            var pointLight = light2DGo.AddComponent<Light2D>();
            pointLight.lightType = Light2D.LightType.Point;
            pointLight.color = new Color(0.7f, 0.9f, 1f, 1f);
            pointLight.intensity = 1.3f;
            pointLight.pointLightInnerRadius = 0.25f;
            pointLight.pointLightOuterRadius = 2.4f;

            // 4. InteractionPromptCanvas as child of DeadFishAltar
            GameObject promptCanvasGo = null;
            if (promptCanvasPrefab != null)
            {
                promptCanvasGo = (GameObject)PrefabUtility.InstantiatePrefab(promptCanvasPrefab, scene);
                promptCanvasGo.name = "InteractionPromptCanvas";
                promptCanvasGo.transform.SetParent(altarRoot.transform, false);
            }

            // 5. Game Logo Title Card (using logo_game_needle_0)
            GameObject logoGo = new GameObject("GameLogo_TitleCard");
            logoGo.transform.SetParent(altarRoot.transform, false);
            logoGo.transform.localPosition = new Vector3(0f, 4.2f, 0f);
            logoGo.transform.localScale = new Vector3(0.85f, 0.85f, 1f);
            var logoSr = logoGo.AddComponent<SpriteRenderer>();
            logoSr.sprite = logoSprite;
            logoSr.color = new Color(1f, 1f, 1f, 0f); // Hidden initially
            logoSr.sortingLayerName = "Default";
            logoSr.sortingOrder = 20;

            // 6. NeedlePickupAltar Component
            var altarComp = altarRoot.AddComponent<NeedlePickupAltar>();
            var altarSerialized = new SerializedObject(altarComp);
            altarSerialized.FindProperty("_interactionRadius").floatValue = 2.4f;
            altarSerialized.FindProperty("_promptCanvasGo").objectReferenceValue = promptCanvasGo;
            altarSerialized.FindProperty("_promptAboveOffset").floatValue = 0.9f;
            altarSerialized.FindProperty("_showActionText").boolValue = false;
            altarSerialized.FindProperty("_needleInPile").objectReferenceValue = needleInPile.transform;
            altarSerialized.FindProperty("_fishMound").objectReferenceValue = fishMound.transform;
            altarSerialized.FindProperty("_altarLight").objectReferenceValue = pointLight;
            altarSerialized.FindProperty("_needlePopHeight").floatValue = 2.0f;
            altarSerialized.FindProperty("_needleRiseDuration").floatValue = 0.30f;
            altarSerialized.FindProperty("_needleApexHangDuration").floatValue = 0.10f;
            altarSerialized.FindProperty("_needleFallDuration").floatValue = 0.26f;
            altarSerialized.FindProperty("_needleSpinTumbleAngle").floatValue = 360f;
            altarSerialized.FindProperty("_needleFallSortingLayer").stringValue = "Player";
            altarSerialized.FindProperty("_needleFallSortingOrder").intValue = 5;
            altarSerialized.FindProperty("_logoSpriteRenderer").objectReferenceValue = logoSr;
            altarSerialized.FindProperty("_pullZoomInOrthoSize").floatValue = 3.8f;
            altarSerialized.FindProperty("_pullZoomInDuration").floatValue = 0.55f;
            altarSerialized.FindProperty("_zoomOutOrthoSize").floatValue = 8.0f;
            altarSerialized.FindProperty("_zoomOutDuration").floatValue = 3.0f;
            altarSerialized.FindProperty("_zoomOutOffsetY").floatValue = 3.2f;
            altarSerialized.FindProperty("_logoFadeInDelay").floatValue = 0.4f;
            altarSerialized.FindProperty("_logoFadeInDuration").floatValue = 2.5f;
            altarSerialized.FindProperty("_titleLingerDuration").floatValue = 2.0f;
            altarSerialized.FindProperty("_logoFadeOutDuration").floatValue = 1.2f;

            var threadPrefab = AssetDatabase.LoadAssetAtPath<ThailandHorrorJam.Systems.Needle.SutureThread>(k_SutureThreadPrefabPath);
            altarSerialized.FindProperty("_sutureThreadPrefab").objectReferenceValue = threadPrefab;
            altarSerialized.FindProperty("_startingThreadNodes").intValue = 52;
            altarSerialized.FindProperty("_startingThreadSlack").floatValue = 1.18f;
            altarSerialized.FindProperty("_startingBendingStiffness").floatValue = 0.03f;
            altarSerialized.FindProperty("_startingGravity").floatValue = 12.0f;
            altarSerialized.FindProperty("_startingAirDamping").floatValue = 1.8f;
            altarSerialized.FindProperty("_enablePixelSnapping").boolValue = false;
            altarSerialized.FindProperty("_playerTetherHeightOffset").floatValue = -0.05f;
            altarSerialized.FindProperty("_needleEyeLocalOffset").vector3Value = Vector3.zero;
            altarSerialized.FindProperty("_showNeedlePinDot").boolValue = true;

            altarSerialized.FindProperty("_altarCheckpoint").objectReferenceValue = cp2;
            altarSerialized.ApplyModifiedProperties();

            // First Puzzle (Stitchable Crate & Anchor) immediately after Altar
            GameObject cratePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(k_StitchableCratePrefabPath);
            GameObject anchorPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(k_NeedleAnchorPrefabPath);
            if (cratePrefab != null)
            {
                var crateGo = (GameObject)PrefabUtility.InstantiatePrefab(cratePrefab, scene);
                crateGo.name = "StitchableCrate_Demo";
                crateGo.transform.SetParent(sector2Root.transform, false);
                crateGo.transform.position = new Vector3(29.5f, 2.5f, 0f);
            }

            if (anchorPrefab != null)
            {
                var anchorGo = (GameObject)PrefabUtility.InstantiatePrefab(anchorPrefab, scene);
                anchorGo.name = "Anchor_Demo";
                anchorGo.transform.SetParent(sector2Root.transform, false);
                anchorGo.transform.position = new Vector3(32.5f, 6.0f, 0f);
            }

            // Hook up Trigger Sector 1 -> Sector 2
            var sbt = triggerGo.AddComponent<SectorBoundaryTrigger>();
            var sbtSerialized = new SerializedObject(sbt);
            sbtSerialized.FindProperty("_isBidirectional").boolValue = true;
            sbtSerialized.FindProperty("_sectorA").objectReferenceValue = sector1Root;
            sbtSerialized.FindProperty("_sectorB").objectReferenceValue = sector2Root;
            sbtSerialized.FindProperty("_expectedDirection").vector2Value = Vector2.right;
            sbtSerialized.FindProperty("_targetSectorIndex").intValue = 1;
            sbtSerialized.FindProperty("_targetSectorObject").objectReferenceValue = sector2Root;
            sbtSerialized.ApplyModifiedProperties();

            // GLOBAL KILL ZONE (Far below at Y = -15)
            GameObject killZonePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(k_KillZonePrefabPath);
            if (killZonePrefab != null)
            {
                var kz = (GameObject)PrefabUtility.InstantiatePrefab(killZonePrefab, scene);
                kz.name = "KillZone_BottomAbyss";
                kz.transform.SetParent(envRoot.transform, false);
                kz.transform.position = new Vector3(10f, -15f, 0f);
                var kzCol = kz.GetComponent<BoxCollider2D>();
                if (kzCol != null)
                {
                    kzCol.size = new Vector2(100f, 3f);
                }
            }

            // 5. HOOK UP LEVEL SEAMLESS MANAGER ---------------------------
            var lmSerialized = new SerializedObject(seamlessMgr);
            if (playerInstance != null)
            {
                lmSerialized.FindProperty("_playerController").objectReferenceValue = playerInstance.GetComponent<PlayerController2D>();
                lmSerialized.FindProperty("_needleController").objectReferenceValue = playerInstance.GetComponent<NeedleController>();
            }

            var sectorsProp = lmSerialized.FindProperty("_sectors");
            sectorsProp.ClearArray();

            // Entry 0
            sectorsProp.InsertArrayElementAtIndex(0);
            var entry0 = sectorsProp.GetArrayElementAtIndex(0);
            entry0.FindPropertyRelative("name").stringValue = "Sector 1 (Tutorial)";
            entry0.FindPropertyRelative("rootObject").objectReferenceValue = sector1Root;
            entry0.FindPropertyRelative("defaultCheckpoint").objectReferenceValue = cp1;

            // Entry 1
            sectorsProp.InsertArrayElementAtIndex(1);
            var entry1 = sectorsProp.GetArrayElementAtIndex(1);
            entry1.FindPropertyRelative("name").stringValue = "Sector 2 (Courtyard)";
            entry1.FindPropertyRelative("rootObject").objectReferenceValue = sector2Root;
            entry1.FindPropertyRelative("defaultCheckpoint").objectReferenceValue = cp2;

            lmSerialized.FindProperty("_activeRadius").intValue = 1;
            lmSerialized.FindProperty("_initialSectorIndex").intValue = 0;
            lmSerialized.ApplyModifiedProperties();

            // 6. SAVE SCENE -----------------------------------------------
            EditorSceneManager.SaveScene(scene, k_SceneOutputPath);
            Debug.Log($"[LevelTemplateCreator] Successfully created Tutorial Level Template Scene at: {k_SceneOutputPath}");
        }

        private static GameObject CreatePlatform(Transform parent, string name, Vector3 pos, Vector2 size, Color color, Sprite sprite, PhysicsMaterial2D mat, int layer)
        {
            GameObject go = new GameObject(name);
            go.layer = layer;
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.localScale = Vector3.one;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.drawMode = SpriteDrawMode.Tiled;
            sr.tileMode = SpriteTileMode.Continuous;
            sr.size = size;
            sr.color = color;
            sr.sortingLayerName = "Default";
            sr.sortingOrder = 0;

            var col = go.AddComponent<BoxCollider2D>();
            col.size = size;
            col.offset = Vector2.zero;
            col.sharedMaterial = mat;

            return go;
        }

        private static GameObject CreateSpriteRendererObject(Transform parent, string name, Vector3 localPos, Vector2 size, Color color, Sprite sprite)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.drawMode = SpriteDrawMode.Tiled;
            sr.tileMode = SpriteTileMode.Continuous;
            sr.size = size;
            sr.color = color;
            sr.sortingLayerName = "Default";
            sr.sortingOrder = 1;
            return go;
        }

        private static GameObject CreateSlope(Transform parent, string name, Vector2 start, Vector2 end, float thickness, Color color, Sprite sprite, PhysicsMaterial2D mat, int layer)
        {
            Vector2 dir = (end - start).normalized;
            Vector2 normal = new Vector2(-dir.y, dir.x);
            float length = (end - start).magnitude;
            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            Vector2 center = (start + end) * 0.5f - normal * (thickness * 0.5f);

            GameObject go = new GameObject(name);
            go.layer = layer;
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(center.x, center.y, 0f);
            go.transform.rotation = Quaternion.Euler(0f, 0f, angle);
            go.transform.localScale = Vector3.one;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.drawMode = SpriteDrawMode.Tiled;
            sr.tileMode = SpriteTileMode.Continuous;
            sr.size = new Vector2(length, thickness);
            sr.color = color;
            sr.sortingLayerName = "Default";
            sr.sortingOrder = 0;

            var col = go.AddComponent<BoxCollider2D>();
            col.size = new Vector2(length, thickness);
            col.offset = Vector2.zero;
            col.sharedMaterial = mat;

            return go;
        }

        private static GameObject CreateWorldHint(Transform parent, string name, Vector3 pos, string text, TMP_FontAsset font)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = pos;

            var tmp = go.AddComponent<TextMeshPro>();
            if (font != null)
            {
                tmp.font = font;
            }
            tmp.fontSize = 4f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = new Color(0.82f, 0.82f, 0.88f, 0.65f);
            tmp.text = text;
            tmp.sortingLayerID = SortingLayer.NameToID("Default");
            tmp.sortingOrder = 5;

            return go;
        }
    }
}
