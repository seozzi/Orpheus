using UnityEngine;

namespace OrpheusVR
{
    /// <summary>한글이 나오는 OS 폰트로 TextMesh를 만드는 도우미 (macOS / Windows / Android·Quest 순으로 찾음)</summary>
    public static class OrpheusText
    {
        static Font korean;

        public static Font Korean
        {
            get
            {
                if (!korean) korean = Font.CreateDynamicFontFromOSFont(new[] { "Apple SD Gothic Neo", "Malgun Gothic", "Noto Sans CJK KR", "Noto Sans KR", "NotoSansCJK-Regular", "Arial" }, 64);
                return korean;
            }
        }

        public static TextMesh Create(Transform parent, string name, float characterSize, TextAnchor anchor)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var tm = go.AddComponent<TextMesh>();
            tm.anchor = anchor;
            tm.alignment = TextAlignment.Center;
            tm.fontSize = 64;
            tm.characterSize = characterSize;
            Font f = Korean;
            if (f) { tm.font = f; go.GetComponent<MeshRenderer>().sharedMaterial = f.material; }
            return tm;
        }
    }
}
