#if UNITY_2018_2_OR_NEWER
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Rendering;
#endif

namespace Sigtrap.Editors.ShaderStripper {
    /// <summary>
	/// Strips shaders by shader asset path.
	/// </summary>
	[CreateAssetMenu(menuName="Sigtrap/Shader Force Keywords")]
    public class ShaderForceKeywords : ShaderStripperBase {
        [System.Serializable]
        struct ForceBuiltin {
            public BuiltinShaderDefine defineToMatch;
            public BuiltinShaderDefine defineToForce;
            [Tooltip("If true, check Define To Match is NOT enabled")]
            public bool invertMatch;
            [Tooltip("If true, DISABLE Define To Force")]
            public bool invertForce;
        }
        [System.Serializable]
        struct ForceKeyword {
            public string keywordToMatch;
            public string keywordToForce;
            [Tooltip("If true, check Keyword To Match is NOT enabled")]
            public bool invertMatch;
            [Tooltip("If true, DISABLE Keyword To Force")]
            public bool invertForce;
        }
        [SerializeField]
        ForceBuiltin[] _forceBuiltins;
        [SerializeField]
        ForceKeyword[] _forceKeywords;

        #if UNITY_EDITOR
        protected override bool _checkShader {get {return false;}}
        protected override bool _checkPass {get {return false;}}
        protected override bool _checkVariants {get {return false;}}
        protected override bool StripCustom(Shader shader, ShaderSnippetData passData, IList<ShaderCompilerData> variantData){
            foreach (var d in variantData){
                // Builtins
                foreach (var b in _forceBuiltins){
                    bool matched = d.platformKeywordSet.IsEnabled(b.defineToMatch);
                    if (b.invertMatch) matched = !matched;
                    if (matched){
                        if (b.invertForce){
                            d.platformKeywordSet.Disable(b.defineToForce);
                        } else {
                            d.platformKeywordSet.Enable(b.defineToForce);
                        }
                    }
                }
                // Keywords
                foreach (var k in _forceKeywords){
                    bool matched = d.shaderKeywordSet.IsEnabled(new ShaderKeyword(k.keywordToMatch));
                    if (k.invertMatch) matched = !matched;
                    if (matched){
                        ShaderKeyword sk = new ShaderKeyword(k.keywordToForce);
                        if (k.invertForce){
                            d.shaderKeywordSet.Disable(sk);
                        } else {
                            d.shaderKeywordSet.Enable(sk);
                        }
                    }
                }
            }
            return true;
        }
        #endif
    }
}
#endif