using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 소형 AGV EDF 스케줄러
///
/// 변경사항:
/// - ScrapTransport → ExecuteScrapTask() 라우팅
///   (기존: ExecuteTask → LowerPanel에서 Stored 덮어써서 ScrapRobot 감지 불가)
///   (수정: ExecuteScrapTask → LowerPanel(forStation:true) → Sorted 유지)
/// </summary>
public class PanelAGVFleetManager : MonoBehaviour
{
    [Header("AGV 목록")]
    public List<AGVController> agvFleet = new List<AGVController>();

    [Header("출하 도크")]
    public Transform shippingDock1;
    public Transform shippingDock2;
    public Transform shippingDock3;

    [Header("재작업 구역 (ReworkRobot 위치)")]
    public Transform reworkZone;

    [Header("스크랩함 (ScrapRobot 위치)")]
    public Transform scrapBox;

    [Header("EDF 설정")]
    public float maxWaitTime = 40f;

    public enum TaskType { PickupToRack, ReworkTransport, ScrapTransport, ShippingRun }

    private class AGVTask
    {
        public GameObject panel;
        public SteelPanel panelData;
        public Vector3    pickupPos;
        public Vector3    dropoffPos;
        public float      deadline;
        public TaskType   type;
        public int        priority;

        public AGVTask(GameObject p, SteelPanel pd, Vector3 pick, Vector3 drop,
            TaskType t, float wait, int pri)
        {
            panel = p; panelData = pd;
            pickupPos = pick; dropoffPos = drop;
            type = t; priority = pri;
            deadline = Time.time + wait;
        }
    }

    private List<AGVTask> taskQueue = new List<AGVTask>();

    void Start()
    {
        if (shippingDock1 == null) shippingDock1 = FindObj("Dock_Plate");
        if (shippingDock2 == null) shippingDock2 = FindObj("Dock_Sheet");
        if (reworkZone    == null) reworkZone    = FindObj("ReworkRobot");
        if (scrapBox      == null) scrapBox      = FindObj("ScrapRobot");

        Debug.Log($"[FleetMgr] 시작 | AGV={agvFleet.Count}대 " +
                  $"| Rework={reworkZone?.name} | Scrap={scrapBox?.name}");
        StartCoroutine(EdfSchedulingLoop());
    }

    Transform FindObj(string name)
    {
        var go = GameObject.Find(name);
        return go != null ? go.transform : null;
    }

    // ─── 태스크 등록 API ──────────────────────────────────────

    /// <summary>정상 패널 → 창고 랙 (일반 드롭, Stored 설정)</summary>
    public void RequestPickupAt(GameObject panel, SteelPanel pd, Vector3 pickup, Vector3 dropoff)
        => AddTask(new AGVTask(panel, pd, pickup, dropoff, TaskType.PickupToRack, maxWaitTime, 3));

    /// <summary>경미한 결함 → 재작업 스테이션 (forStation 드롭, Sorted 유지)</summary>
    public void RequestRework(GameObject panel, SteelPanel pd, Vector3 pickup, Vector3 reworkPos)
        => AddTask(new AGVTask(panel, pd, pickup, reworkPos, TaskType.ReworkTransport, maxWaitTime * 0.7f, 4));

    /// <summary>심각한 결함 → 스크랩 스테이션 (forStation 드롭, Sorted 유지)</summary>
    public void RequestScrap(GameObject panel, SteelPanel pd, Vector3 pickup, Vector3 scrapPos)
        => AddTask(new AGVTask(panel, pd, pickup, scrapPos, TaskType.ScrapTransport, maxWaitTime * 0.5f, 2));

    /// <summary>출하 (팔레트 → 도크)</summary>
    public void RequestShipping(GameObject panel, SteelPanel pd, Vector3 pickup, Vector3 dockPos)
        => AddTask(new AGVTask(panel, pd, pickup, dockPos, TaskType.ShippingRun, 60f, 1));

