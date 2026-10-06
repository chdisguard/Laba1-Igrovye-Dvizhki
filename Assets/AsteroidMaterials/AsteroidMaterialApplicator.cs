using UnityEngine;

[DisallowMultipleComponent]
[AddComponentMenu("Asteroids/Material Applicator")]
public sealed class AsteroidMaterialApplicator : MonoBehaviour
{
    [Tooltip("Номер из списка 00–50; этот же номер — сид текстуры.")]
    [SerializeField, Range(0, 50)] private int materialNumber;
    [SerializeField, Range(.1f, 10f)] private float textureScale = 2f;
    public int MaterialNumber => materialNumber;
    private void Awake() => Apply();
    [ContextMenu("Применить материал")]
    public void Apply() => AsteroidMaterialLibrary.Apply(gameObject, materialNumber, textureScale);
    public void SetMaterial(int number)
    {
        AsteroidMaterialLibrary.Apply(gameObject, number, textureScale);
        materialNumber = number;
    }
}
