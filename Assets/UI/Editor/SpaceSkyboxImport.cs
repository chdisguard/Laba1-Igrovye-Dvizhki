using System;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class SpaceSkyboxImport
{
    static SpaceSkyboxImport() { EditorApplication.delayCall += Configure; }

    public static void Configure()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        { EditorApplication.delayCall += Configure; return; }
        var shader = Shader.Find("Skybox/6 Sided");
        if (shader == null) throw new InvalidOperationException("Built-in Skybox/6 Sided shader missing.");
        for (int number = 1; number <= 3; number++)
        {
            var path = "Assets/LocationSkies/Space" + number;
            var material = AssetDatabase.LoadAssetAtPath<Material>(path + ".mat");
            if (material == null) continue;
            if (material.shader == shader && material.GetTexture("_FrontTex") != null) continue;
            material.shader = shader;
            var faces = new[] { "front", "back", "right", "left", "top", "bot" };
            var properties = new[] { "_FrontTex", "_BackTex", "_LeftTex", "_RightTex", "_UpTex", "_DownTex" };
            for (int i = 0; i < faces.Length; i++)
            {
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path + "_" + faces[i] + ".png");
                if (texture == null) throw new InvalidOperationException("Skybox texture missing: " + path + "_" + faces[i]);
                material.SetTexture(properties[i], texture);
            }
            material.SetColor("_Tint", new Color(0.5f, 0.5f, 0.5f, 0.5f));
            material.SetFloat("_Exposure", 1);
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssetIfDirty(material);
        }
    }
}
