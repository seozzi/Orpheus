using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace OrpheusVR.Hologram.Editor
{
    public static class EurydiceHologramSetup
    {
        const string Root="Assets/OrpheusVR/Hologram";
        public const string TestScene="Assets/OrpheusVR/Scenes/Orpheus_Underworld_HologramTest.unity";
        [Serializable] class Transcript {public EurydiceDirector.Line[] items;}
        [MenuItem("Tools/Orpheus VR/Open Eurydice Hologram Test")]
        public static void BuildOrOpen()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Stop Play before creating the test scene.");
            if(File.Exists(TestScene)){EditorSceneManager.OpenScene(TestScene,OpenSceneMode.Single);return;}
            string original="Assets/OrpheusVR/Scenes/Orpheus_Underworld.unity";
            if(!AssetDatabase.CopyAsset(original,TestScene))throw new Exception("Unable to copy v1 cave.");
            var scene=EditorSceneManager.OpenScene(TestScene,OpenSceneMode.Additive);SceneManager.SetActiveScene(scene);
            foreach(var other in Enumerable.Range(0,SceneManager.sceneCount).Select(SceneManager.GetSceneAt).ToArray())if(other!=scene&&other.isLoaded&&!other.isDirty)EditorSceneManager.CloseScene(other,true);
            var experience=scene.GetRootGameObjects().Select(g=>g.GetComponent<OrpheusCaveExperience>()).First(c=>c!=null);
            foreach(string name in new[]{"eurydice_standing_softglow","eurydice_fallen_softglow"})
            {
                string path=Root+"/Textures/"+name+".png";var importer=(TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType=TextureImporterType.Default;importer.alphaSource=TextureImporterAlphaSource.FromInput;importer.alphaIsTransparency=true;importer.mipmapEnabled=true;importer.wrapMode=TextureWrapMode.Clamp;importer.maxTextureSize=2048;importer.textureCompression=TextureImporterCompression.Uncompressed;importer.SaveAndReimport();
            }
            var root=new GameObject("Eurydice - 2D Hologram Scenario");var director=root.AddComponent<EurydiceDirector>();director.experience=experience;experience.hologram=director;
            var actorObject=new GameObject("Eurydice - life size billboard");actorObject.transform.SetParent(root.transform);
            var actor=actorObject.AddComponent<EurydiceHologramActor>();actor.viewer=experience.view;
            actor.standing=AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"/Textures/eurydice_standing_softglow.png");actor.fallen=AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"/Textures/eurydice_fallen_softglow.png");
            var quad=new GameObject("Transparent PNG surface");quad.transform.SetParent(actorObject.transform,false);
            var mesh=new Mesh{name="Feet-pivot billboard quad"};mesh.vertices=new[]{new Vector3(-.5f,0,0),new Vector3(.5f,0,0),new Vector3(-.5f,1,0),new Vector3(.5f,1,0)};mesh.uv=new[]{Vector2.zero,Vector2.right,Vector2.up,Vector2.one};mesh.triangles=new[]{0,2,1,2,3,1};mesh.RecalculateNormals();mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh,Root+"/Materials/BillboardQuad.asset");quad.AddComponent<MeshFilter>().sharedMesh=mesh;actor.image=quad.AddComponent<MeshRenderer>();
            var shader=Shader.Find("OrpheusVR/Eurydice Unlit Dissolve");if(shader==null)throw new Exception("Hologram shader is not compiled.");
            var material=new Material(shader){name="Eurydice Transparent Unlit Dissolve",enableInstancing=true};material.SetTexture("_MainTex",actor.standing);material.SetFloat("_Opacity",.7f);material.SetFloat("_Dissolve",0);AssetDatabase.CreateAsset(material,Root+"/Materials/Eurydice_Hologram.mat");actor.image.sharedMaterial=material;actor.image.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;actor.image.receiveShadows=false;
            actor.SetPose(EurydiceHologramActor.Pose.Standing);actor.SetFeet(new Vector3(-1.3f,0,.7f));actor.SetDissolve(0);
            director.actor=actor;director.entranceAnchor=Anchor(root.transform,"Enter from left",new Vector3(-4,.2f,1));director.handAnchor=Anchor(root.transform,"Handhold",new Vector3(-1.3f,.2f,.7f));director.fallenAnchor=Anchor(root.transform,"Fall",new Vector3(28,3.8f,60));director.reunionAnchor=Anchor(root.transform,"Reunion",new Vector3(22,7,105));
            director.eurydiceVoice=actorObject.AddComponent<AudioSource>();director.eurydiceVoice.playOnAwake=false;director.eurydiceVoice.spatialBlend=1;director.eurydiceVoice.minDistance=1.5f;director.eurydiceVoice.maxDistance=35;director.eurydiceVoice.dopplerLevel=0;
            var narrator=new GameObject("Hades dialogue source");narrator.transform.SetParent(root.transform);director.hadesVoice=narrator.AddComponent<AudioSource>();director.hadesVoice.playOnAwake=false;director.hadesVoice.spatialBlend=0;
            director.lines=JsonUtility.FromJson<Transcript>("{\"items\":"+File.ReadAllText(Root+"/Audio/dialogue_manifest.json")+"}").items;
            foreach(var line in director.lines)line.clip=AssetDatabase.LoadAssetAtPath<AudioClip>(Root+"/Audio/"+line.id+".wav");
            director.lyreCue=AssetDatabase.LoadAssetAtPath<AudioClip>(Root+"/Audio/lyre_cue.wav");
            // World-space subtitles remain visible in each eye; desktop controls are drawn separately.
            var canvasObject=new GameObject("VR subtitles");canvasObject.transform.SetParent(experience.view.transform,false);canvasObject.transform.localPosition=new Vector3(0,-.40f,1.8f);canvasObject.transform.localScale=Vector3.one*.0012f;
            var canvas=canvasObject.AddComponent<Canvas>();canvas.renderMode=RenderMode.WorldSpace;canvas.worldCamera=experience.view;var rect=canvasObject.GetComponent<RectTransform>();rect.sizeDelta=new Vector2(1100,190);
            var textObject=new GameObject("Subtitle text");textObject.transform.SetParent(canvasObject.transform,false);var text=textObject.AddComponent<Text>();text.font=Font.CreateDynamicFontFromOSFont(new[]{"Apple SD Gothic Neo","Malgun Gothic","Noto Sans CJK KR","Arial"},28);text.fontSize=28;text.alignment=TextAnchor.MiddleCenter;text.color=Color.white;text.horizontalOverflow=HorizontalWrapMode.Wrap;text.verticalOverflow=VerticalWrapMode.Overflow;var tr=text.GetComponent<RectTransform>();tr.anchorMin=Vector2.zero;tr.anchorMax=Vector2.one;tr.offsetMin=tr.offsetMax=Vector2.zero;textObject.AddComponent<Outline>().effectColor=Color.black;director.subtitleText=text;
            // Save a reusable actor prefab without a scene-camera reference.
            var camera=actor.viewer;actor.viewer=null;PrefabUtility.SaveAsPrefabAsset(actorObject,Root+"/Materials/EurydiceHologram.prefab");actor.viewer=camera;
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();Selection.activeGameObject=root;
            Debug.Log("Eurydice hologram test ready: "+TestScene+" (TTS deferred; subtitles active)");
        }
        static Transform Anchor(Transform parent,string name,Vector3 p){var g=new GameObject(name);g.transform.SetParent(parent);g.transform.position=p;return g.transform;}
    }
}
