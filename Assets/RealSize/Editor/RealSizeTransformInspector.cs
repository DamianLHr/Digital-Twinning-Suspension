using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System;
using System.Reflection;
using System.Linq;

namespace RealSize
{
    [CustomEditor(typeof(Transform))]
    [CanEditMultipleObjects]
    public class RealSizeTransformInspector : Editor
    {
        Editor _baseTransformEditor;
        Transform _transform;

        Bounds _currentBounds;
        Vector3 _currentUnitSize;
        Vector3 _uniformScaleFactor = Vector3.one;

        bool _isFirstSelection = true;
        bool _isDirty = false;

        RealSizePrefs _prefs;

        private Vector3 _previousPosition = Vector3.zero;
        private Vector3 _previousRotation = Vector3.zero;
        private Vector3 _previousScale = Vector3.one;

        SizeUnit[] _baseSizeUnits = new SizeUnit[]
        {
            new SizeUnit("inch", 39.37007874f),
            new SizeUnit("feet", 3.280839895f),
            new SizeUnit("yard", 1.0936132983f),
            new SizeUnit("mile", 0.0006213712f),
            new SizeUnit("mm", 1000f),
            new SizeUnit("cm", 100f),
            new SizeUnit("m", 1f),
            new SizeUnit("km", 0.001f),
        };

        // UI
        GUIContent[] _selectableBoundsModes;
        GUIContent[] _selectablePrecisionModes;
        SizeUnit[] _selectableSizeUnits;
        GUIContent[] _selectableSizeUnitLabels;
        GUIContent _uniformScaleLockButton;
        GUIContent _uniformScaleUnlockButton;

        Texture2D _texLabelBackgroundX;
        Texture2D _texLabelBackgroundY;
        Texture2D _texLabelBackgroundZ;


        // Diagnostics
        System.Diagnostics.Stopwatch _calculateBoundsStopwatch = new System.Diagnostics.Stopwatch();
        private bool _showedPreciseModeWarning = false;

        void OnEnable()
        {
            _baseTransformEditor = Editor.CreateEditor(targets, Type.GetType("UnityEditor.TransformInspector, UnityEditor"));
            _transform = target as Transform;

            LoadPreferences();
            CalculateUniformScaleFactor(_transform.localScale);
        }

        void OnDisable()
        {
            if (_baseTransformEditor != null)
            {
                // Destory default editor to avoid memory leakage after calling any required methods (OnDisable)
                MethodInfo disableMethod = _baseTransformEditor.GetType().GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (disableMethod != null)
                {
                    disableMethod.Invoke(_baseTransformEditor, null);
                }

                DestroyImmediate(_baseTransformEditor);
            }
        }

        void LoadBoundsModes()
        {
            string[] modes = Enum.GetNames(typeof(RealSizePrefs.BoundsMode));
            GUIContent[] modeLabels = new GUIContent[modes.Length];
            for (int i = 0; i < modes.Length; i++)
            {
                string modeName = Enum.GetName(typeof(RealSizePrefs.BoundsMode), (RealSizePrefs.BoundsMode)i);
                GUIContent modeLabel = new GUIContent()
                {
                    text = modeName,
                    tooltip = modeName + " Bounds"
                };

                if ((RealSizePrefs.BoundsMode)i == RealSizePrefs.BoundsMode.World)
                {
                    modeLabel.tooltip += " (Unable to change object size directly in this mode)";
                }

                modeLabels[i] = modeLabel;
            }
            _selectableBoundsModes = modeLabels;
        }

        void LoadPrecisionModes()
        {
            GUIContent[] precisionModeLabels = new GUIContent[3];

            precisionModeLabels[0] = new GUIContent()
            {
                text = Enum.GetName(typeof(RealSizePrefs.PrecisionMode), RealSizePrefs.PrecisionMode.Precise),
                tooltip = "Precise (evaluates all vertices)"
            };

            precisionModeLabels[1] = new GUIContent()
            {
                text = Enum.GetName(typeof(RealSizePrefs.PrecisionMode), RealSizePrefs.PrecisionMode.Approximate),
                tooltip = $"Approximate (evaluates {_prefs.maxVerticesApproximateMode} vertices. Define number of vertices to evaluate in Preferences)"
            };

            precisionModeLabels[2] = new GUIContent()
            {
                text = Enum.GetName(typeof(RealSizePrefs.PrecisionMode), RealSizePrefs.PrecisionMode.Bounds),
                tooltip = $"Bounds (imprecise, evaluates local bounds)"
            };

            _selectablePrecisionModes = precisionModeLabels;
        }

        void LoadPreferences()
        {
            _prefs = RealSizePrefs.Instance;
            LoadBoundsModes();
            LoadPrecisionModes();
            LoadSizeUnits();
            LoadResources();
        }

        void LoadResources()
        {
            // Uniform Scale Toggle Content and Style
            _uniformScaleLockButton = _prefs.uniformScaleButtonTextureOn != null ? new GUIContent(_prefs.uniformScaleButtonTextureOn, "Uniform Scale (On)") : new GUIContent("u", "Uniform Scale Enabled");
            _uniformScaleUnlockButton = _prefs.uniformScaleButtonTextureOff != null ? new GUIContent(_prefs.uniformScaleButtonTextureOff, "Uniform Scale (Off)") : new GUIContent("u", "Uniform Scale Disabled");

            // Label backgrounds
            _texLabelBackgroundX = new Texture2D(1, 1);
            _texLabelBackgroundY = new Texture2D(1, 1);
            _texLabelBackgroundZ = new Texture2D(1, 1);
            _texLabelBackgroundX.SetPixel(0, 0, _prefs.sizeLabelBackgroundColorX);
            _texLabelBackgroundY.SetPixel(0, 0, _prefs.sizeLabelBackgroundColorY);
            _texLabelBackgroundZ.SetPixel(0, 0, _prefs.sizeLabelBackgroundColorZ);
            _texLabelBackgroundX.Apply();
            _texLabelBackgroundY.Apply();
            _texLabelBackgroundZ.Apply();
        }

        void LoadSizeUnits()
        {
            List<SizeUnit> sizeUnitList = new List<SizeUnit>(20);
            if (!_prefs.onlyShowCustomUnits)
            {
                sizeUnitList.AddRange(_baseSizeUnits);
            }

            if (_prefs.metersPerUnityUnit <= 0f)
            {
                _prefs.metersPerUnityUnit = 1f;
            }

            sizeUnitList.Add(SizeUnit.unity);

            if (_prefs.customUnits != null)
            {
                for (int i = 0; i < _prefs.customUnits.Length; i++)
                {
                    SizeUnit u = _prefs.customUnits[i];
                    if (!string.IsNullOrEmpty(u.unitName) && u.unitsPerMeter > 0f)
                    {
                        sizeUnitList.Add(u);
                    }
                }
            }

            _selectableSizeUnits = sizeUnitList.ToArray();

            if (_prefs.selectedUnitIndex >= sizeUnitList.Count)
            {
                _prefs.selectedUnitIndex = sizeUnitList.IndexOf(sizeUnitList.First(x => x.unitName.ToLower() == "unity"));
                if (_prefs.selectedUnitIndex < 0)
                {
                    _prefs.selectedUnitIndex = 0;
                }
            }

            if (_prefs.sizeUnitsDisplayedPerRow <= 0)
            {
                _prefs.sizeUnitsDisplayedPerRow = 4;
            }

            _selectableSizeUnitLabels = new GUIContent[sizeUnitList.Count];
            for (int i = 0; i < sizeUnitList.Count; i++)
            {
                GUIContent button;
                if (sizeUnitList[i].unitsPerMeter == -1f && sizeUnitList[i].unitName.ToLower() == "unity")
                {
                    button = new GUIContent(sizeUnitList[i].unitName, $"Unity Worldspace Units");
                }
                else
                {
                    button = new GUIContent(sizeUnitList[i].unitName, $"{sizeUnitList[i].unitName}: {sizeUnitList[i].unitsPerMeter} per meter");
                }
                _selectableSizeUnitLabels[i] = button;
            }
        }

