using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using System.Collections.Generic;
using System.Linq;
using System.IO;

namespace OrpheusVR.Editor
{
    public static class OrpheusCaveBuilder
    {
        const string Root="Assets/OrpheusVR";
        static Transform world, geo, art, props, lights, markers;
        static Material rock, floor, darkRock, gold, jade, waterMat, grass, bark, leaves;
        static Mesh rockMesh;
        static System.Random rng;
        static int meshSerial;
        internal static readonly Vector3[] route={
            new Vector3(0,0,10),new Vector3(0,0,19),new Vector3(6,0,26),new Vector3(15,0,30),
            new Vector3(18,0,39),new Vector3(18,0,47),new Vector3(24,0,52),new Vector3(29,0,59),
            new Vector3(29,0,68),new Vector3(25,0,78),new Vector3(25,0,87),new Vector3(25,0,96),new Vector3(25,0,109)};
        struct Segment { public Vector3 a,b; public float r0,r1,h0,h1; public Segment(Vector3 p,Vector3 q,float r,float s,float h,float j){a=p;b=q;r0=r;r1=s;h0=h;h1=j;} }
        static List<Segment> segments;
        internal static float Ground(float z){return Mathf.Clamp((z-15)*.082f,0,6.9f);}
        static Vector3 P(float x,float z,float y=0){return new Vector3(x,Ground(z)+y,z);}
        static Transform Group(string n,Transform parent){var g=new GameObject(n);g.transform.SetParent(parent,false);return g.transform;}
        static float Rand(float a,float b){return Mathf.Lerp(a,b,(float)rng.NextDouble());}

        [MenuItem("Tools/Orpheus VR/Build Complete Cave Map")]
        public static void Build()
        {
            rng=new System.Random(1711); meshSerial=0;
            foreach(string d in new[]{"Scenes","Meshes","Materials","Textures","Prefabs","Documentation"})Directory.CreateDirectory(Root+"/"+d);
            AssetDatabase.Refresh();
            // Additive creation keeps every existing open scene and unsaved edit intact.
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            world=Group("ORPHEUS - Complete Underworld",null);
            geo=Group("01 - Continuous Cave Shell",world);art=Group("02 - Rock Formations",world);
            props=Group("03 - Story Set Pieces",world);lights=Group("04 - Wayfinding and Lighting",world);markers=Group("05 - Route and Event Anchors",world);
            SetupSegments(); Materials();
            for(int z=-12;z<100;z+=8) BuildShell(z,Mathf.Min(z+8,100));
            rockMesh=CreateRockMesh();
            RockDressing(); Story(); Exterior(); Lighting(); Player();
            var nav=world.gameObject.AddComponent<Unity.AI.Navigation.NavMeshSurface>();
            nav.collectObjects=Unity.AI.Navigation.CollectObjects.Children;
            nav.useGeometry=UnityEngine.AI.NavMeshCollectGeometry.PhysicsColliders;
            nav.overrideVoxelSize=true;nav.voxelSize=.15f;nav.BuildNavMesh();
            if(nav.navMeshData!=null)Save(nav.navMeshData,Root+"/Meshes/UnderworldNavMesh.asset");
            var mapCam=Group("Map Overview - disabled in play",world).gameObject.AddComponent<Camera>();
            mapCam.transform.position=new Vector3(14,115,46);mapCam.transform.rotation=Quaternion.Euler(90,0,0);mapCam.orthographic=true;mapCam.orthographicSize=66;mapCam.enabled=false;
            RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.18f,.23f,.26f);
            RenderSettings.fog=true;RenderSettings.fogMode=FogMode.ExponentialSquared;RenderSettings.fogColor=new Color(.055f,.09f,.10f);RenderSettings.fogDensity=.012f;
            var sky=new Material(Shader.Find("Skybox/Procedural"));sky.SetColor("_SkyTint",new Color(.46f,.66f,.8f));sky.SetColor("_GroundColor",new Color(.2f,.25f,.18f));sky.SetFloat("_AtmosphereThickness",.8f); Save(sky,Root+"/Materials/DaylightSky.mat");RenderSettings.skybox=sky;
            EditorSceneManager.SaveScene(scene,Root+"/Scenes/Orpheus_Underworld.unity");AssetDatabase.SaveAssets();
            foreach(var s in Enumerable.Range(0,SceneManager.sceneCount).Select(SceneManager.GetSceneAt).ToArray())
                if(s!=scene && !s.isDirty)EditorSceneManager.CloseScene(s,false);
            Selection.activeGameObject=world.gameObject;
            if(SceneView.lastActiveSceneView!=null)SceneView.lastActiveSceneView.LookAt(new Vector3(13,0,45),Quaternion.Euler(62,-18,0),75);
            Debug.Log("Orpheus cave map built: "+Root+"/Scenes/Orpheus_Underworld.unity");
        }

