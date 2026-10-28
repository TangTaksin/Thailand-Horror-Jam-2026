using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
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
        private const string k_GameplayScenePath = "Assets/_Project/Scenes/Gameplay.unity";

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
        private const string k_HangingBridgePrefabPath = "Assets/_Project/Prefabs/HangingBridge.prefab";
        private const string k_DeadFishSpriteSheetPath = "Assets/_Project/Art/Sprites/Character/Dead_Fish_SpriteSheet.png";
        private const string k_FallingFishPrefabPath = "Assets/_Project/Prefabs/Gameplay/FallingDeadFish.prefab";
        private const string k_InputTilesheetPath = "Assets/_Project/Art/Sprites/UI/tilemap_white_packed.png";

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

        private static FallingFishHazard2D EnsureFallingFishPrefab(Sprite[] fishSprites)
        {
            var existing = AssetDatabase.LoadAssetAtPath<FallingFishHazard2D>(k_FallingFishPrefabPath);
            if (existing != null) return existing;

            GameObject go = new GameObject("FallingDeadFish");
            var sr = go.AddComponent<SpriteRenderer>();
            if (fishSprites != null && fishSprites.Length > 0)
            {
                sr.sprite = fishSprites[0];
            }
            sr.sortingLayerName = "Default";
            sr.sortingOrder = 6;

            var col = go.AddComponent<CircleCollider2D>();
            col.isTrigger = true;
            col.radius = 0.28f;

            var hazard = go.AddComponent<FallingFishHazard2D>();
            var so = new SerializedObject(hazard);
            if (fishSprites != null && fishSprites.Length > 0)
            {
                var framesProp = so.FindProperty("_animationFrames");
                framesProp.arraySize = fishSprites.Length;
                for (int i = 0; i < fishSprites.Length; i++)
                {
                    framesProp.GetArrayElementAtIndex(i).objectReferenceValue = fishSprites[i];
                }
            }
            so.FindProperty("_fallSpeed").floatValue = 4.2f;
            so.FindProperty("_deflectLateralSpeed").floatValue = 5.0f;
            so.FindProperty("_deflectUpwardSpeed").floatValue = 1.8f;
            so.FindProperty("_knockbackHorizontalForce").floatValue = 4.5f;
            so.FindProperty("_knockbackVerticalForce").floatValue = 1.8f;
            so.FindProperty("_despawnY").floatValue = -3.2f;
            so.ApplyModifiedProperties();

            string dir = Path.GetDirectoryName(k_FallingFishPrefabPath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(go, k_FallingFishPrefabPath);
            Object.DestroyImmediate(go);
            return saved.GetComponent<FallingFishHazard2D>();
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
            CreateMovementHintTrigger(sector1Root.transform, new Vector3(-10f, 1.2f, 0f), new Vector2(6f, 3.5f), fontAsset);

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
            CreateWorldHintTrigger(sector1Root.transform, "Hint_Slope", new Vector3(-4.5f, 1.5f, 0f), new Vector2(4f, 3f), new Vector3(0.75f, 0.8f, 0f), "Walk up Slope", fontAsset);

            // ZONE 3: Mid Platform & Ground 1x1 Stepping Stone (Short Jump)
            // Mid ledge: X from -1.5 to 1.0 (top at Y = 2.0)
            CreatePlatform(sector1Root.transform, "Floor_MidLedge", new Vector3(-0.25f, 1f, 0f), new Vector2(2.5f, 2f), floorColor, squareSprite, onFrictionMat, groundLayer);
            
            // Ground 1x1 block at X = 2.5 (top at Y = 2.0, gap = 1.0 unit on each side)
            CreatePlatform(sector1Root.transform, "Ground_Block_1x1", new Vector3(2.5f, 1.5f, 0f), new Vector2(1f, 1f), floorColor, squareSprite, onFrictionMat, groundLayer);
            CreateSpacebarHintTrigger(sector1Root.transform, "Hint_Jump", new Vector3(0.5f, 2.5f, 0f), new Vector2(3.5f, 3f), new Vector3(2.0f, 0.7f, 0f), "Space", "Tap Jump", fontAsset);

            // ZONE 4: Launch Runway & The Large Gap (Hold Jump required)
            // Launch runway: X from 4.0 to 8.0 (length 4.0, top at Y = 2.0)
            CreatePlatform(sector1Root.transform, "Floor_LaunchRunway", new Vector3(6f, 1f, 0f), new Vector2(4f, 2f), floorColor, squareSprite, onFrictionMat, groundLayer);
            CreateSpacebarHintTrigger(sector1Root.transform, "Hint_HoldJump", new Vector3(6f, 2.5f, 0f), new Vector2(4f, 3f), new Vector3(0.5f, 1.1f, 0f), "Hold Space", "Long Jump", fontAsset);

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
            CreateWorldHintTrigger(sector1Root.transform, "Hint_Retry", new Vector3(9.5f, 0.8f, 0f), new Vector2(3.5f, 2.5f), new Vector3(0f, 1.4f, 0f), "Fell down?\nJump up <--", fontAsset);

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

            // Floor from X = 18 to X = 54 (length 36, center at 36, top at Y = 2.0)
            CreatePlatform(sector2Root.transform, "Floor_Sector2", new Vector3(36f, 1f, 0f), new Vector2(36f, 2f), floorColor, squareSprite, onFrictionMat, groundLayer);

            // Checkpoint 2 (At Altar)
            Checkpoint cp2 = null;
            if (cpPrefab != null)
            {
                var cpGo2 = (GameObject)PrefabUtility.InstantiatePrefab(cpPrefab, scene);
                cpGo2.name = "Checkpoint_Sector2";
                cpGo2.transform.SetParent(sector2Root.transform, false);
                cpGo2.transform.position = new Vector3(40.5f, 2.5f, 0f);
                cp2 = cpGo2.GetComponent<Checkpoint>();
            }

            // --- DEAD FISH PILE ALTAR ---
            GameObject altarRoot = new GameObject("DeadFishAltar");
            altarRoot.transform.SetParent(sector2Root.transform, false);
            altarRoot.transform.position = new Vector3(44.0f, 2.0f, 0f);

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

            // =============================================================
            // SECTOR 3: NEEDLE CUTTING & HANGING DRAWBRIDGE PUZZLE
            // =============================================================
            Checkpoint cp3;
            SectorBoundaryTrigger sbt2to3;
            GameObject sector3Root = BuildSector3Hierarchy(
                scene,
                envRoot.transform,
                sector2Root,
                squareSprite,
                onFrictionMat,
                fontAsset,
                groundLayer,
                floorColor,
                wallColor,
                out cp3,
                out sbt2to3
            );

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

            // Entry 2
            sectorsProp.InsertArrayElementAtIndex(2);
            var entry2 = sectorsProp.GetArrayElementAtIndex(2);
            entry2.FindPropertyRelative("name").stringValue = "Sector 3 (Bridge & Needle Cutting)";
            entry2.FindPropertyRelative("rootObject").objectReferenceValue = sector3Root;
            entry2.FindPropertyRelative("defaultCheckpoint").objectReferenceValue = cp3;

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

        private static GameObject CreateMovementHintTrigger(
            Transform parent,
            Vector3 triggerPos,
            Vector2 triggerSize,
            TMP_FontAsset font)
        {
            GameObject triggerGo = new GameObject("Trigger_Hint_Movement");
            triggerGo.transform.SetParent(parent, false);
            triggerGo.transform.position = triggerPos;

            var col = triggerGo.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = triggerSize;

            GameObject visualGo = new GameObject("Hint_Movement");
            visualGo.transform.SetParent(triggerGo.transform, false);
            visualGo.transform.localPosition = new Vector3(0f, 0.6f, 0f);

            var sprites = AssetDatabase.LoadAllAssetsAtPath(k_InputTilesheetPath).OfType<Sprite>().ToList();
            var spriteA = sprites.FirstOrDefault(s => s.name == "tilemap_white_packed_95");
            var spriteD = sprites.FirstOrDefault(s => s.name == "tilemap_white_packed_97");
            var baseColor = new Color(0.82f, 0.82f, 0.88f, 0.75f);

            if (spriteA != null)
            {
                GameObject iconA = new GameObject("Icon_A");
                iconA.transform.SetParent(visualGo.transform, false);
                iconA.transform.localPosition = new Vector3(-0.32f, 0.32f, 0f);
                var srA = iconA.AddComponent<SpriteRenderer>();
                srA.sprite = spriteA;
                srA.color = baseColor;
                srA.sortingLayerName = "Default";
                srA.sortingOrder = 5;
            }

            if (spriteD != null)
            {
                GameObject iconD = new GameObject("Icon_D");
                iconD.transform.SetParent(visualGo.transform, false);
                iconD.transform.localPosition = new Vector3(0.32f, 0.32f, 0f);
                var srD = iconD.AddComponent<SpriteRenderer>();
                srD.sprite = spriteD;
                srD.color = baseColor;
                srD.sortingLayerName = "Default";
                srD.sortingOrder = 5;
            }

            GameObject textGo = new GameObject("Text_Move");
            textGo.transform.SetParent(visualGo.transform, false);
            textGo.transform.localPosition = new Vector3(0f, -0.22f, 0f);

            var tmp = textGo.AddComponent<TextMeshPro>();
            if (font != null) tmp.font = font;
            tmp.fontSize = 4f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = baseColor;
            tmp.text = "Move";
            tmp.sortingLayerID = SortingLayer.NameToID("Default");
            tmp.sortingOrder = 5;

            var hintTrigger = triggerGo.AddComponent<WorldHintTrigger2D>();
            var triggerSerialized = new SerializedObject(hintTrigger);
            triggerSerialized.FindProperty("_hintRoot").objectReferenceValue = visualGo;
            triggerSerialized.FindProperty("_prerequisite").intValue = (int)HintPrerequisite.None;
            triggerSerialized.FindProperty("_dismissMode").intValue = (int)HintDismissMode.RepeatableOnProximity;
            triggerSerialized.FindProperty("_fadeDuration").floatValue = 0.35f;
            triggerSerialized.ApplyModifiedProperties();

            return triggerGo;
        }

        private static GameObject CreateSpacebarHintTrigger(
            Transform parent,
            string name,
            Vector3 triggerPos,
            Vector2 triggerSize,
            Vector3 visualOffset,
            string keyText,
            string actionText,
            TMP_FontAsset font)
        {
            GameObject triggerGo = new GameObject($"Trigger_{name}");
            triggerGo.transform.SetParent(parent, false);
            triggerGo.transform.position = triggerPos;

            var col = triggerGo.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = triggerSize;

            GameObject visualGo = new GameObject(name);
            visualGo.transform.SetParent(triggerGo.transform, false);
            visualGo.transform.localPosition = visualOffset;

            var sprites = AssetDatabase.LoadAllAssetsAtPath(k_InputTilesheetPath).OfType<Sprite>().ToList();
            var spriteSpace = sprites.FirstOrDefault(s => s.name == "tilemap_white_packed_376");
            var baseColor = new Color(0.82f, 0.82f, 0.88f, 0.75f);

            if (spriteSpace != null)
            {
                GameObject iconGo = new GameObject("Icon_Spacebar");
                iconGo.transform.SetParent(visualGo.transform, false);
                iconGo.transform.localPosition = new Vector3(0f, 0.18f, 0f);
                var sr = iconGo.AddComponent<SpriteRenderer>();
                sr.sprite = spriteSpace;
                sr.color = baseColor;
                sr.sortingLayerName = "Default";
                sr.sortingOrder = 5;

                GameObject keyTextGo = new GameObject("Text_SpaceKey");
                keyTextGo.transform.SetParent(iconGo.transform, false);
                keyTextGo.transform.localPosition = new Vector3(0f, 0.02f, 0f);
                var keyTmp = keyTextGo.AddComponent<TextMeshPro>();
                if (font != null) keyTmp.font = font;
                keyTmp.fontSize = keyText.Length > 6 ? 2.9f : 3.2f;
                keyTmp.alignment = TextAlignmentOptions.Center;
                keyTmp.color = baseColor;
                keyTmp.text = keyText;
                keyTmp.sortingLayerID = SortingLayer.NameToID("Default");
                keyTmp.sortingOrder = 6;
            }

            GameObject actionTextGo = new GameObject("Text_Action");
            actionTextGo.transform.SetParent(visualGo.transform, false);
            actionTextGo.transform.localPosition = new Vector3(0f, -0.22f, 0f);

            var actionTmp = actionTextGo.AddComponent<TextMeshPro>();
            if (font != null) actionTmp.font = font;
            actionTmp.fontSize = 4f;
            actionTmp.alignment = TextAlignmentOptions.Center;
            actionTmp.color = baseColor;
            actionTmp.text = actionText;
            actionTmp.sortingLayerID = SortingLayer.NameToID("Default");
            actionTmp.sortingOrder = 5;

            var hintTrigger = triggerGo.AddComponent<WorldHintTrigger2D>();
            var triggerSerialized = new SerializedObject(hintTrigger);
            triggerSerialized.FindProperty("_hintRoot").objectReferenceValue = visualGo;
            triggerSerialized.FindProperty("_prerequisite").intValue = (int)HintPrerequisite.None;
            triggerSerialized.FindProperty("_dismissMode").intValue = (int)HintDismissMode.RepeatableOnProximity;
            triggerSerialized.FindProperty("_fadeDuration").floatValue = 0.35f;
            triggerSerialized.ApplyModifiedProperties();

            return triggerGo;
        }

        private static GameObject CreateWorldHintTrigger(
            Transform parent,
            string name,
            Vector3 triggerPos,
            Vector2 triggerSize,
            Vector3 visualOffset,
            string text,
            TMP_FontAsset font,
            HintPrerequisite prerequisite = HintPrerequisite.None,
            HintDismissMode dismissMode = HintDismissMode.RepeatableOnProximity,
            MonoBehaviour dismissConditionTarget = null)
        {
            GameObject triggerGo = new GameObject($"Trigger_{name}");
            triggerGo.transform.SetParent(parent, false);
            triggerGo.transform.position = triggerPos;

            var col = triggerGo.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = triggerSize;

            GameObject visualGo = new GameObject(name);
            visualGo.transform.SetParent(triggerGo.transform, false);
            visualGo.transform.localPosition = visualOffset;

            var tmp = visualGo.AddComponent<TextMeshPro>();
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

            var hintTrigger = triggerGo.AddComponent<WorldHintTrigger2D>();
            var triggerSerialized = new SerializedObject(hintTrigger);
            triggerSerialized.FindProperty("_hintRoot").objectReferenceValue = visualGo;
            triggerSerialized.FindProperty("_prerequisite").intValue = (int)prerequisite;
            triggerSerialized.FindProperty("_dismissMode").intValue = (int)dismissMode;
            triggerSerialized.FindProperty("_fadeDuration").floatValue = 0.35f;
            if (dismissConditionTarget != null)
            {
                triggerSerialized.FindProperty("_dismissConditionTarget").objectReferenceValue = dismissConditionTarget;
            }
            triggerSerialized.ApplyModifiedProperties();

            return triggerGo;
        }

        [MenuItem("GameObject/2D Object/Thailand Horror Jam/World Hint Trigger", false, 10)]
        public static void CreateWorldHintTriggerMenuItem(MenuCommand menuCommand)
        {
            GameObject go = new GameObject("Trigger_WorldHint");
            GameObjectUtility.SetParentAndAlign(go, menuCommand.context as GameObject);

            var col = go.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = new Vector2(3f, 3f);

            var hintTrigger = go.AddComponent<WorldHintTrigger2D>();

            GameObject visualGo = new GameObject("Visual_Hint");
            visualGo.transform.SetParent(go.transform, false);
            visualGo.transform.localPosition = new Vector3(0f, 1.2f, 0f);

            var tmp = visualGo.AddComponent<TextMeshPro>();
            var fontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(k_FontAssetPath);
            if (fontAsset != null)
            {
                tmp.font = fontAsset;
            }
            tmp.fontSize = 4f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.text = "Hint Text";
            tmp.color = new Color(0.82f, 0.82f, 0.88f, 0.75f);
            tmp.sortingLayerID = SortingLayer.NameToID("Default");
            tmp.sortingOrder = 5;

            var triggerSerialized = new SerializedObject(hintTrigger);
            triggerSerialized.FindProperty("_hintRoot").objectReferenceValue = visualGo;
            triggerSerialized.FindProperty("_prerequisite").intValue = (int)HintPrerequisite.None;
            triggerSerialized.FindProperty("_dismissMode").intValue = (int)HintDismissMode.RepeatableOnProximity;
            triggerSerialized.FindProperty("_fadeDuration").floatValue = 0.35f;
            triggerSerialized.ApplyModifiedProperties();

            Undo.RegisterCreatedObjectUndo(go, "Create World Hint Trigger");
            Selection.activeObject = go;
        }

        public static GameObject BuildSector3Hierarchy(
            Scene scene,
            Transform envRoot,
            GameObject sector2Root,
            Sprite squareSprite,
            PhysicsMaterial2D onFrictionMat,
            TMP_FontAsset fontAsset,
            int groundLayer,
            Color floorColor,
            Color wallColor,
            out Checkpoint cp3,
            out SectorBoundaryTrigger boundaryTrigger)
        {
            // SECTOR BOUNDARY TRIGGER 2 -> 3 (Bidirectional)
            GameObject trigger2to3Go = new GameObject("Trigger_Sector2_to_Sector3");
            trigger2to3Go.transform.SetParent(envRoot, false);
            trigger2to3Go.transform.position = new Vector3(54f, 3.5f, 0f);

            var trigger2to3Col = trigger2to3Go.AddComponent<BoxCollider2D>();
            trigger2to3Col.isTrigger = true;
            trigger2to3Col.size = new Vector2(2f, 6f);

            GameObject sector3Root = new GameObject("Sector_03 (Bridge & Needle Cutting)");
            sector3Root.transform.SetParent(envRoot, false);

            // ZONE 1: Approach Runway (X: 54 ~ 60, surface at Y = 2.0)
            CreatePlatform(sector3Root.transform, "Floor_Sector3_Approach", new Vector3(57f, 1f, 0f), new Vector2(6f, 2f), floorColor, squareSprite, onFrictionMat, groundLayer);

            // Checkpoint 3 (Before Bridge)
            cp3 = null;
            GameObject cpPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(k_CheckpointPrefabPath);
            if (cpPrefab != null)
            {
                var cpGo3 = (GameObject)PrefabUtility.InstantiatePrefab(cpPrefab, scene);
                cpGo3.name = "Checkpoint_Sector3";
                cpGo3.transform.SetParent(sector3Root.transform, false);
                cpGo3.transform.position = new Vector3(55.5f, 2.5f, 0f);
                cp3 = cpGo3.GetComponent<Checkpoint>();
            }

            // ZONE 2: The Chasm & Deadly Spikes (X: 60.0 ~ 64.25, width: 4.25m)
            CreatePlatform(sector3Root.transform, "Floor_Sector3_Pit", new Vector3(62.125f, -2.5f, 0f), new Vector2(5.5f, 1f), floorColor, squareSprite, onFrictionMat, groundLayer);
            CreatePlatform(sector3Root.transform, "Wall_Pit_Left", new Vector3(59.75f, -0.25f, 0f), new Vector2(0.5f, 3.5f), wallColor, squareSprite, onFrictionMat, groundLayer);
            CreatePlatform(sector3Root.transform, "Wall_Pit_Right", new Vector3(64.5f, -0.25f, 0f), new Vector2(0.5f, 3.5f), wallColor, squareSprite, onFrictionMat, groundLayer);

            // Spikes Visuals (7 decorative dark silhouette spikes)
            Color spikeColor = new Color(0.12f, 0.12f, 0.14f, 1f);
            for (int s = 0; s < 7; s++)
            {
                float spikeX = 60.5f + s * 0.55f;
                GameObject spikeGo = new GameObject($"Spike_Visual_{s}");
                spikeGo.transform.SetParent(sector3Root.transform, false);
                spikeGo.transform.position = new Vector3(spikeX, -1.95f, 0f);
                spikeGo.transform.rotation = Quaternion.Euler(0f, 0f, 45f);
                spikeGo.transform.localScale = new Vector3(0.35f, 0.35f, 1f);
                var spikeSr = spikeGo.AddComponent<SpriteRenderer>();
                spikeSr.sprite = squareSprite;
                spikeSr.color = spikeColor;
                spikeSr.sortingLayerName = "Default";
                spikeSr.sortingOrder = 2;
            }

            // Spikes Hazard KillZone
            GameObject killZonePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(k_KillZonePrefabPath);
            if (killZonePrefab != null)
            {
                var kzSector3 = (GameObject)PrefabUtility.InstantiatePrefab(killZonePrefab, scene);
                kzSector3.name = "KillZone_Sector3_Spikes";
                kzSector3.transform.SetParent(sector3Root.transform, false);
                kzSector3.transform.position = new Vector3(62.125f, -1.8f, 0f);
                var kzCol = kzSector3.GetComponent<BoxCollider2D>();
                if (kzCol != null)
                {
                    kzCol.size = new Vector2(4.25f, 0.8f);
                }
                var kzComp = kzSector3.GetComponent<KillZone>();
                if (kzComp != null)
                {
                    var kzSerialized = new SerializedObject(kzComp);
                    kzSerialized.FindProperty("_hazardType").intValue = (int)HazardType.Spikes;
                    kzSerialized.FindProperty("_cameraShakeIntensity").floatValue = 0.25f;
                    kzSerialized.ApplyModifiedProperties();
                }
            }

            // ZONE 3: Overhead Ceiling Beam (X: 58 ~ 68, Y: 6.75)
            CreatePlatform(sector3Root.transform, "Ceiling_Sector3", new Vector3(62.5f, 6.75f, 0f), new Vector2(10f, 1f), wallColor, squareSprite, onFrictionMat, groundLayer);

            // ZONE 4: Hanging Drawbridge (Hinge on Far Ledge at X = 64.25, Y = 2.0)
            HangingObject2D hangingBridge = null;
            GameObject bridgePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(k_HangingBridgePrefabPath);
            if (bridgePrefab != null)
            {
                var bridgeGo = (GameObject)PrefabUtility.InstantiatePrefab(bridgePrefab, scene);
                bridgeGo.name = "HangingBridge";
                bridgeGo.transform.SetParent(sector3Root.transform, false);
                bridgeGo.transform.position = new Vector3(64.25f, 2.0f, 0f);
                hangingBridge = bridgeGo.GetComponentInChildren<HangingObject2D>();
            }

            // Minimalist Tutorial Hint: Trigger + RMB Sprite + Aim text
            Sprite rmbSprite = AssetDatabase.LoadAllAssetsAtPath(k_InputTilesheetPath)
                .OfType<Sprite>()
                .FirstOrDefault(s => s.name == "tilemap_white_packed_64");

            GameObject hintRoot = new GameObject("Hint_NeedleAim");
            hintRoot.transform.SetParent(sector3Root.transform, false);
            hintRoot.transform.position = new Vector3(58.0f, 4.0f, 0f);

            // 1. Aim Hint: Icon Sprite (tilemap_white_packed_64 = Right Click mouse) + "Aim"
            if (rmbSprite != null)
            {
                GameObject iconGo = new GameObject("Icon_RMB");
                iconGo.transform.SetParent(hintRoot.transform, false);
                iconGo.transform.localPosition = new Vector3(-0.45f, 0f, 0f);
                iconGo.transform.localScale = new Vector3(0.75f, 0.75f, 1f);
                var iconSr = iconGo.AddComponent<SpriteRenderer>();
                iconSr.sprite = rmbSprite;
                iconSr.color = new Color(0.82f, 0.82f, 0.88f, 0.65f);
                iconSr.sortingLayerName = "Default";
                iconSr.sortingOrder = 5;
            }

            GameObject textGo = new GameObject("Text_Aim");
            textGo.transform.SetParent(hintRoot.transform, false);
            textGo.transform.localPosition = new Vector3(0.15f, 0f, 0f);
            var tmp = textGo.AddComponent<TextMeshPro>();
            if (fontAsset != null)
            {
                tmp.font = fontAsset;
            }
            tmp.fontSize = 4f;
            tmp.alignment = TextAlignmentOptions.Left;
            tmp.color = new Color(0.82f, 0.82f, 0.88f, 0.65f);
            tmp.text = "Aim";
            tmp.sortingLayerID = SortingLayer.NameToID("Default");
            tmp.sortingOrder = 5;

            // Trigger Zone before the pit
            GameObject triggerGo = new GameObject("Trigger_Hint_NeedleAim");
            triggerGo.transform.SetParent(sector3Root.transform, false);
            triggerGo.transform.position = new Vector3(58.0f, 2.75f, 0f);
            var triggerCol = triggerGo.AddComponent<BoxCollider2D>();
            triggerCol.size = new Vector2(4.0f, 2.5f);
            triggerCol.isTrigger = true;

            var hintTrigger = triggerGo.AddComponent<WorldHintTrigger2D>();
            var triggerSerialized = new SerializedObject(hintTrigger);
            triggerSerialized.FindProperty("_hintRoot").objectReferenceValue = hintRoot;
            triggerSerialized.FindProperty("_prerequisite").intValue = (int)HintPrerequisite.RequireNeedle;
            triggerSerialized.FindProperty("_dismissMode").intValue = (int)HintDismissMode.OnConditionMet;
            triggerSerialized.FindProperty("_fadeDuration").floatValue = 0.35f;
            if (hangingBridge != null)
            {
                triggerSerialized.FindProperty("_dismissConditionTarget").objectReferenceValue = hangingBridge;
            }
            triggerSerialized.ApplyModifiedProperties();

            // ZONE 5: Landing Platform (X: 64.25 ~ 76.25, surface at Y = 2.0)
            CreatePlatform(sector3Root.transform, "Floor_Sector3_Landing", new Vector3(70.25f, 1f, 0f), new Vector2(12f, 2f), floorColor, squareSprite, onFrictionMat, groundLayer);
            CreatePlatform(sector3Root.transform, "Wall_Sector3_End", new Vector3(76.5f, 6f, 0f), new Vector2(1f, 10f), wallColor, squareSprite, onFrictionMat, groundLayer);

            // Hook up Trigger Sector 2 -> Sector 3
            boundaryTrigger = trigger2to3Go.AddComponent<SectorBoundaryTrigger>();
            var sbt2to3Serialized = new SerializedObject(boundaryTrigger);
            sbt2to3Serialized.FindProperty("_isBidirectional").boolValue = true;
            sbt2to3Serialized.FindProperty("_sectorA").objectReferenceValue = sector2Root;
            sbt2to3Serialized.FindProperty("_sectorB").objectReferenceValue = sector3Root;
            sbt2to3Serialized.FindProperty("_expectedDirection").vector2Value = Vector2.right;
            sbt2to3Serialized.FindProperty("_targetSectorIndex").intValue = 2;
            sbt2to3Serialized.FindProperty("_targetSectorObject").objectReferenceValue = sector3Root;
            sbt2to3Serialized.ApplyModifiedProperties();

            return sector3Root;
        }

        public static GameObject BuildSector4Hierarchy(
            Scene scene,
            Transform envRoot,
            GameObject sector3Root,
            Sprite squareSprite,
            PhysicsMaterial2D onFrictionMat,
            TMP_FontAsset fontAsset,
            int groundLayer,
            Color floorColor,
            Color wallColor,
            out Checkpoint cp4,
            out SectorBoundaryTrigger boundaryTrigger)
        {
            GameObject sector4Root = new GameObject("Sector_04 (Bloated Carcass & Drag Tether)");
            sector4Root.transform.SetParent(envRoot, false);

            // 1. Approach Runway (from X=76.25 to 84.0, surface at Y=2.0)
            CreatePlatform(sector4Root.transform, "Floor_Sector4_Approach", new Vector3(80.125f, 1.0f, 0f), new Vector2(7.75f, 2.0f), floorColor, squareSprite, onFrictionMat, groundLayer);

            // Checkpoint 4
            cp4 = null;
            GameObject cpPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(k_CheckpointPrefabPath);
            if (cpPrefab != null)
            {
                var cpGo = (GameObject)PrefabUtility.InstantiatePrefab(cpPrefab, scene);
                cpGo.name = "Checkpoint_Sector4";
                cpGo.transform.SetParent(sector4Root.transform, false);
                cpGo.transform.position = new Vector3(78.0f, 2.5f, 0f);
                cp4 = cpGo.GetComponent<Checkpoint>();
            }

            // 2. Yellow Stitchable Crate
            GameObject cratePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(k_StitchableCratePrefabPath);
            if (cratePrefab != null)
            {
                var crateGo = (GameObject)PrefabUtility.InstantiatePrefab(cratePrefab, scene);
                crateGo.name = "StitchableCrate_Sector4";
                crateGo.transform.SetParent(sector4Root.transform, false);
                crateGo.transform.position = new Vector3(83.0f, 2.5f, 0f);

                var crateSr = crateGo.GetComponentInChildren<SpriteRenderer>();
                if (crateSr != null)
                {
                    crateSr.color = new Color(0.85f, 0.65f, 0.22f, 1f);
                    crateSr.sortingLayerName = "Default";
                    crateSr.sortingOrder = 3;
                }
            }

            // 3. Hint: RMB Tether
            Sprite rmbSprite = AssetDatabase.LoadAllAssetsAtPath(k_InputTilesheetPath)
                .OfType<Sprite>()
                .FirstOrDefault(s => s.name == "tilemap_white_packed_64");

            GameObject triggerHintGo = new GameObject("Trigger_Hint_Tether");
            triggerHintGo.transform.SetParent(sector4Root.transform, false);
            triggerHintGo.transform.position = new Vector3(81.5f, 2.75f, 0f);
            var triggerCol = triggerHintGo.AddComponent<BoxCollider2D>();
            triggerCol.size = new Vector2(3.5f, 3.0f);
            triggerCol.isTrigger = true;

            GameObject hintRoot = new GameObject("Hint_Tether");
            hintRoot.transform.SetParent(triggerHintGo.transform, false);
            hintRoot.transform.localPosition = new Vector3(0f, 0.8f, 0f);

            if (rmbSprite != null)
            {
                var iconGo = new GameObject("Icon_RMB");
                iconGo.transform.SetParent(hintRoot.transform, false);
                iconGo.transform.localPosition = new Vector3(-0.55f, 0f, 0f);
                iconGo.transform.localScale = new Vector3(0.75f, 0.75f, 1f);
                var sr = iconGo.AddComponent<SpriteRenderer>();
                sr.sprite = rmbSprite;
                sr.color = new Color(0.82f, 0.82f, 0.88f, 0.75f);
                sr.sortingLayerName = "Default";
                sr.sortingOrder = 5;
            }

            GameObject textGo = new GameObject("Text_Tether");
            textGo.transform.SetParent(hintRoot.transform, false);
            textGo.transform.localPosition = new Vector3(0.15f, 0f, 0f);
            var tmp = textGo.AddComponent<TextMeshPro>();
            if (fontAsset != null) tmp.font = fontAsset;
            tmp.fontSize = 4f;
            tmp.alignment = TextAlignmentOptions.Left;
            tmp.text = "Tether";
            tmp.color = new Color(0.82f, 0.82f, 0.88f, 0.75f);
            tmp.sortingLayerID = SortingLayer.NameToID("Default");
            tmp.sortingOrder = 5;

            var hintTrigger = triggerHintGo.AddComponent<WorldHintTrigger2D>();
            hintTrigger.HintRoot = hintRoot;
            hintTrigger.Prerequisite = HintPrerequisite.RequireNeedle;
            hintTrigger.DismissMode = HintDismissMode.RepeatableOnProximity;
            hintTrigger.FadeDuration = 0.35f;

            // 4. Sunken Pit (X: 84.0 ~ 90.0, floor surface at Y=0.0)
            CreatePlatform(sector4Root.transform, "Floor_Sector4_Pit", new Vector3(87.0f, -1.0f, 0f), new Vector2(6.0f, 2.0f), floorColor, squareSprite, onFrictionMat, groundLayer);
            CreatePlatform(sector4Root.transform, "Wall_Pit_Left", new Vector3(83.75f, 1.0f, 0f), new Vector2(0.5f, 2.0f), wallColor, squareSprite, onFrictionMat, groundLayer);

            // Anti-Softlock Return Step (1x1 block at X=84.5, Y=0.5, top at Y=1.0)
            CreatePlatform(sector4Root.transform, "Pit_ReturnStep_1x1", new Vector3(84.5f, 0.5f, 0f), new Vector2(1.0f, 1.0f), floorColor, squareSprite, onFrictionMat, groundLayer);

            // 5. Giant Fish Carcass & Gas Vent
            GameObject carcassGo = new GameObject("GiantFishCarcass");
            carcassGo.transform.SetParent(sector4Root.transform, false);
            carcassGo.transform.position = new Vector3(87.2f, 0.0f, 0f);

            CreateSpriteRendererObject(carcassGo.transform, "Carcass_Base", new Vector3(0f, 0.35f, 0f), new Vector2(3.2f, 0.7f), new Color(0.20f, 0.16f, 0.19f, 1f), squareSprite);
            CreateSpriteRendererObject(carcassGo.transform, "Carcass_Belly", new Vector3(0.15f, 0.65f, 0f), new Vector2(2.2f, 0.55f), new Color(0.28f, 0.16f, 0.20f, 1f), squareSprite);
            CreateSpriteRendererObject(carcassGo.transform, "Carcass_Gore", new Vector3(-0.1f, 0.45f, 0f), new Vector2(1.4f, 0.35f), new Color(0.40f, 0.11f, 0.14f, 1f), squareSprite);

            // Rupture Vent & CorpseGasVent2D
            GameObject ventGo = new GameObject("CorpseGasVent");
            ventGo.transform.SetParent(carcassGo.transform, false);
            ventGo.transform.localPosition = new Vector3(0.3f, 1.25f, 0f);

            var ventCol = ventGo.AddComponent<BoxCollider2D>();
            ventCol.size = new Vector2(1.6f, 2.5f);
            ventCol.isTrigger = true;

            var telegraphGo = CreateSpriteRendererObject(ventGo.transform, "Visual_Telegraph", new Vector3(0f, -0.7f, 0f), new Vector2(0.8f, 0.4f), new Color(0.35f, 0.75f, 0.45f, 0.35f), squareSprite);
            telegraphGo.SetActive(false);

            var eruptionGo = CreateSpriteRendererObject(ventGo.transform, "Visual_Eruption", new Vector3(0f, 0f, 0f), new Vector2(1.2f, 2.4f), new Color(0.35f, 0.75f, 0.45f, 0.55f), squareSprite);
            eruptionGo.SetActive(false);

            GameObject lightGo = new GameObject("GasVent_Light");
            lightGo.transform.SetParent(ventGo.transform, false);
            lightGo.transform.localPosition = new Vector3(0f, -0.6f, 0f);
            var light2D = lightGo.AddComponent<Light2D>();
            light2D.lightType = Light2D.LightType.Point;
            light2D.color = new Color(0.25f, 0.75f, 0.35f, 1f);
            light2D.pointLightOuterRadius = 3.5f;
            light2D.intensity = 0.35f;

            var gasVent = ventGo.AddComponent<CorpseGasVent2D>();
            var ventSo = new SerializedObject(gasVent);
            ventSo.FindProperty("_dormantDuration").floatValue = 2.5f;
            ventSo.FindProperty("_telegraphDuration").floatValue = 0.85f;
            ventSo.FindProperty("_eruptionDuration").floatValue = 2.2f;
            ventSo.FindProperty("_upwardForce").floatValue = 34f;
            ventSo.FindProperty("_maxUpwardVelocity").floatValue = 5.5f;
            ventSo.FindProperty("_telegraphVisual").objectReferenceValue = telegraphGo;
            ventSo.FindProperty("_eruptionVisual").objectReferenceValue = eruptionGo;
            ventSo.FindProperty("_ventLight").objectReferenceValue = light2D;
            ventSo.ApplyModifiedProperties();

            // 6. Obstacle Wall: 2.4m High
            CreatePlatform(sector4Root.transform, "Wall_Obstacle_2_4m", new Vector3(90.0f, 1.2f, 0f), new Vector2(1.0f, 2.4f), wallColor, squareSprite, onFrictionMat, groundLayer);

            // 7. Landing Platform
            CreatePlatform(sector4Root.transform, "Floor_Sector4_Landing", new Vector3(94.25f, 1.0f, 0f), new Vector2(7.5f, 2.0f), floorColor, squareSprite, onFrictionMat, groundLayer);
            CreatePlatform(sector4Root.transform, "Wall_Sector4_End", new Vector3(98.5f, 6.0f, 0f), new Vector2(1.0f, 10.0f), wallColor, squareSprite, onFrictionMat, groundLayer);

            // 8. Sector Boundary Trigger 3 -> 4
            GameObject trigger3to4Go = new GameObject("Trigger_Sector3_to_Sector4");
            trigger3to4Go.transform.SetParent(sector4Root.transform, false);
            trigger3to4Go.transform.position = new Vector3(76.0f, 3.5f, 0f);
            var sbtCol = trigger3to4Go.AddComponent<BoxCollider2D>();
            sbtCol.isTrigger = true;
            sbtCol.size = new Vector2(2.0f, 6.0f);

            boundaryTrigger = trigger3to4Go.AddComponent<SectorBoundaryTrigger>();
            var sbtSo = new SerializedObject(boundaryTrigger);
            sbtSo.FindProperty("_isBidirectional").boolValue = true;
            sbtSo.FindProperty("_sectorA").objectReferenceValue = sector3Root;
            sbtSo.FindProperty("_sectorB").objectReferenceValue = sector4Root;
            sbtSo.FindProperty("_expectedDirection").vector2Value = Vector2.right;
            sbtSo.FindProperty("_targetSectorIndex").intValue = 3;
            sbtSo.FindProperty("_targetSectorObject").objectReferenceValue = sector4Root;
            sbtSo.ApplyModifiedProperties();

            return sector4Root;
        }

        public static GameObject BuildSector5Hierarchy(
            Scene scene,
            Transform envRoot,
            GameObject sector4Root,
            Sprite squareSprite,
            PhysicsMaterial2D onFrictionMat,
            int groundLayer,
            Color floorColor,
            Color wallColor,
            out Checkpoint cp5,
            out SectorBoundaryTrigger boundaryTrigger)
        {
            GameObject sector5Root = new GameObject("Sector_05 (Stitch Rail & Chasm Loop)");
            if (envRoot != null)
            {
                sector5Root.transform.SetParent(envRoot, false);
            }

            // 1. Approach & Left Platform (from X = 99.0 to 107.0, surface at Y = 2.0)
            CreatePlatform(sector5Root.transform, "Floor_Sector5_Left", new Vector3(103.0f, 1.0f, 0f), new Vector2(8.0f, 2.0f), floorColor, squareSprite, onFrictionMat, groundLayer);

            // 2. Alcove Low Ceiling (Overhang from X = 98.75 to 103.25 at Y = 4.5)
            CreatePlatform(sector5Root.transform, "Ceiling_Sector5_Alcove", new Vector3(101.0f, 4.5f, 0f), new Vector2(4.5f, 1.0f), wallColor, squareSprite, onFrictionMat, groundLayer);

            // 3. Checkpoint 5 (at X = 99.5, Y = 2.5)
            cp5 = null;
            GameObject cpPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(k_CheckpointPrefabPath);
            if (cpPrefab != null)
            {
                var cpGo = (GameObject)PrefabUtility.InstantiatePrefab(cpPrefab, scene);
                cpGo.name = "Checkpoint_Sector5";
                cpGo.transform.SetParent(sector5Root.transform, false);
                cpGo.transform.position = new Vector3(99.5f, 2.5f, 0f);
                cp5 = cpGo.GetComponent<Checkpoint>();
            }

            // 4. Yellow Stitchable Crate inside Alcove (at X = 101.5, Y = 2.5)
            GameObject cratePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(k_StitchableCratePrefabPath);
            if (cratePrefab != null)
            {
                var crateGo = (GameObject)PrefabUtility.InstantiatePrefab(cratePrefab, scene);
                crateGo.name = "StitchableCrate_Sector5";
                crateGo.transform.SetParent(sector5Root.transform, false);
                crateGo.transform.position = new Vector3(101.5f, 2.5f, 0f);

                var crateSr = crateGo.GetComponentInChildren<SpriteRenderer>();
                if (crateSr != null)
                {
                    crateSr.color = new Color(0.85f, 0.65f, 0.22f, 1f); // Distinct warm yellow
                    crateSr.sortingLayerName = "Default";
                    crateSr.sortingOrder = 5;
                }

                var stitchable = crateGo.GetComponent<StitchableObject>();
                if (stitchable != null)
                {
                    var so = new SerializedObject(stitchable);
                    so.FindProperty("_travelSpeed").floatValue = 1.5f;
                    so.FindProperty("_waypointDwellTime").floatValue = 0.8f;
                    so.FindProperty("_preserveLoopOnRespawn").boolValue = true;
                    so.ApplyModifiedProperties();
                }
            }

            // 5. Chasm Pit & KillZone (Gap from X = 107.0 to 112.0 = 5.0m span)
            CreatePlatform(sector5Root.transform, "Floor_Sector5_Pit", new Vector3(109.5f, -3.0f, 0f), new Vector2(7.0f, 1.0f), floorColor, squareSprite, onFrictionMat, groundLayer);

            GameObject killZonePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(k_KillZonePrefabPath);
            if (killZonePrefab != null)
            {
                var kzSector5 = (GameObject)PrefabUtility.InstantiatePrefab(killZonePrefab, scene);
                kzSector5.name = "KillZone_Sector5_Chasm";
                kzSector5.transform.SetParent(sector5Root.transform, false);
                kzSector5.transform.position = new Vector3(109.5f, -2.0f, 0f);
                var kzCol = kzSector5.GetComponent<BoxCollider2D>();
                if (kzCol != null)
                {
                    kzCol.size = new Vector2(6.5f, 2.0f);
                }
            }

            // 6. Right Receiving Platform (from X = 112.0 to 121.0, surface at Y = 2.0)
            CreatePlatform(sector5Root.transform, "Floor_Sector5_Right", new Vector3(116.5f, 1.0f, 0f), new Vector2(9.0f, 2.0f), floorColor, squareSprite, onFrictionMat, groundLayer);

            // 7. Green Needle Anchor Point at Right Cliff Face (at X = 112.0, Y = 2.5)
            GameObject anchorPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(k_NeedleAnchorPrefabPath);
            if (anchorPrefab != null)
            {
                var anchorGo = (GameObject)PrefabUtility.InstantiatePrefab(anchorPrefab, scene);
                anchorGo.name = "NeedleAnchor_Sector5";
                anchorGo.transform.SetParent(sector5Root.transform, false);
                anchorGo.transform.position = new Vector3(112.0f, 2.5f, 0f);

                var anchorSr = anchorGo.GetComponentInChildren<SpriteRenderer>();
                if (anchorSr != null)
                {
                    anchorSr.color = new Color(0.28f, 0.88f, 0.45f, 1f); // Vibrant green
                    anchorSr.sortingOrder = 4;
                }

                // Add PointLight2D for atmospheric environmental guidance
                GameObject anchorLightGo = new GameObject("Anchor_Light");
                anchorLightGo.transform.SetParent(anchorGo.transform, false);
                anchorLightGo.transform.localPosition = Vector3.zero;
                var light2D = anchorLightGo.AddComponent<Light2D>();
                light2D.lightType = Light2D.LightType.Point;
                light2D.color = new Color(0.25f, 0.95f, 0.45f, 1f);
                light2D.pointLightOuterRadius = 3.5f;
                light2D.intensity = 0.65f;
            }

            // 8. Solid End Wall (at X = 121.5, Y = 6.0)
            CreatePlatform(sector5Root.transform, "Wall_Sector5_End", new Vector3(121.5f, 6.0f, 0f), new Vector2(1.0f, 10.0f), wallColor, squareSprite, onFrictionMat, groundLayer);

            // 9. Boundary Trigger Sector 4 -> Sector 5
            GameObject trigger4to5Go = new GameObject("Trigger_Sector4_to_Sector5");
            trigger4to5Go.transform.SetParent(sector5Root.transform, false);
            trigger4to5Go.transform.position = new Vector3(98.5f, 3.0f, 0f);
            var sbtCol = trigger4to5Go.AddComponent<BoxCollider2D>();
            sbtCol.isTrigger = true;
            sbtCol.size = new Vector2(1.5f, 3.5f);

            boundaryTrigger = trigger4to5Go.AddComponent<SectorBoundaryTrigger>();
            var sbtSo = new SerializedObject(boundaryTrigger);
            sbtSo.FindProperty("_isBidirectional").boolValue = true;
            sbtSo.FindProperty("_sectorA").objectReferenceValue = sector4Root;
            sbtSo.FindProperty("_sectorB").objectReferenceValue = sector5Root;
            sbtSo.FindProperty("_expectedDirection").vector2Value = Vector2.right;
            sbtSo.FindProperty("_targetSectorIndex").intValue = 4;
            sbtSo.FindProperty("_targetSectorObject").objectReferenceValue = sector5Root;
            sbtSo.ApplyModifiedProperties();

            // 10. Fish Waterfall Hazard (over chasm gap from X = 107.0 to 112.0)
            Sprite[] fishSprites = AssetDatabase.LoadAllAssetsAtPath(k_DeadFishSpriteSheetPath)
                .OfType<Sprite>()
                .OrderBy(s => s.name)
                .ToArray();

            FallingFishHazard2D fishPrefab = EnsureFallingFishPrefab(fishSprites);

            GameObject waterfallGo = new GameObject("FishWaterfall_Sector5");
            waterfallGo.transform.SetParent(sector5Root.transform, false);
            waterfallGo.transform.position = new Vector3(109.5f, 7.5f, 0f);

            // Overhead spawner lip / ceiling structure
            CreatePlatform(waterfallGo.transform, "Ceiling_Waterfall_Lip", new Vector3(109.5f, 7.9f, 0f), new Vector2(5.5f, 0.6f), wallColor, squareSprite, onFrictionMat, groundLayer);

            // Atmospheric dripping light
            GameObject waterfallLightGo = new GameObject("Waterfall_AtmosphericLight");
            waterfallLightGo.transform.SetParent(waterfallGo.transform, false);
            waterfallLightGo.transform.localPosition = new Vector3(0f, -0.3f, 0f);
            var wfLight = waterfallLightGo.AddComponent<Light2D>();
            wfLight.lightType = Light2D.LightType.Point;
            wfLight.color = new Color(0.35f, 0.75f, 0.85f, 1f);
            wfLight.pointLightOuterRadius = 4.5f;
            wfLight.intensity = 0.45f;

            // Spawner component
            var spawner = waterfallGo.AddComponent<FishWaterfallSpawner2D>();
            var spawnerSo = new SerializedObject(spawner);
            spawnerSo.FindProperty("_spawnHeightY").floatValue = 7.5f;
            spawnerSo.FindProperty("_fishPrefab").objectReferenceValue = fishPrefab;
            spawnerSo.FindProperty("_minInterval").floatValue = 1.15f;
            spawnerSo.FindProperty("_maxInterval").floatValue = 1.65f;
            spawnerSo.FindProperty("_activeMinX").floatValue = 94.0f;
            spawnerSo.FindProperty("_activeMaxX").floatValue = 126.0f;

            if (fishSprites != null && fishSprites.Length > 0)
            {
                var framesProp = spawnerSo.FindProperty("_deadFishFrames");
                framesProp.arraySize = fishSprites.Length;
                for (int i = 0; i < fishSprites.Length; i++)
                {
                    framesProp.GetArrayElementAtIndex(i).objectReferenceValue = fishSprites[i];
                }
            }
            spawnerSo.ApplyModifiedProperties();

            return sector5Root;
        }

        [MenuItem("Tools/Horror Jam/Build Sector 05 in Gameplay Scene", false, 12)]
        public static void BuildSector05InGameplayScene()
        {
            Scene scene = EditorSceneManager.GetActiveScene();
            if (!scene.path.EndsWith("Gameplay.unity"))
            {
                scene = EditorSceneManager.OpenScene(k_GameplayScenePath, OpenSceneMode.Single);
            }

            // Find or modify Wall_Sector4_End into an overhead doorway lintel
            GameObject existingWall4End = GameObject.Find("Wall_Sector4_End");
            if (existingWall4End != null)
            {
                existingWall4End.transform.position = new Vector3(98.5f, 7.0f, 0f);
                var col = existingWall4End.GetComponent<BoxCollider2D>();
                if (col != null) col.size = new Vector2(1.0f, 6.0f);
                var sr = existingWall4End.GetComponent<SpriteRenderer>();
                if (sr != null) sr.size = new Vector2(1.0f, 6.0f);
                EditorUtility.SetDirty(existingWall4End);
            }

            // Remove existing Sector 5 if re-running builder
            GameObject existingSector5 = GameObject.Find("Sector_05 (Stitch Rail & Chasm Loop)");
            if (existingSector5 != null)
            {
                Object.DestroyImmediate(existingSector5);
            }

            GameObject sector4Root = GameObject.Find("Sector_04 (Bloated Carcass & Drag Tether)");
            GameObject envRoot = GameObject.Find("--- ENVIRONMENT --------------------------------");
            Transform envTransform = envRoot != null ? envRoot.transform : null;

            Sprite squareSprite = AssetDatabase.LoadAssetAtPath<Sprite>(AssetDatabase.GUIDToAssetPath(k_SquareSpriteGuid));
            PhysicsMaterial2D frictionMat = AssetDatabase.LoadAssetAtPath<PhysicsMaterial2D>(k_PhysicsMaterialPath);
            int groundLayer = LayerMask.NameToLayer("Ground");
            if (groundLayer < 0) groundLayer = 0;

            Color floorColor = new Color(0.22f, 0.22f, 0.25f, 1f);
            Color wallColor = new Color(0.15f, 0.15f, 0.18f, 1f);

            GameObject sector5Root = BuildSector5Hierarchy(
                scene,
                envTransform,
                sector4Root,
                squareSprite,
                frictionMat,
                groundLayer,
                floorColor,
                wallColor,
                out Checkpoint cp5,
                out SectorBoundaryTrigger boundaryTrigger
            );

            // Register with LevelSeamlessManager if present
            LevelSeamlessManager seamlessMgr = Object.FindAnyObjectByType<LevelSeamlessManager>();
            if (seamlessMgr != null)
            {
                var lmSo = new SerializedObject(seamlessMgr);
                var sectorsProp = lmSo.FindProperty("_sectors");
                if (sectorsProp != null)
                {
                    bool found = false;
                    for (int i = 0; i < sectorsProp.arraySize; i++)
                    {
                        var el = sectorsProp.GetArrayElementAtIndex(i);
                        if (el.FindPropertyRelative("name").stringValue == "Sector 5 (Stitch Rail & Chasm Loop)")
                        {
                            el.FindPropertyRelative("rootObject").objectReferenceValue = sector5Root;
                            el.FindPropertyRelative("defaultCheckpoint").objectReferenceValue = cp5;
                            found = true;
                            break;
                        }
                    }
                    if (!found)
                    {
                        int idx = sectorsProp.arraySize;
                        sectorsProp.InsertArrayElementAtIndex(idx);
                        var newEl = sectorsProp.GetArrayElementAtIndex(idx);
                        newEl.FindPropertyRelative("name").stringValue = "Sector 5 (Stitch Rail & Chasm Loop)";
                        newEl.FindPropertyRelative("rootObject").objectReferenceValue = sector5Root;
                        newEl.FindPropertyRelative("defaultCheckpoint").objectReferenceValue = cp5;
                    }
                    lmSo.ApplyModifiedProperties();
                }
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[LevelTemplateCreator] Successfully built Sector_05 in Gameplay.unity!");
        }

        [MenuItem("Tools/Horror Jam/Setup LevelSeamless in Gameplay Scene", false, 13)]
        public static void SetupLevelSeamlessInGameplayScene()
        {
            Scene scene = EditorSceneManager.GetActiveScene();
            if (!scene.path.EndsWith("Gameplay.unity"))
            {
                scene = EditorSceneManager.OpenScene(k_GameplayScenePath, OpenSceneMode.Single);
            }

            // 1. Locate Sector roots
            GameObject s1 = GameObject.Find("Sector_01 (Intro)");
            GameObject s2 = GameObject.Find("Sector_02(Credit)") ?? GameObject.Find("Sector_02 (Courtyard)");
            GameObject s3 = GameObject.Find("Sector_03 (Bridge & Needle Cutting)");
            GameObject s4 = GameObject.Find("Sector_04 (Bloated Carcass & Drag Tether)");
            GameObject s5 = GameObject.Find("Sector_05 (Stitch Rail & Chasm Loop)");

            if (s1 == null || s2 == null || s3 == null || s4 == null || s5 == null)
            {
                Debug.LogError($"[LevelTemplateCreator] Missing sector roots in Gameplay scene! S1:{s1!=null}, S2:{s2!=null}, S3:{s3!=null}, S4:{s4!=null}, S5:{s5!=null}");
                return;
            }

            // 2. Locate Checkpoints by proximity
            Checkpoint[] allCps = Object.FindObjectsByType<Checkpoint>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            Checkpoint FindNearestCheckpoint(float targetX)
            {
                Checkpoint best = null;
                float bestDist = float.MaxValue;
                foreach (var cp in allCps)
                {
                    float d = Mathf.Abs(cp.transform.position.x - targetX);
                    if (d < bestDist)
                    {
                        bestDist = d;
                        best = cp;
                    }
                }
                return best;
            }

            Checkpoint cp1 = FindNearestCheckpoint(-12f);
            Checkpoint cp2 = FindNearestCheckpoint(44f);
            Checkpoint cp3 = FindNearestCheckpoint(55.5f);
            Checkpoint cp4 = FindNearestCheckpoint(78f);
            Checkpoint cp5 = FindNearestCheckpoint(104f);

            // 3. Find or create LevelSeamlessManager
            LevelSeamlessManager seamlessMgr = Object.FindAnyObjectByType<LevelSeamlessManager>(FindObjectsInactive.Include);
            if (seamlessMgr == null)
            {
                GameObject mgrRoot = GameObject.Find("Manager ======================== ") 
                    ?? GameObject.Find("System========================");
                GameObject levelMgrGo = new GameObject("LevelSeamlessManager");
                if (mgrRoot != null)
                {
                    levelMgrGo.transform.SetParent(mgrRoot.transform, false);
                }
                seamlessMgr = levelMgrGo.AddComponent<LevelSeamlessManager>();
                Debug.Log("[LevelTemplateCreator] Created LevelSeamlessManager GameObject.");
            }

            // 4. Configure LevelSeamlessManager SerializedObject
            var lmSo = new SerializedObject(seamlessMgr);
            PlayerController2D player = Object.FindAnyObjectByType<PlayerController2D>(FindObjectsInactive.Include);
            NeedleController needle = Object.FindAnyObjectByType<NeedleController>(FindObjectsInactive.Include);

            if (player != null) lmSo.FindProperty("_playerController").objectReferenceValue = player;
            if (needle != null) lmSo.FindProperty("_needleController").objectReferenceValue = needle;

            lmSo.FindProperty("_activeRadius").intValue = 1;
            lmSo.FindProperty("_initialSectorIndex").intValue = 0;

            var sectorsProp = lmSo.FindProperty("_sectors");
            sectorsProp.ClearArray();

            var sectorDefs = new (string name, GameObject go, Checkpoint cp)[]
            {
                ("Sector 1 (Intro)", s1, cp1),
                ("Sector 2 (Credit)", s2, cp2),
                ("Sector 3 (Bridge & Needle Cutting)", s3, cp3),
                ("Sector 4 (Bloated Carcass & Drag Tether)", s4, cp4),
                ("Sector 5 (Stitch Rail & Chasm Loop)", s5, cp5)
            };

            for (int i = 0; i < sectorDefs.Length; i++)
            {
                sectorsProp.InsertArrayElementAtIndex(i);
                var entry = sectorsProp.GetArrayElementAtIndex(i);
                entry.FindPropertyRelative("name").stringValue = sectorDefs[i].name;
                entry.FindPropertyRelative("rootObject").objectReferenceValue = sectorDefs[i].go;
                entry.FindPropertyRelative("defaultCheckpoint").objectReferenceValue = sectorDefs[i].cp;
            }
            lmSo.ApplyModifiedProperties();

            // 5. Ensure Triggers
            Transform triggersParent = GameObject.Find("Triggers ======================== ")?.transform;

            // Trigger S1 -> S2 (x = 17)
            GameObject t12 = GameObject.Find("Trigger_Sector1_to_Sector2");
            if (t12 == null)
            {
                t12 = new GameObject("Trigger_Sector1_to_Sector2");
                if (triggersParent != null) t12.transform.SetParent(triggersParent, false);
                t12.transform.position = new Vector3(17f, 3.5f, 0f);

                var col = t12.AddComponent<BoxCollider2D>();
                col.size = new Vector2(2f, 8f);
                col.isTrigger = true;

                var trigger = t12.AddComponent<SectorBoundaryTrigger>();
                var tSo = new SerializedObject(trigger);
                tSo.FindProperty("_isBidirectional").boolValue = true;
                tSo.FindProperty("_sectorA").objectReferenceValue = s1;
                tSo.FindProperty("_sectorB").objectReferenceValue = s2;
                tSo.FindProperty("_targetSectorIndex").intValue = 1;
                tSo.FindProperty("_targetSectorObject").objectReferenceValue = s2;
                tSo.ApplyModifiedProperties();
                Debug.Log("[LevelTemplateCreator] Created Trigger_Sector1_to_Sector2 at x=17.");
            }

            // Verify S2 -> S3 (x = 54)
            GameObject t23 = GameObject.Find("Trigger_Sector2_to_Sector3");
            if (t23 != null)
            {
                var trigger = t23.GetComponent<SectorBoundaryTrigger>();
                if (trigger != null)
                {
                    var tSo = new SerializedObject(trigger);
                    tSo.FindProperty("_isBidirectional").boolValue = true;
                    tSo.FindProperty("_sectorA").objectReferenceValue = s2;
                    tSo.FindProperty("_sectorB").objectReferenceValue = s3;
                    tSo.FindProperty("_targetSectorIndex").intValue = 2;
                    tSo.FindProperty("_targetSectorObject").objectReferenceValue = s3;
                    tSo.ApplyModifiedProperties();
                }
            }

            // Verify S3 -> S4 (x = 76)
            GameObject t34 = GameObject.Find("Trigger_Sector3_to_Sector4");
            if (t34 != null)
            {
                var trigger = t34.GetComponent<SectorBoundaryTrigger>();
                if (trigger != null)
                {
                    var tSo = new SerializedObject(trigger);
                    tSo.FindProperty("_isBidirectional").boolValue = true;
                    tSo.FindProperty("_sectorA").objectReferenceValue = s3;
                    tSo.FindProperty("_sectorB").objectReferenceValue = s4;
                    tSo.FindProperty("_targetSectorIndex").intValue = 3;
                    tSo.FindProperty("_targetSectorObject").objectReferenceValue = s4;
                    tSo.ApplyModifiedProperties();
                }
            }

            // Verify S4 -> S5 (x = 98.5)
            GameObject t45 = GameObject.Find("Trigger_Sector4_to_Sector5");
            if (t45 != null)
            {
                var trigger = t45.GetComponent<SectorBoundaryTrigger>();
                if (trigger != null)
                {
                    var tSo = new SerializedObject(trigger);
                    tSo.FindProperty("_isBidirectional").boolValue = true;
                    tSo.FindProperty("_sectorA").objectReferenceValue = s4;
                    tSo.FindProperty("_sectorB").objectReferenceValue = s5;
                    tSo.FindProperty("_targetSectorIndex").intValue = 4;
                    tSo.FindProperty("_targetSectorObject").objectReferenceValue = s5;
                    tSo.ApplyModifiedProperties();
                }
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[LevelTemplateCreator] Successfully configured LevelSeamlessManager and all triggers in Gameplay.unity!");
        }
    }
}
