using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 스마트 팩토리 실시간 관제 대시보드 v2
///
/// 섹션 구성:
///   1. 공정 현황   - 총검사 / 정상 / 결함 / 불량률 / 처리량
///   2. 비전 모델   - 전체 정확도 / 평균 confidence / 클래스별 정확도
///   3. 불량품 처리 - 재작업(통과율) / 스크랩
///   4. AGV 상태    - 5대 상태 + 배터리
///   5. 시스템      - 시간 / 가동시간
///
/// 비전 정확도:
///   현재(룰베이스): predicted == actual → 항상 100%
///   Sentis 연동 후: 실제 모델 오분류 반영
///   → RecordVisionResult(predicted, actual, confidence) 호출
/// </summary>
public class FactoryDashboard : MonoBehaviour
{
    public static FactoryDashboard Instance;

    // ─── 공정 현황 ────────────────────────────────────────────
    [Header("=== 공정 현황 ===")]
    public Text txt_TotalInspected;
    public Text txt_Normal;
    public Text txt_Defect;
    public Text txt_DefectRate;
    public Text txt_Throughput;

    // ─── 비전 모델 성능 ───────────────────────────────────────
    [Header("=== 비전 모델 성능 ===")]
    public Text txt_VisionAccuracy;
    public Text txt_AvgConfidence;
    // 클래스별: crazing/inclusion/patches/pitted_surface/rolled_in_scale/scratches
    public Text[] txt_ClassAccuracy = new Text[6];

    // ─── 불량품 처리 ─────────────────────────────────────────
    [Header("=== 불량품 처리 ===")]
    public Text txt_ReworkTotal;
    public Text txt_ReworkPass;
    public Text txt_ReworkFail;
    public Text txt_ScrapTotal;

    // ─── AGV 상태 ────────────────────────────────────────────
    [Header("=== AGV 상태 (5대) ===")]
    public Text[] txt_AGVStatus  = new Text[5];
    public Text[] txt_AGVBattery = new Text[5];

    // ─── 시스템 ─────────────────────────────────────────────
    [Header("=== 시스템 ===")]
    public Text txt_SystemTime;
    public Text txt_Uptime;

    // ─── 내부 데이터 ─────────────────────────────────────────
    private int   totalInspected  = 0;
    private int   normalCount     = 0;
    private int   defectCount     = 0;
    private int   reworkTotal     = 0;
    private int   reworkPass      = 0;
    private int   reworkFail      = 0;
    private int   scrapTotal      = 0;
    private float startTime;

    // 비전 모델 정확도 추적
    private int   visionTotal     = 0;
    private int   visionCorrect   = 0;
    private float confidenceSum   = 0f;

    // 클래스별 정확도 (인덱스 = NEUDefectType 순서)
    // crazing=0, inclusion=1, patches=2, pitted_surface=3, rolled_in_scale=4, scratches=5
    private int[] classTotal   = new int[6];
    private int[] classCorrect = new int[6];

    private static readonly string[] CLASS_NAMES_KOR = {
        "균열(Crazing)",
        "개재물(Inclusion)",
        "패치(Patches)",
        "피팅(PittedSurf)",
        "압입(RolledScale)",
        "스크래치(Scratch)"
    };

