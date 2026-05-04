using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// NEU Surface Defect 텍스처 관리자
/// Assets/Resources/NEU_Textures/ 폴더에서 자동 로드
///
/// 폴더 구조 (Resources 하위):
/// Assets/Resources/NEU_Textures/
///   crazing/
///   inclusion/
///   patches/
///   pitted/
///   rolled/
///   scratches/
/// </summary>
public class NEUTextureManager : MonoBehaviour
{
    public static NEUTextureManager Instance;

    // 런타임에 자동 로드된 텍스처
    private Dictionary<NEUDefectType, List<Texture2D>> texturePool
        = new Dictionary<NEUDefectType, List<Texture2D>>();

    // 폴더명 매핑
    private static readonly Dictionary<NEUDefectType, string> FolderMap
        = new Dictionary<NEUDefectType, string>
    {
        { NEUDefectType.Crazing,       "NEU_Textures/crazing"       },
        { NEUDefectType.Inclusion,     "NEU_Textures/inclusion"     },
        { NEUDefectType.Patches,       "NEU_Textures/patches"       },
        { NEUDefectType.PittedSurface, "NEU_Textures/pitted_surface" },
        { NEUDefectType.RolledInScale, "NEU_Textures/rolled_in_scale" },
        { NEUDefectType.Scratches,     "NEU_Textures/scratches"     },
    };

    void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }

        DontDestroyOnLoad(gameObject); // 씬 재건 시에도 유지
        LoadAllTextures();
    }

    void LoadAllTextures()
    {
        int total = 0;

        foreach (var kv in FolderMap)
        {
            NEUDefectType type = kv.Key;
            string folder = kv.Value;

            // Resources.LoadAll로 폴더 내 모든 텍스처 자동 로드
            Texture2D[] textures = Resources.LoadAll<Texture2D>(folder);

            if (textures == null || textures.Length == 0)
            {
                Debug.LogWarning($"[NEU] {folder} 폴더 텍스처 없음!\n" +
                    $"Assets/Resources/{folder}/ 경로에 이미지를 넣어주세요.");
                texturePool[type] = new List<Texture2D>();
                continue;
            }

            texturePool[type] = new List<Texture2D>(textures);
            total += textures.Length;
            Debug.Log($"[NEU] {type}: {textures.Length}장 로드 완료");
        }

        Debug.Log($"[NEU] 총 {total}장 텍스처 로드 완료");
    }

    public Texture2D GetTexture(NEUDefectType defectType)
    {
        if (defectType == NEUDefectType.Normal) return null;

        if (!texturePool.ContainsKey(defectType) ||
            texturePool[defectType].Count == 0)
        {
            Debug.LogWarning($"[NEU] {defectType} 텍스처 없음");
            return null;
        }

        var pool = texturePool[defectType];
        return pool[Random.Range(0, pool.Count)];
    }

    public void ApplyTexture(GameObject panel, NEUDefectType defectType)
    {
        if (panel == null) return;

        Texture2D tex = GetTexture(defectType);

        foreach (MeshRenderer mr in panel.GetComponentsInChildren<MeshRenderer>())
        {
            if (mr.gameObject.name.ToLower().Contains("leg") ||
                mr.gameObject.name.ToLower().Contains("indicator")) continue;

            Material mat = new Material(mr.sharedMaterial ??
                new Material(Shader.Find("Universal Render Pipeline/Lit") ??
                             Shader.Find("Standard")));

            if (tex != null)
            {
                mat.SetTexture("_BaseMap", tex);
                mat.mainTexture = tex;
                mat.SetColor("_BaseColor", new Color(0.9f, 0.9f, 0.9f));
            }
            else
            {
                // Normal - 깨끗한 금속
                mat.SetColor("_BaseColor", new Color(0.80f, 0.82f, 0.85f));
            }

            mat.SetFloat("_Metallic",   defectType == NEUDefectType.Normal ? 0.8f : 0.5f);
            mat.SetFloat("_Smoothness", defectType == NEUDefectType.Normal ? 0.6f : 0.3f);
            mr.material = mat;
        }
    }

    public int GetLoadedCount(NEUDefectType type)
    {
        return texturePool.ContainsKey(type) ? texturePool[type].Count : 0;
    }
}