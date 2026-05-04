using System.Collections;
using UnityEngine;

/// <summary>
/// AGV 차량 컨트롤러 v2
///
/// 변경사항:
/// - LowerPanel(bool forStation): forStation=true 시 status를 Stored로 덮어쓰지 않음
///   → DefectProcessingStation(로봇암)이 Sorted 상태 패널을 정상 감지 가능
/// - ExecuteScrapTask(): 스크랩 전용 태스크 (LowerPanel forStation=true)
/// - ExecuteReworkTask(): 15초 대기 제거 → AGV는 드롭 후 즉시 복귀
///   DefectProcessingStation이 OverlapSphere로 독립 처리
/// </summary>
public class AGVController : MonoBehaviour
{
    [Header("AGV 설정")]
    public string agvID   = "AGV_01";
    public float maxSpeed = 3.5f;
    public float acceleration = 2.5f;
    public AGVStatus status = AGVStatus.Idle;

    [Header("화물 플랫폼")]
    public Transform cargoPlate;
    public float liftHeight = 0.3f;

    [Header("레인 (자동 계산)")]
    public float subLaneZ = 42f;
    const float BASE_LANE_Z  = 42f;
    const float LANE_SPACING = 1.2f;

    [Header("배터리")]
    [Range(0f,100f)] public float batteryLevel = 100f;
    public float batteryDrainRate = 0.5f;
    public Transform chargingStation;

    [Header("시각 효과")]
    public Light statusLight;

    [HideInInspector] public LayerMask obstacleLayer;
    [HideInInspector] public bool showLidar = false;

    private GameObject carriedPanel;
    private float currentSpeed = 0f;
    private Vector3 startPos;

    private static System.Collections.Generic.HashSet<string> occupiedZones
        = new System.Collections.Generic.HashSet<string>();
    private string myZone = "";

    public System.Action<AGVController> OnTaskComplete;

    public enum AGVStatus { Idle, Moving, Lifting, Carrying, Lowering,
                            Reworking, Charging, Error }

    void Start()
    {
        startPos = transform.position;

        string num = agvID.Replace("AGV_", "").TrimStart('0');
        if (int.TryParse(num, out int parsed))
            subLaneZ = BASE_LANE_Z + (parsed - 1) * LANE_SPACING;

        if (cargoPlate == null)
        {
            Transform f = transform.Find("CargoPlatform");
            if (f != null) cargoPlate = f;
            else
            {
                var cp = new GameObject("CargoPlatform");
                cp.transform.SetParent(transform);
                cp.transform.localPosition = new Vector3(0, 0.35f, 0);
                cargoPlate = cp.transform;
            }
        }

        StartCoroutine(BatteryLoop());
        Debug.Log($"[{agvID}] 초기화 | subLaneZ={subLaneZ:F1}");
    }

    void Update() => UpdateStatusLight();

    // ─── 일반 태스크 (픽업 → 드롭 → 복귀) ─────────────────

