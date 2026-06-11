using UnityEngine;

/// <summary>
/// Renders a third-party asset credits / attribution panel via IMGUI.
/// Satisfies the CC BY 4.0 attribution requirements (creator, title,
/// source link, license + link, and indication of modification).
///
/// Usage: attach to any GameObject in the scene. A permanent "Credits"
/// button sits in the top-right corner; click it (or press F1) to toggle
/// the panel, which opens anchored to the top-right.
/// </summary>
public class CreditsPanel : MonoBehaviour
{
    [Tooltip("Key used to toggle the credits panel.")]
    public KeyCode toggleKey = KeyCode.F1;

    [Tooltip("Show the panel on start.")]
    public bool visible = false;

    private const float Margin = 20f;
    private const float WindowWidth = 460f;
    private const float ButtonWidth = 110f;
    private const float ButtonHeight = 28f;

    [Tooltip("If true, shows the 'models were modified' notice (required by CC BY 4.0 when changes were made).")]
    public bool modelsWereModified = true;

    private struct Asset
    {
        public string title;
        public string author;
        public string sourceUrl;
        public Asset(string title, string author, string sourceUrl)
        {
            this.title = title;
            this.author = author;
            this.sourceUrl = sourceUrl;
        }
    }

    private static readonly Asset[] assets =
    {
        new Asset("Suspension", "BumpkinCZ", "https://skfb.ly/ooOwD"),
        new Asset("F25 With Quarter Turn Actuator", "sadaoo", "https://skfb.ly/opzoo"),
    };

    private const string LicenseName = "Creative Commons Attribution 4.0 International (CC BY 4.0)";
    private const string LicenseUrl = "http://creativecommons.org/licenses/by/4.0/";
    private const string LegalCodeUrl = "https://creativecommons.org/licenses/by/4.0/legalcode.en";

    private Vector2 scroll;
    private Rect windowRect = new Rect(0, 0, WindowWidth, 0);
    private bool positioned = false;

    private void Update()
    {
        if (Input.GetKeyDown(toggleKey))
            visible = !visible;
    }

    private void OnGUI()
    {
        // Permanent button, always anchored to the top-right corner.
        float btnX = Screen.width - ButtonWidth - Margin;
        var btnRect = new Rect(btnX, Margin, ButtonWidth, ButtonHeight);
        if (GUI.Button(btnRect, visible ? "Hide Credits" : "Credits"))
            visible = !visible;

        if (!visible)
            return;

        // Anchor the window to the top-right the first time it opens
        // (afterwards the user can drag it freely).
        if (!positioned)
        {
            windowRect.x = Screen.width - WindowWidth - Margin;
            windowRect.y = Margin + ButtonHeight + 6f;
            positioned = true;
        }

        windowRect = GUILayout.Window(GetInstanceID(), windowRect, DrawWindow,
            "Credits / Third-Party Assets");
    }

    private void DrawWindow(int id)
    {
        scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(320));

        GUILayout.Label("This project uses the following 3D models under the\n" + LicenseName + ":");
        GUILayout.Space(6);

        foreach (var a in assets)
        {
            GUILayout.BeginVertical(GUI.skin.box);

            GUILayout.Label("\u201C" + a.title + "\u201D  by  " + a.author);

            if (LinkButton("Source: " + a.sourceUrl))
                Application.OpenURL(a.sourceUrl);

            if (LinkButton("License: CC BY 4.0"))
                Application.OpenURL(LicenseUrl);

            GUILayout.EndVertical();
            GUILayout.Space(4);
        }

        if (modelsWereModified)
        {
            GUILayout.Space(4);
            GUILayout.Label("Both models were modified for use in this project.");
        }

        GUILayout.Space(8);
        if (LinkButton("Full license text"))
            Application.OpenURL(LegalCodeUrl);

        GUILayout.EndScrollView();

        GUILayout.Space(4);
        if (GUILayout.Button("Close"))
            visible = false;

        GUI.DragWindow(new Rect(0, 0, 10000, 20));
    }

    private static bool LinkButton(string label)
    {
        var style = new GUIStyle(GUI.skin.label)
        {
            normal = { textColor = new Color(0.4f, 0.7f, 1f) }
        };
        var rect = GUILayoutUtility.GetRect(new GUIContent(label), style);
        return GUI.Button(rect, label, style);
    }
}