        private void ModifyObjectLocalSize(Transform t, Vector3 previousSize, Vector3 newSize, bool uniformScaleEnabled)
        {
            // x = new / old
            // x * effectiveScale / parentScale = new object scale

            Vector3 effectiveScale; // object local scale, multiplied by each parent scale to the root
            Vector3 parentScale = Vector3.one; // scale of the parent, multiplied by each parent scale to the root
            Transform parent = t.parent;
            while (parent)
            {
                parentScale = Vector3.Scale(parentScale, parent.localScale);
                parent = parent.parent;
            }
            effectiveScale = Vector3.Scale(t.localScale, parentScale);

            Vector3 diff = newSize - previousSize;
            Vector3 affectedAxis = diff.normalized; // singles out the axis being modified
            Vector3 scaledAxis = Vector3.one; // final scaled axis, the unaffected axes should be 0f

            bool changed = false;
            if (affectedAxis != Vector3.zero)
            {
                Vector3 ratio = Vector3.Scale(newSize, new Vector3(previousSize.x != 0f ? 1f / previousSize.x : 0f, previousSize.y != 0f ? 1f / previousSize.y : 0f, previousSize.z != 0f ? 1f / previousSize.z : 0f));
                scaledAxis = Vector3.Scale(Vector3.Scale(ratio, effectiveScale), new Vector3(parentScale.x != 0f ? 1f / parentScale.x : 0f, parentScale.y != 0f ? 1f / parentScale.y : 0f, parentScale.z != 0f ? 1f / parentScale.z : 0f));
                changed = true;
            }

            if (changed)
            {
                Vector3 newTransformScale = t.localScale;
                float changedScale = 1f;
                for (int i = 0; i < 3; i++)
                {
                    if (affectedAxis[i] != 0f)
                    {
                        if (scaledAxis[i] == 0f)
                        {
                            // The input was set to zero, so disregard changes (non-zero number required in input field)
                            return;
                        }

                        newTransformScale[i] = scaledAxis[i];
                        changedScale = (newTransformScale[i] - t.localScale[i]);
                        changedScale *= _uniformScaleFactor[i] != 0f ? 1f / _uniformScaleFactor[i] : 0f;
                    }
                }

                if (uniformScaleEnabled)
                {
                    for (int i = 0; i < 3; i++)
                    {
                        if (affectedAxis[i] == 0f)
                        {
                            newTransformScale[i] += (changedScale * _uniformScaleFactor[i]);
                        }
                    }
                }
                else
                {
                    CalculateUniformScaleFactor(newTransformScale);
                }

                t.localScale = newTransformScale;
            }
        }

        void CalculateUniformScaleFactor(Vector3 currentScale)
        {
            bool nonzeroFound = false;
            //find a non-zero unit to set the relative scale value
            for (int i = 0; i < 3; i++)
            {
                if (currentScale[i] != 0)
                {
                    for (int j = 0; j < 3; j++)
                    {
                        if (j == i)
                        {
                            _uniformScaleFactor[j] = 1f;
                        }
                        else
                        {
                            _uniformScaleFactor[j] = currentScale[j] / currentScale[i];
                        }

                    }

                    nonzeroFound = true;
                    break;
                }
            }

            if (!nonzeroFound)
            {
                _uniformScaleFactor = Vector3.zero;
            }
        }