    void AddTask(AGVTask task)
    {
        if (task.panel == null) return;
        taskQueue.Add(task);
        Debug.Log($"[Fleet] 태스크 등록: {task.panel.name} " +
                  $"({task.type} | 픽업={task.pickupPos:F1} → 드롭={task.dropoffPos:F1})");
    }

    // ─── EDF 스케줄링 ────────────────────────────────────────

    IEnumerator EdfSchedulingLoop()
    {
        while (true)
        {
            yield return new WaitForSeconds(0.5f);
            taskQueue.RemoveAll(t => t.panel == null);
            if (taskQueue.Count == 0) continue;

            // 우선순위 낮을수록 긴급 (1=ShippingRun이 제일 긴급)
            // 동일 우선순위면 마감시한(EDF) 순
            taskQueue.Sort((a, b) => {
                if (a.priority != b.priority) return a.priority.CompareTo(b.priority);
                return a.deadline.CompareTo(b.deadline);
            });

            AGVTask next = taskQueue[0];
            AGVController agv = GetNearestAvailableAGV(next.pickupPos);
            if (agv == null) continue;

            taskQueue.RemoveAt(0);
            Debug.Log($"[Fleet] EDF 배차: {agv.agvID} → {next.panel?.name} ({next.type})");

            switch (next.type)
            {
                case TaskType.ReworkTransport:
                    // ★ ExecuteReworkTask: 드롭 후 즉시 복귀 (forStation=true)
                    StartCoroutine(agv.ExecuteReworkTask(
                        next.panel, next.pickupPos, next.dropoffPos, this));
                    break;

                case TaskType.ScrapTransport:
                    // ★ ExecuteScrapTask: 스크랩 전용 (forStation=true)
                    //   기존 ExecuteTask 사용 시 LowerPanel이 Stored 덮어써서
                    //   ScrapRobot OverlapSphere 감지 불가 → 버그 수정
                    StartCoroutine(agv.ExecuteScrapTask(
                        next.panel, next.pickupPos, next.dropoffPos));
                    break;

                default:
                    // PickupToRack, ShippingRun → 일반 드롭 (Stored 설정)
                    StartCoroutine(agv.ExecuteTask(
                        next.panel, null, next.pickupPos, next.dropoffPos));
                    break;
            }
        }
    }

    AGVController GetNearestAvailableAGV(Vector3 pos)
    {
        AGVController best    = null;
        float         minDist = float.MaxValue;
        foreach (var agv in agvFleet)
        {
            if (agv == null || !agv.IsAvailable()) continue;
            float d = Vector3.Distance(agv.transform.position, pos);
            if (d < minDist) { minDist = d; best = agv; }
        }
        return best;
    }

    // ─── 재작업 완료 콜백 ────────────────────────────────────

    /// <summary>
    /// DefectProcessingStation(로봇암)이 재작업/재검사를 전담하므로
    /// AGV 콜백에서는 로그만 출력.
    /// </summary>
    public void OnReworkComplete(GameObject panel, SteelPanel pd, Vector3 currentPos)
    {
        if (panel == null) return;
        Debug.Log($"[Fleet] 재작업존 도착 확인: {panel?.name} → DefectProcessingStation 처리 중");
    }

    /// <summary>
    /// 팔레트 생성 시 해당 패널들의 태스크 취소
    /// PalletObject.Create() 또는 StorageRackManager.TriggerShipping()에서 호출
    /// </summary>
    public void CancelTasksForPanels(List<UnityEngine.GameObject> panels)
    {
        if (panels == null || panels.Count == 0) return;
        var panelSet = new System.Collections.Generic.HashSet<UnityEngine.GameObject>(panels);
        int before = taskQueue.Count;
        taskQueue.RemoveAll(t => t.panel != null && panelSet.Contains(t.panel));
        int cancelled = before - taskQueue.Count;
        if (cancelled > 0)
            Debug.Log($"[Fleet] ★팔레트 태스크 취소: {cancelled}개 제거 (팔레트 생성)");
    }

    public int GetQueueCount() => taskQueue.Count;
}