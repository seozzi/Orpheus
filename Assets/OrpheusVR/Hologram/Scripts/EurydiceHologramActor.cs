using System.Collections;
using UnityEngine;

namespace OrpheusVR.Hologram
{
    public class EurydiceHologramActor : MonoBehaviour
    {
        public enum Pose { Standing, Fallen }
        public Camera viewer;
        public MeshRenderer image;
        public Texture2D standing, fallen;
        public float standingHeight=1.75f, fallenHeight=1.12f;
        public Pose CurrentPose {get;private set;}
        public float Dissolve {get;private set;}=1;
        public float Opacity {get;private set;}=.7f;
        public bool IsVisible {get{return Dissolve<.99f;}}
        MaterialPropertyBlock block;
        Coroutine fade;
        void Awake(){block=new MaterialPropertyBlock();SetPose(Pose.Standing);SetDissolve(1);}
        void LateUpdate()
        {
            if(!viewer || !image)return;
            Vector3 away=transform.position-viewer.transform.position;away.y=0;
            if(away.sqrMagnitude>.001f)transform.rotation=Quaternion.LookRotation(away);
        }
        public void SetPose(Pose pose)
        {
            CurrentPose=pose;var tex=pose==Pose.Standing?standing:fallen;
            float height=pose==Pose.Standing?standingHeight:fallenHeight;
            image.transform.localScale=new Vector3(height*tex.width/tex.height,height,1);
            if(block==null)block=new MaterialPropertyBlock();
            image.GetPropertyBlock(block);block.SetTexture("_MainTex",tex);image.SetPropertyBlock(block);
        }
        public void SetDissolve(float amount)
        {
            Dissolve=Mathf.Clamp01(amount);if(block==null)block=new MaterialPropertyBlock();
            image.GetPropertyBlock(block);block.SetFloat("_Dissolve",Dissolve);block.SetFloat("_Opacity",Opacity);image.SetPropertyBlock(block);
        }
        public void SetOpacity(float value){Opacity=value;SetDissolve(Dissolve);}
        public void Fade(bool visible,float seconds=.8f)
        {
            if(fade!=null)StopCoroutine(fade);fade=StartCoroutine(FadeRoutine(visible,seconds));
        }
        IEnumerator FadeRoutine(bool visible,float seconds)
        {
            float start=Dissolve,end=visible?0:1;
            for(float t=0;t<seconds;t+=Time.deltaTime){SetDissolve(Mathf.Lerp(start,end,Mathf.SmoothStep(0,1,t/seconds)));yield return null;}
            SetDissolve(end);fade=null;
        }
        public void CancelFade(){if(fade!=null)StopCoroutine(fade);fade=null;}
        public void SetFeet(Vector3 p)
        {
            RaycastHit hit;
            if(Physics.Raycast(p+Vector3.up*2,Vector3.down,out hit,5,~0,QueryTriggerInteraction.Ignore))p.y=hit.point.y+.025f;
            transform.position=p;
        }
    }
}