    public IEnumerator ExecuteTask(GameObject panel, PartData pd,
        Vector3 pickupPos, Vector3 dropoffPos)
    {
        carriedPanel = panel;
        var tm = AGVTrafficManager.Instance;
        Debug.Log($"[{agvID}] ★태스크: {panel?.name} | 픽업={pickupPos:F1}");

        SetStatus(AGVStatus.Moving);

        // ━━━ 픽업 ━━━
        Vector3 wp1 = new Vector3(pickupPos.x, 0, subLaneZ);
        if (tm != null) yield return StartCoroutine(tm.ReservePath(agvID, transform.position, wp1, maxSpeed));
        yield return StartCoroutine(MoveTo(wp1));

        if (tm != null) yield return StartCoroutine(tm.ReservePath(agvID, wp1, pickupPos, maxSpeed));
        yield return StartCoroutine(MoveTo(pickupPos));
        if (tm != null) tm.ReleaseAll(agvID);

        if (carriedPanel == null) { SetStatus(AGVStatus.Idle); yield break; }

        SetStatus(AGVStatus.Lifting);
        yield return StartCoroutine(LiftPanel());

        SortingSlotInfo slot = carriedPanel?.GetComponent<SortingSlotInfo>();
        if (slot != null) PanelSortingGate.ReleaseSlot(slot.slotX);

        // ━━━ 드롭오프 ━━━
        SetStatus(AGVStatus.Carrying);

        Vector3 wp4 = new Vector3(transform.position.x, 0, subLaneZ);
        if (tm != null) yield return StartCoroutine(tm.ReservePath(agvID, transform.position, wp4, maxSpeed));
        yield return StartCoroutine(MoveTo(wp4));

        Vector3 wp5 = new Vector3(dropoffPos.x, 0, subLaneZ);
        if (tm != null) yield return StartCoroutine(tm.ReservePath(agvID, wp4, wp5, maxSpeed));
        yield return StartCoroutine(MoveTo(wp5));

        if (tm != null) yield return StartCoroutine(tm.ReservePath(agvID, wp5, dropoffPos, maxSpeed));
        yield return StartCoroutine(MoveTo(dropoffPos));
        if (tm != null) tm.ReleaseAll(agvID);

        SetStatus(AGVStatus.Lowering);
        yield return StartCoroutine(LowerPanel(false, dropoffPos.y));

        // ━━━ 복귀 ━━━
        SetStatus(AGVStatus.Moving);
        yield return StartCoroutine(ReturnToBase());

        Debug.Log($"[{agvID}] ★완료.");
        SetStatus(AGVStatus.Idle);
        OnTaskComplete?.Invoke(this);
        if (FactoryDashboard.Instance != null)
            FactoryDashboard.Instance.RecordAGVTask(this);
    }

    // ─── 재작업 태스크 ────────────────────────────────────────
    /// <summary>
    /// 소터존 → ReworkStation 드롭 → 즉시 복귀
    /// ★ 15초 대기 제거: DefectProcessingStation이 OverlapSphere로 독립 처리
    /// ★ LowerPanel(forStation:true): status를 Sorted 유지 → 로봇이 감지 가능
    /// </summary>
    public IEnumerator ExecuteReworkTask(GameObject panel, Vector3 pickupPos,
        Vector3 reworkPos, PanelAGVFleetManager fleetMgr)
    {
        carriedPanel = panel;
        var tm = AGVTrafficManager.Instance;
        Debug.Log($"[{agvID}] ★재작업 태스크: {panel?.name}");

        SetStatus(AGVStatus.Moving);

        // 픽업
        Vector3 wp1 = new Vector3(pickupPos.x, 0, subLaneZ);
        if (tm != null) yield return StartCoroutine(tm.ReservePath(agvID, transform.position, wp1, maxSpeed));
        yield return StartCoroutine(MoveTo(wp1));
        if (tm != null) yield return StartCoroutine(tm.ReservePath(agvID, wp1, pickupPos, maxSpeed));
        yield return StartCoroutine(MoveTo(pickupPos));
        if (tm != null) tm.ReleaseAll(agvID);

        if (carriedPanel == null) { SetStatus(AGVStatus.Idle); yield break; }

        SetStatus(AGVStatus.Lifting);
        yield return StartCoroutine(LiftPanel());

        SortingSlotInfo slot = carriedPanel?.GetComponent<SortingSlotInfo>();
        if (slot != null) PanelSortingGate.ReleaseSlot(slot.slotX);

        // 재작업구역으로 이송
        SetStatus(AGVStatus.Carrying);
        Vector3 wp2 = new Vector3(reworkPos.x, 0, subLaneZ);
        if (tm != null) yield return StartCoroutine(tm.ReservePath(agvID, transform.position, wp2, maxSpeed));
        yield return StartCoroutine(MoveTo(wp2));
        if (tm != null) yield return StartCoroutine(tm.ReservePath(agvID, wp2, reworkPos, maxSpeed));
        yield return StartCoroutine(MoveTo(reworkPos));
        if (tm != null) tm.ReleaseAll(agvID);

        // ★ forStation=true → Sorted 유지, DefectProcessingStation이 감지 가능
        SetStatus(AGVStatus.Lowering);
        yield return StartCoroutine(LowerPanel(forStation: true));

        // ★ AGV는 즉시 복귀 (로봇암이 독립 처리)
        SetStatus(AGVStatus.Moving);
        yield return StartCoroutine(ReturnToBase());

        Debug.Log($"[{agvID}] ★재작업 드롭 완료 → DefectProcessingStation 인계.");
        SetStatus(AGVStatus.Idle);
        OnTaskComplete?.Invoke(this);
        if (FactoryDashboard.Instance != null)
            FactoryDashboard.Instance.RecordAGVTask(this);
    }

