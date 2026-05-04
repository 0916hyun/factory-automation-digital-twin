using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 강판 분류 게이트 (개선판)
/// 
/// 핵심 변경: 파킹슬롯 대기 로직 제거
/// → 비전검사 완료 즉시 현재 위치에서 AGV 배차 요청
/// → 슬롯 점유 대기 없음 → AGV 유휴 + 패널 적체 해소
/// 
/// 분류:
///   정상          → 창고랙 (AGV 배차)
///   경미한 결함   → 재작업구역 (Scratches / Patches / PittedSurface)
///   심각한 결함   → 스크랩함 (Crazing / Inclusion / RolledInScale)
/// </summary>
public class PanelSortingGate : MonoBehaviour
{
    [Header("AGV Fleet 연결")]
    public PanelAGVFleetManager fleetManager;

    [Header("구역 위치")]
    public Transform reworkZone;
    public Transform scrapBox;

    [Header("통계 UI")]
    public Text normalCountText;
    public Text defectCountText;
    public Text reworkCountText;
    public Text scrapCountText;

    // 소터 구역 Z (비전스테이션 Z=28 바로 뒤)
    const float SORTER_Z = 33f;

    private Queue<(GameObject, SteelPanel)> pending   = new Queue<(GameObject, SteelPanel)>();
    private bool                             busy      = false;

    private int normalCount, reworkCount, scrapCount;

    // ─── 초기화 ───────────────────────────────────────────

    void Start()
    {
        if (fleetManager == null)
            fleetManager = Object.FindFirstObjectByType<PanelAGVFleetManager>();

        if (reworkZone == null)
        {
            var g = GameObject.Find("ReworkZone");
            reworkZone = g != null ? g.transform : MakeDummy("ReworkZone", new Vector3(22f, 0, 47f));
        }
        if (scrapBox == null)
        {
            var g = GameObject.Find("DefectBox");
            scrapBox = g != null ? g.transform : MakeDummy("DefectBox", new Vector3(-22f, 0, 40f));
        }

        Debug.Log($"[Sorter] 초기화 | FleetManager={fleetManager != null} " +
                  $"| ReworkZone={reworkZone?.name} | ScrapBox={scrapBox?.name}");
    }

    Transform MakeDummy(string n, Vector3 pos)
    {
        var g = new GameObject(n);
        g.transform.position = pos;
        return g.transform;
    }

    // ─── 비전스테이션에서 호출 ───────────────────────────

    public void ReceiveResult(GameObject panelObj, SteelPanel panel)
    {
        if (panelObj == null || panel == null) return;
        pending.Enqueue((panelObj, panel));
        if (!busy) StartCoroutine(ProcessQueue());
    }

    // ─── 처리 큐 ─────────────────────────────────────────

    IEnumerator ProcessQueue()
    {
        busy = true;
        while (pending.Count > 0)
        {
            var (obj, sp) = pending.Dequeue();
            if (obj == null) continue;
            yield return StartCoroutine(RoutePanel(obj, sp));
            yield return null; // 1프레임 쉬고 다음
        }
        busy = false;
    }

    IEnumerator RoutePanel(GameObject panelObj, SteelPanel panel)
    {
        if (panelObj == null) yield break;

        // 패널 정지
        Rigidbody rb = panelObj.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.linearVelocity  = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic     = true;
        }

        // 심각도 판정
        var severity = GetSeverity(panel.defectType);

        if (severity == Severity.Normal)
            yield return StartCoroutine(HandleNormal(panelObj, panel));
        else if (severity == Severity.Minor)
            yield return StartCoroutine(HandleMinor(panelObj, panel));
        else
            yield return StartCoroutine(HandleMajor(panelObj, panel));

