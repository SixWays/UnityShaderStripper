#if UNITY_2018_2_OR_NEWER
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Rendering;
using System.Linq;

namespace Sigtrap.Editors.ShaderStripper {
	public class ShaderStripperEditor : EditorWindow, IPreprocessShaders, IPreprocessBuildWithReport, IPostprocessBuildWithReport {
		public const string KEY_LOG = "ShaderStripperLogPath";
		public const string KEY_ENABLE = "ShaderStripperGlobalEnable";

		[MenuItem("Tools/Sigtrap/Shader Stripper")]
		public static void Launch(){
			EditorWindow.GetWindow<ShaderStripperEditor>().Show();
		}

		static bool _enabled;
		static string _logPath;
		static List<ShaderStripperBase> _strippers = new List<ShaderStripperBase>();
		static System.Diagnostics.Stopwatch _swStrip = new System.Diagnostics.Stopwatch();
		static System.Diagnostics.Stopwatch _swBuild = new System.Diagnostics.Stopwatch();
		
		public int callbackOrder {get {return 0;}}
		Vector2 _scroll;
		List<string> _keptShaders = new List<string>();

		#region GUI
		bool GetEnabled(){
			if (EditorPrefs.HasKey(KEY_ENABLE)){
				return EditorPrefs.GetBool(KEY_ENABLE);
			} else {
				EditorPrefs.SetBool(KEY_ENABLE, true);
				return true;
			}
		}

		void OnEnable(){
			titleContent = new GUIContent("Shader Stripper");
			RefreshSettings();
			_logPath = EditorPrefs.GetString(KEY_LOG);
			_enabled = GetEnabled();
		}
		void OnGUI(){
			Color gbc = GUI.backgroundColor;

			EditorGUILayout.Space();
			if (!_enabled){
				GUI.backgroundColor = Color.magenta;
			}
			EditorGUILayout.BeginVertical(EditorStyles.helpBox); {
				GUI.backgroundColor = gbc;

				// Title
				EditorGUILayout.BeginHorizontal(); {
					EditorGUILayout.LabelField(new GUIContent("Shader Stripping","Any checked settings are applied at build time."), EditorStyles.largeLabel, GUILayout.Height(25));
					GUILayout.FlexibleSpace();
					
					GUI.backgroundColor = Color.blue;
					if (GUILayout.Button("Refresh Settings", GUILayout.Width(125))){
						RefreshSettings();
					}
					GUI.backgroundColor = gbc;
				} EditorGUILayout.EndHorizontal();

				// Toggle stripping
				EditorGUI.BeginChangeCheck(); {
					_enabled = EditorGUILayout.ToggleLeft("Enable Stripping", _enabled);
				} if (EditorGUI.EndChangeCheck()){
					EditorPrefs.SetBool(KEY_ENABLE, _enabled);
					Repaint();
				}

				// Log folder
				EditorGUILayout.Space();
				EditorGUI.BeginChangeCheck(); {
					EditorGUILayout.BeginHorizontal(); {
						_logPath = EditorGUILayout.TextField("Log output file folder", _logPath);
						if (GUILayout.Button("...", GUILayout.Width(25))){
							string path = EditorUtility.OpenFolderPanel("Select log output folder", _logPath, "");
							if (!string.IsNullOrEmpty(path)){
								_logPath = path;
							}
						}
					} EditorGUILayout.EndHorizontal();
				} if (EditorGUI.EndChangeCheck()){
					EditorPrefs.SetString(KEY_LOG, _logPath);
					Repaint();
				}
				
				// Strippers
				EditorGUILayout.Space();
				bool reSort = false;
				_scroll = EditorGUILayout.BeginScrollView(_scroll, EditorStyles.helpBox); {
					for (int i=0; i<_strippers.Count; ++i){
						var s = _strippers[i];
						var so = new SerializedObject(s);
						var active = so.FindProperty("_active");
						GUI.backgroundColor = Color.Lerp(Color.grey, Color.red, active.boolValue ? 0 : 1);
						EditorGUILayout.BeginVertical(EditorStyles.helpBox); {
							GUI.backgroundColor = gbc;
							var expanded = so.FindProperty("_expanded");
							EditorGUILayout.BeginHorizontal(); {
								// Info
								EditorGUILayout.BeginHorizontal(); {
									active.boolValue = EditorGUILayout.Toggle(active.boolValue, GUILayout.Width(25));
									expanded.boolValue = EditorGUILayout.Foldout(expanded.boolValue, s.name + (active.boolValue ? "" : " (inactive)"));
									GUILayout.FlexibleSpace();
									GUILayout.Label(new GUIContent(s.description, "Class: "+s.GetType().Name));

									// Buttons
									GUILayout.FlexibleSpace();
									GUI.enabled = i > 0;
									if (GUILayout.Button("UP")){
										--so.FindProperty("_order").intValue;
										var soPrev = new SerializedObject(_strippers[i-1]);
										++soPrev.FindProperty("_order").intValue;
										soPrev.ApplyModifiedProperties();
										reSort = true;
									}
									GUI.enabled = i < (_strippers.Count-1);
									if (GUILayout.Button("DOWN")){
										++so.FindProperty("_order").intValue;
										var soNext = new SerializedObject(_strippers[i+1]);
										--soNext.FindProperty("_order").intValue;
										soNext.ApplyModifiedProperties();
										reSort = true;
									}
									GUI.enabled = true;
									if (GUILayout.Button("Select")){
										EditorGUIUtility.PingObject(s);
									}
								} EditorGUILayout.EndHorizontal();
							} EditorGUILayout.EndHorizontal();
							if (expanded.boolValue){
								string help = s.help;
								if (!string.IsNullOrEmpty(help)){
									EditorGUILayout.HelpBox(help, MessageType.Info);
								}
								// Settings
								var sp = so.GetIterator();
								sp.NextVisible(true);
								while (sp.NextVisible(false)){
									if ((sp.name == "_active") || (sp.name == "_expanded")) continue;
									EditorGUILayout.PropertyField(sp, true);
								}
							}
						} EditorGUILayout.EndVertical();
						EditorGUILayout.Space();

						so.ApplyModifiedProperties();
					}
				} EditorGUILayout.EndScrollView();
				
				if (reSort){
					SortSettings();
				}
			} EditorGUILayout.EndVertical();
			GUI.backgroundColor = gbc;
		}
		void RefreshSettings(){
			_strippers.Clear();
			foreach (var guid in AssetDatabase.FindAssets("t:ShaderStripperBase")){
				string path = AssetDatabase.GUIDToAssetPath(guid);
				_strippers.Add(AssetDatabase.LoadAssetAtPath<ShaderStripperBase>(path));
			}
			SortSettings();
		}
		void SortSettings(){
			_strippers = _strippers.OrderBy(x=>new SerializedObject(x).FindProperty("_order").intValue).ToList();
			// Apply new sort orders
			for (int i=0; i<_strippers.Count; ++i){
				var so = new SerializedObject(_strippers[i]);
				so.FindProperty("_order").intValue = i;
				so.ApplyModifiedProperties();
			}
		}
		#endregion

