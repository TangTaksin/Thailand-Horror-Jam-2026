using System.IO;
using UnityEditor;
using UnityEngine;

namespace ThailandHorrorJam.Editor
{
    public static class LocalizationEditorTools
    {
        private const string k_LibreOfficeCalcPath = @"C:\Program Files\LibreOffice\program\scalc.exe";
        private const string k_CsvAssetPath = "Assets/_Project/Resources/Localization.csv";
        private const string k_TsvAssetPath = "Assets/_Project/Resources/Localization.tsv";

        [MenuItem("Tools/Localization/Open with LibreOffice Calc %&l")]
        public static void OpenInLibreOfficeCalc()
        {
            string targetPath = GetExistingLocalizationFilePath();
            if (string.IsNullOrEmpty(targetPath))
            {
                EditorUtility.DisplayDialog("Error", "Localization file not found in Assets/_Project/Resources/.", "OK");
                return;
            }

            if (File.Exists(k_LibreOfficeCalcPath))
            {
                System.Diagnostics.Process.Start(k_LibreOfficeCalcPath, $"\"{targetPath}\"");
                Debug.Log($"[Localization] Opened in LibreOffice Calc: {targetPath}");
            }
            else
            {
                // Fallback to default OS handler
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(targetPath) { UseShellExecute = true });
            }
        }

        [MenuItem("Tools/Localization/Open Localization File (Default Editor)")]
        public static void OpenInDefaultEditor()
        {
            string targetPath = GetExistingLocalizationFilePath();
            if (string.IsNullOrEmpty(targetPath)) return;

            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(FileUtil.GetProjectRelativePath(targetPath));
            if (asset != null)
            {
                AssetDatabase.OpenAsset(asset);
            }
            else
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(targetPath) { UseShellExecute = true });
            }
        }

        [MenuItem("Tools/Localization/Apply Localization to Gameplay Scene")]
        public static void ApplyLocalizationToGameplayScene()
        {
            string scenePath = "Assets/_Project/Scenes/Gameplay.unity";
            var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(scenePath, UnityEditor.SceneManagement.OpenSceneMode.Single);
            if (!scene.IsValid())
            {
                Debug.LogError($"[Localization] Could not open scene at {scenePath}");
                return;
            }

            var fontAsset = AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>("Assets/_Project/Art/Fonts/SDF/2005_iannnnnAMD SDF.asset");

            var targets = new (string parentPattern, string goName, string textMatch, string key)[]
            {
                ("Hint_Movement", "Text_Move", "Move", "tut_move"),
                ("Hint_Jump", "Text_Action", "Tap Jump", "tut_tap_jump"),
                ("Hint_Jump", "Text_SpaceKey", "Space", "tut_space"),
                ("Hint_HoldJump", "Text_Action", "Long Jump", "tut_long_jump"),
                ("Hint_HoldJump", "Text_SpaceKey", "Hold Space", "tut_hold_space"),
                ("Hint_NeedleAim", "Text_Aim", "Aim", "tut_aim"),
                ("Hint_NeedleShoot", "Text_Move", "Pin", "tut_pin"),
                ("EndCardPanel", "TitleText", "THE END", "ui_the_end"),
                ("EndCardPanel", "CreditsText", "Thank you for playing!", "ui_thank_you"),
                ("MainMenuButton", "BtnText", "Menu", "ui_menu")
            };

            int attachedCount = 0;
            var allTMP = Object.FindObjectsByType<TMPro.TMP_Text>(FindObjectsInactive.Include);

            foreach (var (parentPattern, goName, textMatch, key) in targets)
            {
                foreach (var tmp in allTMP)
                {
                    if (tmp.gameObject.name != goName) continue;

                    // Check if either direct parent or grandparent matches parentPattern
                    bool parentMatches = (tmp.transform.parent != null && tmp.transform.parent.name == parentPattern)
                                      || (tmp.transform.parent != null && tmp.transform.parent.parent != null && tmp.transform.parent.parent.name == parentPattern);
                    if (!parentMatches) continue;

                    // If textMatch is specified, verify it
                    if (!string.IsNullOrEmpty(textMatch) && !tmp.text.Contains(textMatch) && !tmp.text.Contains(SimpleLocalization.LocalizationManager.Get(key)))
                    {
                        continue;
                    }

                    var localizedText = tmp.GetComponent<SimpleLocalization.LocalizedText>();
                    if (localizedText == null)
                    {
                        localizedText = Undo.AddComponent<SimpleLocalization.LocalizedText>(tmp.gameObject);
                    }

                    var serializedObj = new SerializedObject(localizedText);
                    var keyProp = serializedObj.FindProperty("key");
                    if (keyProp != null)
                    {
                        keyProp.stringValue = key;
                        serializedObj.ApplyModifiedProperties();
                    }

                    if (fontAsset != null)
                    {
                        tmp.font = fontAsset;
                    }

                    EditorUtility.SetDirty(tmp.gameObject);
                    attachedCount++;
                    Debug.Log($"[Localization] Attached LocalizedText '{key}' to {tmp.transform.parent?.name}/{goName}");
                    break;
                }
            }

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
            Debug.Log($"[Localization] Finished applying localization to Gameplay scene. Total components configured: {attachedCount}");
        }

        private static string GetExistingLocalizationFilePath()
        {
            string csvFull = Path.GetFullPath(k_CsvAssetPath);
            if (File.Exists(csvFull)) return csvFull;

            string tsvFull = Path.GetFullPath(k_TsvAssetPath);
            if (File.Exists(tsvFull)) return tsvFull;

            return null;
        }
    }
}
