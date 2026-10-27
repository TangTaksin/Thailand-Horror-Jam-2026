using UnityEditor;
using UnityEngine;
using ThailandHorrorJam.Systems.Needle;

namespace ThailandHorrorJam.Editor
{
    [CustomEditor(typeof(HangingObject2D))]
    [CanEditMultipleObjects]
    public sealed class HangingObject2DEditor : UnityEditor.Editor
    {
        private HangingObject2D _target;

        private void OnEnable()
        {
            _target = (HangingObject2D)target;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            SerializedProperty modeProp = serializedObject.FindProperty("_mode");
            EditorGUILayout.PropertyField(modeProp);

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Ceiling & Attachment Anchors", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_ceilingAnchor"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_objectAnchor"));

            HangingMode currentMode = (HangingMode)modeProp.enumValueIndex;

            if (currentMode == HangingMode.Drawbridge)
            {
                EditorGUILayout.Space(6);
                EditorGUILayout.LabelField("Drawbridge Settings", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("_hingePivot"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("_flatRestAngle"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("_fallRotationSpeed"));

                EditorGUILayout.Space(6);
                EditorGUILayout.LabelField("Designer Dimensions", EditorStyles.boldLabel);
                SerializedProperty manageGeomProp = serializedObject.FindProperty("_manageBridgeGeometry");
                EditorGUILayout.PropertyField(manageGeomProp);
                if (manageGeomProp.boolValue)
                {
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("_bridgeLength"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("_bridgeThickness"));
                }
            }
            else // FreeFall
            {
                EditorGUILayout.Space(6);
                EditorGUILayout.LabelField("FreeFall Settings", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("_freeFallGravityScale"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("_lockAsStaticOnLanding"));

                if (_target.GetComponent<StitchableObject>() != null)
                {
                    EditorGUILayout.HelpBox("📦 StitchableObject Detected: When cut, this object will drop with gravity and seamlessly unlock as a tetherable/haulable object!", MessageType.Info);
                }
            }

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Landing Impact FX", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_enableLandingShake"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_landingShakeIntensity"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_landingShakeDuration"));

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Thread Component", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_sutureThread"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_sutureThreadPrefab"));

            EditorGUILayout.Space(12);
            EditorGUILayout.LabelField("🛠️ Level Designer Quick Tools", EditorStyles.boldLabel);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                // Snap Ceiling Upward Button
                if (GUILayout.Button("⚡ Snap Ceiling Upward (Auto-Raycast)", GUILayout.Height(30)))
                {
                    if (_target.CeilingAnchor != null)
                    {
                        Undo.RecordObject(_target.CeilingAnchor, "Snap Ceiling Anchor");
                        if (_target.SnapCeilingAnchorUpward())
                        {
                            EditorUtility.SetDirty(_target.CeilingAnchor);
                            Debug.Log("[HangingObject] Ceiling anchor snapped to ceiling collider above!");
                        }
                        else
                        {
                            Debug.LogWarning("[HangingObject] No ceiling collider found directly above anchor. Ensure ceiling has a 2D collider.");
                        }
                    }
                }

                // Align Ceiling with Anchor X
                if (_target.CeilingAnchor != null && _target.ObjectAnchor != null)
                {
                    if (GUILayout.Button("📐 Align Ceiling X to Anchor (Vertical Thread)", GUILayout.Height(24)))
                    {
                        Undo.RecordObject(_target.CeilingAnchor, "Align Ceiling X");
                        Vector3 cPos = _target.CeilingAnchor.position;
                        cPos.x = _target.ObjectAnchor.position.x;
                        _target.CeilingAnchor.position = cPos;
                        EditorUtility.SetDirty(_target.CeilingAnchor);
                    }
                }
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void OnSceneGUI()
        {
            if (_target == null) return;

            Transform ceiling = _target.CeilingAnchor;
            Transform tip = _target.ObjectAnchor;
            Vector3 pivot = _target.HingePivot != null ? _target.HingePivot.position : _target.transform.position;

            // 1. Interactive Handle for Ceiling Anchor
            if (ceiling != null)
            {
                EditorGUI.BeginChangeCheck();
                Vector3 newCeilingPos = Handles.PositionHandle(ceiling.position, Quaternion.identity);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(ceiling, "Move Ceiling Anchor");
                    ceiling.position = newCeilingPos;
                    EditorUtility.SetDirty(ceiling);
                }

                Handles.color = Color.yellow;
                Handles.Label(ceiling.position + Vector3.up * 0.28f, "🟡 Ceiling Anchor", EditorStyles.boldLabel);
            }

            // 2. Interactive Length Slider Handle at Bridge Tip (Drawbridge only)
            if (_target.Mode == HangingMode.Drawbridge && _target.ManageBridgeGeometry && tip != null)
            {
                Vector3 tipPos = tip.position;
                Vector3 bridgeDir = (tipPos - pivot).normalized;
                if (bridgeDir.sqrMagnitude < 0.001f) bridgeDir = -_target.transform.right;

                float currentLength = _target.BridgeLength;

                EditorGUI.BeginChangeCheck();
                Handles.color = Color.cyan;
                Vector3 newTipPos = Handles.Slider(tipPos, bridgeDir, HandleUtility.GetHandleSize(tipPos) * 0.16f, Handles.SphereHandleCap, 0.05f);
                if (EditorGUI.EndChangeCheck())
                {
                    float newDist = Vector3.Distance(pivot, newTipPos);
                    Undo.RecordObject(_target, "Resize Bridge Length");
                    _target.ApplyDimensions(newDist, _target.BridgeThickness);
                    EditorUtility.SetDirty(_target);
                }

                // 3. Information Tag in Scene View
                string info = $"Bridge: {currentLength:F1}m | Angle: {_target.transform.eulerAngles.z:F0}° -> Rest: {_target.FlatRestAngle:F0}°";
                GUIStyle style = new GUIStyle(EditorStyles.helpBox)
                {
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter
                };
                Handles.Label(pivot + Vector3.up * 0.55f, info, style);
            }
        }
    }
}