        public void DrawToolSection()
        {
            bool initialGUIEnabledValue = GUI.enabled;
            #region Object Size Section
            GUILayout.Space(5);
            EditorGUILayout.BeginHorizontal();
            {
                // Object size label
                EditorGUILayout.LabelField($"Object Size ({_selectableSizeUnits[_prefs.selectedUnitIndex].unitName})", GUILayout.MaxWidth(EditorGUIUtility.currentViewWidth * 0.27f));
                Vector3 modifiedObjectSize = _currentUnitSize;
                GUILayout.FlexibleSpace();

                // Uniform Scale Toggle
                GUIContent activeLockButton = _prefs.uniformScaleEnabled ? _uniformScaleLockButton : _uniformScaleUnlockButton;
                GUIStyle uniformScaleToggleStyle = new GUIStyle(GUI.skin.button);
                uniformScaleToggleStyle.padding = new RectOffset(2, 2, 2, 2);

                // Only show lock icon if object size is editable (local mode)
                if (_prefs.boundsMode == RealSizePrefs.BoundsMode.Local)
                {
                    bool uniformScaleToggleValue = GUILayout.Toggle(_prefs.uniformScaleEnabled, activeLockButton, uniformScaleToggleStyle, GUILayout.MaxWidth(16f), GUILayout.MaxHeight(18f));
                    if (uniformScaleToggleValue != _prefs.uniformScaleEnabled)
                    {
                        Undo.RecordObject(_prefs, "Toggle Uniform Scale");
                        _prefs.uniformScaleEnabled = uniformScaleToggleValue;
                        _isDirty = true;
                        EditorUtility.SetDirty(_prefs);
                    }
                    GUILayout.Space(2);
                }


                // Make object size read-only when not in Local Bounds mode
                if (_prefs.boundsMode == RealSizePrefs.BoundsMode.World)
                {
                    GUI.enabled = false;
                }

                // Object Size Fields, Field is disabled if the value is 0 and uniform scale is enabled, because a value of 0 does not give enough information to properly scale
                EditorGUIUtility.labelWidth = 10f;
                if (_currentUnitSize.x == 0f)
                {
                    GUI.enabled = false;
                }
                modifiedObjectSize.x = EditorGUILayout.FloatField("X", _currentUnitSize.x, GUILayout.MaxWidth(EditorGUIUtility.currentViewWidth * 0.19f));
                if (_prefs.boundsMode == RealSizePrefs.BoundsMode.Local)
                {
                    GUI.enabled = true;
                }

                if (_currentUnitSize.y == 0f)
                {
                    GUI.enabled = false;
                }
                modifiedObjectSize.y = EditorGUILayout.FloatField("Y", _currentUnitSize.y, GUILayout.MaxWidth(EditorGUIUtility.currentViewWidth * 0.19f));
                if (_prefs.boundsMode == RealSizePrefs.BoundsMode.Local)
                {
                    GUI.enabled = true;
                }

                if (_currentUnitSize.z == 0f)
                {
                    GUI.enabled = false;
                }
                modifiedObjectSize.z = EditorGUILayout.FloatField("Z", _currentUnitSize.z, GUILayout.MaxWidth(EditorGUIUtility.currentViewWidth * 0.19f));
                if (_prefs.boundsMode == RealSizePrefs.BoundsMode.Local)
                {
                    GUI.enabled = true;
                }

                if (modifiedObjectSize != _currentUnitSize)
                {
                    Undo.RecordObject(_transform, "Object Size Change");

                    ModifyObjectLocalSize(_transform, _currentUnitSize, modifiedObjectSize, _prefs.uniformScaleEnabled);
                }

                // return gui to pre-tool state
                GUI.enabled = initialGUIEnabledValue;
            }
            EditorGUILayout.EndHorizontal();
            #endregion Object Size Section

            #region Size Unit Selection
            GUILayout.Space(5);
            //selectedUnit = GUI.SelectionGrid(new Rect(25, 25, 100, 30), selectedUnit, unitSelections, 4);
            int selectedUnitChange = GUILayout.SelectionGrid(_prefs.selectedUnitIndex, _selectableSizeUnitLabels, _prefs.sizeUnitsDisplayedPerRow);
            if (_prefs.selectedUnitIndex != selectedUnitChange)
            {
                Undo.RecordObject(_prefs, "Unit Selection Change");
                _prefs.selectedUnitIndex = selectedUnitChange;
                _isDirty = true;
                EditorUtility.SetDirty(_prefs);
            }
            #endregion Size Unit Selection


            // If base transform inspector is modified, recalulate the bounds
            if (_transform.position != _previousPosition || _transform.rotation.eulerAngles != _previousRotation || _transform.localScale != _previousScale || _isFirstSelection)
            {
                _isFirstSelection = false;
                _isDirty = true;
                CalculateUniformScaleFactor(_transform.localScale);
            }

            GUILayout.Space(5);

            #region Bounds Mode Selection
            EditorGUILayout.BeginHorizontal();
            {
                // Object size label
                EditorGUILayout.LabelField("Bounds", GUILayout.MaxWidth(EditorGUIUtility.currentViewWidth * 0.18f));

                int selectedBoundsMode = GUILayout.SelectionGrid((int)_prefs.boundsMode, _selectableBoundsModes, _selectableBoundsModes.Length);
                if ((int)_prefs.boundsMode != selectedBoundsMode)
                {
                    Undo.RecordObject(_prefs, "Bounds Mode Changed");
                    _prefs.boundsMode = (RealSizePrefs.BoundsMode)selectedBoundsMode;
                    _isDirty = true;
                    EditorUtility.SetDirty(_prefs);
                }
            }
            EditorGUILayout.EndHorizontal();
            #endregion Bounds Mode Selection

            #region Precision Mode Selection
            EditorGUILayout.BeginHorizontal();
            {
                // Object size label
                EditorGUILayout.LabelField("Precision", GUILayout.MaxWidth(EditorGUIUtility.currentViewWidth * 0.18f));

                int selectedPrecisionMode = GUILayout.SelectionGrid((int)_prefs.precisionMode, _selectablePrecisionModes, _selectablePrecisionModes.Length);
                if ((int)_prefs.precisionMode != selectedPrecisionMode)
                {
                    Undo.RecordObject(_prefs, "Precision Mode Changed");
                    _prefs.precisionMode = (RealSizePrefs.PrecisionMode)selectedPrecisionMode;
                    _showedPreciseModeWarning = false;
                    _isDirty = true;
                    EditorUtility.SetDirty(_prefs);
                }
            }
            EditorGUILayout.EndHorizontal();
            #endregion Precision Mode Selection

            GUILayout.Space(5);
            GUILayout.BeginHorizontal();
            {
                #region Include Children Toggle
                GUIContent isolateSelectedMeshLabel = new GUIContent()
                {
                    text = "Include Children",
                    tooltip = _prefs.includeChildren ? "Include Children (On)" : "Include Children (Off)"
                };

                bool isolateSelectedMeshToggleValue = GUILayout.Toggle(_prefs.includeChildren, isolateSelectedMeshLabel, "button");
                if (isolateSelectedMeshToggleValue != _prefs.includeChildren)
                {
                    Undo.RecordObject(_prefs, "Toggle Isolate Selected Mesh");
                    _prefs.includeChildren = isolateSelectedMeshToggleValue;
                    _isDirty = true;
                    EditorUtility.SetDirty(_prefs);
                }
                #endregion Include Children Toggle


                #region Ignore Non-Mesh Children Toggle
                GUIContent ignoreNonMeshChildrenToggleLabel = new GUIContent()
                {
                    text = "Ignore Non-Mesh Children",
                    tooltip = _prefs.ignoreNonMeshChildren ? "Ignore Non-Mesh Children (On)" : "Ignore Non-Mesh Children (Off)"
                };


                bool ignoreNonMeshValue = GUILayout.Toggle(_prefs.ignoreNonMeshChildren, ignoreNonMeshChildrenToggleLabel, "button");
                if (ignoreNonMeshValue != _prefs.ignoreNonMeshChildren)
                {
                    Undo.RecordObject(_prefs, "Toggle Ignore Non-Mesh Children");
                    _prefs.ignoreNonMeshChildren = ignoreNonMeshValue;
                    _isDirty = true;
                    EditorUtility.SetDirty(_prefs);
                }
                #endregion Ignore Non-Mesh Children Toggle
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(5);
            GUILayout.BeginHorizontal();
            {
                #region Draw Bounds Toggle
                GUIContent drawBoundsLabel = new GUIContent()
                {
                    text = "Draw Size Bounds",
                    tooltip = _prefs.drawBoundsInSceneView ? "Draw Size Bounds (On)" : "Draw Size Bounds (Off)"
                };

                bool drawBoundsToggleValue = GUILayout.Toggle(_prefs.drawBoundsInSceneView, drawBoundsLabel, "button");
                if (drawBoundsToggleValue != _prefs.drawBoundsInSceneView)
                {
                    Undo.RecordObject(_prefs, "Toggle Draw Size Bounds");
                    _prefs.drawBoundsInSceneView = drawBoundsToggleValue;
                    _isDirty = true;
                    EditorUtility.SetDirty(_prefs);
                }
                #endregion Draw Bounds Toggle


                #region Draw Size Labels Toggle
                GUIContent drawSizeLabelsToggleLabel = new GUIContent()
                {
                    text = "Draw Size Labels",
                    tooltip = _prefs.drawSizeLabelsInSceneView ? "Draw Size Labels (On)" : "Draw Size Labels (Off)"
                };

                bool drawSizeLabelsValue = GUILayout.Toggle(_prefs.drawSizeLabelsInSceneView, drawSizeLabelsToggleLabel, "button");
                if (drawSizeLabelsValue != _prefs.drawSizeLabelsInSceneView)
                {
                    Undo.RecordObject(_prefs, "Toggle Draw Size Labels");
                    _prefs.drawSizeLabelsInSceneView = drawSizeLabelsValue;
                    _isDirty = true;
                    EditorUtility.SetDirty(_prefs);
                }
                #endregion Draw Size Labels Toggle
            }
            GUILayout.EndHorizontal();
        }



        public override void OnInspectorGUI()
        {
            _isDirty = false;
            #region Base Transform Inspector
            EditorGUI.BeginChangeCheck();
            _baseTransformEditor.OnInspectorGUI();
            if (EditorGUI.EndChangeCheck())
            {
                // Base editor has changed
                _isDirty = true;
            }
            #endregion Base Transform Inspector

            if(targets.Length > 1)
            {
                return;
            }

            if (_prefs.showTools)
            {
                DrawToolSection();
            }

            #region Show Real Size Button And Preferences
            GUILayout.Space(5);
            string showToolsLabel = _prefs.showTools ? "Hide Real Size" : "Show Real Size";
            EditorGUI.BeginChangeCheck();
            {
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (_prefs.showTools)
                {
                    // Preferences
                    if (GUILayout.Button("Preferences", GUILayout.MaxWidth(100f)))
                    {
                        Selection.activeObject = RealSizePrefs.Instance;
                    }
                }
                if (GUILayout.Button(showToolsLabel, GUILayout.MaxWidth(120f)))
                {
                    Undo.RecordObject(_prefs, "Toggle Show Real Size");
                    _prefs.showTools = !_prefs.showTools;
                    if (_prefs.showTools)
                    {
                        _isDirty = true;
                    }
                    EditorUtility.SetDirty(_prefs);
                }
                GUILayout.EndHorizontal();
            }
            if (EditorGUI.EndChangeCheck())
            {
                // Show Real Size button changed, force a repaint
                SceneView.RepaintAll();
            }

            if (!_prefs.showTools)
            {
                // Early return to disable the tool when not in use
                return;
            }
            #endregion Show Real Size Button And Preferences


            if (_isDirty)
            {
                CalculateBounds();
                CalculateBoundsUnitSize();
                SceneView.RepaintAll();
            }

            _previousPosition = _transform.position;
            _previousRotation = _transform.rotation.eulerAngles;
            _previousScale = _transform.localScale;
        }

        private void CalculateBoundsUnitSize()
        {
            if (!_transform || !_prefs || (_currentBounds != null && _currentBounds.size == Vector3.zero))
            {
                _currentUnitSize = Vector3.zero;
                return;
            }

            Transform transform = _transform;
            Bounds bounds = _currentBounds;

            Vector3 size = Vector3.zero;

            switch (_prefs.boundsMode)
            {
                case RealSizePrefs.BoundsMode.Local:
                    {
                        SizeUnit selectedUnit = _selectableSizeUnits[_prefs.selectedUnitIndex];
                        size.x = bounds.size.x * transform.localScale.x;
                        size.y = bounds.size.y * transform.localScale.y;
                        size.z = bounds.size.z * transform.localScale.z;

                        Transform parent = transform.parent;
                        while (parent)
                        {
                            size = Vector3.Scale(size, parent.localScale);
                            parent = parent.parent;
                        }

                        if (selectedUnit.unitsPerMeter == -1f && selectedUnit.unitName.ToLower() == "unity")
                        {
                            // Don't multiply when using Unity units
                            break;
                        }

                        size = size * _prefs.metersPerUnityUnit * selectedUnit.unitsPerMeter;
                        break;
                    }
                case RealSizePrefs.BoundsMode.World:
                    {
                        SizeUnit selectedUnit = _selectableSizeUnits[_prefs.selectedUnitIndex];
                        size.x = bounds.size.x;
                        size.y = bounds.size.y;
                        size.z = bounds.size.z;

                        if (selectedUnit.unitsPerMeter == -1f && selectedUnit.unitName.ToLower() == "unity")
                        {
                            // Don't multiply when using Unity units
                            break;
                        }

                        size = size * _prefs.metersPerUnityUnit * selectedUnit.unitsPerMeter;
                        break;
                    }
                default:
                    break;
            }

            _currentUnitSize = size;
        }

        Bounds EncapsulateChildBoundsLocalPreciseVertices(Transform t, Bounds bounds)
        {
            int verticesChecked = 0;

            List<Transform> allChildren = new List<Transform>(_transform.GetComponentsInChildren<Transform>());

            MeshFilter[] meshFilters = _transform.GetComponentsInChildren<MeshFilter>();
            for (int i = 0; i < meshFilters.Length; ++i)
            {
                Mesh sharedMesh = meshFilters[i].sharedMesh;
                if(!sharedMesh)
                {
                    continue;
                }

                Vector3[] vertices = sharedMesh.vertices;
                Transform parent = meshFilters[i].transform;
                allChildren.Remove(parent);

                for (int v = 0; v < vertices.Length; ++v)
                {
                    bounds.Encapsulate(_transform.InverseTransformPoint(parent.TransformPoint(vertices[v])));
                    verticesChecked++;
                }
            }

            SkinnedMeshRenderer[] skinnedMeshRenderers = _transform.GetComponentsInChildren<SkinnedMeshRenderer>();
            for (int i = 0; i < skinnedMeshRenderers.Length; i++)
            {
                Mesh baked = new Mesh();
#if UNITY_2020_3_OR_NEWER
                skinnedMeshRenderers[i].BakeMesh(baked, true);
#else
                skinnedMeshRenderers[i].BakeMesh(baked);
#endif
                Vector3[] vertices = baked.vertices;
                Transform parent = skinnedMeshRenderers[i].transform;
                allChildren.Remove(parent);

                for (int v = 0; v < vertices.Length; ++v)
                {
                    bounds.Encapsulate(_transform.InverseTransformPoint(parent.TransformPoint(vertices[v])));
                    verticesChecked++;
                }
            }

            if (!_prefs.ignoreNonMeshChildren)
            {
                allChildren.Remove(_transform);
                for (int i = 0; i < allChildren.Count; i++)
                {
                    bounds.Encapsulate(_transform.InverseTransformPoint(allChildren[i].position));
                }
            }

            //Debug.Log($"Precise Vertices Checked: {verticesChecked}");
            return bounds;
        }

        Bounds EncapsulateChildBoundsLocalImpreciseVertices(Transform t, Bounds bounds, int maxVertices)
        {
            int verticesChecked = 0;
            if (maxVertices == 0)
            {
                Debug.LogError($"Max Vertices cannot be zero!");
                return bounds;
            }

            List<Transform> allChildren = new List<Transform>(_transform.GetComponentsInChildren<Transform>());
            MeshFilter[] meshFilters = _transform.GetComponentsInChildren<MeshFilter>();
            SkinnedMeshRenderer[] skinnedMeshRenderers = _transform.GetComponentsInChildren<SkinnedMeshRenderer>();

            int totalVertices = 0;
            for (int i = 0; i < meshFilters.Length; ++i)
            {
                totalVertices += meshFilters[i].sharedMesh?.vertexCount ?? 0;
            }
            for (int i = 0; i < skinnedMeshRenderers.Length; i++)
            {
                totalVertices += skinnedMeshRenderers[i].sharedMesh?.vertexCount ?? 0;
            }

            float vertexOffset = (float)totalVertices / maxVertices;
            if (vertexOffset < 1f)
            {
                vertexOffset = 1f;
            }


            for (int i = 0; i < meshFilters.Length; ++i)
            {
                Vector3[] vertices = meshFilters[i].sharedMesh?.vertices;
                if (vertices == null) 
                {
                    continue; 
                }

                Transform parent = meshFilters[i].transform;
                allChildren.Remove(parent);

                for (float v = 0; v < vertices.Length; v += vertexOffset)
                {
                    int vertex = Mathf.FloorToInt(v);
                    bounds.Encapsulate(_transform.InverseTransformPoint(parent.TransformPoint(vertices[vertex])));
                    verticesChecked++;
                }
            }

            for (int i = 0; i < skinnedMeshRenderers.Length; ++i)
            {
                Mesh baked = new Mesh();
#if UNITY_2020_3_OR_NEWER
                skinnedMeshRenderers[i].BakeMesh(baked, true);
#else
                skinnedMeshRenderers[i].BakeMesh(baked);
#endif
                Vector3[] vertices = baked.vertices;
                Transform parent = skinnedMeshRenderers[i].transform;
                allChildren.Remove(parent);

                for (float v = 0; v < vertices.Length; v += vertexOffset)
                {
                    int vertex = Mathf.FloorToInt(v);
                    bounds.Encapsulate(_transform.InverseTransformPoint(parent.TransformPoint(vertices[vertex])));
                    verticesChecked++;
                }
            }

            if (!_prefs.ignoreNonMeshChildren)
            {
                allChildren.Remove(_transform);
                for (int i = 0; i < allChildren.Count; i++)
                {
                    bounds.Encapsulate(_transform.InverseTransformPoint(allChildren[i].position));
                }
            }

            //Debug.Log($"Imprecise Vertices checked: {verticesChecked}");
            return bounds;
        }

        Bounds EncapsulateChildBoundsLocalImpreciseLocalBounds(Transform t, Bounds bounds)
        {
            List<Transform> allChildren = new List<Transform>(_transform.GetComponentsInChildren<Transform>());
            MeshFilter[] meshFilters = _transform.GetComponentsInChildren<MeshFilter>();
            for (int i = 0; i < meshFilters.Length; ++i)
            {
                Transform parent = meshFilters[i].transform;
                allChildren.Remove(parent);
                if (TryGetLocalMeshBounds(parent, out Bounds filterBounds))
                {
                    bounds.Encapsulate(_transform.InverseTransformPoint(parent.TransformPoint(new Vector3(filterBounds.min.x, filterBounds.min.y, filterBounds.min.z))));
                    bounds.Encapsulate(_transform.InverseTransformPoint(parent.TransformPoint(new Vector3(filterBounds.min.x, filterBounds.min.y, filterBounds.max.z))));
                    bounds.Encapsulate(_transform.InverseTransformPoint(parent.TransformPoint(new Vector3(filterBounds.max.x, filterBounds.min.y, filterBounds.min.z))));
                    bounds.Encapsulate(_transform.InverseTransformPoint(parent.TransformPoint(new Vector3(filterBounds.max.x, filterBounds.min.y, filterBounds.max.z))));
                    bounds.Encapsulate(_transform.InverseTransformPoint(parent.TransformPoint(new Vector3(filterBounds.min.x, filterBounds.max.y, filterBounds.min.z))));
                    bounds.Encapsulate(_transform.InverseTransformPoint(parent.TransformPoint(new Vector3(filterBounds.min.x, filterBounds.max.y, filterBounds.max.z))));
                    bounds.Encapsulate(_transform.InverseTransformPoint(parent.TransformPoint(new Vector3(filterBounds.max.x, filterBounds.max.y, filterBounds.min.z))));
                    bounds.Encapsulate(_transform.InverseTransformPoint(parent.TransformPoint(new Vector3(filterBounds.max.x, filterBounds.max.y, filterBounds.max.z))));
                }
            }

            SkinnedMeshRenderer[] skinnedMeshRenderers = _transform.GetComponentsInChildren<SkinnedMeshRenderer>();
            for (int i = 0; i < skinnedMeshRenderers.Length; ++i)
            {
                Transform parent = skinnedMeshRenderers[i].transform;
                allChildren.Remove(parent);
                if (TryGetLocalMeshBounds(parent, out Bounds filterBounds))
                {
                    bounds.Encapsulate(_transform.InverseTransformPoint(parent.TransformPoint(new Vector3(filterBounds.min.x, filterBounds.min.y, filterBounds.min.z))));
                    bounds.Encapsulate(_transform.InverseTransformPoint(parent.TransformPoint(new Vector3(filterBounds.min.x, filterBounds.min.y, filterBounds.max.z))));
                    bounds.Encapsulate(_transform.InverseTransformPoint(parent.TransformPoint(new Vector3(filterBounds.max.x, filterBounds.min.y, filterBounds.min.z))));
                    bounds.Encapsulate(_transform.InverseTransformPoint(parent.TransformPoint(new Vector3(filterBounds.max.x, filterBounds.min.y, filterBounds.max.z))));
                    bounds.Encapsulate(_transform.InverseTransformPoint(parent.TransformPoint(new Vector3(filterBounds.min.x, filterBounds.max.y, filterBounds.min.z))));
                    bounds.Encapsulate(_transform.InverseTransformPoint(parent.TransformPoint(new Vector3(filterBounds.min.x, filterBounds.max.y, filterBounds.max.z))));
                    bounds.Encapsulate(_transform.InverseTransformPoint(parent.TransformPoint(new Vector3(filterBounds.max.x, filterBounds.max.y, filterBounds.min.z))));
                    bounds.Encapsulate(_transform.InverseTransformPoint(parent.TransformPoint(new Vector3(filterBounds.max.x, filterBounds.max.y, filterBounds.max.z))));
                }
            }

            if (!_prefs.ignoreNonMeshChildren)
            {
                allChildren.Remove(_transform);
                for (int i = 0; i < allChildren.Count; i++)
                {
                    bounds.Encapsulate(_transform.InverseTransformPoint(allChildren[i].position));
                }
            }


            return bounds;
        }

        Bounds EncapsulateChildBoundsWorldPreciseVertices(Transform t, Bounds bounds)
        {
            int verticesChecked = 0;

            List<Transform> allChildren = new List<Transform>(_transform.GetComponentsInChildren<Transform>());

            MeshFilter[] meshFilters = _transform.GetComponentsInChildren<MeshFilter>();
            for (int i = 0; i < meshFilters.Length; ++i)
            {
                Vector3[] vertices = meshFilters[i].sharedMesh?.vertices;
                if (vertices == null)
                {
                    continue;
                }

                Transform parent = meshFilters[i].transform;
                allChildren.Remove(parent);

                for (int v = 0; v < vertices.Length; ++v)
                {
                    bounds.Encapsulate(parent.TransformPoint(vertices[v]));
                    verticesChecked++;
                }
            }

            SkinnedMeshRenderer[] skinnedMeshRenderers = _transform.GetComponentsInChildren<SkinnedMeshRenderer>();
            for (int i = 0; i < skinnedMeshRenderers.Length; i++)
            {
                Mesh baked = new Mesh();
#if UNITY_2020_3_OR_NEWER
                skinnedMeshRenderers[i].BakeMesh(baked, true);
#else
                skinnedMeshRenderers[i].BakeMesh(baked);
#endif
                Vector3[] vertices = baked.vertices;
                Transform parent = skinnedMeshRenderers[i].transform;
                allChildren.Remove(parent);

                for (int v = 0; v < vertices.Length; ++v)
                {
                    bounds.Encapsulate(parent.TransformPoint(vertices[v]));
                    verticesChecked++;
                }
            }

            if (!_prefs.ignoreNonMeshChildren)
            {
                allChildren.Remove(_transform);
                for (int i = 0; i < allChildren.Count; i++)
                {
                    bounds.Encapsulate(allChildren[i].position);
                }
            }

            //Debug.Log($"Precise Vertices Checked: {verticesChecked}");
            return bounds;
        }

        Bounds EncapsulateChildBoundsWorldImpreciseVertices(Transform t, Bounds bounds, int maxVertices)
        {
            int verticesChecked = 0;
            if (maxVertices == 0)
            {
                Debug.LogError($"Max Vertices cannot be zero. Unable to calculate bounds.");
                return bounds;
            }

            List<Transform> allChildren = new List<Transform>(_transform.GetComponentsInChildren<Transform>());
            MeshFilter[] meshFilters = _transform.GetComponentsInChildren<MeshFilter>();
            SkinnedMeshRenderer[] skinnedMeshRenderers = _transform.GetComponentsInChildren<SkinnedMeshRenderer>();

            int totalVertices = 0;
            for (int i = 0; i < meshFilters.Length; ++i)
            {
                totalVertices += meshFilters[i].sharedMesh?.vertexCount ?? 0;
            }
            for (int i = 0; i < skinnedMeshRenderers.Length; i++)
            {
                totalVertices += skinnedMeshRenderers[i].sharedMesh?.vertexCount ?? 0;
            }

            float vertexOffset = (float)totalVertices / maxVertices;
            if (vertexOffset < 1f)
            {
                vertexOffset = 1f;
            }


            for (int i = 0; i < meshFilters.Length; ++i)
            {
                Vector3[] vertices = meshFilters[i].sharedMesh?.vertices;
                if (vertices == null)
                {
                    continue;
                }

                Transform parent = meshFilters[i].transform;
                allChildren.Remove(parent);

                for (float v = 0; v < vertices.Length; v += vertexOffset)
                {
                    int vertex = Mathf.FloorToInt(v);
                    bounds.Encapsulate(parent.TransformPoint(vertices[vertex]));
                    verticesChecked++;
                }
            }

            for (int i = 0; i < skinnedMeshRenderers.Length; ++i)
            {
                Mesh baked = new Mesh();
#if UNITY_2020_3_OR_NEWER
                skinnedMeshRenderers[i].BakeMesh(baked, true);
#else
                skinnedMeshRenderers[i].BakeMesh(baked);
#endif
                Vector3[] vertices = baked.vertices;
                Transform parent = skinnedMeshRenderers[i].transform;
                allChildren.Remove(parent);

                for (float v = 0; v < vertices.Length; v += vertexOffset)
                {
                    int vertex = Mathf.FloorToInt(v);
                    bounds.Encapsulate(parent.TransformPoint(vertices[vertex]));
                    verticesChecked++;
                }
            }

            if (!_prefs.ignoreNonMeshChildren)
            {
                allChildren.Remove(_transform);
                for (int i = 0; i < allChildren.Count; i++)
                {
                    bounds.Encapsulate(allChildren[i].position);
                }
            }

            //Debug.Log($"Imprecise Vertices checked: {verticesChecked}");
            return bounds;
        }

        Bounds EncapsulateChildBoundsWorldImpreciseLocalBounds(Transform t, Bounds bounds)
        {
            List<Transform> allChildren = new List<Transform>(_transform.GetComponentsInChildren<Transform>());
            MeshFilter[] meshFilters = _transform.GetComponentsInChildren<MeshFilter>();
            for (int i = 0; i < meshFilters.Length; ++i)
            {
                Transform parent = meshFilters[i].transform;
                allChildren.Remove(parent);
                if (TryGetLocalMeshBounds(parent, out Bounds filterBounds))
                {
                    bounds.Encapsulate(parent.TransformPoint(new Vector3(filterBounds.min.x, filterBounds.min.y, filterBounds.min.z)));
                    bounds.Encapsulate(parent.TransformPoint(new Vector3(filterBounds.min.x, filterBounds.min.y, filterBounds.max.z)));
                    bounds.Encapsulate(parent.TransformPoint(new Vector3(filterBounds.max.x, filterBounds.min.y, filterBounds.min.z)));
                    bounds.Encapsulate(parent.TransformPoint(new Vector3(filterBounds.max.x, filterBounds.min.y, filterBounds.max.z)));
                    bounds.Encapsulate(parent.TransformPoint(new Vector3(filterBounds.min.x, filterBounds.max.y, filterBounds.min.z)));
                    bounds.Encapsulate(parent.TransformPoint(new Vector3(filterBounds.min.x, filterBounds.max.y, filterBounds.max.z)));
                    bounds.Encapsulate(parent.TransformPoint(new Vector3(filterBounds.max.x, filterBounds.max.y, filterBounds.min.z)));
                    bounds.Encapsulate(parent.TransformPoint(new Vector3(filterBounds.max.x, filterBounds.max.y, filterBounds.max.z)));
                }
            }

            SkinnedMeshRenderer[] skinnedMeshRenderers = _transform.GetComponentsInChildren<SkinnedMeshRenderer>();
            for (int i = 0; i < skinnedMeshRenderers.Length; ++i)
            {
                Transform parent = skinnedMeshRenderers[i].transform;
                allChildren.Remove(parent);
                if (TryGetLocalMeshBounds(parent, out Bounds filterBounds))
                {
                    bounds.Encapsulate(parent.TransformPoint(new Vector3(filterBounds.min.x, filterBounds.min.y, filterBounds.min.z)));
                    bounds.Encapsulate(parent.TransformPoint(new Vector3(filterBounds.min.x, filterBounds.min.y, filterBounds.max.z)));
                    bounds.Encapsulate(parent.TransformPoint(new Vector3(filterBounds.max.x, filterBounds.min.y, filterBounds.min.z)));
                    bounds.Encapsulate(parent.TransformPoint(new Vector3(filterBounds.max.x, filterBounds.min.y, filterBounds.max.z)));
                    bounds.Encapsulate(parent.TransformPoint(new Vector3(filterBounds.min.x, filterBounds.max.y, filterBounds.min.z)));
                    bounds.Encapsulate(parent.TransformPoint(new Vector3(filterBounds.min.x, filterBounds.max.y, filterBounds.max.z)));
                    bounds.Encapsulate(parent.TransformPoint(new Vector3(filterBounds.max.x, filterBounds.max.y, filterBounds.min.z)));
                    bounds.Encapsulate(parent.TransformPoint(new Vector3(filterBounds.max.x, filterBounds.max.y, filterBounds.max.z)));
                }
            }

            if (!_prefs.ignoreNonMeshChildren)
            {
                allChildren.Remove(_transform);
                for (int i = 0; i < allChildren.Count; i++)
                {
                    bounds.Encapsulate(allChildren[i].position);
                }
            }


            return bounds;
        }

        bool TryGetLocalMeshBounds(Transform t, out Bounds localBounds)
        {
            localBounds = new Bounds(Vector3.zero, Vector3.zero);

            MeshFilter meshFilter = t.GetComponent<MeshFilter>();
            if (meshFilter && meshFilter.sharedMesh)
            {
                localBounds = meshFilter.sharedMesh.bounds;
                return true;
            }

            SkinnedMeshRenderer skinnedMeshRenderer = t.GetComponent<SkinnedMeshRenderer>();
            if (skinnedMeshRenderer)
            {
                Mesh baked = new Mesh();
#if UNITY_2020_3_OR_NEWER
                skinnedMeshRenderer.BakeMesh(baked, true);
#else
                skinnedMeshRenderer.BakeMesh(baked);
#endif
                baked.RecalculateBounds();
                localBounds = baked.bounds;
                return true;
            }

            if (!_prefs.ignoreNonMeshChildren)
            {
                return true;
            }

            return false;
        }

        private bool TransformHasMesh(Transform t)
        {
            bool hasMesh = false;
            if (_transform.TryGetComponent<MeshRenderer>(out MeshRenderer r))
            {
                hasMesh = true;
            }
            if (_transform.TryGetComponent<SkinnedMeshRenderer>(out SkinnedMeshRenderer s))
            {
                hasMesh = true;
            }
            return hasMesh;
        }

        private void CalculateBoundsLocal()
        {
            Bounds originBounds = new Bounds(Vector3.zero, Vector3.zero);

            if (TryGetLocalMeshBounds(_transform, out Bounds localBounds))
            {
                originBounds = localBounds;
            }


            if (_prefs.includeChildren)
            {
                switch (_prefs.precisionMode)
                {
                    case RealSizePrefs.PrecisionMode.Precise:
                        {
                            _calculateBoundsStopwatch.Reset();
                            _calculateBoundsStopwatch.Start();

                            // Precise
                            originBounds = EncapsulateChildBoundsLocalPreciseVertices(_transform, originBounds);

                            _calculateBoundsStopwatch.Stop();
                            if (_calculateBoundsStopwatch.ElapsedMilliseconds > 20f)
                            {
                                if (!_showedPreciseModeWarning && _prefs.showPerformanceWarningsInConsole)
                                {
                                    Debug.LogWarning($"Real Size is processing many vertices. If performance is suffering, try changing Precision mode to \"Approximate\" and adjusting max verticies in Preferences. Vertex Processing Time Elapsed: {_calculateBoundsStopwatch.ElapsedMilliseconds} ms. You can disable these warnings in Preferences.");
                                    _showedPreciseModeWarning = true;
                                }
                            }
                            break;
                        }
                    case RealSizePrefs.PrecisionMode.Approximate:
                        {
                            _calculateBoundsStopwatch.Reset();
                            _calculateBoundsStopwatch.Start();

                            // Approximate
                            originBounds = EncapsulateChildBoundsLocalImpreciseVertices(_transform, originBounds, _prefs.maxVerticesApproximateMode);

                            _calculateBoundsStopwatch.Stop();
                            if (_calculateBoundsStopwatch.ElapsedMilliseconds > 20f)
                            {
                                if (!_showedPreciseModeWarning && _prefs.showPerformanceWarningsInConsole)
                                {
                                    Debug.LogWarning($"Real Size is processing many vertices. If performance is suffering, try reducing the amount of vertices evaluated in Preferences. Vertex Processing Time Elapsed: {_calculateBoundsStopwatch.ElapsedMilliseconds} ms. You can disable these warnings in Preferences.", _prefs);
                                    _showedPreciseModeWarning = true;
                                }
                            }
                            break;
                        }
                    case RealSizePrefs.PrecisionMode.Bounds:
                        {
                            // Use Bounds
                            originBounds = EncapsulateChildBoundsLocalImpreciseLocalBounds(_transform, originBounds);
                            break;
                        }
                    default:
                        {
                            Debug.LogError($"Precision mode not defined: {_prefs.precisionMode}");
                            break;
                        }
                }
            }

            _currentBounds = originBounds;
        }

        private void CalculateBoundsWorld()
        {
            Bounds originBounds = new Bounds(_transform.position, Vector3.zero);

            if (TryGetWorldspaceBoundsPreciseVertices(_transform, out Bounds worldBounds))
            {
                originBounds = worldBounds;
            }

            if (_prefs.includeChildren)
            {
                switch (_prefs.precisionMode)
                {
                    case RealSizePrefs.PrecisionMode.Precise:
                        {
                            _calculateBoundsStopwatch.Reset();
                            _calculateBoundsStopwatch.Start();


                            // Precise
                            originBounds = EncapsulateChildBoundsWorldPreciseVertices(_transform, originBounds);

                            _calculateBoundsStopwatch.Stop();
                            if (_calculateBoundsStopwatch.ElapsedMilliseconds > 20f)
                            {
                                if (!_showedPreciseModeWarning && _prefs.showPerformanceWarningsInConsole)
                                {
                                    Debug.LogWarning($"Real Size is processing many vertices. If performance is suffering, try changing Precision mode to \"Approximate\" and adjusting max verticies in Preferences. Vertex Processing Time Elapsed: {_calculateBoundsStopwatch.ElapsedMilliseconds} ms. You can disable these warnings in Preferences.");
                                    _showedPreciseModeWarning = true;
                                }
                            }
                            break;
                        }
                    case RealSizePrefs.PrecisionMode.Approximate:
                        {
                            _calculateBoundsStopwatch.Reset();
                            _calculateBoundsStopwatch.Start();

                            // Approximate
                            originBounds = EncapsulateChildBoundsWorldImpreciseVertices(_transform, originBounds, _prefs.maxVerticesApproximateMode);

                            _calculateBoundsStopwatch.Stop();
                            if (_calculateBoundsStopwatch.ElapsedMilliseconds > 20f)
                            {
                                if (!_showedPreciseModeWarning && _prefs.showPerformanceWarningsInConsole)
                                {
                                    Debug.LogWarning($"Real Size is processing many vertices. If performance is suffering, try reducing the amount of vertices evaluated in Preferences. Vertex Processing Time Elapsed: {_calculateBoundsStopwatch.ElapsedMilliseconds} ms. You can disable these warnings in Preferences.", _prefs);
                                    _showedPreciseModeWarning = true;
                                }
                            }
                            break;
                        }
                    case RealSizePrefs.PrecisionMode.Bounds:
                        {
                            // Use Bounds
                            originBounds = EncapsulateChildBoundsWorldImpreciseLocalBounds(_transform, originBounds);
                            break;
                        }
                    default:
                        {
                            Debug.LogError($"Precision mode not defined: {_prefs.precisionMode}");
                            break;
                        }
                }
            }

            _currentBounds = originBounds;
        }

        private void CalculateBounds()
        {
            if (!_transform || !_prefs)
            {
                _currentBounds = new Bounds(Vector3.zero, Vector3.zero);
                return;
            }

            if (_prefs.ignoreNonMeshChildren && !_prefs.includeChildren)
            {
                if (!TransformHasMesh(_transform))
                {
                    // No mesh found on selected object while ignorning non-mesh
                    _currentBounds = new Bounds(Vector3.zero, Vector3.zero);
                    return;
                }
            }

            switch (_prefs.boundsMode)
            {
                case RealSizePrefs.BoundsMode.Local:
                    {
                        CalculateBoundsLocal();
                        break;
                    }
                case RealSizePrefs.BoundsMode.World:
                    {
                        CalculateBoundsWorld();
                        break;
                    }
                default:
                    {
                        break;
                    }
            }
        }


        protected virtual void OnSceneGUI()
        {
            if (!_transform || !_prefs || !_prefs.showTools)
            {
                return;
            }

            if (_currentBounds.size != Vector3.zero)
            {
                DrawBounds(_currentBounds);
            }
        }

        bool TryGetWorldspaceBoundsPreciseVertices(Transform t, out Bounds bounds)
        {
            bounds = new Bounds(t.transform.position, Vector3.zero);

            MeshFilter filter = t.GetComponent<MeshFilter>();
            if (!filter || !filter.sharedMesh)
            {
                if (_prefs.ignoreNonMeshChildren)
                {
                    return false;
                }
                else
                {
                    return true;
                }
            }

            Vector3[] vertices = filter.sharedMesh.vertices;

            // TransformPoint converts the local mesh vertices dependent on the transform
            // position, scale and orientation into a global position
            var min = t.TransformPoint(vertices[0]);
            var max = min;

            // Iterate through all vertices
            // except first one
            for (var i = 1; i < vertices.Length; i++)
            {
                var V = t.TransformPoint(vertices[i]);

                // Go through X,Y and Z of the Vector3
                for (var n = 0; n < 3; n++)
                {
                    max[n] = Mathf.Max(V[n], max[n]);
                    min[n] = Mathf.Min(V[n], min[n]);
                }
            }

            bounds.SetMinMax(min, max);
            return true;
        }

        void DrawBounds(Bounds b)
        {
            if (!_transform || (!_prefs.drawBoundsInSceneView && !_prefs.drawSizeLabelsInSceneView))
            {
                return;
            }

            Vector3 pointLeftBottomClose;
            Vector3 pointRightBottomClose;
            Vector3 pointRightBottomFar;
            Vector3 pointLeftBottomFar;
            Vector3 pointLeftTopClose;
            Vector3 pointRightTopClose;
            Vector3 pointRightTopFar;
            Vector3 pointLeftTopFar;

            switch (_prefs.boundsMode)
            {
                case RealSizePrefs.BoundsMode.Local:
                    {
                        // bottom
                        pointLeftBottomClose = _transform.TransformPoint(new Vector3(b.min.x, b.min.y, b.min.z));
                        pointRightBottomClose = _transform.TransformPoint(new Vector3(b.max.x, b.min.y, b.min.z));
                        pointRightBottomFar = _transform.TransformPoint(new Vector3(b.max.x, b.min.y, b.max.z));
                        pointLeftBottomFar = _transform.TransformPoint(new Vector3(b.min.x, b.min.y, b.max.z));

                        // top
                        pointLeftTopClose = _transform.TransformPoint(new Vector3(b.min.x, b.max.y, b.min.z));
                        pointRightTopClose = _transform.TransformPoint(new Vector3(b.max.x, b.max.y, b.min.z));
                        pointRightTopFar = _transform.TransformPoint(new Vector3(b.max.x, b.max.y, b.max.z));
                        pointLeftTopFar = _transform.TransformPoint(new Vector3(b.min.x, b.max.y, b.max.z));
                        break;
                    }
                case RealSizePrefs.BoundsMode.World:
                    {
                        // bottom
                        pointLeftBottomClose = new Vector3(b.min.x, b.min.y, b.min.z);
                        pointRightBottomClose = new Vector3(b.max.x, b.min.y, b.min.z);
                        pointRightBottomFar = new Vector3(b.max.x, b.min.y, b.max.z);
                        pointLeftBottomFar = new Vector3(b.min.x, b.min.y, b.max.z);

                        // top
                        pointLeftTopClose = new Vector3(b.min.x, b.max.y, b.min.z);
                        pointRightTopClose = new Vector3(b.max.x, b.max.y, b.min.z);
                        pointRightTopFar = new Vector3(b.max.x, b.max.y, b.max.z);
                        pointLeftTopFar = new Vector3(b.min.x, b.max.y, b.max.z);
                        break;
                    }
                default:
                    {
                        pointLeftBottomClose = Vector3.zero;
                        pointRightBottomClose = Vector3.zero;
                        pointRightBottomFar = Vector3.zero;
                        pointLeftBottomFar = Vector3.zero;
                        pointLeftTopClose = Vector3.zero;
                        pointRightTopClose = Vector3.zero;
                        pointRightTopFar = Vector3.zero;
                        pointLeftTopFar = Vector3.zero;
                        break;
                    }
            }


            if (_prefs.drawBoundsInSceneView)
            {
                Vector3 lineThickness = _prefs.boundsLineThickness;

                if (lineThickness.x > 0f)
                {
                    Handles.color = _prefs.boundsColorX;
#if UNITY_2020_3_OR_NEWER
                    Handles.DrawLine(pointLeftTopClose, pointRightTopClose, lineThickness.x);
                    Handles.DrawLine(pointLeftBottomClose, pointRightBottomClose, lineThickness.x);
                    Handles.DrawLine(pointRightTopFar, pointLeftTopFar, lineThickness.x);
                    Handles.DrawLine(pointRightBottomFar, pointLeftBottomFar, lineThickness.x);
#else
                    Handles.DrawLine(pointLeftTopClose, pointRightTopClose);
                    Handles.DrawLine(pointLeftBottomClose, pointRightBottomClose);
                    Handles.DrawLine(pointRightTopFar, pointLeftTopFar);
                    Handles.DrawLine(pointRightBottomFar, pointLeftBottomFar);
#endif

                }

                if (lineThickness.y > 0f)
                {
                    Handles.color = _prefs.boundsColorY;

#if UNITY_2020_3_OR_NEWER
                    Handles.DrawLine(pointLeftBottomClose, pointLeftTopClose, lineThickness.y);
                    Handles.DrawLine(pointRightBottomClose, pointRightTopClose, lineThickness.y);
                    Handles.DrawLine(pointRightBottomFar, pointRightTopFar, lineThickness.y);
                    Handles.DrawLine(pointLeftBottomFar, pointLeftTopFar, lineThickness.y);
#else
                    Handles.DrawLine(pointLeftBottomClose, pointLeftTopClose);
                    Handles.DrawLine(pointRightBottomClose, pointRightTopClose);
                    Handles.DrawLine(pointRightBottomFar, pointRightTopFar);
                    Handles.DrawLine(pointLeftBottomFar, pointLeftTopFar);
#endif

                }

                if (lineThickness.z > 0f)
                {
                    Handles.color = _prefs.boundsColorZ;

#if UNITY_2020_3_OR_NEWER
                    Handles.DrawLine(pointRightTopClose, pointRightTopFar, lineThickness.z);
                    Handles.DrawLine(pointRightBottomClose, pointRightBottomFar, lineThickness.z);
                    Handles.DrawLine(pointLeftTopFar, pointLeftTopClose, lineThickness.z);
                    Handles.DrawLine(pointLeftBottomFar, pointLeftBottomClose, lineThickness.z);
#else
                    Handles.DrawLine(pointRightTopClose, pointRightTopFar);
                    Handles.DrawLine(pointRightBottomClose, pointRightBottomFar);
                    Handles.DrawLine(pointLeftTopFar, pointLeftTopClose);
                    Handles.DrawLine(pointLeftBottomFar, pointLeftBottomClose);
#endif
                }
            }

            if (_prefs.drawSizeLabelsInSceneView)
            {
                GUIStyle labelStyle = new GUIStyle(GUI.skin.label);
                labelStyle.normal.textColor = _prefs.sizeLabelBackgroundColorX;
                labelStyle.alignment = TextAnchor.MiddleCenter;
                labelStyle.fontSize = _prefs.labelFontSize;
                labelStyle.fontStyle = FontStyle.Bold;

#if UNITY_2021_3_OR_NEWER
                labelStyle.padding = new RectOffset(3, 3, 2, 2);
#else
                labelStyle.padding = new RectOffset(1, 1, 0, 0);
#endif

                // Size unit
                string unitDisplay = "";
                if (_prefs.displaySizeUnit)
                {
                    unitDisplay = !_selectableSizeUnits[_prefs.selectedUnitIndex].Equals(SizeUnit.unity) 
                        ? $" {_selectableSizeUnits[_prefs.selectedUnitIndex].unitName}" 
                        : " unity";
                }


                // X
                if (_prefs.drawSizeLabelX)
                {
                    labelStyle.normal.background = _texLabelBackgroundX;
                    labelStyle.normal.textColor = _prefs.sizeLabelTextColorX;
                    Handles.Label((pointLeftBottomClose + pointRightBottomClose) / 2f, _currentUnitSize.x.ToString(_prefs.roundSizeLabel ? "0.##" : null) + unitDisplay, labelStyle);
                    Handles.Label((pointRightTopFar + pointLeftTopFar) / 2f, _currentUnitSize.x.ToString(_prefs.roundSizeLabel ? "0.##" : null) + unitDisplay, labelStyle);
                    Handles.Label((pointRightBottomFar + pointLeftBottomFar) / 2f, _currentUnitSize.x.ToString(_prefs.roundSizeLabel ? "0.##" : null) + unitDisplay, labelStyle);
                    Handles.Label((pointLeftTopClose + pointRightTopClose) / 2f, _currentUnitSize.x.ToString(_prefs.roundSizeLabel ? "0.##" : null) + unitDisplay, labelStyle);
                }

                // Y
                if (_prefs.drawSizeLabelY)
                {
                    labelStyle.normal.background = _texLabelBackgroundY;
                    labelStyle.normal.textColor = _prefs.sizeLabelTextColorY;
                    Handles.Label((pointLeftBottomClose + pointLeftTopClose) / 2f, _currentUnitSize.y.ToString(_prefs.roundSizeLabel ? "0.##" : null) + unitDisplay, labelStyle);
                    Handles.Label((pointRightBottomClose + pointRightTopClose) / 2f, _currentUnitSize.y.ToString(_prefs.roundSizeLabel ? "0.##" : null) + unitDisplay, labelStyle);
                    Handles.Label((pointRightBottomFar + pointRightTopFar) / 2f, _currentUnitSize.y.ToString(_prefs.roundSizeLabel ? "0.##" : null) + unitDisplay, labelStyle);
                    Handles.Label((pointLeftBottomFar + pointLeftTopFar) / 2f, _currentUnitSize.y.ToString(_prefs.roundSizeLabel ? "0.##" : null) + unitDisplay, labelStyle);
                }

                // Z
                if (_prefs.drawSizeLabelZ)
                {
                    labelStyle.normal.background = _texLabelBackgroundZ;
                    labelStyle.normal.textColor = _prefs.sizeLabelTextColorZ;
                    Handles.Label((pointRightTopClose + pointRightTopFar) / 2f, _currentUnitSize.z.ToString(_prefs.roundSizeLabel ? "0.##" : null) + unitDisplay, labelStyle);
                    Handles.Label((pointRightBottomClose + pointRightBottomFar) / 2f, _currentUnitSize.z.ToString(_prefs.roundSizeLabel ? "0.##" : null) + unitDisplay, labelStyle);
                    Handles.Label((pointLeftTopFar + pointLeftTopClose) / 2f, _currentUnitSize.z.ToString(_prefs.roundSizeLabel ? "0.##" : null) + unitDisplay, labelStyle);
                    Handles.Label((pointLeftBottomFar + pointLeftBottomClose) / 2f, _currentUnitSize.z.ToString(_prefs.roundSizeLabel ? "0.##" : null) + unitDisplay, labelStyle);
                }
            }
        }
    }

#if UNITY_2019_4_OR_NEWER
#else
    public static class CompatibilityExtensions
    {
        public static bool TryGetComponent<T>(this Transform transform, out T result)
        {
            result = transform.GetComponent<T>();
            if(result != null)
            {
                return true;
            }
                
            return false;
        }
    }
#endif
}
