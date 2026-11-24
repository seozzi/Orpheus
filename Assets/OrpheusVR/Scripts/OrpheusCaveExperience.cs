using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using CommonUsages = UnityEngine.XR.CommonUsages;

namespace OrpheusVR
{
    // Preview locomotion and spatial event hooks. Voice/character production is intentionally separate.
    public class OrpheusCaveExperience : MonoBehaviour
    {
        public Camera view;
        public Transform water, resonanceStone, lyre, waitChoice, leaveChoice;
        public GameObject alternateGuide;
        public Transform[] route;
        public bool loopLayout;
        public Transform deadEndAnchor;
        public float[] routeStations;
        bool journeyStarted;
        public float speed = 1.25f;
        public bool demoAutowalk;
        public string stage = "S1 / Hades chamber";
        CharacterController body;
        float yaw, pitch, turnHold, floodTime, waterBase;
        int waypoint;
        bool flooded, drained, played, lost, waiting, finished, showHelp = true;
        float waitUntil;
        Vector3 checkpoint;
        string message = "Explore the chamber. E / controller A: interact. Enter: begin the return.";

        void Start()
        {
            body = GetComponent<CharacterController>();
            yaw = transform.eulerAngles.y;
            checkpoint = transform.position;
            if (water) waterBase = water.position.y;
        }

