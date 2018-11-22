#if UNITY_2018_2_OR_NEWER
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;
using UnityEngine.Rendering;
using System.Text;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Rendering;
#endif

namespace Sigtrap.Editors.ShaderStripper {
    /// <summary>
    /// Strips ALL shaders and variants except those in the supplied ShaderVariantCollection assets.
    /// Does not strip built-in shaders.
    /// </summary>
    [CreateAssetMenu(menuName="Sigtrap/Shader Stripper Variant Collection")]
    public class ShaderStripperVariantCollection : ShaderStripperBase {
        [SerializeField][Tooltip("Only shader variants in these collections will NOT be stripped (except built-in shaders).")]
        List<ShaderVariantCollection> _whitelistedCollections;
        [SerializeField][Tooltip("Strip Hidden shaders. Be careful - shaders in Resources might get stripped.\nHidden shaders in collections will always have their variants stripped.")]
        bool _stripHidden = false;

		[SerializeField][Tooltip("Shaders matching these names will be ignored (not stripped)")]
		StringMatch[] _ignoreShadersByName;

		bool _valid = false;

        #if UNITY_EDITOR
        Dictionary<Shader, Dictionary<PassType, List<ShaderVariantCollection.ShaderVariant>>> _variantsByShader = new Dictionary<Shader, Dictionary<PassType, List<ShaderVariantCollection.ShaderVariant>>>();

