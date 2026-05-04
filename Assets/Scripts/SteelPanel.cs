using UnityEngine;

public enum NEUDefectType
{
    Normal,
    Crazing,
    Inclusion,
    Patches,
    PittedSurface,
    RolledInScale,
    Scratches
}

/// <summary>패널 모델 종류 (랙 분류 기준)</summary>
public enum PanelModelType
{
    Unknown,
    Plate,   // Steel_Plate → SteelPlate_Rack (X=-15)
    Sheet    // Sheet       → Sheet_Rack      (X=+15)
}

public class SteelPanel : MonoBehaviour
{
    [Header("패널 정보")]
    public NEUDefectType  defectType  = NEUDefectType.Normal;
    public PanelModelType modelType   = PanelModelType.Unknown;
    public bool isDefective => defectType != NEUDefectType.Normal;

    [Header("검사 결과")]
    public float confidence = 0f;
    public bool  inspected  = false;
    public PanelStatus status = PanelStatus.OnConveyor;

    // ★ 추가: Sentis 추론용 원본 텍스처 (PanelSpawner에서 할당)
    [Header("비전 모델용")]
    public Texture2D defectTexture;

    [Header("추적")]
    public float spawnTime;
    public int   panelID;

    public enum PanelStatus
    {
        OnConveyor, Inspecting, Sorted, OnAGV, Stored, Shipped
    }

    void Awake() { spawnTime = Time.time; }

    public string GetDefectKorean()
    {
        switch (defectType)
        {
            case NEUDefectType.Normal:        return "정상";
            case NEUDefectType.Crazing:       return "균열";
            case NEUDefectType.Inclusion:     return "개재물";
            case NEUDefectType.Patches:       return "패치";
            case NEUDefectType.PittedSurface: return "피팅";
            case NEUDefectType.RolledInScale: return "압입스케일";
            case NEUDefectType.Scratches:     return "스크래치";
            default:                          return "알수없음";
        }
    }

    public Color GetDefectColor()
    {
        switch (defectType)
        {
            case NEUDefectType.Normal:        return new Color(0.2f, 0.9f, 0.3f);
            case NEUDefectType.Crazing:       return new Color(1.0f, 0.2f, 0.2f);
            case NEUDefectType.Inclusion:     return new Color(1.0f, 0.6f, 0.0f);
            case NEUDefectType.Patches:       return new Color(0.8f, 0.8f, 0.0f);
            case NEUDefectType.PittedSurface: return new Color(0.8f, 0.0f, 0.8f);
            case NEUDefectType.RolledInScale: return new Color(0.0f, 0.6f, 1.0f);
            case NEUDefectType.Scratches:     return new Color(1.0f, 0.4f, 0.0f);
            default:                          return Color.white;
        }
    }
}