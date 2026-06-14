#if UNITY_EDITOR
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// Editor window (menu: <b>Diagnostics ▸ Analysis Runner</b>) that runs the DIAG
/// Python plotting scripts on the recorded CSVs without leaving Unity. It shells out
/// to your Python interpreter, captures the output into the window + Console, and
/// reveals the generated figure. Plots still live in Python — this just launches it.
/// </summary>
public class DiagAnalysisWindow : EditorWindow
{
    private const string AnalysisRel = "Scripts/Diagnostics/Analysis";

    private string _python = "python";
    private string _dataDir = "";
    private string _constRun = "run_Constant";
    private string _predRun = "run_Predictive";
    private string _bRun = "run_Predictive";
    private Vector2 _scroll;
    private string _log = "";

    [MenuItem("Diagnostics/Analysis Runner")]
    public static void Open() => GetWindow<DiagAnalysisWindow>("DIAG Analysis");

    private void OnEnable()
    {
        _python = EditorPrefs.GetString("diag.python", "python");
        _dataDir = EditorPrefs.GetString("diag.dataDir", Path.Combine(Application.dataPath, "DiagnosticsData"));
        _constRun = EditorPrefs.GetString("diag.const", "run_Constant");
        _predRun = EditorPrefs.GetString("diag.pred", "run_Predictive");
        _bRun = EditorPrefs.GetString("diag.b", "run_Predictive");
    }

    private void Persist()
    {
        EditorPrefs.SetString("diag.python", _python);
        EditorPrefs.SetString("diag.dataDir", _dataDir);
        EditorPrefs.SetString("diag.const", _constRun);
        EditorPrefs.SetString("diag.pred", _predRun);
        EditorPrefs.SetString("diag.b", _bRun);
    }

    private string AnalysisDir => Path.Combine(Application.dataPath, AnalysisRel);

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Setup", EditorStyles.boldLabel);
        _python = EditorGUILayout.TextField("Python executable", _python);
        using (new EditorGUILayout.HorizontalScope())
        {
            _dataDir = EditorGUILayout.TextField("CSV folder", _dataDir);
            if (GUILayout.Button("…", GUILayout.Width(28)))
            {
                string p = EditorUtility.OpenFolderPanel("CSV folder", _dataDir, "");
                if (!string.IsNullOrEmpty(p)) _dataDir = p;
            }
        }
        if (GUILayout.Button("Install Python requirements")) RunPip();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Experiment A — predictive vs constant", EditorStyles.boldLabel);
        _constRun = EditorGUILayout.TextField("Constant run", _constRun);
        _predRun = EditorGUILayout.TextField("Predictive run", _predRun);
        if (GUILayout.Button("Run Experiment A"))
            RunScript("experiment_a.py", $"--const {_constRun} --pred {_predRun}", "experiment_a");

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Experiment B — predicted vs actual jolt", EditorStyles.boldLabel);
        _bRun = EditorGUILayout.TextField("Predictive run", _bRun);
        if (GUILayout.Button("Run Experiment B"))
            RunScript("experiment_b.py", $"--run {_bRun}", "experiment_b");

        EditorGUILayout.Space();
        if (GUILayout.Button("Open CSV / output folder")) EditorUtility.RevealInFinder(_dataDir);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Last output", EditorStyles.boldLabel);
        _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.MinHeight(140));
        EditorGUILayout.TextArea(_log, GUILayout.ExpandHeight(true));
        EditorGUILayout.EndScrollView();
    }

    private void RunScript(string script, string args, string outBase)
    {
        Persist();
        string scriptPath = Path.Combine(AnalysisDir, script);
        if (!File.Exists(scriptPath)) { _log = "Script not found: " + scriptPath; return; }
        if (!Directory.Exists(_dataDir)) { _log = "CSV folder not found: " + _dataDir; return; }

        string outPath = Path.Combine(_dataDir, outBase);
        string full = $"\"{scriptPath}\" {args} --data \"{_dataDir}\" --out \"{outPath}\"";
        int code = Run(_python, full, AnalysisDir, out string outp, out string errp);
        _log = $"$ {_python} {full}\n\n{outp}\n{errp}\n[exit {code}]";

        AssetDatabase.Refresh();
        if (code == 0)
        {
            Debug.Log($"[DIAG] {script} done → {outPath}.png");
            if (File.Exists(outPath + ".png")) EditorUtility.RevealInFinder(outPath + ".png");
        }
        else Debug.LogError($"[DIAG] {script} failed (exit {code}). See the Analysis window log.");
    }

    private void RunPip()
    {
        Persist();
        string req = Path.Combine(AnalysisDir, "requirements.txt");
        int code = Run(_python, $"-m pip install -r \"{req}\"", AnalysisDir, out string o, out string e);
        _log = $"$ {_python} -m pip install -r requirements.txt\n\n{o}\n{e}\n[exit {code}]";
    }

    private static int Run(string exe, string args, string workdir, out string stdout, out string stderr)
    {
        stdout = ""; stderr = "";
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = args,
                WorkingDirectory = workdir,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            psi.EnvironmentVariables["MPLBACKEND"] = "Agg";   // headless matplotlib — save only
            using var p = Process.Start(psi);
            stdout = p.StandardOutput.ReadToEnd();
            stderr = p.StandardError.ReadToEnd();
            p.WaitForExit(120000);
            return p.ExitCode;
        }
        catch (System.Exception ex)
        {
            stderr = ex.Message + "\n(Is Python on PATH? Put the full path to python.exe above.)";
            return -1;
        }
    }
}
#endif