        static void Save(Object obj,string path){AssetDatabase.CreateAsset(obj,AssetDatabase.GenerateUniqueAssetPath(path));}
        internal static void SetupSegments()
        {
            segments=new List<Segment>();
            segments.Add(new Segment(new Vector3(0,0,-2),route[0],8,3.2f,8,4.8f));
            for(int i=0;i<route.Length-2;i++)
            {
                float r0=(i==7||i==8)?6.5f:(i==10?4.3f:3.2f),r1=(i+1==7||i+1==8)?6.5f:(i+1==10?4.3f:3.2f);
                segments.Add(new Segment(route[i],route[i+1],r0,r1,r0>5?8:5,r1>5?8:5));
            }
            segments.Add(new Segment(route[11],new Vector3(25,0,105),3.2f,4.2f,5,6));
            segments.Add(new Segment(route[5],new Vector3(9,0,52),3.1f,2.8f,5,4.6f));
            segments.Add(new Segment(new Vector3(9,0,52),new Vector3(5,0,58),2.8f,2.4f,4.6f,4));
        }

        internal static float Field(Vector3 p)
        {
            float f=999;
            foreach(var s in segments)
            {
                Vector2 a=new Vector2(s.a.x,s.a.z),b=new Vector2(s.b.x,s.b.z),q=new Vector2(p.x,p.z);
                Vector2 d=b-a; float t=Mathf.Clamp01(Vector2.Dot(q-a,d)/d.sqrMagnitude);
                float r=Mathf.Lerp(s.r0,s.r1,t),h=Mathf.Lerp(s.h0,s.h1,t);
                float horizontal=Vector2.Distance(q,a+d*t),up=(p.y-Ground(p.z))/h;
                f=Mathf.Min(f,(Mathf.Sqrt(horizontal*horizontal/(r*r)+up*up)-1)*r);
            }
            // Small coherent changes keep the silhouette natural without creating floor obstacles.
            float noise=(Mathf.PerlinNoise(p.x*.32f+p.y*.18f,p.z*.28f)-.5f)*.6f;
            noise+=(Mathf.PerlinNoise(p.x*.83f+p.z*.2f,p.y*.7f)-.5f)*.18f;
            return Mathf.Max(f+noise,Ground(p.z)-p.y);
        }
        static Vector3 Normal(Vector3 p)
        {
            const float e=.035f;
            return -new Vector3(Field(p+Vector3.right*e)-Field(p-Vector3.right*e),Field(p+Vector3.up*e)-Field(p-Vector3.up*e),Field(p+Vector3.forward*e)-Field(p-Vector3.forward*e)).normalized;
        }
        static readonly int[,] Tets={{0,5,1,6},{0,1,2,6},{0,2,3,6},{0,3,7,6},{0,7,4,6},{0,4,5,6}};
        static readonly Vector3[] Corner={new Vector3(0,0,0),new Vector3(1,0,0),new Vector3(1,0,1),new Vector3(0,0,1),new Vector3(0,1,0),new Vector3(1,1,0),new Vector3(1,1,1),new Vector3(0,1,1)};
        static void BuildShell(float z0,float z1)
        {
            const float step=1;
            var verts=new List<Vector3>();var norms=new List<Vector3>();var uv=new List<Vector2>();var tris=new List<int>();
            var pos=new Vector3[8];var val=new float[8];
            for(float x=-12;x<43;x+=step)for(float z=z0;z<z1;z+=step)for(float y=-2;y<18;y+=step)
            {
                int negative=0;
                for(int c=0;c<8;c++){pos[c]=new Vector3(x,y,z)+Corner[c]*step;val[c]=Field(pos[c]);if(val[c]<0)negative++;}
                if(negative==0||negative==8)continue;
                for(int t=0;t<6;t++)
                {
                    var ins=new List<int>(4);var outs=new List<int>(4);
                    for(int j=0;j<4;j++){int k=Tets[t,j];if(val[k]<0)ins.Add(k);else outs.Add(k);}
                    if(ins.Count==0||outs.Count==0)continue;
                    System.Func<int,int,Vector3> cut=(a,b)=>Vector3.Lerp(pos[a],pos[b],val[a]/(val[a]-val[b]));
                    if(ins.Count==1)Tri(cut(ins[0],outs[0]),cut(ins[0],outs[1]),cut(ins[0],outs[2]),verts,norms,uv,tris);
                    else if(ins.Count==3)Tri(cut(outs[0],ins[0]),cut(outs[0],ins[1]),cut(outs[0],ins[2]),verts,norms,uv,tris);
                    else {Vector3 a=cut(ins[0],outs[0]),b=cut(ins[0],outs[1]),c=cut(ins[1],outs[0]),d=cut(ins[1],outs[1]);Tri(a,b,c,verts,norms,uv,tris);Tri(b,d,c,verts,norms,uv,tris);}
                }
            }
            Mesh m=new Mesh();m.name="CaveShell_"+z0;m.indexFormat=IndexFormat.UInt32;m.SetVertices(verts);m.SetNormals(norms);m.SetUVs(0,uv);m.SetTriangles(tris,0);m.RecalculateBounds();m.RecalculateTangents();
            Save(m,Root+"/Meshes/"+m.name+".asset");
            var go=Group(m.name,geo).gameObject;go.AddComponent<MeshFilter>().sharedMesh=m;
            var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=rock;renderer.shadowCastingMode=ShadowCastingMode.TwoSided;
            go.AddComponent<MeshCollider>().sharedMesh=m;go.isStatic=true;
        }
        static void Tri(Vector3 a,Vector3 b,Vector3 c,List<Vector3> v,List<Vector3> n,List<Vector2> u,List<int> t)
        {
            Vector3 na=Normal(a),nb=Normal(b),nc=Normal(c);
            if(Vector3.Dot(Vector3.Cross(b-a,c-a),na+nb+nc)<0){var p=b;b=c;c=p;var q=nb;nb=nc;nc=q;}
            foreach(var p in new[]{a,b,c}) {v.Add(p);u.Add(new Vector2(p.x+p.y*.7f,p.z+p.y*.9f)*.22f);}
            n.Add(na);n.Add(nb);n.Add(nc);t.Add(v.Count-3);t.Add(v.Count-2);t.Add(v.Count-1);
        }