    // ─── 스크랩 태스크 (★ 신규) ──────────────────────────────
    /// <summary>
    /// 소터존 → ScrapStation 드롭 → 즉시 복귀
    /// ★ LowerPanel(forStation:true): Sorted 유지 → ScrapRobot이 감지 가능
    /// </summary>
    public IEnumerator ExecuteScrapTask(GameObject panel, Vector3 pickupPos, Vector3 scrapPos)
    {
        carriedPanel = panel;
        var tm = AGVTrafficManager.Instance;
        Debug.Log($"[{agvID}] ★스크랩 태스크: {panel?.name}");

        SetStatus(AGVStatus.Moving);

        // 픽업
        Vector3 wp1 = new Vector3(pickupPos.x, 0, subLaneZ);
        if (tm != null) yield return StartCoroutine(tm.ReservePath(agvID, transform.position, wp1, maxSpeed));
        yield return StartCoroutine(MoveTo(wp1));
        if (tm != null) yield return StartCoroutine(tm.ReservePath(agvID, wp1, pickupPos, maxSpeed));
        yield return StartCoroutine(MoveTo(pickupPos));
        if (tm != null) tm.ReleaseAll(agvID);

        if (carriedPanel == null) { SetStatus(AGVStatus.Idle); yield break; }

        SetStatus(AGVStatus.Lifting);
        yield return StartCoroutine(LiftPanel());

        SortingSlotInfo slot = carriedPanel?.GetComponent<SortingSlotInfo>();
        if (slot != null) PanelSortingGate.ReleaseSlot(slot.slotX);

        // 스크랩함으로 이송
        SetStatus(AGVStatus.Carrying);
        Vector3 wp2 = new Vector3(scrapPos.x, 0, subLaneZ);
        if (tm != null) yield return StartCoroutine(tm.ReservePath(agvID, transform.position, wp2, maxSpeed));
        yield return StartCoroutine(MoveTo(wp2));
        if (tm != null) yield return StartCoroutine(tm.ReservePath(agvID, wp2, scrapPos, maxSpeed));
        yield return StartCoroutine(MoveTo(scrapPos));
        if (tm != null) tm.ReleaseAll(agvID);

        // ★ forStation=true → Sorted 유지, ScrapRobot이 감지 가능
        SetStatus(AGVStatus.Lowering);
        yield return StartCoroutine(LowerPanel(forStation: true));

        // AGV 즉시 복귀
        SetStatus(AGVStatus.Moving);
        yield return StartCoroutine(ReturnToBase());

        Debug.Log($"[{agvID}] ★스크랩 드롭 완료 → ScrapRobot 인계.");
        SetStatus(AGVStatus.Idle);
        OnTaskComplete?.Invoke(this);
        if (FactoryDashboard.Instance != null)
            FactoryDashboard.Instance.RecordAGVTask(this);
    }

    // ─── 복귀 ─────────────────────────────────────────────────

