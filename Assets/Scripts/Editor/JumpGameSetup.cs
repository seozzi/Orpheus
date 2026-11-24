using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// 메뉴 RollBall > Setup Jump Game: 바닥, 공(플레이어), 카메라 추적을 현재 씬에 구성한다.
// 이미 있는 오브젝트는 다시 만들지 않으므로 여러 번 실행해도 안전하다.
public static class JumpGameSetup
{
    const string MaterialFolder = "Assets/Materials";
    static readonly Vector3 PlayerSpawn = new Vector3(-8f, 0.5f, 0f);

    [MenuItem("RollBall/Setup Jump Game")]
    public static void Setup()
    {
        // 바닥: 30 x 20 크기
        var ground = GameObject.Find("Ground");
        if (ground == null)
        {
            ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.position = Vector3.zero;
            ground.transform.localScale = new Vector3(3f, 1f, 2f);
            Undo.RegisterCreatedObjectUndo(ground, "Create Ground");
        }
        ground.GetComponent<MeshRenderer>().sharedMaterial = GetOrCreateMaterial("Ground", new Color(0.55f, 0.55f, 0.55f));

        // 플레이어 공
        var player = GameObject.Find("Player");
        if (player == null)
        {
            player = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            player.name = "Player";
            player.transform.position = PlayerSpawn;
            Undo.RegisterCreatedObjectUndo(player, "Create Player");
        }
        player.tag = "Player";
        player.GetComponent<MeshRenderer>().sharedMaterial = GetOrCreateMaterial("Player", new Color(0.2f, 0.45f, 1f));

        var rb = player.GetComponent<Rigidbody>();
        if (rb == null) rb = Undo.AddComponent<Rigidbody>(player);
        rb.mass = 1f;
        rb.linearDamping = 0.5f;
        rb.angularDamping = 0.5f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;

        if (player.GetComponent<PlayerController>() == null)
            Undo.AddComponent<PlayerController>(player);

        // 카메라 추적
        var cam = Camera.main;
        if (cam != null)
        {
            var follow = cam.GetComponent<CameraFollow>();
            if (follow == null) follow = Undo.AddComponent<CameraFollow>(cam.gameObject);
            follow.target = player.transform;
            cam.transform.position = player.transform.position + follow.offset;
            cam.transform.LookAt(player.transform.position + Vector3.up * 0.5f);
        }
        else
        {
            Debug.LogWarning("JumpGameSetup: Main Camera를 찾지 못해 카메라 추적을 설정하지 않았습니다.");
        }

        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("JumpGameSetup: 씬 구성 완료. Play를 눌러 방향키로 이동, 스페이스바로 점프하세요.");
    }

    static Material GetOrCreateMaterial(string name, Color color)
    {
        string path = $"{MaterialFolder}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat != null) return mat;

        if (!AssetDatabase.IsValidFolder(MaterialFolder))
            AssetDatabase.CreateFolder("Assets", "Materials");

        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        mat = new Material(shader);
        mat.SetColor("_BaseColor", color);
        mat.color = color;
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }
}