    // AGV 상태 캐시
    private string[] agvStatusCache  = { "Idle","Idle","Idle","Idle","Idle" };
    private float[]  agvBatteryCache = { 100f,100f,100f,100f,100f };

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
        startTime = Time.time;
    }

    void Start()
    {
        StartCoroutine(UpdateLoop());
    }

    IEnumerator UpdateLoop()
    {
        while (true)
        {
            yield return new WaitForSeconds(0.5f);
            RefreshUI();
        }
    }

    // ─── 외부 호출 API ────────────────────────────────────────

    /// <summary>
    /// 비전 검사 결과 기록
    /// predicted: 모델이 예측한 결함 타입 (룰베이스/Sentis)
    /// actual:    패널의 실제 결함 타입 (텍스처 기반 GT)
    /// confidence: 예측 신뢰도
    /// </summary>
    public void RecordVisionResult(NEUDefectType predicted, NEUDefectType actual, float confidence)
    {
        totalInspected++;
        visionTotal++;
        confidenceSum += confidence;

        bool isCorrect = (predicted == actual);
        if (isCorrect) visionCorrect++;

        // 실제 클래스 기준으로 정확도 집계
        int actualIdx = DefectTypeToIndex(actual);
        if (actualIdx >= 0)
        {
            classTotal[actualIdx]++;
            if (isCorrect) classCorrect[actualIdx]++;
        }

        // 정상 / 결함 카운트
        if (actual == NEUDefectType.Normal)
            normalCount++;
        else
            defectCount++;
    }

    /// <summary>재작업 완료 기록</summary>
    public void RecordReworkResult(bool passed)
    {
        reworkTotal++;
        if (passed) reworkPass++;
        else        reworkFail++;
    }

    /// <summary>스크랩 처리 기록</summary>
    public void RecordScrap()
    {
        scrapTotal++;
    }

    /// <summary>AGV 상태 업데이트 (AGVController에서 호출)</summary>
    public void UpdateAGVStatus(string agvID, string status)
    {
        int idx = AGVIDToIndex(agvID);
        if (idx < 0) return;
        agvStatusCache[idx] = status;
    }

    /// <summary>AGV 배터리 업데이트</summary>
    public void UpdateAGVBattery(string agvID, float battery)
    {
        int idx = AGVIDToIndex(agvID);
        if (idx < 0) return;
        agvBatteryCache[idx] = battery;
    }

    /// <summary>레거시 호환 (PartData 기반 구버전)</summary>
    public void RecordInspection(PartData pd) { }
    public void RecordAGVTask(AGVController agv) { }

    // ─── UI 갱신 ─────────────────────────────────────────────

    void RefreshUI()
    {
        float uptime = Time.time - startTime;

        // 공정 현황
        float defectRate  = totalInspected > 0 ? (float)defectCount / totalInspected * 100f : 0f;
        float throughput  = uptime > 0 ? totalInspected / (uptime / 60f) : 0f;

        SetText(txt_TotalInspected, $"총 검사:  {totalInspected} 장");
        SetText(txt_Normal,         $"정  상:  {normalCount} 장  ({(totalInspected>0?(float)normalCount/totalInspected*100f:0f):F1}%)");
        SetText(txt_Defect,         $"결  함:  {defectCount} 장  ({defectRate:F1}%)");
        SetText(txt_DefectRate,     $"불량률:  {defectRate:F1}%");
        SetText(txt_Throughput,     $"처리량:  {throughput:F1} 장/min");

        // 비전 모델 성능
        float visionAcc  = visionTotal > 0 ? (float)visionCorrect / visionTotal * 100f : 0f;
        float avgConf    = visionTotal > 0 ? confidenceSum / visionTotal * 100f : 0f;

        SetText(txt_VisionAccuracy, $"전체 정확도:  {visionAcc:F1}%  ({visionCorrect}/{visionTotal})");
        SetText(txt_AvgConfidence,  $"평균 Confidence:  {avgConf:F1}%");

        for (int i = 0; i < 6; i++)
        {
            if (txt_ClassAccuracy == null || i >= txt_ClassAccuracy.Length) break;
            float acc = classTotal[i] > 0 ? (float)classCorrect[i] / classTotal[i] * 100f : -1f;
            string bar = acc >= 0 ? MakeBar(acc / 100f, 8) : "--------";
            string accStr = acc >= 0 ? $"{acc:F0}%" : "N/A";
            SetText(txt_ClassAccuracy[i],
                $"{CLASS_NAMES_KOR[i],-16} {bar} {accStr,4}");
        }

        // 불량품 처리
        float passRate = reworkTotal > 0 ? (float)reworkPass / reworkTotal * 100f : 0f;
        SetText(txt_ReworkTotal, $"재작업:  {reworkTotal} 장");
        SetText(txt_ReworkPass,  $"  통과:  {reworkPass} 장  ({passRate:F0}%)");
        SetText(txt_ReworkFail,  $"  실패:  {reworkFail} 장");
        SetText(txt_ScrapTotal,  $"스크랩:  {scrapTotal} 장");

        // AGV 상태
        for (int i = 0; i < 5; i++)
        {
            string statusIcon = AgvStatusIcon(agvStatusCache[i]);
            SetText(txt_AGVStatus[i],
                $"AGV_{i+1:00}  {statusIcon} {agvStatusCache[i],-8}");
            SetText(txt_AGVBattery[i],
                $"{MakeBar(agvBatteryCache[i]/100f, 8)} {agvBatteryCache[i]:F0}%");
        }

        // 시스템 시간
        SetText(txt_SystemTime, System.DateTime.Now.ToString("HH:mm:ss"));
        SetText(txt_Uptime,     $"가동: {FormatTime(uptime)}");
    }

    // ─── 유틸 ────────────────────────────────────────────────

    static void SetText(Text t, string s)
    {
        if (t != null) t.text = s;
    }

    /// <summary>ASCII 진행 바 (0~1)</summary>
    static string MakeBar(float ratio, int len)
    {
        ratio = Mathf.Clamp01(ratio);
        int filled = Mathf.RoundToInt(ratio * len);
        return new string('█', filled) + new string('░', len - filled);
    }

    static string AgvStatusIcon(string status) => status switch
    {
        "Idle"      => "●",
        "Moving"    => "▶",
        "Lifting"   => "↑",
        "Carrying"  => "◆",
        "Lowering"  => "↓",
        "Reworking" => "⚙",
        "Charging"  => "⚡",
        _           => "✕"
    };

    static string FormatTime(float seconds)
    {
        int m = (int)(seconds / 60);
        int s = (int)(seconds % 60);
        return $"{m:00}:{s:00}";
    }

    static int AGVIDToIndex(string agvID)
    {
        string num = agvID.Replace("AGV_", "").TrimStart('0');
        return int.TryParse(num, out int n) ? n - 1 : -1;
    }

    static int DefectTypeToIndex(NEUDefectType t) => t switch
    {
        NEUDefectType.Crazing        => 0,
        NEUDefectType.Inclusion      => 1,
        NEUDefectType.Patches        => 2,
        NEUDefectType.PittedSurface  => 3,
        NEUDefectType.RolledInScale  => 4,
        NEUDefectType.Scratches      => 5,
        _                            => -1
    };

    /// <summary>레거시 AGVFleetManager 호환용 스텁</summary>
    public void UpdateQueueCount(int count) { }
    public void RecordScheduling(string agvID, string taskType) { }
}