        UpdateUI();
    }

    // ─── 정상 → 창고 ──────────────────────────────────────

    IEnumerator HandleNormal(GameObject panelObj, SteelPanel panel)
    {
        // 소터존으로 슬라이드 (파킹 슬롯 대기 없음)
        Vector3 stopPos = new Vector3(panelObj.transform.position.x, 0.65f, SORTER_Z);
        yield return StartCoroutine(SlideTo(panelObj, stopPos));

        panel.status = SteelPanel.PanelStatus.Sorted;
        normalCount++;

        // 랙 슬롯 배정
        var rackMgr = StorageRackManager.Instance;
        Vector3 rackPos = Vector3.zero;
        int rackIdx = -1, slotIdx = -1;

        if (rackMgr != null)
            rackPos = rackMgr.AssignSlot(panelObj, out rackIdx, out slotIdx);

        // ★ 핵심 수정: 랙 만석 시 X=0 fallback 제거 → 슬롯 빌 때까지 소터존에서 대기
        // 기존: 즉시 (0, 0.7, 55) 고정 위치로 배차 → 두 랙 사이 바닥에 공중 부유
        float slotWait = 0f;
        while (rackPos == Vector3.zero && slotWait < 30f)
        {
            yield return new WaitForSeconds(0.5f);
            slotWait += 0.5f;
            if (rackMgr != null)
                rackPos = rackMgr.AssignSlot(panelObj, out rackIdx, out slotIdx);
        }

        if (rackPos == Vector3.zero)
        {
            Debug.LogError($"[Sorter] {panelObj.name} 슬롯 배정 30초 초과 → 패널 드롭 취소");
            yield break;
        }

        Debug.Log($"[Sorter] {panelObj.name} → 정상 | " +
                  $"랙{(rackIdx >= 0 ? (char)('A' + rackIdx) : '?')} 슬롯{slotIdx} | " +
                  $"픽업={stopPos:F1} → 드롭={rackPos:F1}");

        // ★ 즉시 AGV 배차 요청 (슬롯 대기 없음)
        if (fleetManager != null)
            fleetManager.RequestPickupAt(panelObj, panel, stopPos, rackPos);
        else
            Debug.LogError("[Sorter] FleetManager 없음! AGV 배차 실패");
    }

    // ─── 경미한 결함 → 재작업 ────────────────────────────

    IEnumerator HandleMinor(GameObject panelObj, SteelPanel panel)
    {
        Vector3 stopPos = new Vector3(panelObj.transform.position.x, 0.65f, SORTER_Z);
        yield return StartCoroutine(SlideTo(panelObj, stopPos));

        panel.status = SteelPanel.PanelStatus.Sorted;
        reworkCount++;

        Vector3 reworkPos = reworkZone != null
            ? reworkZone.position + new Vector3(Random.Range(-2.5f, 2.5f), 0.65f, 0)
            : new Vector3(22f, 0.65f, 47f);

        Debug.Log($"[Sorter] {panelObj.name} → 경미({panel.GetDefectKorean()}) → 재작업");

        if (fleetManager != null)
            fleetManager.RequestRework(panelObj, panel, stopPos, reworkPos);
        else
            Debug.LogError("[Sorter] FleetManager 없음! 재작업 배차 실패");
    }

    // ─── 심각한 결함 → 스크랩 ────────────────────────────

    IEnumerator HandleMajor(GameObject panelObj, SteelPanel panel)
    {
        Vector3 stopPos = new Vector3(panelObj.transform.position.x, 0.65f, SORTER_Z);
        yield return StartCoroutine(SlideTo(panelObj, stopPos));

        panel.status = SteelPanel.PanelStatus.Sorted;
        scrapCount++;

        Vector3 scrapPos = scrapBox != null
            ? scrapBox.position + new Vector3(Random.Range(-1f, 1f), 0.65f, Random.Range(-1f, 1f))
            : new Vector3(-22f, 0.65f, 40f);

        Debug.Log($"[Sorter] {panelObj.name} → 심각({panel.GetDefectKorean()}) → 스크랩");

        if (fleetManager != null)
            fleetManager.RequestScrap(panelObj, panel, stopPos, scrapPos);
        else
            Debug.LogError("[Sorter] FleetManager 없음! 스크랩 배차 실패");
    }

    // ─── 유틸 ────────────────────────────────────────────

    IEnumerator SlideTo(GameObject obj, Vector3 dest)
    {
        if (obj == null) yield break;
        Vector3 start = obj.transform.position;
        float duration = 0.6f;

        for (float el = 0f; el < duration; el += Time.deltaTime)
        {
            if (obj == null) yield break;
            float t = el / duration;
            t = t * t * (3f - 2f * t); // smooth step
            obj.transform.position = Vector3.Lerp(start, dest, t);
            yield return null;
        }

        if (obj != null) obj.transform.position = dest;
    }

    enum Severity { Normal, Minor, Major }

    Severity GetSeverity(NEUDefectType type)
    {
        switch (type)
        {
            case NEUDefectType.Normal:                           return Severity.Normal;
            case NEUDefectType.Scratches:
            case NEUDefectType.Patches:
            case NEUDefectType.PittedSurface:                   return Severity.Minor;
            default:                                            return Severity.Major;
        }
    }

    void UpdateUI()
    {
        if (normalCountText != null) normalCountText.text = $"정상: {normalCount}장";
        if (defectCountText  != null) defectCountText.text  = $"결함: {reworkCount + scrapCount}장";
        if (reworkCountText  != null) reworkCountText.text  = $"재작업: {reworkCount}장";
        if (scrapCountText   != null) scrapCountText.text   = $"스크랩: {scrapCount}장";
    }

    // ─── 레거시 호환 (AGVController에서 호출할 수 있어서 유지) ──
    public static void ReleaseSlot(int slotX)
    {
        // 파킹슬롯 시스템 제거됨 → 아무것도 안 해도 됨
        Debug.Log($"[Sorter] ReleaseSlot({slotX}) - 파킹슬롯 시스템 제거됨, 무시");
    }
}

/// <summary>레거시 호환용 - AGVController에서 참조함</summary>
public class SortingSlotInfo : MonoBehaviour
{
    public int slotX;
}