        #region Parse YAML - thanks Unity for not having a simple ShaderVariantCollection.GetVariants or something
        public override void Initialize(){
			foreach (var c in _whitelistedCollections){
				// Load asset YAML
				var file = new List<string>(System.IO.File.ReadAllLines(
					(Application.dataPath + AssetDatabase.GetAssetPath(c)).Replace("AssetsAssets","Assets")
				));

				#region Pre-process to get rid of mid-list line breaks
				var yaml = new List<string>();

				// Find shaders list
                int i = 0;
				for (; i<file.Count; ++i){
					if (YamlLineHasKey(file[i], "m_Shaders")) break;
				}

                // Process and fill
                int indent = 0;
				for (; i<file.Count; ++i){
					string f = file[i];
					int myIndent = GetYamlIndent(f);
					if (myIndent > indent){
						// If no "<key>: ", continuation of previous line
						if (!f.EndsWith(":") && !f.Contains(": ")){
							yaml[yaml.Count-1] += " " + f.Trim();
							continue;
						}
					}

					yaml.Add(f);
					indent = myIndent;
				}
                #endregion

				#region Iterate over shaders
				for (i=0; i<yaml.Count; ++i){
					string y = yaml[i];
					if (yaml[i].Contains("first:")){
						string guid = GetValueFromYaml(y, "guid");

						// Move to variants contents (skip current line, "second:" and "variants:")
						i += 3;
						indent = GetYamlIndent(yaml[i]);
						var sv = new ShaderVariantCollection.ShaderVariant();
						for (; i<yaml.Count; ++i){
							y = yaml[i];

                            // If indent changes, variants have ended
							if (GetYamlIndent(y) != indent){
								// Outer loop will increment, so counteract
								i -= 1;
								break;
							}

							if (IsYamlLineNewEntry(y)) {
								// First entry will be a new entry but no variant info present yet, so skip
								// Builtin shaders will also be null
								if (sv.shader != null){	
                                    Dictionary<PassType, List<ShaderVariantCollection.ShaderVariant>> variantsByPass = null;
                                    if (!_variantsByShader.TryGetValue(sv.shader, out variantsByPass)){
                                        variantsByPass = new Dictionary<PassType, List<ShaderVariantCollection.ShaderVariant>>();
                                        _variantsByShader.Add(sv.shader, variantsByPass);
                                    }
                                    List<ShaderVariantCollection.ShaderVariant> variants = null;
                                    if (!variantsByPass.TryGetValue(sv.passType, out variants)){
                                        variants = new List<ShaderVariantCollection.ShaderVariant>();
                                        variantsByPass.Add(sv.passType, variants);
                                    }
                                    variants.Add(sv);
								}
								sv = new ShaderVariantCollection.ShaderVariant();
								sv.shader = AssetDatabase.LoadAssetAtPath<Shader>(AssetDatabase.GUIDToAssetPath(guid.ToString()));
								//if (sv.shader != null){
								//	Debug.Log("\t"+sv.shader.name);
								//}
							}

                            // Get contents
							if (YamlLineHasKey(y, "passType")){
								sv.passType = (PassType)int.Parse(GetValueFromYaml(y, "passType"));
								//Debug.Log("\t" + sv.passType.ToString());
							}
							if (YamlLineHasKey(y, "keywords")){
								sv.keywords = GetValuesFromYaml(y, "keywords");
								//string log = "\tKEYWORDS: ";
								//foreach (var k in sv.keywords){
								//	log += k + " ";
								//}
								//Debug.Log(log);
							}
						}
					}
                }
                #endregion

                LogMessage(this, "Parsing ShaderVariantCollection "+c.name);
                // Loop over shaders
				foreach (var s in _variantsByShader){
					string log = "Shader: " + s.Key.name;
                    // Loop over passes
                    foreach (var p in s.Value){
                        log += string.Format("\n   Pass: {0} [{1}]", p.Key, (int)p.Key);
                        // Loop over variants
                        for (int v=0; v<p.Value.Count; ++v){
                            log += string.Format("\n      Variant [{0}] Keywords:\n", v);
                            // Loop over keywords
                            foreach (var k in p.Value[v].keywords){
                                log += k + " ";
                            }
                        }
                    }
					LogMessage(this, log);
				}
			}

			_valid = (_variantsByShader != null && _variantsByShader.Count > 0);
		}
        int GetYamlIndent(string line){
			for (int i=0; i<line.Length; ++i){
				if (line[i] != ' ' && line[i] != '-') return i;
			}
			return 0;
		}
		bool IsYamlLineNewEntry(string line){
			foreach (var c in line){
				// If a dash (before a not-space appears) this is a new entry
				if (c == '-') return true;
				// If not a dash, must be a space or indent has ended
				if (c != ' ') return false;
			}
			return false;
		}
		int GetIndexOfYamlValue(string line, string key){
			int i = line.IndexOf(key+":", System.StringComparison.Ordinal);
			if (i >= 0){
				// Skip to value
				i += key.Length + 2;
			}
			return i;
		}
		bool YamlLineHasKey(string line, string key){
			return GetIndexOfYamlValue(line, key) >= 0;
		}
		string GetValueFromYaml(string line, string key){
			int i = GetIndexOfYamlValue(line, key);
			if (i < 0){
				return "";
				//throw new System.Exception((string.Format("Value not found for key {0} in YAML line {1}", key, line)));
			}
			StringBuilder sb = new StringBuilder();
			for (; i<line.Length; ++i){
				char c = line[i];
				if (c == ',' || c == ' ') break;
				sb.Append(c);
			}
			return sb.ToString();
		}
		string[] GetValuesFromYaml(string line, string key){
			int i = GetIndexOfYamlValue(line, key);
			if (i < 0){
				throw new System.Exception((string.Format("Value not found for key {0} in YAML line {1}", key, line)));
			}
			List<string> result = new List<string>();
			StringBuilder sb = new StringBuilder();
			for (; i<line.Length; ++i){
				char c = line[i];
				bool end = false;
				bool brk = false;
				if (c == ','){
					// Comma delimits keys
					// Add the current entry and stop parsing
					end = brk = true;
				}
				if (c == ' '){
					// Space delimits entries
					// Add current entry, move to next
					end = true;
				}
				if (end){
					result.Add(sb.ToString());
					sb.Length = 0;
					if (brk) break;
				} else {
					sb.Append(c);
				}
			}
			// Catch last entry if line ends
			if (sb.Length > 0){
				result.Add(sb.ToString());
			}
			return result.ToArray();
		}
        #endregion

