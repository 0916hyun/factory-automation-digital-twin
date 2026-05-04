using UnityEngine;

/// <summary>
/// 부품 데이터 - 공장 내 모든 부품이 가지는 정보
/// </summary>
public enum PartType { HexNut, Screw, Transistor }
public enum PartStatus { OnConveyor, Inspecting, Sorted, OnAGV, Stored, Shipped, Defective }

public class PartData : MonoBehaviour
{
    [Header("부품 정보")]
    public PartType partType;
    public bool isDefective = false;
    public PartStatus status = PartStatus.OnConveyor;

    [Header("추적 정보")]
    public float spawnTime;
    public float inspectionTime;
    public string assignedAGV = "";
    public int targetRackSlot = -1;

    [Header("비전 검사 결과")]
    public float defectConfidence = 0f;
    public string defectType = "none"; // scratch, dent, contamination 등

    void Awake()
    {
        spawnTime = Time.time;
    }

    public string GetLabel()
    {
        return $"{partType} | {(isDefective ? "DEFECT" : "OK")} | {status}";
    }
}
