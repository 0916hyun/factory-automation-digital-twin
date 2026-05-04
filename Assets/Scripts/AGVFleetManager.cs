using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// AGV 군집 관제 시스템
/// - EDF 스케줄링으로 태스크 우선순위 결정
/// - 유휴 AGV에 동적 작업 할당
/// - 경로 최적화 (가장 가까운 AGV 배정)
/// 로템 JD: AGV 관제시스템, 알고리즘 기반 이동 경로 최적화
/// </summary>
public class AGVFleetManager : MonoBehaviour
{
    [Header("AGV 목록")]
    public List<AGVController> agvFleet = new List<AGVController>();

    [Header("드롭오프 위치")]
    public Transform hexNutStorage;
    public Transform screwStorage;
    public Transform transistorStorage;
    public Transform shippingZone;

    [Header("EDF 설정")]
    public float maxWaitTime = 30f;   // 최대 대기 허용 시간

    // 태스크 큐 (EDF)
    private class AGVTask
    {
        public GameObject part;
        public PartData partData;
        public Vector3 pickupPos;
        public Vector3 dropoffPos;
        public float deadline;         // EDF 마감 시한
        public float requestTime;

        public AGVTask(GameObject p, PartData pd, Vector3 pickup, Vector3 dropoff, float wait)
        {
            part = p; partData = pd;
            pickupPos = pickup; dropoffPos = dropoff;
            requestTime = Time.time;
            deadline = Time.time + wait;
        }
    }

    private List<AGVTask> taskQueue = new List<AGVTask>();
    private bool isScheduling = false;

    void Start()
    {
        StartCoroutine(SchedulingLoop());
    }

    public void RequestPickup(GameObject part, PartData pd, Transform sortedPos)
    {
        Vector3 dropoff = GetDropoffPos(pd.partType);
        float waitTime = maxWaitTime - (Time.time - pd.spawnTime);
        waitTime = Mathf.Max(5f, waitTime);

        AGVTask task = new AGVTask(
            part, pd,
            sortedPos.position,
            dropoff,
            waitTime);

        taskQueue.Add(task);
        Debug.Log($"[Fleet] 태스크 추가: {part.name} → {pd.partType} storage (마감: {task.deadline:F1}s)");

        if (FactoryDashboard.Instance != null)
            FactoryDashboard.Instance.UpdateQueueCount(taskQueue.Count);
    }

    IEnumerator SchedulingLoop()
    {
        while (true)
        {
            yield return new WaitForSeconds(0.5f);

            if (taskQueue.Count == 0) continue;

            // EDF: 마감 시한이 가장 임박한 태스크 선택
            AGVTask nextTask = GetEDFTask();
            if (nextTask == null) continue;

            // 최적 AGV 선택 (가장 가까운 유휴 AGV)
            AGVController bestAGV = GetNearestAvailableAGV(nextTask.pickupPos);
            if (bestAGV == null) continue;

            // 태스크 할당
            taskQueue.Remove(nextTask);
            Debug.Log($"[Fleet] EDF 할당: {bestAGV.agvID} → {nextTask.part?.name}");

            if (FactoryDashboard.Instance != null)
            {
                FactoryDashboard.Instance.UpdateQueueCount(taskQueue.Count);
                FactoryDashboard.Instance.RecordScheduling(bestAGV.agvID, nextTask.part?.name);
            }

            StartCoroutine(bestAGV.ExecuteTask(
                nextTask.part, nextTask.partData,
                nextTask.pickupPos, nextTask.dropoffPos));
        }
    }

    AGVTask GetEDFTask()
    {
        if (taskQueue.Count == 0) return null;

        // 마감 시한 기준 정렬
        taskQueue.Sort((a, b) => a.deadline.CompareTo(b.deadline));

        // 유효한 태스크 (part가 아직 존재)
        foreach (var t in taskQueue)
            if (t.part != null) return t;

        // 무효 태스크 제거
        taskQueue.RemoveAll(t => t.part == null);
        return null;
    }

    AGVController GetNearestAvailableAGV(Vector3 pos)
    {
        AGVController nearest = null;
        float minDist = float.MaxValue;

        foreach (var agv in agvFleet)
        {
            if (agv == null || !agv.IsAvailable()) continue;
            float dist = Vector3.Distance(agv.transform.position, pos);
            if (dist < minDist) { minDist = dist; nearest = agv; }
        }
        return nearest;
    }

    Vector3 GetDropoffPos(PartType type)
    {
        switch (type)
        {
            case PartType.HexNut:     return hexNutStorage?.position ?? Vector3.zero;
            case PartType.Screw:      return screwStorage?.position ?? Vector3.zero;
            case PartType.Transistor: return transistorStorage?.position ?? Vector3.zero;
            default: return shippingZone?.position ?? Vector3.zero;
        }
    }

    public int GetQueueCount() => taskQueue.Count;
    public int GetActiveAGVCount()
    {
        int count = 0;
        foreach (var agv in agvFleet)
            if (agv != null && agv.status != AGVController.AGVStatus.Idle) count++;
        return count;
    }
}
