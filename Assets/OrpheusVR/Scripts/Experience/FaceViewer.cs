using UnityEngine;

namespace OrpheusVR
{
    /// <summary>라벨이 항상 카메라를 향하도록 (Y축만)</summary>
    public class FaceViewer : MonoBehaviour
    {
        Transform cam;
        void LateUpdate()
        {
            if (!cam) { var c = Camera.main; if (!c) return; cam = c.transform; }
            Vector3 d = transform.position - cam.position; d.y = 0f;
            if (d.sqrMagnitude > 1e-4f) transform.rotation = Quaternion.LookRotation(d);
        }
    }
}
