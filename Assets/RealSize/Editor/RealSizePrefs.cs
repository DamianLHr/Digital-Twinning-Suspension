using UnityEditor;
using UnityEngine;

namespace RealSize
{
    public class RealSizePrefs : ScriptableObject
    {
        private static RealSizePrefs _instance = null;
        public static RealSizePrefs Instance
        {
            get
            {
                if (_instance == null)
                {
                    string[] assets = AssetDatabase.FindAssets($"t:{nameof(RealSizePrefs)}");
                    RealSizePrefs tmp = null;
                    if (assets != null && assets.Length > 0)
                    {
                        string assetPath = AssetDatabase.GUIDToAssetPath(assets[0]);
                        tmp = AssetDatabase.LoadAssetAtPath<RealSizePrefs>(assetPath);
                    }

                    if (tmp)
                    {
                        _instance = tmp;
                        _instance.editorPath = GetEditorFolderPath();
                    }
                    else
                    {
                        _instance = ScriptableObject.CreateInstance<RealSizePrefs>();
                        _instance.editorPath = GetEditorFolderPath();

                        AssetDatabase.CreateAsset(_instance, _instance.editorPath + "/RealSizePreferences.asset");
#if UNITY_2021_2_OR_NEWER
                        AssetDatabase.SaveAssetIfDirty(_instance);
#else
                        AssetDatabase.SaveAssets();
#endif
                    }

                    _instance.LoadTextures();
                }

                return _instance;
            }
        }

        private static string GetEditorFolderPath()
        {
            string[] assets = AssetDatabase.FindAssets($"t:Script {nameof(RealSizePrefs)}");
            if (assets != null && assets.Length > 0)
            {
                string assetPath = System.IO.Path.GetDirectoryName(AssetDatabase.GUIDToAssetPath(assets[0])).Replace('\\', '/');
                return assetPath;
            }

            return "Assets";
        }

        void LoadTextures()
        {
            string[] results = AssetDatabase.FindAssets("t:texture LockLocked", new string[] { $"{editorPath}/Textures" });
            if (results != null && results.Length > 0)
            {
                var assetPath = AssetDatabase.GUIDToAssetPath(results[0]);
                uniformScaleButtonTextureOn = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            }

            results = AssetDatabase.FindAssets("t:texture LockUnlocked", new string[] { $"{editorPath}/Textures" });
            if (results != null && results.Length > 0)
            {
                var assetPath = AssetDatabase.GUIDToAssetPath(results[0]);
                uniformScaleButtonTextureOff = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            }
        }

        [HideInInspector] public string editorPath = "";
        [HideInInspector] public Texture2D uniformScaleButtonTextureOn;
        [HideInInspector] public Texture2D uniformScaleButtonTextureOff;

        [Header("Inspector Properties")]
        public bool showTools = true;
        public bool uniformScaleEnabled = true;
        public bool includeChildren = true;
        public bool ignoreNonMeshChildren = false;

        public BoundsMode boundsMode = BoundsMode.Local;

        [Header("Precision Preferences")]
        public PrecisionMode precisionMode = PrecisionMode.Precise;
        [Tooltip("Maximum vertices to evaluate when Precision Mode is set to \"Approximate\" (Default: 5000)")]
        public int maxVerticesApproximateMode = 5000;
        [Tooltip("If evaluating verticies takes longer than 20ms, log a warning in the console")]
        public bool showPerformanceWarningsInConsole = true;

        [HideInInspector] public int selectedUnitIndex = 0;

        [Header("Custom Unit Preferences")]
        [Tooltip("Meters Per Unity Worldspace Unit (Default: 1)")]
        [Min(0.0001f)]public float metersPerUnityUnit = 1f;

        [Tooltip("Unit Size selections to display per row in the inspector (Default: 4)")]
        [Min(1)]public int sizeUnitsDisplayedPerRow = 4;
        [Tooltip("Hide the default unit selections included with Real Size (Default: false)")]
        public bool onlyShowCustomUnits = false;
        public SizeUnit[] customUnits;

        [Header("Size Bounds Preferences (Scene View)")]
        public bool drawBoundsInSceneView = true;
        [Tooltip("Thickness of the bounds visualization lines (Default: (1,1,1))")]
        [Min(0f)]public Vector3 boundsLineThickness = new Vector3(1f,1f,1f);
        public Color boundsColorX = new Color(1f, 0f, 0f);
        public Color boundsColorY = new Color(0f, 1f, 0f);
        public Color boundsColorZ = new Color(0f, 0f, 1f);

        [Header("Size Label Preferences (Scene View)")]
        public bool drawSizeLabelsInSceneView = true;
        [Tooltip("Round size labels to two decimal places for better readability (Default: true)")]
        public bool roundSizeLabel = true;
        public bool displaySizeUnit = true;
        public int labelFontSize = 11;
        public bool drawSizeLabelX = true;
        public Color sizeLabelTextColorX = Color.white;
        public Color sizeLabelBackgroundColorX = new Color(1f, 0f, 0f, 0.25f);
        public bool drawSizeLabelY = true;
        public Color sizeLabelTextColorY = Color.white;
        public Color sizeLabelBackgroundColorY = new Color(0f, 1f, 0f, 0.25f);
        public bool drawSizeLabelZ = true;
        public Color sizeLabelTextColorZ = Color.white;
        public Color sizeLabelBackgroundColorZ = new Color(0f, 0f, 1f, 0.25f);

        public enum PrecisionMode
        {
            Precise,
            Approximate,
            Bounds
        }

        public enum BoundsMode
        {
            Local,
            World
        }
    }
}