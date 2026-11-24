using System.Collections.Generic;
using UnityEngine;

namespace OrpheusVR
{
    /// <summary>
    /// 대사 자막 (대사 음성 VO가 들어오기 전 임시 + 이후 자막용).
    /// 시야 아래쪽 앞에 월드 공간 글자가 머리를 천천히 따라옴 (헤드셋·데스크톱 공통).
    /// Say()로 줄을 쌓으면 순서대로 보여 줌.
    /// </summary>
    public class OrpheusSubtitle : MonoBehaviour
    {
        public OrpheusPlayer player;
        public float distance = 1.6f;
        public float dropBelowEye = 0.35f;
        public float followSpeed = 2.5f;
        public float characterSize = 0.012f;
        [Tooltip("한 줄 최대 글자 수 (자동 줄바꿈)")]
        public int wrap = 24;

        struct Line { public string speaker, text; public float seconds; }
        readonly Queue<Line> queue = new Queue<Line>();
        TextMesh mesh;
        Transform holder;
        string current = "", status = "";
        float until;

        public bool Busy => queue.Count > 0 || Time.time < until;

        void Start()
        {
            mesh = OrpheusText.Create(transform, "Subtitle (world)", characterSize, TextAnchor.MiddleCenter);
            holder = mesh.transform;
            mesh.color = new Color(.92f, .96f, .94f);
        }

        /// <summary>대사 한 줄 추가. seconds 0이면 글자 수로 자동 계산</summary>
        public void Say(string speaker, string text, float seconds = 0f)
        {
            if (seconds <= 0f) seconds = Mathf.Clamp(1.2f + text.Length * 0.09f, 2.2f, 9f);
            queue.Enqueue(new Line { speaker = speaker, text = text, seconds = seconds });
        }

        public void Clear() { queue.Clear(); until = 0f; current = ""; }

        /// <summary>대사와 별개로 짧게 띄우는 상태 문구 (경고 등). 빈 문자열이면 지움</summary>
        public void SetStatus(string s) { status = s ?? ""; }

        void LateUpdate()
        {
            if (Time.time >= until)
            {
                if (queue.Count > 0)
                {
                    var l = queue.Dequeue();
                    current = (string.IsNullOrEmpty(l.speaker) ? "" : l.speaker + "\n") + Wrap(l.text);
                    until = Time.time + l.seconds;
                }
                else current = "";
            }
            if (!mesh || !player) return;
            string shown = string.IsNullOrEmpty(status) ? current : (string.IsNullOrEmpty(current) ? status : current + "\n\n" + status);
            mesh.text = shown;

            Vector3 target = player.HeadPosition + player.HeadForwardFlat * distance - Vector3.up * dropBelowEye;
            float k = 1f - Mathf.Exp(-followSpeed * Time.deltaTime);
            holder.position = Vector3.Distance(holder.position, target) > 4f ? target : Vector3.Lerp(holder.position, target, k);
            Vector3 look = holder.position - player.HeadPosition; look.y = 0f;
            if (look.sqrMagnitude > 1e-4f) holder.rotation = Quaternion.LookRotation(look);
        }

        string Wrap(string s)
        {
            if (wrap <= 0 || s.Length <= wrap) return s;
            var sb = new System.Text.StringBuilder();
            int count = 0;
            foreach (string word in s.Split(' '))
            {
                if (count > 0 && count + word.Length > wrap) { sb.Append('\n'); count = 0; }
                else if (count > 0) { sb.Append(' '); count++; }
                sb.Append(word); count += word.Length;
            }
            return sb.ToString();
        }
    }
}