        void Update()
        {
            if (!view || !body) return;
            var kb = Keyboard.current;
            if (kb != null && kb.hKey.wasPressedThisFrame) showHelp = !showHelp;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
            bool xr = XRSettings.isDeviceActive;
            Vector2 move = Vector2.zero;
            bool interact = kb != null && kb.eKey.wasPressedThisFrame;
            bool begin = kb != null && kb.enterKey.wasPressedThisFrame;
            if (xr)
            {
                var head = InputDevices.GetDeviceAtXRNode(XRNode.Head);
                Vector3 hp; Quaternion hr;
                if (head.TryGetFeatureValue(CommonUsages.devicePosition, out hp)) view.transform.localPosition = hp;
                if (head.TryGetFeatureValue(CommonUsages.deviceRotation, out hr)) view.transform.localRotation = hr;
                var left = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
                var rightDevice = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
                left.TryGetFeatureValue(CommonUsages.primary2DAxis, out move);
                bool a;
                if (rightDevice.TryGetFeatureValue(CommonUsages.primaryButton, out a)) { interact |= a && !lastA; begin |= a && !lastA; lastA = a; }
                Vector2 turn;
                if (rightDevice.TryGetFeatureValue(CommonUsages.primary2DAxis, out turn))
                {
                    if (Mathf.Abs(turn.x) > .7f && turnReady) { transform.Rotate(0, Mathf.Sign(turn.x)*30, 0); yaw=transform.eulerAngles.y; turnReady=false; }
                    if (Mathf.Abs(turn.x)<.3f) turnReady=true;
                }
                // Keep the collision capsule under the tracked head, including room-scale leaning.
                var local = view.transform.localPosition;
                body.height = Mathf.Clamp(local.y, 1.1f, 2.2f);
                body.center = new Vector3(local.x, body.height*.5f+.06f, local.z);
            }
            else
            {
                if (kb != null)
                {
                    move.x=(kb.dKey.isPressed?1:0)-(kb.aKey.isPressed?1:0);
                    move.y=(kb.wKey.isPressed?1:0)-(kb.sKey.isPressed?1:0);
                    if (kb.fKey.wasPressedThisFrame) { demoAutowalk=!demoAutowalk; if(demoAutowalk && (loopLayout ? !journeyStarted : transform.position.z<9)) BeginJourney(); }
                }
                if (Mouse.current != null && Mouse.current.rightButton.isPressed)
                {
                    var delta=Mouse.current.delta.ReadValue();
                    yaw+=delta.x*.12f; pitch=Mathf.Clamp(pitch-delta.y*.12f,-65,65);
                    transform.rotation=Quaternion.Euler(0,yaw,0);
                    view.transform.localRotation=Quaternion.Euler(pitch,0,0);
                }
            }
            if (begin && (loopLayout ? !journeyStarted : transform.position.z<9)) BeginJourney();
            if (demoAutowalk && !xr && route!=null && route.Length>0)
            {
                var target=route[Mathf.Clamp(waypoint,0,route.Length-1)].position;
                Vector3 d=target-transform.position; d.y=0;
                if(d.magnitude<1.2f && waypoint<route.Length-1) waypoint++;
                if(d.sqrMagnitude>.1f) { var q=Quaternion.LookRotation(d); transform.rotation=Quaternion.RotateTowards(transform.rotation,q,50*Time.deltaTime); yaw=transform.eulerAngles.y; move=Vector2.up; }
            }
            Vector3 forward=Vector3.ProjectOnPlane(view.transform.forward,Vector3.up).normalized;
            Vector3 right=Vector3.Cross(Vector3.up,forward);
            Vector3 velocity=(forward*move.y+right*move.x);
            if (velocity.sqrMagnitude>1) velocity.Normalize();
            if(waiting || finished) velocity=Vector3.zero;
            body.Move((velocity*speed+Vector3.down*3)*Time.deltaTime);
            if(transform.position.y<-10) MoveTo(checkpoint);

            float z=loopLayout ? NearestStoryStation() : transform.position.z;
            if(loopLayout && z>12)journeyStarted=true;
            if(loopLayout && !journeyStarted)z=0;
            stage=z<9?"S1 / Hades chamber":z<43?"S2-1 / Lyre passage":z<58?"S2-2 / Blind fork":z<82?"S2-3 / Flood gallery":z<100?"S3 / The choice":"Epilogue / Daylight";
            if(z>10 && z<100 && !lost)
            {
                Vector3 tangent=PathTangent();
                float angle=Vector3.Angle(tangent,forward);
                turnHold=angle>=120?turnHold+Time.deltaTime:0;
                if(turnHold>=2) { lost=true; message="Eurydice is lost. Continue the remaining journey alone."; }
                else if(angle>=70) message="Do not look back. Turn toward the passage ahead.";
            }
            if((loopLayout ? deadEndAnchor && Vector3.Distance(transform.position,deadEndAnchor.position)<3.5f : z>47 && transform.position.x<12) && alternateGuide) { alternateGuide.SetActive(true); message="A dead end. Walk backward to the fork, then take the illuminated passage."; }
            if(z>60 && !flooded) { flooded=true; floodTime=Time.time; message="The gallery is flooding. Find the resonating stone ahead."; }
            if(flooded && water)
            {
                float target=drained?waterBase:waterBase+1.8f;
                var p=water.position; p.y=Mathf.MoveTowards(p.y,target,Time.deltaTime*(drained?.65f:.22f)); water.position=p;
            }
            if(interact)
            {
                if(lyre && Vector3.Distance(view.transform.position,lyre.position)<3) { played=true; message="The lyre's melody quiets the spirits."; }
                else if(resonanceStone && Vector3.Distance(view.transform.position,resonanceStone.position)<3 && flooded && Time.time-floodTime>5)
                { drained=true; message=played?"The remembered melody resonates. The water recedes.":"The stone resonates. The water recedes. (Map preview)"; }
                else if(waitChoice && Vector3.Distance(view.transform.position,waitChoice.position)<3)
                { waiting=true; waitUntil=Time.time+4; message=lost?"Only silence answers. Continue to the exit.":"You chose to wait for Eurydice."; }
                else if(leaveChoice && Vector3.Distance(view.transform.position,leaveChoice.position)<3)
                { message="You chose to leave alone. Follow the light."; }
            }
            if(waiting && Time.time>waitUntil) { waiting=false; message=lost?"Continue alone.":"Eurydice has recovered. Leave together."; }
            if(z>103 && !finished) { finished=true; message=lost?"Daylight. You returned alone.":"Daylight. Map preview complete."; }
        }
        void BeginJourney()
        {
            journeyStarted=true;waypoint=1;
            MoveTo(loopLayout && route!=null && route.Length>1 ? route[0].position : new Vector3(0,.25f,10));
            if(loopLayout && route.Length>1) {var d=route[1].position-route[0].position;d.y=0;transform.rotation=Quaternion.LookRotation(d);yaw=transform.eulerAngles.y;}
            message="Follow the amber lights. Keep facing the direction of travel.";
        }
        float NearestStoryStation()
        {
            if(route==null || routeStations==null || routeStations.Length!=route.Length)return 0;
            float best=float.MaxValue,result=routeStations[0];
            for(int i=0;i<route.Length-1;i++)
            {
                Vector3 a=route[i].position,b=route[i+1].position,p=transform.position;a.y=b.y=p.y=0;
                Vector3 d=b-a;if(d.sqrMagnitude<.0001f)continue;
                float t=Mathf.Clamp01(Vector3.Dot(p-a,d)/d.sqrMagnitude),dist=(p-a-d*t).sqrMagnitude;
                if(dist<best){best=dist;result=Mathf.Lerp(routeStations[i],routeStations[i+1],t);}
            }
            return result;
        }
        bool lastA,turnReady=true;
        Vector3 PathTangent()
        {
            if(route==null || route.Length<2) return Vector3.forward;
            float best=float.MaxValue; Vector3 result=Vector3.forward;
            for(int i=0;i<route.Length-1;i++)
            {
                Vector3 a=route[i].position,b=route[i+1].position,p=transform.position; a.y=b.y=p.y=0;
                Vector3 d=b-a; float t=Mathf.Clamp01(Vector3.Dot(p-a,d)/d.sqrMagnitude);
                float dist=(p-a-d*t).sqrMagnitude;
                if(dist<best) { best=dist; result=d.normalized; }
            }
            return result;
        }
        void MoveTo(Vector3 p) { body.enabled=false; transform.position=p; body.enabled=true; }
        void OnGUI()
        {
            if(!showHelp || XRSettings.isDeviceActive) return;
            GUI.color=new Color(.82f,.93f,.92f);
            GUI.Box(new Rect(18,18,630,108),"");
            GUI.Label(new Rect(32,26,600,24),"ORPHEUS / LOVE IN THE HESITATION  |  "+stage);
            GUI.Label(new Rect(32,52,600,24),message);
            GUI.Label(new Rect(32,80,600,24),"WASD move  /  RMB look  /  E interact  /  Enter begin  /  F auto-tour  /  H hide");
        }
    }
}
