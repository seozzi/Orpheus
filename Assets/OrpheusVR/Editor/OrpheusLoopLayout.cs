using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;

namespace OrpheusVR.Editor
{
    // Reversible v2 layout: the v1 scene and meshes are never overwritten.
    public static class OrpheusLoopLayout
    {
        const string Root="Assets/OrpheusVR";
        const string ScenePath=Root+"/Scenes/Orpheus_Underworld_Loop_v2.unity";
        const float Radius=18, ArcEnd=88, TailEnd=100;
        static readonly float[] Z={10,19,26,30,39,47,52,59,68,78,87,96,109};
        static readonly float[] X={0,0,6,15,18,18,24,29,29,25,25,25,25};
        static string meshes=Root+"/Meshes/Loop_v2";
        static float SourceX(float z)
        {
            if(z<=Z[0])return X[0];
            for(int i=0;i<Z.Length-1;i++)if(z<=Z[i+1])return Mathf.Lerp(X[i],X[i+1],Mathf.InverseLerp(Z[i],Z[i+1],z));
            return X[X.Length-1];
        }
        static float Ground(float z){return Mathf.Clamp((z-15)*.082f,0,6.9f);}
        static void Frame(float z,out Vector3 c,out Vector3 forward)
        {
            float theta=(Mathf.Min(z,ArcEnd)+2)/90f*285*Mathf.Deg2Rad;
            c=new Vector3(Radius*Mathf.Sin(theta),0,Radius*(1-Mathf.Cos(theta)));
            forward=new Vector3(Mathf.Cos(theta),0,Mathf.Sin(theta));
            if(z<=ArcEnd)return;
            float s=Mathf.Min(z-ArcEnd,TailEnd-ArcEnd),k=-Mathf.PI/24;
            float speed=Radius*285*Mathf.Deg2Rad/90;
            c+=new Vector3((Mathf.Sin(theta+k*s)-Mathf.Sin(theta))/k,0,(Mathf.Cos(theta)-Mathf.Cos(theta+k*s))/k)*speed;
            forward=new Vector3(Mathf.Cos(theta+k*s),0,Mathf.Sin(theta+k*s));
            if(z>TailEnd)c+=forward*(z-TailEnd)*speed;
        }
        public static Vector3 Warp(Vector3 p)
        {
            Vector3 c,t;Frame(p.z,out c,out t);
            float offset=p.x-SourceX(p.z);
            // Keep the blind branch inside the ring without folding the cavity over itself.
            float weight=Mathf.SmoothStep(0,1,Mathf.InverseLerp(43,48,p.z))*(1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(60,65,p.z)));
            if(offset< -4)offset=Mathf.Lerp(offset,-4+(offset+4)*.45f,weight);
            return c+Vector3.Cross(Vector3.up,t)*offset+Vector3.up*p.y;
        }
        static Quaternion Turn(float z)
        {
            Vector3 c,t;Frame(z,out c,out t);
            Vector3 oldForward=new Vector3(SourceX(z+.05f)-SourceX(z-.05f),0,.1f).normalized;
            return Quaternion.LookRotation(t)*Quaternion.Inverse(Quaternion.LookRotation(oldForward));
        }
        static Vector3 WarpNormal(Vector3 p,Vector3 n)
        {
            const float e=.04f;var m=Matrix4x4.identity;
            Vector3 a=(Warp(p+Vector3.right*e)-Warp(p-Vector3.right*e))/(2*e);
            Vector3 b=(Warp(p+Vector3.up*e)-Warp(p-Vector3.up*e))/(2*e);
            Vector3 c=(Warp(p+Vector3.forward*e)-Warp(p-Vector3.forward*e))/(2*e);
            m.SetColumn(0,new Vector4(a.x,a.y,a.z,0));m.SetColumn(1,new Vector4(b.x,b.y,b.z,0));m.SetColumn(2,new Vector4(c.x,c.y,c.z,0));
            return m.inverse.transpose.MultiplyVector(n).normalized;
        }
        struct Pose {public Transform t;public Vector3 p;public Quaternion q;public Matrix4x4 matrix;}