    IEnumerator ReturnToBase()
    {
        var tm = AGVTrafficManager.Instance;
        Vector3 wr1 = new Vector3(transform.position.x, 0, subLaneZ);
        Vector3 wr2 = new Vector3(startPos.x, 0, subLaneZ);

        if (tm != null) yield return StartCoroutine(tm.ReservePath(agvID, transform.position, wr1, maxSpeed));
        yield return StartCoroutine(MoveTo(wr1));
        if (tm != null) yield return StartCoroutine(tm.ReservePath(agvID, wr1, wr2, maxSpeed));
        yield return StartCoroutine(MoveTo(wr2));
        if (tm != null) yield return StartCoroutine(tm.ReservePath(agvID, wr2, startPos, maxSpeed));
        yield return StartCoroutine(MoveTo(startPos));
        if (tm != null) tm.ReleaseAll(agvID);
    }

    // ─── 이동 ──────────────────────────────────────────────────

    IEnumerator MoveTo(Vector3 goal)
    {
        goal = new Vector3(goal.x, transform.position.y, goal.z);
        var tm = AGVTrafficManager.Instance;
        float logT = 0f;

        while (Vector3.Distance(transform.position, goal) > 0.2f)
        {
            Vector3 nextPos = transform.position
                + (goal - transform.position).normalized * currentSpeed * 0.5f;
            bool blocked = tm != null && tm.IsOccupied(agvID, nextPos);

            if (blocked)
            {
                currentSpeed = Mathf.MoveTowards(currentSpeed, 0, acceleration * Time.deltaTime);
                yield return null;
                continue;
            }

            float dist = Vector3.Distance(transform.position, goal);
            currentSpeed = Mathf.MoveTowards(currentSpeed,
                dist < 2f ? dist * 1.5f : maxSpeed, acceleration * Time.deltaTime);

            Vector3 dir = (goal - transform.position).normalized;
            transform.position += dir * currentSpeed * Time.deltaTime;
            if (dir.magnitude > 0.1f)
                transform.rotation = Quaternion.Slerp(transform.rotation,
                    Quaternion.LookRotation(dir), 8f * Time.deltaTime);

            batteryLevel = Mathf.Max(0f, batteryLevel - batteryDrainRate * Time.deltaTime / 60f);

            logT += Time.deltaTime;
            if (logT > 4f)
            {
                logT = 0;
                Debug.Log($"[{agvID}] 이동중 {transform.position:F1} → {goal:F1} ({dist:F1}m)");
            }
            yield return null;
        }
        currentSpeed = 0f;
    }

    // ─── 리프트 / 하강 ────────────────────────────────────────

    IEnumerator LiftPanel()
    {
        if (carriedPanel == null) yield break;

        // ★ 중복 픽업 방지: 이미 다른 AGV가 운반 중이면 취소
        SteelPanel spCheck = carriedPanel.GetComponent<SteelPanel>();
        if (spCheck != null && spCheck.status == SteelPanel.PanelStatus.OnAGV)
        {
            Debug.LogWarning($"[{agvID}] 중복 픽업 방지: {carriedPanel.name} 이미 OnAGV 상태 → 태스크 취소");
            carriedPanel = null;
            yield break;
        }

        // ★ 즉시 OnAGV 예약 → 다른 AGV가 동시에 체크해도 중복 픽업 방지
        if (spCheck != null) spCheck.status = SteelPanel.PanelStatus.OnAGV;

        Debug.Log($"[{agvID}] 리프트: {carriedPanel.name}");
        Rigidbody rb = carriedPanel.GetComponent<Rigidbody>();
        if (rb != null) rb.isKinematic = true;
        carriedPanel.transform.SetParent(cargoPlate);
        Vector3 s = carriedPanel.transform.localPosition, t = Vector3.up * liftHeight;
        for (float el = 0; el < 0.8f; el += Time.deltaTime)
        {
            if (carriedPanel == null) yield break;
            carriedPanel.transform.localPosition = Vector3.Lerp(s, t, el / 0.8f);
            yield return null;
        }
        if (carriedPanel != null)
        {
            SteelPanel sp = carriedPanel.GetComponent<SteelPanel>();
            if (sp != null) sp.status = SteelPanel.PanelStatus.OnAGV;
            Debug.Log($"[{agvID}] ★리프트 완료: {carriedPanel.name}");
        }
    }

