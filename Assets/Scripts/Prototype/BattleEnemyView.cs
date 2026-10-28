using UnityEngine;

namespace AlbedoGambit.Prototype
{
    public sealed class BattleEnemyView : MonoBehaviour
    {
        public int Participant { get; private set; } = -1;
        public void Bind(int participant) { Participant = participant; }
        private Mesh capMesh;
        public void SetCap(Material material)
        {
            var cap = transform.Find("Role Cap");
            if (cap == null)
            {
                var source = transform.Find("Capsule Visual").GetComponent<MeshFilter>();
                // Reuse the capsule's exact upper hemisphere, without another collider.
                var vertices = source.sharedMesh.vertices;
                var indices = source.sharedMesh.triangles;
                var top = new System.Collections.Generic.List<int>();
                for (int i = 0; i < indices.Length; i += 3)
                    if (vertices[indices[i]].y >= .499f && vertices[indices[i + 1]].y >= .499f && vertices[indices[i + 2]].y >= .499f)
                    { top.Add(indices[i]); top.Add(indices[i + 1]); top.Add(indices[i + 2]); }
                capMesh = Instantiate(source.sharedMesh); capMesh.name = "Enemy upper hemisphere";
                capMesh.triangles = top.ToArray(); capMesh.RecalculateBounds();
                cap = new GameObject("Role Cap", typeof(MeshFilter), typeof(MeshRenderer)).transform;
                cap.SetParent(transform, false);
                cap.localPosition = source.transform.localPosition;
                cap.localRotation = source.transform.localRotation;
                cap.localScale = source.transform.localScale * 1.003f;
                cap.GetComponent<MeshFilter>().sharedMesh = capMesh;
            }
            cap.GetComponent<Renderer>().sharedMaterial = material;
        }
        private void OnDestroy() { if (capMesh != null) Destroy(capMesh); }
    }
}