        [MenuItem("Tools/Orpheus VR/Create Curved Loop Map v2")]
        public static void Build()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Stop Play mode before changing the layout.");
            if(File.Exists(ScenePath))throw new InvalidOperationException("Loop v2 already exists. Open it instead of applying the deformation twice.");
            meshes=Root+"/Meshes/Loop_v2";Directory.CreateDirectory(meshes);AssetDatabase.Refresh();
            if(!AssetDatabase.CopyAsset(Root+"/Scenes/Orpheus_Underworld.unity",ScenePath))throw new Exception("Could not copy the original scene.");
            var scene=EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Additive);SceneManager.SetActiveScene(scene);
            foreach(var other in Enumerable.Range(0,SceneManager.sceneCount).Select(SceneManager.GetSceneAt).ToArray())
                if(other!=scene && !other.isDirty)EditorSceneManager.CloseScene(other,false);
            var world=scene.GetRootGameObjects().First(g=>g.name=="ORPHEUS - Complete Underworld");
            var player=scene.GetRootGameObjects().First(g=>g.GetComponent<OrpheusCaveExperience>()!=null);
            var poses=world.GetComponentsInChildren<Transform>(true).Concat(player.GetComponentsInChildren<Transform>(true))
                .Select(t=>new Pose{t=t,p=t.position,q=t.rotation,matrix=t.localToWorldMatrix}).ToArray();
            var sourceMatrices=poses.ToDictionary(p=>p.t,p=>p.matrix);
            foreach(var pose in poses)
            {
                if(pose.t==world.transform)continue;
                pose.t.position=Warp(pose.p);pose.t.rotation=Turn(pose.p.z)*pose.q;
            }
            foreach(var mf in world.GetComponentsInChildren<MeshFilter>(true).Where(m=>m.name.StartsWith("CaveShell_")))
            {
                var original=mf.sharedMesh;var m=UnityEngine.Object.Instantiate(original);m.name=original.name+"_Loop";
                var matrix=sourceMatrices[mf.transform];var v=original.vertices;var n=original.normals;var normalMatrix=matrix.inverse.transpose;
                for(int i=0;i<v.Length;i++){Vector3 p=matrix.MultiplyPoint3x4(v[i]);n[i]=WarpNormal(p,normalMatrix.MultiplyVector(n[i]).normalized);v[i]=Warp(p);}
                m.vertices=v;m.normals=n;m.RecalculateBounds();m.RecalculateTangents();
                AssetDatabase.CreateAsset(m,meshes+"/"+m.name+".asset");
                mf.transform.position=Vector3.zero;mf.transform.rotation=Quaternion.identity;mf.transform.localScale=Vector3.one;
                mf.sharedMesh=m;mf.GetComponent<MeshCollider>().sharedMesh=m;
            }
            world.name="ORPHEUS - Curved Return Loop v2";
            RebuildShell(world.transform);
            RebuildWater(world.transform);
            CompactExterior(world.transform,poses);
            var markers=world.transform.Find("05 - Route and Event Anchors");
            var route=markers.Find("Walkable main route");
            foreach(Transform child in route.Cast<Transform>().ToArray())UnityEngine.Object.DestroyImmediate(child.gameObject);
            var stations=new List<float>();
            for(float z=10;z<109;z+=1.5f)stations.Add(z);stations.Add(109);
            for(int i=0;i<stations.Count;i++)
            {
                float z=stations[i];var g=new GameObject("Loop Waypoint "+i.ToString("00"));g.transform.SetParent(route);g.transform.position=Warp(new Vector3(SourceX(z),Ground(z)+.2f,z));
            }
            var e=player.GetComponent<OrpheusCaveExperience>();e.loopLayout=true;e.routeStations=stations.ToArray();e.route=route.Cast<Transform>().ToArray();
            e.deadEndAnchor=markers.GetComponentsInChildren<Transform>(true).First(t=>t.name=="Dead end / retreat trigger");e.demoAutowalk=false;
            // The room looks along its outgoing tangent; the panorama no longer uses world +Z.
            player.transform.rotation=Turn(-3);player.GetComponentInChildren<Camera>().transform.localRotation=Quaternion.identity;
            var nav=world.GetComponent<Unity.AI.Navigation.NavMeshSurface>();nav.RemoveData();nav.BuildNavMesh();
            if(nav.navMeshData!=null)AssetDatabase.CreateAsset(nav.navMeshData,meshes+"/LoopNavMesh.asset");
            var overview=world.GetComponentsInChildren<Camera>(true).First(c=>c.name.StartsWith("Map Overview"));
            overview.transform.position=new Vector3(-9,120,18);overview.transform.rotation=Quaternion.Euler(90,0,0);overview.orthographicSize=43;overview.enabled=false;
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
            Validate(scene);
            foreach(var s in Enumerable.Range(0,SceneManager.sceneCount).Select(SceneManager.GetSceneAt).ToArray())if(s!=scene&&!s.isDirty)EditorSceneManager.CloseScene(s,false);
            Selection.activeGameObject=world;
            if(SceneView.lastActiveSceneView!=null)SceneView.lastActiveSceneView.LookAt(new Vector3(-4,2,19),Quaternion.Euler(74,0,0),55);
            Capture(scene);
            Debug.Log("Orpheus loop v2 created and validated: "+ScenePath);
        }
        struct CaveLine {public Vector3 a,b;public float ra,rb,ha,hb;}
        static List<CaveLine> caveLines;
        static float Width(float z)
        {
            if(z<10)return Mathf.Lerp(8,3.2f,Mathf.InverseLerp(-2,10,z));
            if(z<52)return 3.2f;
            if(z<59)return Mathf.Lerp(3.2f,6.5f,(z-52)/7);
            if(z<68)return 6.5f;
            if(z<78)return Mathf.Lerp(6.5f,3.2f,(z-68)/10);
            if(z<87)return Mathf.Lerp(3.2f,4.3f,(z-78)/9);
            if(z<96)return Mathf.Lerp(4.3f,3.2f,(z-87)/9);
            return 3.2f;
        }
        static float LoopField(Vector3 p)
        {
            float nearest=999,floor=0,field=999;
            foreach(var line in caveLines)
            {
                Vector2 a=new Vector2(line.a.x,line.a.z),b=new Vector2(line.b.x,line.b.z),q=new Vector2(p.x,p.z),d=b-a;
                float t=Mathf.Clamp01(Vector2.Dot(q-a,d)/d.sqrMagnitude),dist=Vector2.Distance(q,a+d*t);
                if(dist<nearest){nearest=dist;floor=Mathf.Lerp(line.a.y,line.b.y,t);}
            }
            foreach(var line in caveLines)
            {
                Vector2 a=new Vector2(line.a.x,line.a.z),b=new Vector2(line.b.x,line.b.z),q=new Vector2(p.x,p.z),d=b-a;
                float t=Mathf.Clamp01(Vector2.Dot(q-a,d)/d.sqrMagnitude),dist=Vector2.Distance(q,a+d*t),r=Mathf.Lerp(line.ra,line.rb,t),h=Mathf.Lerp(line.ha,line.hb,t),up=(p.y-floor)/h;
                field=Mathf.Min(field,(Mathf.Sqrt(dist*dist/(r*r)+up*up)-1)*r);
            }
            float noise=(Mathf.PerlinNoise(p.x*.32f+p.y*.18f,p.z*.28f)-.5f)*.45f;
            return Mathf.Max(field+noise,floor-p.y);
        }
        static Vector3 LoopNormal(Vector3 p)
        {
            const float e=.04f;
            return -new Vector3(LoopField(p+Vector3.right*e)-LoopField(p-Vector3.right*e),LoopField(p+Vector3.up*e)-LoopField(p-Vector3.up*e),LoopField(p+Vector3.forward*e)-LoopField(p-Vector3.forward*e)).normalized;
        }
        static readonly int[,] Tetra={{0,5,1,6},{0,1,2,6},{0,2,3,6},{0,3,7,6},{0,7,4,6},{0,4,5,6}};
        static readonly Vector3[] Corners={new Vector3(0,0,0),new Vector3(1,0,0),new Vector3(1,0,1),new Vector3(0,0,1),new Vector3(0,1,0),new Vector3(1,1,0),new Vector3(1,1,1),new Vector3(0,1,1)};
        public static void RebuildShell(Transform world)
        {
            caveLines=new List<CaveLine>();
            for(float z=-2;z<106;z+=2)
            {
                float a=Width(z),b=Width(z+2);
                caveLines.Add(new CaveLine{a=Warp(new Vector3(SourceX(z),Ground(z),z)),b=Warp(new Vector3(SourceX(z+2),Ground(z+2),z+2)),ra=a,rb=b,ha=a>5?8:5,hb=b>5?8:5});
            }
            Vector3[] branch={new Vector3(18,Ground(47),47),new Vector3(9,Ground(52),52),new Vector3(5,Ground(58),58)};
            for(int i=0;i<2;i++)for(int j=0;j<4;j++)caveLines.Add(new CaveLine{a=Warp(Vector3.Lerp(branch[i],branch[i+1],j/4f)),b=Warp(Vector3.Lerp(branch[i],branch[i+1],(j+1)/4f)),ra=2.7f,rb=2.7f,ha=4.8f,hb=4.8f});
            var geo=world.Find("01 - Continuous Cave Shell");var material=geo.GetComponentInChildren<MeshRenderer>().sharedMaterial;
            foreach(Transform child in geo.Cast<Transform>().ToArray())UnityEngine.Object.DestroyImmediate(child.gameObject);
            Vector3 exit,exitDirection;Frame(100,out exit,out exitDirection);
            for(int z0=-12;z0<50;z0+=8)
            {
                var v=new List<Vector3>();var n=new List<Vector3>();var uv=new List<Vector2>();var tr=new List<int>();
                var pos=new Vector3[8];var val=new float[8];
                Action<Vector3,Vector3,Vector3> raw=(a,b,c)=>
                {
                    Vector3 na=LoopNormal(a),nb=LoopNormal(b),nc=LoopNormal(c);
                    if(Vector3.Dot(Vector3.Cross(b-a,c-a),na+nb+nc)<0){var swap=b;b=c;c=swap;var sn=nb;nb=nc;nc=sn;}
                    int k=v.Count;v.AddRange(new[]{a,b,c});n.AddRange(new[]{na,nb,nc});foreach(var q in new[]{a,b,c})uv.Add(new Vector2(q.x+q.y*.7f,q.z+q.y*.9f)*.22f);tr.AddRange(new[]{k,k+1,k+2});
                };
                Action<Vector3,Vector3,Vector3> tri=(a,b,c)=>
                {
                    var input=new[]{a,b,c};var clipped=new List<Vector3>();
                    for(int i=0;i<3;i++){var p=input[i];var q=input[(i+1)%3];float dp=Vector3.Dot(p-exit,exitDirection),dq=Vector3.Dot(q-exit,exitDirection);if(dp<=0)clipped.Add(p);if((dp<=0)!=(dq<=0))clipped.Add(Vector3.Lerp(p,q,dp/(dp-dq)));}
                    for(int i=1;i<clipped.Count-1;i++)raw(clipped[0],clipped[i],clipped[i+1]);
                };
                for(float x=-35;x<30;x++)for(float z=z0;z<Mathf.Min(z0+8,50);z++)for(float y=-2;y<18;y++)
                {
                    int negative=0;for(int i=0;i<8;i++){pos[i]=new Vector3(x,y,z)+Corners[i];val[i]=LoopField(pos[i]);if(val[i]<0)negative++;}if(negative==0||negative==8)continue;
                    for(int t=0;t<6;t++)
                    {
                        var ins=new List<int>();var outs=new List<int>();for(int j=0;j<4;j++){int k=Tetra[t,j];if(val[k]<0)ins.Add(k);else outs.Add(k);}if(ins.Count==0||outs.Count==0)continue;
                        Func<int,int,Vector3> cut=(a,b)=>Vector3.Lerp(pos[a],pos[b],val[a]/(val[a]-val[b]));
                        if(ins.Count==1)tri(cut(ins[0],outs[0]),cut(ins[0],outs[1]),cut(ins[0],outs[2]));
                        else if(ins.Count==3)tri(cut(outs[0],ins[0]),cut(outs[0],ins[1]),cut(outs[0],ins[2]));
                        else{Vector3 a=cut(ins[0],outs[0]),b=cut(ins[0],outs[1]),c=cut(ins[1],outs[0]),d=cut(ins[1],outs[1]);tri(a,b,c);tri(b,d,c);}
                    }
                }
                if(v.Count==0)continue;
                var m=new Mesh();m.name="SmoothLoopShell_"+z0;m.indexFormat=UnityEngine.Rendering.IndexFormat.UInt32;m.SetVertices(v);m.SetNormals(n);m.SetUVs(0,uv);m.SetTriangles(tr,0);m.RecalculateBounds();m.RecalculateTangents();
                AssetDatabase.CreateAsset(m,AssetDatabase.GenerateUniqueAssetPath(Root+"/Meshes/Loop_v2/"+m.name+".asset"));var go=new GameObject(m.name);go.transform.SetParent(geo);go.transform.position=Vector3.zero;go.transform.rotation=Quaternion.identity;go.transform.localScale=Vector3.one;go.AddComponent<MeshFilter>().sharedMesh=m;var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.TwoSided;go.AddComponent<MeshCollider>().sharedMesh=m;go.isStatic=true;
            }
            var art=world.Find("02 - Rock Formations");foreach(var t in art.Cast<Transform>().Where(t=>t.name=="Wall buttress"||t.name=="Hanging limestone").ToArray())UnityEngine.Object.DestroyImmediate(t.gameObject);
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Root+"/Prefabs/BoulderModule.prefab");
            for(float z=10;z<100;z+=3.2f)foreach(int side in new[]{-1,1})
            {
                if(z>40 && z<54)continue; // Keep the fork entrance clear of dressing.
                Vector3 c,t;Frame(z,out c,out t);var g=(GameObject)PrefabUtility.InstantiatePrefab(prefab);g.name="Loop wall buttress";g.transform.SetParent(art);g.transform.position=c+Vector3.Cross(Vector3.up,t)*(Width(z)-.2f)*side+Vector3.up*(Ground(z)+.8f);g.transform.rotation=Quaternion.Euler(0,z*17,0);g.transform.localScale=new Vector3(.65f,1.4f+Mathf.Sin(z)*.4f,.8f);
            }
        }
        static void RebuildWater(Transform world)
        {
            var water=world.Find("03 - Story Set Pieces/S2-3 - Flood and Resonance/Animated flood surface");
            var vertices=new List<Vector3>();var uv=new List<Vector2>();var triangles=new List<int>();const int nx=30,nz=36;
            for(int j=0;j<=nz;j++)for(int i=0;i<=nx;i++)
            {float x=Mathf.Lerp(18,38,(float)i/nx),z=Mathf.Lerp(55.5f,80.5f,(float)j/nz);vertices.Add(Warp(new Vector3(x,3.25f,z)));uv.Add(new Vector2(x,z)*.1f);}
            for(int j=0;j<nz;j++)for(int i=0;i<nx;i++){int a=j*(nx+1)+i,b=a+nx+1;triangles.AddRange(new[]{a,b,a+1,a+1,b,b+1});}
            var m=new Mesh();m.name="Curved flood surface";m.SetVertices(vertices);m.SetUVs(0,uv);m.SetTriangles(triangles,0);m.RecalculateNormals();m.RecalculateBounds();
            AssetDatabase.CreateAsset(m,meshes+"/CurvedWater.asset");water.position=Vector3.zero;water.rotation=Quaternion.identity;water.localScale=Vector3.one;water.GetComponent<MeshFilter>().sharedMesh=m;
        }
        static void CompactExterior(Transform world,Pose[] source)
        {
            var exterior=world.Find("Epilogue - Return to Daylight");var meadow=exterior.Find("Walkable meadow");
            meadow.position=Warp(new Vector3(25,6.65f,111));meadow.rotation=Turn(111);meadow.localScale=new Vector3(24,.5f,24);
            foreach(var pose in source.Where(p=>p.t.parent==exterior&&p.t.name.StartsWith("tree_oak")))
            {
                Vector3 p=pose.p;p.x=25+(p.x-25)*.35f;p.z=100+(p.z-100)*.4f;
                pose.t.position=Warp(p);pose.t.rotation=Turn(p.z)*pose.q;
            }
        }
        public static void Validate(Scene scene)
        {
            var e=scene.GetRootGameObjects().Select(g=>g.GetComponent<OrpheusCaveExperience>()).First(c=>c!=null);
            var checks=new List<string>();
            var points=e.route.Select(t=>t.position).Concat(new[]{e.deadEndAnchor.position}).ToArray();
            UnityEngine.AI.NavMeshHit start;
            if(!UnityEngine.AI.NavMesh.SamplePosition(e.transform.position,out start,3,UnityEngine.AI.NavMesh.AllAreas))throw new Exception("Loop spawn is not walkable.");
            foreach(var p in points)
            {
                UnityEngine.AI.NavMeshHit target;var path=new UnityEngine.AI.NavMeshPath();
                if(!UnityEngine.AI.NavMesh.SamplePosition(p,out target,2,UnityEngine.AI.NavMesh.AllAreas)||!UnityEngine.AI.NavMesh.CalculatePath(start.position,target.position,UnityEngine.AI.NavMesh.AllAreas,path)||path.status!=UnityEngine.AI.NavMeshPathStatus.PathComplete)
                    throw new Exception("Loop route disconnected at "+p);
                checks.Add("PathComplete");
            }
            File.WriteAllText(Root+"/Documentation/Loop_v2_validation.txt","Layout: radius 18m, main arc 285 degrees, smooth outward exit.\n"+checks.Count+" waypoint / dead-end NavMesh paths: PathComplete\nSource v1 scene and geometry preserved.\nHeadset and physical room scale not verified.\n");
        }
        public static void Capture(Scene scene)
        {
            var cameras=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Camera>(true)).ToArray();
            var camera=cameras.First(c=>c.name.StartsWith("Map Overview"));var water=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).First(t=>t.name=="Animated flood surface").GetComponent<Renderer>();
            var ambient=RenderSettings.ambientLight;bool fog=RenderSettings.fog;var prev=RenderTexture.active;var previousTarget=camera.targetTexture;float aspect=camera.aspect;
            var rt=new RenderTexture(1600,1600,24);var tex=new Texture2D(1600,1600,TextureFormat.RGB24,false);
            try
            {
                water.enabled=false;RenderSettings.ambientLight=new Color(.65f,.7f,.72f);RenderSettings.fog=false;
                camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.04f,.06f,.075f);camera.targetTexture=rt;camera.aspect=1;
                camera.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,1600,1600),0,0);tex.Apply();
                File.WriteAllBytes(Root+"/Documentation/Loop_v2_Overview.png",tex.EncodeToPNG());
            }
            finally {water.enabled=true;RenderSettings.ambientLight=ambient;RenderSettings.fog=fog;camera.targetTexture=previousTarget;camera.aspect=aspect;RenderTexture.active=prev;UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(tex);}
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
        }
    }
}
