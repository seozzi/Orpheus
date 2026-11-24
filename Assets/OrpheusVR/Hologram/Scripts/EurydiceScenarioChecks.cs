using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace OrpheusVR.Hologram
{
    // Exercises actual runtime state transitions; intended for editor Play mode only.
    public class EurydiceScenarioChecks : MonoBehaviour
    {
        [Serializable] public class Result {public string name;public bool passed;public string detail;}
        [Serializable] public class Report {public string testedAt;public bool passed;public Result[] checks;}
        public EurydiceDirector director;
        public bool Done {get;private set;}
        readonly List<Result> results=new List<Result>();
        void Check(string name,bool condition,string detail="")
        {detail+=" | phase="+director.phase;results.Add(new Result{name=name,passed=condition,detail=detail});Debug.Log("HOLOGRAM_TEST "+name+": "+condition+" "+detail);}
        public void Run(){StartCoroutine(RunAll());}
        IEnumerator Until(Func<bool> ready,float timeout)
        {
            float deadline=Time.time+timeout;
            while(!ready()&&Time.time<deadline)yield return null;
        }
        IEnumerator RunAll()
        {
            yield return null;yield return null;
            var d=director;var e=d.experience;float oldScale=Time.timeScale;Time.timeScale=6;d.muteForTests=true;
            d.DebugJump("intro");d.SkipIntro();yield return new WaitForSeconds(4);
            Check("intro and transparent appearance",d.phase==EurydiceDirector.Phase.Handhold&&d.actor.IsVisible&&d.CanStartJourney,"phase="+d.phase+", ready="+d.CanStartJourney);
            d.RequestJourney();yield return new WaitForSeconds(2);
            Check("handhold starts return",e.JourneyStarted&&d.phase==EurydiceDirector.Phase.Following,"phase="+d.phase);
            Vector3 before=d.actor.transform.position;e.SetPreviewLocation(e.transform.position+Vector3.forward,Vector3.forward);yield return new WaitForSeconds(1.5f);
            Check("follows player trail",Vector3.Distance(before,d.actor.transform.position)>.2f);
            e.SetPreviewLocation(e.transform.position,-e.RouteTangent);yield return new WaitForSeconds(1);
            Check("look-back pending is cancellable",d.phase!=EurydiceDirector.Phase.Lost&&d.GazeHold>.4f);
            e.SetPreviewLocation(e.transform.position,e.RouteTangent);yield return new WaitForSeconds(.3f);
            Check("look-back timer resets",d.GazeHold<.05f);
            e.SetPreviewLocation(e.transform.position,-e.RouteTangent);yield return new WaitForSeconds(3.3f);
            Check("120 degrees two seconds loses Eurydice",d.phase==EurydiceDirector.Phase.Lost&&e.IsLost&&d.actor.Dissolve>.95f);
            d.DebugJump("fall");yield return Until(()=>d.phase==EurydiceDirector.Phase.Fallen&&d.actor.CurrentPose==EurydiceHologramActor.Pose.Fallen&&d.actor.Dissolve<.05f,6);
            Check("flood triggers fallen PNG",d.phase==EurydiceDirector.Phase.Fallen&&d.actor.CurrentPose==EurydiceHologramActor.Pose.Fallen&&d.actor.IsVisible);
            Vector3 fallen=d.actor.transform.position;e.SetPreviewLocation(new Vector3(29,4.5f,65),Vector3.forward);yield return new WaitForSeconds(.6f);
            Check("injured character stays behind",Vector3.Distance(fallen,d.actor.transform.position)<.05f);
            d.DebugJump("choice");yield return new WaitForSeconds(.3f);d.ChooseWait();yield return new WaitForSeconds(3);
            Check("recovery waits at least four seconds",!d.Recovered);
            yield return Until(()=>d.Recovered&&d.phase==EurydiceDirector.Phase.Following,8);
            Check("waiting recovers standing character",d.Recovered&&d.phase==EurydiceDirector.Phase.Following&&d.actor.CurrentPose==EurydiceHologramActor.Pose.Standing);
            e.SetPreviewLocation(new Vector3(25,7.1f,103.3f),Vector3.forward);yield return new WaitForSeconds(15);
            Check("both outside enables reunion",d.phase==EurydiceDirector.Phase.Returned&&d.actor.Opacity>.99f);
            d.DebugJump("choice");yield return new WaitForSeconds(.3f);d.ChooseAlone();yield return new WaitForSeconds(.3f);
            Check("leave-alone branch",d.phase==EurydiceDirector.Phase.Alone&&d.CanExit&&!d.Recovered);
            d.DebugJump("choice");yield return new WaitForSeconds(.3f);e.SetPreviewLocation(e.transform.position,-e.RouteTangent);yield return new WaitForSeconds(3.1f);
            Check("turn at final choice stays underground",d.phase==EurydiceDirector.Phase.Stay&&e.scenarioHold&&!d.CanExit);
            Time.timeScale=6;d.ResetAndReplay();d.guidedDemo=true;d.SkipIntro();float deadline=Time.time+180;
            while(Time.time<deadline&&d.phase!=EurydiceDirector.Phase.Returned&&d.phase!=EurydiceDirector.Phase.Lost&&d.phase!=EurydiceDirector.Phase.Stay)yield return new WaitForSeconds(1);
            Check("guided demo reaches return ending",d.phase==EurydiceDirector.Phase.Returned,"phase="+d.phase+", station="+e.StoryStation);
            var report=new Report{testedAt=DateTime.UtcNow.ToString("o"),passed=results.TrueForAll(r=>r.passed),checks=results.ToArray()};
            string output=Path.Combine(Application.dataPath,"OrpheusVR/Hologram/Documentation/scenario_test_results.json");File.WriteAllText(output,JsonUtility.ToJson(report,true));
            Time.timeScale=oldScale;d.muteForTests=false;d.guidedDemo=false;Done=true;
            Debug.Log("HOLOGRAM_TEST completed: "+report.passed);
        }
    }
}