		#region Stripping Callbacks
		public void OnPreprocessBuild(UnityEditor.Build.Reporting.BuildReport report){
			_logPath = EditorPrefs.GetString(KEY_LOG);
			_enabled = GetEnabled();

			if (_enabled){
				Debug.Log("Initialising ShaderStrippers");
				if (!string.IsNullOrEmpty(_logPath)){
					Debug.Log("Logfiles will be created in "+_logPath);
				}
				_keptShaders.Clear();
				_keptShaders.Add("Unstripped Shaders:");
				RefreshSettings();
				ShaderStripperBase.OnPreBuild();
				foreach (var s in _strippers){
					if (s.active){
						s.Initialize();
					}
				}
				_swStrip.Reset();
				_swBuild.Reset();
				_swBuild.Start();
			} else {
				Debug.Log("ShaderStripper DISABLED");
			}
		}
		public void OnProcessShader(Shader shader, ShaderSnippetData snippet, IList<ShaderCompilerData> data){
			if (!_enabled) return;

			_swStrip.Start();
			for (int i=0; i<_strippers.Count; ++i){
				var s = _strippers[i];
				if (!s.active) continue;
				s.Strip(shader, snippet, data);
				if (data.Count == 0) break;
			}
			_swStrip.Stop();
			if (data.Count > 0){
				_keptShaders.Add(string.Format("    {0}::{1} [{2} variants]", shader.name, snippet.passType, data.Count));
			}
		}
		public void OnPostprocessBuild(UnityEditor.Build.Reporting.BuildReport report){
			if (!_enabled) return;

			_swBuild.Stop();
			Debug.LogFormat("Shader stripping took {0}ms total", _swStrip.ElapsedMilliseconds);
			Debug.LogFormat("Build took {0}ms total", _swBuild.ElapsedMilliseconds);
			_swStrip.Reset();
			_swBuild.Reset();
			string logPath = EditorPrefs.GetString(ShaderStripperEditor.KEY_LOG);
			ShaderStripperBase.OnPostBuild(logPath, _keptShaders);
			_keptShaders.Clear();
		}
		#endregion
	}
}
#endif