    /// <summary>
    /// 패널 하강
    /// forStation=false(기본): 창고/출하 → status=Stored
    /// forStation=true:        로봇암 스테이션 → status 건드리지 않음 (Sorted 유지)
    ///                         → DefectProcessingStation OverlapSphere 감지 가능
    /// </summary>
    IEnumerator LowerPanel(bool forStation = false, float targetY = 0.65f)
    {
        if (carriedPanel == null) yield break;
        carriedPanel.transform.SetParent(null);
        Vector3 s = carriedPanel.transform.position;
        Vector3 t = new Vector3(s.x, targetY, s.z);
        for (float el = 0; el < 0.8f; el += Time.deltaTime)
        {
            if (carriedPanel == null) yield break;
            carriedPanel.transform.position = Vector3.Lerp(s, t, el / 0.8f);
            yield return null;
        }
        if (carriedPanel != null)
        {
            Rigidbody rb = carriedPanel.GetComponent<Rigidbody>();
            SteelPanel sp = carriedPanel.GetComponent<SteelPanel>();

            if (forStation)
            {
                // 스테이션 드롭: 물리 해제, Sorted 유지 (로봇암 감지용)
                if (sp != null) sp.status = SteelPanel.PanelStatus.Sorted;
                if (rb != null) { rb.isKinematic = false; rb.linearVelocity = rb.angularVelocity = Vector3.zero; }
            }
            else
            {
                // ★ 랙 드롭: Kinematic 유지 + 슬롯 위치 강제 고정
                // 물리 해제 시 선반 없는 슬롯에서 바닥으로 낙하하는 버그 방지
                if (rb != null) rb.isKinematic = true;
                carriedPanel.transform.position = new Vector3(
                    carriedPanel.transform.position.x, targetY,
                    carriedPanel.transform.position.z);
                if (sp != null) sp.status = SteelPanel.PanelStatus.Stored;
            }

            Debug.Log($"[{agvID}] ★하강 완료: {carriedPanel.name} | forStation={forStation} Y={targetY:F2}");
            carriedPanel = null;
        }
    }

    // ─── 유틸 ──────────────────────────────────────────────────

    IEnumerator BatteryLoop()
    {
        while (true)
        {
            yield return new WaitForSeconds(1f);
            if (FactoryDashboard.Instance != null)
                FactoryDashboard.Instance.UpdateAGVBattery(agvID, batteryLevel);
        }
    }

    void SetStatus(AGVStatus s)
    {
        status = s;
        if (FactoryDashboard.Instance != null)
            FactoryDashboard.Instance.UpdateAGVStatus(agvID, s.ToString());
    }

    void UpdateStatusLight()
    {
        if (statusLight == null) return;
        statusLight.color = status switch
        {
            AGVStatus.Idle      => Color.green,
            AGVStatus.Moving    => Color.yellow,
            AGVStatus.Lifting   => new Color(0, 0.7f, 1f),
            AGVStatus.Carrying  => new Color(0, 0.7f, 1f),
            AGVStatus.Lowering  => Color.cyan,
            AGVStatus.Reworking => new Color(1f, 0.5f, 0f),
            AGVStatus.Charging  => Color.magenta,
            _                   => Color.red
        };
    }

    void OnDestroy() { AGVTrafficManager.Instance?.ReleaseAll(agvID); }

    public bool IsAvailable() => status == AGVStatus.Idle && batteryLevel > 15f;
    public float GetBattery() => batteryLevel;
}