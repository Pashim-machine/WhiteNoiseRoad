using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;

public static class EmptyMeshFixer
{
    // БЕЗОПАСНО: обнуляем ТОЛЬКО действительно пустые меши (0 вершин / 0 субмешей)
    [MenuItem("Tools/Починить пустые меши в префабах дорог")]
    static void Fix()
    {
        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/PreFabs" });
        int fixedCount = 0;
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            bool dirty = false;
            foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh m = mf.sharedMesh;
                if (m != null && (m.subMeshCount == 0 || m.vertexCount == 0))
                {
                    Debug.Log($"[Fix] {path} -> '{mf.gameObject.name}': меш '{m.name}' ПУСТОЙ -> None");
                    mf.sharedMesh = null;
                    dirty = true;
                    fixedCount++;
                }
            }
            if (dirty) PrefabUtility.SaveAsPrefabAsset(root, path);
            PrefabUtility.UnloadPrefabContents(root);
        }
        Debug.Log(fixedCount == 0 ? "[Fix] Пустых мешей не найдено." : $"[Fix] Исправлено: {fixedCount}");
    }

    // ГЛАВНЫЙ ИНСТРУМЕНТ: конвертирует Quads -> Triangles.
    // Растер рендерил такие меши, а RTAS — нет (та самая ошибка 'Road').
    [MenuItem("Tools/Конвертировать меши дорог в треугольники (RTAS-fix)")]
    static void ConvertToTriangles()
    {
        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/PreFabs" });
        int converted = 0;
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            bool dirty = false;

            foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh m = mf.sharedMesh;
                if (m == null || m.vertexCount == 0 || HasTriangles(m)) continue;

                Mesh tri = Triangulate(m);
                if (tri == null)
                {
                    Debug.LogWarning($"[Tri] {path} -> '{mf.gameObject.name}': меш '{m.name}' не дал ни одного треугольника — пропускаю.");
                    continue;
                }

                AssetDatabase.AddObjectToAsset(tri, prefabAsset); // вшиваем в префаб
                mf.sharedMesh = tri;
                dirty = true;
                converted++;
                Debug.Log($"[Tri] {path} -> '{mf.gameObject.name}': топология -> Triangles ({tri.triangles.Length / 3} трис).");
            }

            if (dirty) PrefabUtility.SaveAsPrefabAsset(root, path);
            PrefabUtility.UnloadPrefabContents(root);
        }
        AssetDatabase.SaveAssets();
        Debug.Log(converted == 0
            ? "[Tri] Мешей с не-треугольной топологией не найдено."
            : $"[Tri] Готово! Конвертировано: {converted}. Дороги видимы, RTAS-ошибка исчезнет.");
    }

    // ЖАТЬ ВО ВРЕМЯ PLAY — ищет рантайм-объекты (чанки)
    [MenuItem("Tools/Найти меши без треугольников (в PLAY)")]
    static void FindInPlay()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[Scan] Сначала запусти игру, потом жми: ищем рантайм-чанки.");
            return;
        }
        int found = 0;
        foreach (MeshFilter mf in Object.FindObjectsByType<MeshFilter>(FindObjectsInactive.Include))
        {
            Mesh m = mf.sharedMesh;
            if (m != null && !HasTriangles(m))
            {
                Debug.LogError($"[Scan] '{FullName(mf.gameObject)}' -> меш '{m.name}' без треугольников — ломает RTAS!", mf.gameObject);
                found++;
            }
        }
        Debug.Log(found == 0 ? "[Scan] В Play-сцене таких мешей нет." : $"[Scan] Найдено: {found}");
    }

    static bool HasTriangles(Mesh m)
    {
        if (m.subMeshCount == 0 || m.vertexCount == 0) return false;
        for (int i = 0; i < m.subMeshCount; i++)
        {
            SubMeshDescriptor sub = m.GetSubMesh(i);
            if (sub.topology == MeshTopology.Triangles && sub.indexCount > 0) return true;
        }
        return false;
    }

    static Mesh Triangulate(Mesh src)
    {
        Mesh dst = new Mesh
        {
            name = src.name + "_Tri",
            vertices = src.vertices
        };

        // Копируем атрибуты только если они реально есть
        // (hasNormals/hasUV/hasUV2/hasColors в Unity не существует — используем HasVertexAttribute)
        if (src.HasVertexAttribute(VertexAttribute.Normal)) dst.normals = src.normals;
        if (src.HasVertexAttribute(VertexAttribute.TexCoord0)) dst.uv = src.uv;
        if (src.HasVertexAttribute(VertexAttribute.TexCoord1)) dst.uv2 = src.uv2;
        if (src.HasVertexAttribute(VertexAttribute.Color)) dst.colors = src.colors;

        dst.subMeshCount = src.subMeshCount;

        bool any = false;
        for (int s = 0; s < src.subMeshCount; s++)
        {
            MeshTopology topo = src.GetSubMesh(s).topology;
            int[] idx = src.GetIndices(s);
            List<int> tris = new List<int>();

            if (topo == MeshTopology.Triangles)
            {
                tris.AddRange(idx);
            }
            else if (topo == MeshTopology.Quads)
            {
                // 4 индекса квада -> 2 треугольника
                for (int i = 0; i + 3 < idx.Length; i += 4)
                {
                    tris.Add(idx[i]); tris.Add(idx[i + 1]); tris.Add(idx[i + 2]);
                    tris.Add(idx[i]); tris.Add(idx[i + 2]); tris.Add(idx[i + 3]);
                }
            }
            // Lines / LineStrip / Points в RTAS не нужны — пропускаем

            if (tris.Count > 0) any = true;
            dst.SetTriangles(tris, s);
        }

        if (!any)
        {
            Object.DestroyImmediate(dst);
            return null;
        }

        dst.RecalculateBounds();
        return dst;
    }

    static string FullName(GameObject go)
    {
        string path = go.name;
        Transform t = go.transform.parent;
        while (t != null) { path = t.name + "/" + path; t = t.parent; }
        return path;
    }
}