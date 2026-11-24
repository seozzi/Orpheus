using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

// One-time setup for the portable project copy only.
[InitializeOnLoad]
public static class OrpheusPortableFirstOpen
{
    const string Marker = "UserSettings/OrpheusHologramOpened.txt";
    const string ScenePath = "Assets/OrpheusVR/Scenes/Orpheus_Underworld_HologramTest.unity";
    static OrpheusPortableFirstOpen() { EditorApplication.delayCall += OpenOnce; }
    static void OpenOnce()
    {
        if (File.Exists(Marker)) return;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode)
        { EditorApplication.delayCall += OpenOnce; return; }
        if (SceneManager.GetActiveScene().isDirty || !File.Exists(ScenePath)) return;
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        Directory.CreateDirectory("UserSettings");
        File.WriteAllText(Marker, "The v1 hologram test scene was opened on first import.");
    }
}
