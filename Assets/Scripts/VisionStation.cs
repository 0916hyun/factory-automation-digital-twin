using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 비전 검사 스테이션 (갠트리형 고정 카메라)
/// 컨베이어 위를 지나가는 부품을 촬영하여 AI 판별
/// 로템 JD: 컴퓨터 비전 및 영상처리 소프트웨어
/// </summary>
public class VisionStation : MonoBehaviour
{
    [Header("검사 설정")]
    public int stationIndex = 0;
    public float detectionZone = 1.5f;   // 감지 범위
    public float inspectionTime = 0.8f;  // 검사 소요 시간

    [Header("시각 효과")]
    public Light scanLight;              // 스캔 조명
    public GameObject scanBeam;         // 레이저 빔 이펙트
    public Color normalColor  = new Color(0.2f, 1f, 0.3f);
    public Color defectColor  = new Color(1f, 0.2f, 0.2f);
    public Color scanningColor= new Color(0.3f, 0.7f, 1f);

    [Header("UI 디스플레이")]
    public Text resultText;
    public Text confidenceText;
    public Image statusLight;

    [Header("연결")]
    public SortingGate sortingGate;

    private bool isScanning = false;
    private int totalInspected = 0;
    private int defectFound = 0;

    void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("TargetObject")) return;
        if (isScanning) return;

        PartData pd = other.GetComponent<PartData>();
        if (pd == null || pd.status != PartStatus.OnConveyor) return;

        pd.status = PartStatus.Inspecting;
        StartCoroutine(InspectPart(other.gameObject, pd));
    }

    IEnumerator InspectPart(GameObject part, PartData pd)
    {
        isScanning = true;

        // 스캔 시작 이펙트
        SetScanEffect(true);
        UpdateDisplay("검사중...", scanningColor, 0f);

        // 검사 시간
        float elapsed = 0f;
        while (elapsed < inspectionTime)
        {
            elapsed += Time.deltaTime;
            // 스캔 빔 회전
            if (scanBeam != null)
                scanBeam.transform.Rotate(0, 0, 360f * Time.deltaTime / inspectionTime);
            yield return null;
        }

        // 검사 결과 (실제로는 AI 모델 추론)
        float confidence = Random.Range(0.82f, 0.99f);
        pd.defectConfidence = pd.isDefective ? confidence : 1f - confidence * 0.3f;
        pd.inspectionTime = Time.time;
        pd.status = PartStatus.Sorted;

        // 결함 타입 시뮬레이션
        if (pd.isDefective)
        {
            string[] defects = {"scratch", "dent", "contamination", "crack"};
            pd.defectType = defects[Random.Range(0, defects.Length)];
        }

        // 결과 표시
        SetScanEffect(false);
        Color resultCol = pd.isDefective ? defectColor : normalColor;
        string result = pd.isDefective
            ? $"DEFECT [{pd.defectType}]"
            : $"PASS [{pd.partType}]";
        UpdateDisplay(result, resultCol, confidence);

        // 조명 결과 표시 (2초)
        if (scanLight != null)
        {
            scanLight.color = resultCol;
            scanLight.intensity = 3f;
        }

        // 소터에 결과 전달
        if (sortingGate != null)
            sortingGate.ReceiveInspectionResult(part, pd);

        totalInspected++;
        if (pd.isDefective) defectFound++;

        // 대시보드 업데이트
        if (FactoryDashboard.Instance != null)
            FactoryDashboard.Instance.RecordInspection(pd);

        yield return new WaitForSeconds(1.5f);

        // 조명 리셋
        if (scanLight != null) scanLight.intensity = 1f;
        UpdateDisplay("대기중", Color.white, 0f);
        isScanning = false;
    }

    void SetScanEffect(bool active)
    {
        if (scanBeam != null) scanBeam.SetActive(active);
        if (scanLight != null)
        {
            scanLight.color = active ? scanningColor : Color.white;
            scanLight.intensity = active ? 5f : 1f;
        }
    }

    void UpdateDisplay(string result, Color color, float confidence)
    {
        if (resultText != null)
        {
            resultText.text = result;
            resultText.color = color;
        }
        if (confidenceText != null && confidence > 0)
            confidenceText.text = $"{confidence * 100f:F1}%";
        if (statusLight != null)
            statusLight.color = color;
    }
}