        static List<string> _tempKeywordsToMatch = new List<string>();
        static List<string> _tempKeywordsToMatchCached = new List<string>();
        protected override bool StripCustom(Shader shader, ShaderSnippetData passData, IList<ShaderCompilerData> variantData){
			// Don't strip anything if no collections present
			if (!_valid) return true;
            // Always ignore built-in shaders
            if (string.IsNullOrEmpty(AssetDatabase.GetAssetPath(shader))) return true;
			// Ignore shaders by name
			foreach (var s in _ignoreShadersByName){
				if (s.Evaluate(shader.name)) return true;
			}

            // Try to match shader
            Dictionary<PassType, List<ShaderVariantCollection.ShaderVariant>> variantsByPass = null;
            if (_variantsByShader.TryGetValue(shader, out variantsByPass)){
                // Try to match pass
                List<ShaderVariantCollection.ShaderVariant> passVariants = null;
                if (variantsByPass.TryGetValue(passData.passType, out passVariants)){
                    // Loop over supplied variants
                    // Iterate backwards over supplied variants to allow index-based removal
                    int count = variantData.Count;
                    for (int i=count-1; i>=0; --i){
                        var variantIn = variantData[i];

                        // Fill temp buffer to fill OTHER temp buffer each time SIGH
                        _tempKeywordsToMatchCached.Clear();
                        foreach (var sk in variantIn.shaderKeywordSet.GetShaderKeywords()){
							#if UNITY_2018_3_OR_NEWER
							string sn = sk.GetKeywordName();
							#else
							string sn = sk.GetName();
							#endif
							
                            _tempKeywordsToMatchCached.Add(sn);
                        }
                        bool variantMatched = false;

                        // Loop over cached variants
                        foreach (var variantMatch in passVariants){
                            // Must match ALL keywords
                            _tempKeywordsToMatch.Clear();
                            _tempKeywordsToMatch.AddRange(_tempKeywordsToMatchCached);

                            // Early out (no match) if keyword counts don't match
                            if (_tempKeywordsToMatch.Count != variantMatch.keywords.Length) break;

                            // Early out (match) if both have no keywords
                            if (_tempKeywordsToMatch.Count == 0 && variantMatch.keywords.Length == 0){
                                variantMatched = true;
                                break;
                            }

                            // Check all keywords
                            foreach (var k in variantMatch.keywords){
                                bool removed = _tempKeywordsToMatch.Remove(k);
                                if (!removed) break;
                            }
                            // If all keywords removed, all keywords matched
                            if (_tempKeywordsToMatch.Count == 0){
                                variantMatched = true;
                            }
                        }

                        // Strip this variant
                        if (!variantMatched){
                            LogRemoval(this, shader, passData, i, count);
                            variantData.RemoveAt(i);
                        }
                    }
                } else {
                    // If not matched pass, clear all variants
                    LogRemoval(this, shader, passData);
                    variantData.Clear();
                }
            } else {
                // If not matched shader, clear all
                // Check if shader is hidden
                if (_stripHidden || !shader.name.StartsWith("Hidden/")){
                    LogRemoval(this, shader, passData);
                    variantData.Clear();
                }
            }

            return true;
        }
        
        public override string description {get {return "Strips ALL (non-built-in) shaders not in selected ShaderVariantCollection assets.";}}
        public override string help {
            get {
                string result = _stripHidden ? "WILL strip Hidden shaders." : "Will NOT strip Hidden shaders.";
                result += " Will NOT strip built-in shaders. Use other strippers to remove these.";
                return result;
            }
        }

        protected override bool _checkShader {get {return false;}}
        protected override bool _checkPass {get {return false;}}
        protected override bool _checkVariants {get {return false;}}
        #endif
    }
}
#endif