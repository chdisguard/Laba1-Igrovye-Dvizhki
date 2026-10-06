using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(AsteroidMaterialApplicator)), CanEditMultipleObjects]
public sealed class AsteroidMaterialApplicatorEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        if (targets.Length == 1)
        {
            var component = (AsteroidMaterialApplicator)target;
            EditorGUILayout.LabelField("Материал", AsteroidMaterialLibrary.Get(component.MaterialNumber).name);
        }
        if (GUILayout.Button("Применить к астероиду"))
            foreach (var item in targets)
            {
                var component = (AsteroidMaterialApplicator)item;
                Undo.RecordObjects(component.GetComponentsInChildren<Renderer>(true), "Назначить материал астероида");
                component.Apply();
                foreach (var renderer in component.GetComponentsInChildren<Renderer>(true))
                {
                    EditorUtility.SetDirty(renderer);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                }
            }
    }
}