        static Material Mat(string name,Color color,float rough=0,Color? emission=null)
        {
            var m=new Material(Shader.Find("Universal Render Pipeline/Lit"));m.name=name;m.SetColor("_BaseColor",color);m.SetFloat("_Smoothness",rough);m.enableInstancing=true;
            if(emission.HasValue){m.EnableKeyword("_EMISSION");m.SetColor("_EmissionColor",emission.Value);}
            Save(m,Root+"/Materials/"+name+".mat");return m;
        }
        static void Materials()
        {
            rock=Mat("Slate - cave stone",new Color(.42f,.46f,.47f),.12f);
            var tex=AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"/Textures/cave-rock-albedo.png");if(tex)rock.SetTexture("_BaseMap",tex);
            floor=Mat("Basalt floor",new Color(.16f,.21f,.22f));darkRock=Mat("Dark basalt",new Color(.13f,.19f,.20f),.22f);
            gold=Mat("Amber wayfinding",new Color(.8f,.46f,.13f),.5f,new Color(2.3f,1.05f,.23f));
            jade=Mat("Soul jade",new Color(.18f,.68f,.64f),.3f,new Color(.25f,1.6f,1.4f));
            waterMat=Mat("Still underworld water",new Color(.025f,.19f,.22f),.94f);
            grass=Mat("Daylight meadow",new Color(.22f,.38f,.16f));bark=Mat("Tree bark",new Color(.22f,.16f,.1f));leaves=Mat("Leaves",new Color(.3f,.46f,.16f));
        }
        static Mesh CreateRockMesh()
        {
            var vs=new List<Vector3>();var ts=new List<int>();var us=new List<Vector2>();
            const int rows=7,cols=11;
            for(int i=0;i<=rows;i++)for(int j=0;j<=cols;j++)
            {float a=i*Mathf.PI/rows,b=j*2*Mathf.PI/cols;float r=1+.14f*Mathf.Sin(j*5.1f+i*3.4f);vs.Add(new Vector3(Mathf.Sin(a)*Mathf.Cos(b),Mathf.Cos(a),Mathf.Sin(a)*Mathf.Sin(b))*r);us.Add(new Vector2((float)j/cols,(float)i/rows));}
            for(int i=0;i<rows;i++)for(int j=0;j<cols;j++){int a=i*(cols+1)+j,b=a+cols+1;ts.AddRange(new[]{a,a+1,b,a+1,b+1,b});}
            var m=new Mesh();m.name="Reusable fractured boulder";m.SetVertices(vs);m.SetUVs(0,us);m.SetTriangles(ts,0);m.RecalculateNormals();m.RecalculateBounds();Save(m,Root+"/Meshes/FracturedBoulder.asset");return m;
        }
        static GameObject Boulder(string name,Vector3 p,Vector3 scale,Transform parent,Material mat=null,bool collide=true)
        {
            var g=Group(name,parent).gameObject;g.transform.position=p;g.transform.localScale=scale;g.transform.rotation=Quaternion.Euler(Rand(-12,12),Rand(0,360),Rand(-10,10));
            g.AddComponent<MeshFilter>().sharedMesh=rockMesh;g.AddComponent<MeshRenderer>().sharedMaterial=mat?mat:rock;
            if(collide)g.AddComponent<MeshCollider>().sharedMesh=rockMesh;g.isStatic=true;return g;
        }
        static GameObject Primitive(string name,PrimitiveType type,Vector3 p,Vector3 scale,Transform parent,Material mat,bool collision=true)
        {
            var g=GameObject.CreatePrimitive(type);g.name=name;g.transform.SetParent(parent);g.transform.position=p;g.transform.localScale=scale;g.GetComponent<Renderer>().sharedMaterial=mat;
            if(!collision)Object.DestroyImmediate(g.GetComponent<Collider>());g.isStatic=true;return g;
        }
        static void Point(string n,Vector3 p,Color c,float intensity,float range)
        {
            var l=Group(n,lights).gameObject.AddComponent<Light>();l.transform.position=p;l.type=LightType.Point;l.color=c;l.intensity=intensity;l.range=range;l.shadows=LightShadows.None;l.renderMode=LightRenderMode.Auto;
        }
        static GameObject Imported(string token,Vector3 pos,float size,Transform parent,Material material)
        {
            var path=AssetDatabase.FindAssets(token+" t:Model",new[]{Root+"/ThirdParty"}).Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault(p=>Path.GetFileNameWithoutExtension(p)==token);
            if(string.IsNullOrEmpty(path))return null;
            var source=AssetDatabase.LoadAssetAtPath<GameObject>(path);var g=(GameObject)PrefabUtility.InstantiatePrefab(source);g.transform.SetParent(parent);g.transform.position=Vector3.zero;
            var renderers=g.GetComponentsInChildren<Renderer>();if(renderers.Length==0)return g;
            Bounds b=renderers[0].bounds;foreach(var r in renderers)b.Encapsulate(r.bounds);
            float s=size/Mathf.Max(b.size.x,Mathf.Max(b.size.y,b.size.z));g.transform.localScale=Vector3.one*s;
            g.transform.position=pos-new Vector3(b.center.x,b.min.y,b.center.z)*s;
            foreach(var r in renderers)
                r.sharedMaterials=material?Enumerable.Repeat(material,r.sharedMaterials.Length).ToArray():r.sharedMaterials.Select(m=>m!=null&&m.name.ToLowerInvariant().Contains("leaf")?leaves:bark).ToArray();
            g.isStatic=true;return g;
        }
        static void RockDressing()
        {
            for(int i=0;i<route.Length-2;i++)
            {
                Vector3 a=route[i],b=route[i+1];Vector3 d=(b-a).normalized,side=Vector3.Cross(Vector3.up,d);
                float r=(i==7||i==8)?5.6f:2.9f;
                for(float t=0;t<Vector3.Distance(a,b);t+=3.1f)
                foreach(int sign in new[]{-1,1})
                {
                    Vector3 p=a+d*t+side*r*sign;p.y=Ground(p.z)+Rand(.1f,.7f);
                    Boulder("Wall buttress",p,new Vector3(Rand(.7f,1.2f),Rand(1.2f,2.8f),Rand(.7f,1.5f)),art);
                    if(rng.NextDouble()<.4)Boulder("Hanging limestone",p+Vector3.up*3.3f,new Vector3(.35f,Rand(.7f,1.4f),.4f),art,null,false);
                }
                for(float t=1;t<Vector3.Distance(a,b);t+=2.3f)
                {
                    if(i==5||i==6)continue; // The fork intentionally has no initial wayfinding.
                    Vector3 p=a+d*t+side*(i%2==0?-1:1)*1.1f;p.y=Ground(p.z)+.045f;
                    Primitive("Amber floor inlay",PrimitiveType.Cube,p,new Vector3(.12f,.025f,.65f),lights,gold,false).transform.rotation=Quaternion.LookRotation(d);
                }
            }
            // Introduce the downloaded CC0 rocks repeatedly as recognizable dressing.
            for(int i=0;i<18;i++) {Vector3 p=P(Rand(-6,6),Rand(-7,4));if(Mathf.Abs(p.x)<2)p.x+=3;Imported("rock_largeA",p,Rand(1.1f,2.3f),art,rock);}
            var sample=Boulder("Boulder module",new Vector3(500,0,0),Vector3.one,art);
            PrefabUtility.SaveAsPrefabAsset(sample,AssetDatabase.GenerateUniqueAssetPath(Root+"/Prefabs/BoulderModule.prefab"));Object.DestroyImmediate(sample);
        }
        static Transform Anchor(string n,Vector3 p){var t=Group(n,markers);t.position=p;return t;}
        static void Story()
        {
            var chamber=Group("S1 - Hades Chamber",props);
            Primitive("Hades dais",PrimitiveType.Cylinder,P(0,3,.2f),new Vector3(4,.2f,4),chamber,darkRock);
            var hades=Primitive("Hades apparition - replace with face asset",PrimitiveType.Sphere,P(0,3,2.5f),Vector3.one*1.1f,chamber,jade,false);
            Point("Hades jade aura",P(0,3,3),new Color(.25f,.85f,.76f),5,12);
            Anchor("Hades face / dialogue anchor",hades.transform.position);Anchor("Eurydice enters from left",P(-4,1,.2f));Anchor("Handhold interaction",P(-1,1,1.1f));
            foreach(int side in new[]{-1,1})for(int i=0;i<3;i++)Boulder("Chamber basalt column",P(side*(5+i*.4f),-4+i*4,2),new Vector3(.8f,2.6f,1),chamber,darkRock);
            var lyre=Group("S2-1 - Lyre pickup",props);lyre.position=P(13.5f,30,.8f);
            Boulder("Lyre plinth",P(13.5f,30,.25f),new Vector3(.7f,.25f,.6f),props,darkRock);
            for(int side=-1;side<=1;side+=2)Primitive("Lyre arm",PrimitiveType.Cylinder,lyre.position+new Vector3(side*.25f,.3f,0),new Vector3(.07f,.38f,.07f),lyre,gold,false);
            Primitive("Lyre bridge",PrimitiveType.Cube,lyre.position+new Vector3(0,.65f,0),new Vector3(.58f,.08f,.1f),lyre,gold,false);
            Primitive("Lyre bowl",PrimitiveType.Sphere,lyre.position,new Vector3(.65f,.3f,.15f),lyre,gold,false);
            for(int i=0;i<5;i++)Primitive("Lyre string",PrimitiveType.Cylinder,lyre.position+new Vector3(-.16f+i*.08f,.32f,0),new Vector3(.008f,.28f,.008f),lyre,jade,false);
            Point("Lyre highlight",lyre.position+Vector3.up,new Color(1,.67f,.3f),3,7);
            var alt=Group("S2-2 - Alternative route reveal",lights);alt.gameObject.SetActive(false);
            for(int i=0;i<5;i++)Primitive("Revealed jade route",PrimitiveType.Sphere,P(19+i*1.5f,48+i*1.5f,.07f),new Vector3(.14f,.06f,.35f),alt,jade,false);
            Anchor("Fork - no guidance initially",P(18,47));Anchor("Dead end / retreat trigger",P(5,58));Anchor("Eurydice fall / sound anchor",P(28,60));
            var flood=Group("S2-3 - Flood and Resonance",props);
            var w=Primitive("Animated flood surface",PrimitiveType.Cube,new Vector3(28,3.25f,68),new Vector3(20,.06f,25),flood,waterMat,false);w.isStatic=false;
            var stone=Boulder("Resonance stone - palm interaction",P(31,70,.8f),new Vector3(1.1f,.85f,1),flood,darkRock);
            Primitive("Resonant palm mark",PrimitiveType.Sphere,P(30.5f,69.3f,1.3f),new Vector3(.28f,.3f,.1f),flood,jade,false);
            Point("Resonance jade glow",P(30,69,2),new Color(.18f,.85f,.8f),4,9);
            for(int i=0;i<6;i++)Boulder("Resonating path stone "+i,P(27.5f+Mathf.Sin(i)*.4f,64+i*2.4f,.04f),new Vector3(.7f,.10f,.7f),flood,darkRock);
            var choices=Group("S3 - Choice threshold",props);
            Primitive("Wait pedestal",PrimitiveType.Cylinder,P(23.3f,88,.5f),new Vector3(.8f,.5f,.8f),choices,darkRock);
            var hour=Group("WAIT - hourglass interaction",choices);hour.position=P(23.3f,88,1.3f);
            foreach(int side in new[]{-1,1})Primitive("Hourglass cap",PrimitiveType.Cylinder,hour.position+Vector3.up*.35f*side,new Vector3(.5f,.04f,.5f),hour,gold,false);
            Primitive("Hourglass upper",PrimitiveType.Sphere,hour.position+Vector3.up*.15f,new Vector3(.3f,.3f,.3f),hour,jade,false);
            Primitive("Hourglass lower",PrimitiveType.Sphere,hour.position-Vector3.up*.15f,new Vector3(.3f,.3f,.3f),hour,jade,false);
            var leave=Anchor("LEAVE - luminous path interaction",P(26.6f,90,1));
            for(int i=0;i<8;i++)Primitive("Exit light inset",PrimitiveType.Cube,P(25,90+i,.04f),new Vector3(.4f,.025f,.6f),choices,gold,false);
            Point("Choice amber pool",P(25,88,3),new Color(1,.68f,.3f),4,10);
            Anchor("Eurydice recovery and reunion",P(22,105));Anchor("Ending gaze / journey trace",P(25,105));
            Anchor("REF_Lyre",lyre.position);Anchor("REF_Resonance",stone.transform.position);Anchor("REF_Wait",hour.position);Anchor("REF_Leave",leave.position);
            var all=Group("Walkable main route",markers);for(int i=0;i<route.Length;i++) {var a=Group("Waypoint "+i.ToString("00"),all);a.position=P(route[i].x,route[i].z,.2f);}
        }
        static void Exterior()
        {
            var outside=Group("Epilogue - Return to Daylight",world);
            Primitive("Walkable meadow",PrimitiveType.Cube,new Vector3(25,6.65f,122),new Vector3(70,.5f,48),outside,grass);
            for(int i=0;i<20;i++)
            {
                float x=25+(i%2==0?-1:1)*Rand(6,25),z=Rand(104,143);
                var g=Imported("tree_oak",new Vector3(x,6.9f,z),Rand(4,7),outside,null);
                if(g==null){Primitive("Tree trunk",PrimitiveType.Cylinder,new Vector3(x,8.4f,z),new Vector3(.45f,1.5f,.45f),outside,bark);Boulder("Leaf canopy",new Vector3(x,10.3f,z),new Vector3(2.4f,2.5f,2.2f),outside,leaves,false);}
            }
            for(int side=-1;side<=1;side+=2)for(int i=0;i<6;i++)Boulder("Rocky cave mouth",new Vector3(25+side*(4+i*1.1f),Ground(99)+2,97+i*.5f),new Vector3(1.8f,3.5f,2),outside,darkRock);
            for(int i=0;i<7;i++)Boulder("Cave mouth arch",new Vector3(19+i*2,12.5f+Mathf.Sin(i*Mathf.PI/6)*1.3f,98),new Vector3(1.8f,1.5f,1.5f),outside,darkRock);
        }
        static void Lighting()
        {
            for(int i=0;i<route.Length-1;i++)
            {
                if(i==5||i==6)continue;
                var p=P(route[i].x,route[i].z,2.5f);
                Point("Passage fill "+i,p,i%3==0?new Color(.3f,.65f,.68f):new Color(.85f,.61f,.35f),i==7||i==8?4:2.5f,10);
            }
            var sun=Group("Daylight - main directional",lights).gameObject.AddComponent<Light>();sun.type=LightType.Directional;sun.color=new Color(1,.9f,.72f);sun.intensity=1.5f;sun.shadows=LightShadows.Soft;sun.shadowStrength=.95f;sun.transform.rotation=Quaternion.Euler(48,-35,0);RenderSettings.sun=sun;
            Point("Daylight entering exit",P(25,101,3),new Color(1,.88f,.65f),12,15);
        }
        static void Player()
        {
            var player=Group("Player - Desktop and XR Preview",null).gameObject;player.transform.position=new Vector3(0,.2f,-3);
            var cc=player.AddComponent<CharacterController>();cc.height=1.75f;cc.radius=.25f;cc.center=new Vector3(0,.95f,0);cc.stepOffset=.2f;cc.slopeLimit=35;
            var camera=Group("Main Camera",player.transform).gameObject.AddComponent<Camera>();camera.tag="MainCamera";camera.transform.localPosition=new Vector3(0,1.65f,0);camera.nearClipPlane=.08f;camera.farClipPlane=180;camera.fieldOfView=75;camera.gameObject.AddComponent<AudioListener>();
            var exp=player.AddComponent<OrpheusCaveExperience>();exp.view=camera;
            exp.water=props.Find("S2-3 - Flood and Resonance/Animated flood surface");exp.resonanceStone=markers.Find("REF_Resonance");exp.lyre=markers.Find("REF_Lyre");exp.waitChoice=markers.Find("REF_Wait");exp.leaveChoice=markers.Find("REF_Leave");exp.alternateGuide=lights.Find("S2-2 - Alternative route reveal").gameObject;
            exp.route=markers.Find("Walkable main route").Cast<Transform>().ToArray();
            Anchor("Player spawn",player.transform.position);
        }
    }
}
