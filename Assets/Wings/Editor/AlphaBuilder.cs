using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Wings.Editor
{
    public static class AlphaBuilder
    {
        const string ScenePath="Assets/Wings/Scenes/WingsAlpha.unity";
        [MenuItem("WINGS/Setup Alpha Scene")]
        public static void Setup()
        {
            Directory.CreateDirectory("Assets/Wings/Scenes");Directory.CreateDirectory("Assets/Wings/Resources/Characters");
            if(!AssetDatabase.LoadAssetAtPath<Material>("Assets/Wings/Resources/WingsSurface.mat"))
                AssetDatabase.CreateAsset(new Material(Shader.Find("Universal Render Pipeline/Lit")),"Assets/Wings/Resources/WingsSurface.mat");
            if(!AssetDatabase.LoadAssetAtPath<Wings.AI.AIConfiguration>("Assets/Wings/Resources/WingsAI.asset"))
                AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<Wings.AI.AIConfiguration>(),"Assets/Wings/Resources/WingsAI.asset");
            foreach(Bird b in Enum.GetValues(typeof(Bird))) {
                string path="Assets/Wings/Resources/Characters/"+b+".asset";
                if(!AssetDatabase.LoadAssetAtPath<CharacterDefinition>(path))AssetDatabase.CreateAsset(CharacterDefinition.Create(b),path);
            }
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            new GameObject("WINGS REVOLT").AddComponent<WingsGame>();
            EditorSceneManager.SaveScene(scene,ScenePath);
            EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene(ScenePath,true)};
            PlayerSettings.companyName="WINGS";PlayerSettings.productName="WINGS REVOLT Alpha";
            PlayerSettings.defaultScreenWidth=1600;PlayerSettings.defaultScreenHeight=900;
            PlayerSettings.SplashScreen.show=false;
            PlayerSettings.fullScreenMode=FullScreenMode.Windowed;PlayerSettings.resizableWindow=true;
            PlayerSettings.runInBackground=true;PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Standalone,ScriptingImplementation.Mono2x);
            AssetDatabase.SaveAssets();AssetDatabase.Refresh();
            Debug.Log("WINGS SETUP COMPLETE");
        }
        [MenuItem("WINGS/Build Windows Alpha")]
        public static void Build()
        {
            Setup();Directory.CreateDirectory("Builds/Alpha");
            var result=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{ScenePath},locationPathName="Builds/Alpha/WINGS_REVOLT_Alpha.exe",target=BuildTarget.StandaloneWindows64,options=BuildOptions.Development});
            if(result.summary.result!=BuildResult.Succeeded)throw new Exception("WINGS build failed: "+result.summary.result);
            Debug.Log("WINGS BUILD SUCCEEDED: "+result.summary.outputPath);
        }
        [MenuItem("WINGS/Build Refined AI")]
        public static void BuildRefinedAI()
        {
            if(!AssetDatabase.LoadAssetAtPath<Wings.AI.AIConfiguration>("Assets/Wings/Resources/WingsAI.asset"))
                AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<Wings.AI.AIConfiguration>(),"Assets/Wings/Resources/WingsAI.asset");
            AssetDatabase.SaveAssets();Directory.CreateDirectory("Builds/AIRefined");
            var result=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{ScenePath},locationPathName="Builds/AIRefined/WINGS_REVOLT_AI.exe",target=BuildTarget.StandaloneWindows64,options=BuildOptions.Development});
            if(result.summary.result!=BuildResult.Succeeded)throw new Exception("WINGS AI build failed: "+result.summary.result);
            Debug.Log("WINGS AI BUILD SUCCEEDED: "+result.summary.outputPath);
        }
    }
}
