using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace OrpheusVR.Hologram
{
    public class EurydiceDirector : MonoBehaviour
    {
        public enum Phase { Intro, Handhold, Departing, Following, Fallen, Choice, Waiting, Recovering, Returned, Alone, Lost, Stay }
        [Serializable] public class Line {public string id,speaker,text;public AudioClip clip;}
        public OrpheusCaveExperience experience;
        public EurydiceHologramActor actor;
        public AudioSource eurydiceVoice,hadesVoice;
        public Line[] lines;
        public Transform entranceAnchor,handAnchor,fallenAnchor,reunionAnchor;
        public AudioClip lyreCue;
        public Text subtitleText;
        public bool guidedDemo, muteForTests;
        public Phase phase=Phase.Intro;
        public string subtitle="",speaker="",ending="";
        public bool CanStartJourney {get{return phase==Phase.Handhold && handReady;}}
        public bool CanExit {get{return ending=="wait"&&recovered||ending=="alone"||phase==Phase.Lost;}}
        public float GazeHold {get;private set;}
        public float GazeAngle {get;private set;}
        public bool Recovered {get{return recovered;}}
        public int SpokenCount {get;private set;}
        readonly List<Vector3> trail=new List<Vector3>();
        bool journey,fallenOnce,choiceOnce,lyreOnce,humOnce,recovered,departing,exited,handReady;
        float nextWarning,fallAt;
        Font font;
        GUIStyle label,button;

        void Start()
        {
            if(subtitleText)subtitleText.font=Font.CreateDynamicFontFromOSFont(new[]{"Apple SD Gothic Neo","Malgun Gothic","Noto Sans CJK KR","Arial"},28);
            ResetAndReplay();
        }
        void Update()
        {
            if(!experience || !actor)return;
            var k=Keyboard.current;
            if(k!=null)
            {
                if(k.rKey.wasPressedThisFrame)ResetAndReplay();
                if(k.digit1Key.wasPressedThisFrame)DebugJump("intro");
                if(k.digit2Key.wasPressedThisFrame)DebugJump("follow");
                if(k.digit3Key.wasPressedThisFrame)DebugJump("fall");
                if(k.digit4Key.wasPressedThisFrame)DebugJump("choice");
                if(k.digit5Key.wasPressedThisFrame)ChooseWait();
                if(k.digit6Key.wasPressedThisFrame)ChooseAlone();
                if(k.digit7Key.wasPressedThisFrame)Lose(false);
                if(k.digit8Key.wasPressedThisFrame)Lose(true);
                if(k.tabKey.wasPressedThisFrame && phase==Phase.Intro)SkipIntro();
                if(phase==Phase.Handhold && (k.eKey.wasPressedThisFrame||k.enterKey.wasPressedThisFrame))RequestJourney();
            }
            if(phase==Phase.Handhold && experience.PreviewActionRequested){experience.PreviewActionRequested=false;RequestJourney();}
            if(journey && !fallenOnce && phase==Phase.Following)FollowTrail();
            if(recovered && phase==Phase.Following)FollowTrail();
            if(journey && experience.LyrePlayed && !lyreOnce)
            {lyreOnce=true;if(lyreCue)eurydiceVoice.PlayOneShot(lyreCue,.7f);StartCoroutine(Say("eurydice_laugh"));}
            if(journey && experience.IsFlooded && !fallenOnce)StartCoroutine(Fall());
            if(fallenOnce && experience.IsDrained && !humOnce && phase!=Phase.Lost && phase!=Phase.Stay)
            {humOnce=true;StartCoroutine(Say("eurydice_hum"));}
            float station=experience.StoryStation;
            if(journey && station>=82 && !choiceOnce && phase!=Phase.Lost && phase!=Phase.Stay)StartCoroutine(Choice());
            if(choiceOnce && ending=="" && station>94)experience.SetPreviewLocation(new Vector3(25,6.5f,92),Vector3.forward);
            if(choiceOnce && ending=="" && experience.SelectedEnding=="wait")ChooseWait();
            if(choiceOnce && ending=="" && experience.SelectedEnding=="alone")ChooseAlone();
            if(guidedDemo)
            {
                if(phase==Phase.Handhold && !departing)RequestJourney();
                if(phase==Phase.Following || phase==Phase.Fallen)experience.demoAutowalk=true;
                if(!lyreOnce && Vector3.Distance(experience.view.transform.position,experience.lyre.position)<2.8f)experience.PreviewActionRequested=true;
                if(phase==Phase.Fallen && Time.time-fallAt>5 && Vector3.Distance(experience.view.transform.position,experience.resonanceStone.position)<2.8f)experience.PreviewActionRequested=true;
            }
            bool gazeActive=journey && !guidedDemo && !exited && ending!="alone" && phase!=Phase.Lost && phase!=Phase.Stay && phase!=Phase.Departing;
            if(gazeActive)
            {
                Vector3 f=Vector3.ProjectOnPlane(experience.view.transform.forward,Vector3.up).normalized;
                GazeAngle=Vector3.Angle(experience.RouteTangent,f);
                GazeHold=GazeAngle>=120?GazeHold+Time.deltaTime:0;
                if(GazeAngle>=70 && Time.time>nextWarning){nextWarning=Time.time+6;StartCoroutine(Say("hades_warning"));}
                if(GazeHold>=2)Lose(choiceOnce);
            }
            else {GazeAngle=0;GazeHold=0;}
            if(recovered && station>103 && !exited && experience.transform.position.z>100 && actor.transform.position.z>=100)
            {exited=true;phase=Phase.Returned;actor.SetOpacity(1);StartCoroutine(Say("eurydice_exit"));}
            if(ending=="alone" && station>103)exited=true;
            if(subtitleText)subtitleText.text=(speaker.Length>0?speaker+"\n":"")+subtitle;
        }
        public void ResetAndReplay()
        {
            StopAllCoroutines();eurydiceVoice.Stop();hadesVoice.Stop();actor.CancelFade();actor.SetDissolve(1);actor.SetOpacity(.7f);actor.SetPose(EurydiceHologramActor.Pose.Standing);
            experience.ResetScenarioPreview();experience.scenarioHold=true;phase=Phase.Intro;
            journey=fallenOnce=choiceOnce=lyreOnce=humOnce=recovered=departing=exited=handReady=false;GazeHold=0;ending="";trail.Clear();subtitle="";speaker="";
            StartCoroutine(Intro());
        }
        IEnumerator Intro()
        {
            yield return Say("hades_intro");
            yield return Appear();
        }
        IEnumerator Appear()
        {
            phase=Phase.Handhold;experience.scenarioHold=false;actor.SetFeet(entranceAnchor.position);actor.SetPose(EurydiceHologramActor.Pose.Standing);actor.Fade(true);
            Vector3 a=actor.transform.position,b=handAnchor.position;
            for(float t=0;t<1.5f;t+=Time.deltaTime){actor.SetFeet(Vector3.Lerp(a,b,t/1.5f));yield return null;}
            actor.SetFeet(b);yield return Say("eurydice_hand");handReady=true;
        }
        public void SkipIntro()
        {
            if(phase!=Phase.Intro && phase!=Phase.Handhold)return;
            StopAllCoroutines();eurydiceVoice.Stop();hadesVoice.Stop();StartCoroutine(Appear());
        }
        public void RequestJourney()
        {
            if(phase!=Phase.Handhold||departing||!handReady)return;
            // This button is the prototype equivalent of a handhold, not articulated hand tracking.
            if(!guidedDemo && Vector3.Distance(experience.view.transform.position,actor.transform.position)>4.5f)
            {subtitle="가까이 다가와 E / 컨트롤러 A로 손을 잡아 주세요.";speaker="안내";return;}
            StopAllCoroutines();departing=true;StartCoroutine(Depart());
        }
        IEnumerator Depart()
        {
            phase=Phase.Departing;experience.scenarioHold=true;
            yield return Say("hades_start");actor.Fade(false,.5f);yield return new WaitForSeconds(.55f);
            experience.BeginJourney();experience.scenarioHold=false;experience.demoAutowalk=guidedDemo;
            PlaceBehind();phase=Phase.Following;journey=true;departing=false;actor.Fade(true,.6f);
        }
        void PlaceBehind()
        {
            Vector3 p=experience.transform.position-experience.RouteTangent*2.5f;actor.SetFeet(p);trail.Clear();trail.Add(actor.transform.position);trail.Add(experience.transform.position);
        }
        void FollowTrail()
        {
            Vector3 player=experience.transform.position;
            if(trail.Count==0 || Vector3.Distance(trail[trail.Count-1],player)>.2f)trail.Add(player);
            if(trail.Count>500)trail.RemoveRange(0,100);
            float length=0;Vector3 target=trail[0];
            for(int i=trail.Count-1;i>0;i--)
            {float d=Vector3.Distance(trail[i],trail[i-1]);if(length+d>=2.5f){target=Vector3.Lerp(trail[i],trail[i-1],(2.5f-length)/Mathf.Max(d,.001f));break;}length+=d;}
            actor.SetFeet(Vector3.MoveTowards(actor.transform.position,target,1.7f*Time.deltaTime));
        }
        IEnumerator Fall()
        {
            fallenOnce=true;phase=Phase.Fallen;fallAt=Time.time;
            actor.Fade(false,.22f);yield return new WaitForSeconds(.25f);actor.SetFeet(fallenAnchor.position);actor.SetPose(EurydiceHologramActor.Pose.Fallen);actor.Fade(true,.35f);
            yield return Say("eurydice_fall");
        }
        IEnumerator Choice()
        {
            choiceOnce=true;phase=Phase.Choice;experience.scenarioHold=true;experience.demoAutowalk=false;
            yield return Say("hades_choice");experience.scenarioHold=false;
            if(guidedDemo)ChooseWait();
        }
        public void ChooseWait()
        {
            if(!choiceOnce || ending!="" || phase==Phase.Lost||phase==Phase.Stay)return;
            StopAllCoroutines();ending="wait";experience.SelectedEnding="wait";StartCoroutine(WaitAndRecover());
        }
        IEnumerator WaitAndRecover()
        {
            phase=Phase.Waiting;experience.scenarioHold=true;experience.demoAutowalk=false;
            yield return Say("hades_wait");yield return new WaitForSeconds(4);
            phase=Phase.Recovering;actor.Fade(false,.35f);yield return new WaitForSeconds(.4f);actor.SetPose(EurydiceHologramActor.Pose.Standing);PlaceBehind();actor.Fade(true,.75f);
            yield return Say("eurydice_recover");recovered=true;phase=Phase.Following;experience.scenarioHold=false;experience.demoAutowalk=guidedDemo;
        }
        public void ChooseAlone()
        {
            if(!choiceOnce||ending!=""||phase==Phase.Lost||phase==Phase.Stay)return;
            StopAllCoroutines();ending="alone";experience.SelectedEnding="alone";phase=Phase.Alone;StartCoroutine(Leave());
        }
        IEnumerator Leave(){experience.scenarioHold=true;yield return Say("hades_leave");experience.scenarioHold=false;experience.demoAutowalk=guidedDemo;}
        public void Lose(bool stay)
        {
            if(phase==Phase.Lost||phase==Phase.Stay)return;
            StopAllCoroutines();eurydiceVoice.Stop();hadesVoice.Stop();experience.MarkEurydiceLost();experience.scenarioHold=stay;experience.demoAutowalk=false;
            phase=stay?Phase.Stay:Phase.Lost;ending=stay?"stay":"lost";actor.Fade(false,.85f);StartCoroutine(Say(stay?"hades_stay":"hades_lost"));
        }
        IEnumerator Say(string id)
        {
            var line=Array.Find(lines,l=>l.id==id);if(line==null)yield break;
            speaker=line.speaker=="Eurydice"?"에우리디케":line.speaker=="Hades"?"하데스":"정령";subtitle=line.text;SpokenCount++;
            var source=line.speaker=="Eurydice"?eurydiceVoice:hadesVoice;
            if(!muteForTests && line.clip && line.clip.length>.1f){source.Stop();source.pitch=line.speaker=="Hades"?.9f:1;source.clip=line.clip;source.Play();yield return new WaitForSeconds(line.clip.length/source.pitch);}
            else yield return new WaitForSeconds(muteForTests?.08f:Mathf.Max(2,line.text.Length*.11f));
        }
        public void StartGuidedDemo()
        {
            guidedDemo=true;
            if(phase==Phase.Intro)SkipIntro();
            else if(phase==Phase.Handhold)RequestJourney();
            else if(phase==Phase.Following||phase==Phase.Fallen)experience.demoAutowalk=!experience.demoAutowalk;
        }
        public void DebugJump(string point)
        {
            StopAllCoroutines();eurydiceVoice.Stop();hadesVoice.Stop();guidedDemo=false;actor.CancelFade();experience.ResetScenarioPreview();trail.Clear();
            journey=fallenOnce=choiceOnce=lyreOnce=humOnce=recovered=departing=exited=false;ending="";GazeHold=0;nextWarning=Time.time+2;
            if(point=="intro"){ResetAndReplay();return;}
            journey=true;experience.BeginJourney();experience.scenarioHold=false;
            if(point=="follow"){experience.SetPreviewLocation(new Vector3(0,.6f,19),new Vector3(6,0,7));phase=Phase.Following;actor.SetPose(EurydiceHologramActor.Pose.Standing);PlaceBehind();}
            else if(point=="fall"){experience.SetPreviewLocation(new Vector3(29,4,61),Vector3.forward);phase=Phase.Following;PlaceBehind();}
            else {experience.SetPreviewLocation(new Vector3(25,6,85),Vector3.forward);fallenOnce=true;phase=Phase.Fallen;actor.SetFeet(fallenAnchor.position);actor.SetPose(EurydiceHologramActor.Pose.Fallen);StartCoroutine(Choice());}
            actor.SetDissolve(0);
        }
        void OnGUI()
        {
            if(experience==null||UnityEngine.XR.XRSettings.isDeviceActive)return;
            if(font==null)font=Font.CreateDynamicFontFromOSFont(new[]{"Apple SD Gothic Neo","Malgun Gothic","Noto Sans CJK KR","Arial"},16);
            if(label==null){label=new GUIStyle(GUI.skin.label){font=font,fontSize=16,wordWrap=true};button=new GUIStyle(GUI.skin.button){font=font,fontSize=14};}
            float y=Mathf.Max(145,Screen.height-255);GUI.Box(new Rect(16,y,Mathf.Min(850,Screen.width-32),237),"");
            GUI.Label(new Rect(30,y+8,800,25),"에우리디케 2D 홀로그램 · "+phase+"  |  뒤돌아보기 "+GazeHold.ToString("0.0")+" / 2초",label);
            GUI.Label(new Rect(30,y+39,Mathf.Min(810,Screen.width-60),65),speaker+": "+subtitle,label);
            string[] names={"1 등장 다시","2 동행 테스트","3 넘어짐","4 선택 지점","5 기다리기","6 홀로 나가기","7 상실","8 함께 남기"};
            for(int i=0;i<8;i++){int index=i;float bx=30+(i%4)*196,by=y+110+(i/4)*36;if(GUI.Button(new Rect(bx,by,190,31),names[i],button)){if(index<4)DebugJump(new[]{"intro","follow","fall","choice"}[index]);else if(index==4)ChooseWait();else if(index==5)ChooseAlone();else Lose(index==7);}}
            if(GUI.Button(new Rect(30,y+187,190,30),"F 자동 시나리오 테스트",button))StartGuidedDemo();
            if(GUI.Button(new Rect(232,y+187,190,30),"Tab 도입 설명 건너뛰기",button))SkipIntro();
            if(phase==Phase.Handhold && GUI.Button(new Rect(434,y+187,190,30),"E 손 잡고 출발",button))RequestJourney();
        }
    }
}
