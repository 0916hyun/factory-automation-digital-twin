using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 불량품 처리 로봇암 스테이션 v2
///
/// 변경사항 (버그 수정):
/// ScanForPanels() 필터 수정:
///   기존: sp.status == Stored || sp.status == Shipped → 무시
///   수정: sp.status != Sorted → 무시 (Sorted 패널만 처리)
///
/// 이유: AGVController.LowerPanel(forStation:true)가 status를 Sorted 유지.
///       기존 코드는 Stored도 무시하여 로봇이 영원히 패널 감지 불가.
///       수정 후: AGV 드롭 → Sorted 유지 → ScanForPanels 감지 → 로봇 처리
///
/// StationType.Rework  (X=+21, Z=42): 경미한 결함
///   → 잡기 → 재작업 테이블(Z=47) 이동 → 15초 처리 → 70% 창고 / 30% 폐기
///
/// StationType.Scrap   (X=-21, Z=42): 심각한 결함
///   → 잡기 → 스크랩함(Z=37) 이동 → 투하 → Destroy
/// </summary>
public class DefectProcessingStation : MonoBehaviour
{
    public enum StationType { Rework, Scrap }

    [Header("스테이션 타입")]
    public StationType stationType = StationType.Rework;

    [Header("로봇 관절 (BuildSmartFactory 자동 연결)")]
    public Transform rotationBase;   // Y축 수평 선회
    public Transform arm1Pivot;      // X축 어깨 (ShoulderPivot)
    public Transform arm2Pivot;      // X축 팔꿈치 (ElbowPivot)
    public Transform gripperRoot;    // 그리퍼 루트
    public Transform gripperL;       // 왼쪽 손가락
    public Transform gripperR;       // 오른쪽 손가락

    [Header("위치 레퍼런스")]
    public Transform processingPoint;  // 처리 목표 (테이블 or 스크랩함)

    [Header("연결")]
    public PanelAGVFleetManager fleetManager;

    [Header("재작업 설정")]
    public float reworkDuration = 15f;
    [Range(0f, 1f)]
    public float passRate = 0.7f;

    // ─── 내부 ──────────────────────────────────────────────────

    const float A1_IDLE  = -15f;
    const float A2_IDLE  =  35f;
    const float A1_PICK  = -75f;
    const float A2_PICK  =  10f;
    const float A1_CARRY = -30f;
    const float A2_CARRY =  60f;

    const float GRIP_OPEN   = 0.22f;
    const float GRIP_CLOSED = 0.09f;

    private Queue<(GameObject panel, SteelPanel sp)> queue = new();
    private bool processing = false;
    private Light statusLight;

    // ─── 초기화 ────────────────────────────────────────────────

    void Start()
    {
        processing = false;  // 명시적 초기화

        if (fleetManager == null)
            fleetManager = Object.FindFirstObjectByType<PanelAGVFleetManager>();

        statusLight = GetComponentInChildren<Light>();

        SetIdlePose();
        SetLight(Color.green);
        Debug.Log($"[{name}] 초기화 | 타입={stationType} | FleetMgr={fleetManager != null}");

        StartCoroutine(ScanForPanels());
    }

    // ─── 패널 스캔 (0.4초 폴링) ───────────────────────────────
    /// <summary>
    /// OnTriggerEnter 대신 OverlapSphere 폴링 사용
    /// → AGV가 스피어 중앙에 내려놔도 확실 감지
    ///
    /// ★ 필터 수정: Sorted 상태 패널만 수락
    ///   (AGVController.LowerPanel forStation=true가 Sorted 유지)
    ///   Shipped 패널은 제외, OnAGV 패널(운반 중)은 AGVController 체크로 제외
    /// </summary>
    IEnumerator ScanForPanels()
    {
        const float SCAN_RADIUS   = 8f;
        const float SCAN_INTERVAL = 0.4f;

        while (true)
        {
            yield return new WaitForSeconds(SCAN_INTERVAL);

            if (processing) continue;

            var hits = Physics.OverlapSphere(transform.position, SCAN_RADIUS);
            Debug.Log($"[{name}] 스캔: {hits.Length}개 감지");
            foreach (var hit in hits)
            {
                if (hit == null || hit.isTrigger) continue;

                var sp = hit.GetComponentInParent<SteelPanel>();
                if (sp == null) continue;
                Debug.Log($"[{name}] 후보: {sp.gameObject.name} | status={sp.status}");  // ← 추가

                // AGV 운반 중 패널 무시
                if (hit.transform.GetComponentInParent<AGVController>() != null) continue;

                // Sorted 상태만 수락
                if (sp.status != SteelPanel.PanelStatus.Sorted) continue;

                // 중복 큐 방지 (★ sp.gameObject 기준 - 자식 콜라이더가 아닌 루트)
                bool alreadyQueued = false;
                foreach (var (p, _) in queue)
                    if (p == sp.gameObject) { alreadyQueued = true; break; }
                if (alreadyQueued) continue;

                // ★ hit.gameObject(자식) 대신 sp.gameObject(루트) 큐에 추가
                queue.Enqueue((sp.gameObject, sp));
                Debug.Log($"[{name}] ▶ 패널 감지: {sp.gameObject.name} | 큐={queue.Count}");
            }

            if (queue.Count > 0 && !processing)
                StartCoroutine(ProcessQueue());
        }
    }

    // ─── 처리 큐 ──────────────────────────────────────────────

    IEnumerator ProcessQueue()
    {
        processing = true;
        while (queue.Count > 0)
        {
            var (panel, sp) = queue.Dequeue();
            if (panel == null) continue;

            if (stationType == StationType.Rework)
                yield return StartCoroutine(DoRework(panel, sp));
            else
                yield return StartCoroutine(DoScrap(panel));

            yield return new WaitForSeconds(0.5f);
        }
        processing = false;
        yield return StartCoroutine(ReturnToIdle());
        SetLight(Color.green);
    }

    // ─── 재작업 처리 (경미한 결함) ───────────────────────────

    IEnumerator DoRework(GameObject panel, SteelPanel sp)
    {
        if (panel == null) yield break;
        Debug.Log($"[{name}] ★ 재작업 시작: {panel.name}");
        SetLight(new Color(1f, 0.6f, 0.1f));

        // 1. 패널 방향 선회
        yield return StartCoroutine(TurnBaseToward(panel.transform.position, 0.7f));

        // 2. 픽업 자세
        yield return StartCoroutine(RotateJoint(arm1Pivot, A1_PICK, 0.6f));
        yield return StartCoroutine(RotateJoint(arm2Pivot, A2_PICK, 0.4f));

        // 3. 그립
        SetGripper(false);
        yield return new WaitForSeconds(0.25f);
        GrabPanel(panel);
        yield return new WaitForSeconds(0.15f);
        SetGripper(true);
        yield return new WaitForSeconds(0.3f);

        // 4. 들어올리기
        yield return StartCoroutine(RotateJoint(arm1Pivot, A1_CARRY, 0.5f));
        yield return StartCoroutine(RotateJoint(arm2Pivot, A2_CARRY, 0.4f));

        // 5. 처리 방향 선회
        Vector3 procTarget = processingPoint != null
            ? processingPoint.position
            : transform.position + transform.forward * 2f;
        yield return StartCoroutine(TurnBaseToward(procTarget, 0.6f));

        // 6. 재작업 중 (진동 애니메이션)
        SetLight(Color.yellow);
        yield return StartCoroutine(ReworkAnimation(reworkDuration));

        // 7. 재검사 판정
        bool passed = Random.value < passRate;
        Debug.Log($"[{name}] 재검사 결과: {panel?.name} → {(passed ? "통과 → 창고" : "실패 → 폐기")}");

        // 8. 패널 해제
        SetGripper(false);
        yield return new WaitForSeconds(0.2f);
        ReleasePanel(panel);

        // 9. 결과 처리
        if (passed && fleetManager != null && panel != null)
        {
            sp.status = SteelPanel.PanelStatus.Sorted;
            var rackMgr = StorageRackManager.Instance;
            Vector3 rackPos = Vector3.zero;
            if (rackMgr != null)
                rackPos = rackMgr.AssignSlot(panel, out _, out _);
            if (rackPos == Vector3.zero)
                rackPos = new Vector3(sp.modelType == PanelModelType.Sheet ? 15f : -15f, 0.65f, 55f);

            fleetManager.RequestPickupAt(panel, sp, panel.transform.position, rackPos);
            SetLight(Color.green);
            Debug.Log($"[{name}] 통과 → 창고 AGV 요청: {rackPos:F1}");

            // ★ 대시보드: 재작업 통과 기록
            if (FactoryDashboard.Instance != null)
                FactoryDashboard.Instance.RecordReworkResult(passed: true);
        }
        else if (panel != null)
        {
            sp.status = SteelPanel.PanelStatus.Sorted;
            SetLight(Color.red);
            Object.Destroy(panel, 1.2f);
            Debug.Log($"[{name}] 실패 → 패널 폐기");

            // ★ 대시보드: 재작업 실패 기록
            if (FactoryDashboard.Instance != null)
                FactoryDashboard.Instance.RecordReworkResult(passed: false);
        }

        // 10. 대기 자세 복귀
        yield return new WaitForSeconds(0.4f);
        yield return StartCoroutine(RotateJoint(arm1Pivot, A1_IDLE, 0.5f));
        yield return StartCoroutine(RotateJoint(arm2Pivot, A2_IDLE, 0.4f));
        yield return StartCoroutine(TurnBase(0f, 0.5f));
    }

    // ─── 스크랩 처리 (심각한 결함) ───────────────────────────

    IEnumerator DoScrap(GameObject panel)
    {
        if (panel == null) yield break;
        Debug.Log($"[{name}] ★ 스크랩 시작: {panel.name}");
        SetLight(new Color(1f, 0.1f, 0.1f));

        // 1. 패널 방향 선회
        yield return StartCoroutine(TurnBaseToward(panel.transform.position, 0.6f));

        // 2. 팔 뻗기
        yield return StartCoroutine(RotateJoint(arm1Pivot, A1_PICK, 0.5f));
        yield return StartCoroutine(RotateJoint(arm2Pivot, A2_PICK, 0.35f));

        // 3. 잡기
        SetGripper(false);
        yield return new WaitForSeconds(0.2f);
        GrabPanel(panel);
        SetGripper(true);
        yield return new WaitForSeconds(0.25f);

        // 4. 빠르게 들어올리기
        yield return StartCoroutine(RotateJoint(arm1Pivot, -10f, 0.4f));
        yield return StartCoroutine(RotateJoint(arm2Pivot,  25f, 0.3f));

        // 5. 스크랩함 방향 선회
        Vector3 scrapTarget = processingPoint != null
            ? processingPoint.position
            : transform.position - transform.forward * 2f;
        yield return StartCoroutine(TurnBaseToward(scrapTarget, 0.55f));

        // 6. 스크랩함 위로 팔 이동
        yield return StartCoroutine(RotateJoint(arm1Pivot, -45f, 0.4f));
        yield return StartCoroutine(RotateJoint(arm2Pivot,  15f, 0.3f));
        yield return new WaitForSeconds(0.2f);

        // 7. 투하
        SetGripper(false);
        ReleasePanel(panel);
        if (panel != null) Object.Destroy(panel, 1.5f);
        Debug.Log($"[{name}] 스크랩 투하 완료");

        // ★ 대시보드: 스크랩 기록
        if (FactoryDashboard.Instance != null)
            FactoryDashboard.Instance.RecordScrap();

        // 8. 복귀
        yield return new WaitForSeconds(0.3f);
        yield return StartCoroutine(RotateJoint(arm1Pivot, A1_IDLE, 0.5f));
        yield return StartCoroutine(RotateJoint(arm2Pivot, A2_IDLE, 0.4f));
        yield return StartCoroutine(TurnBase(0f, 0.4f));
        SetLight(Color.green);
    }

    // ─── 대기 자세 복귀 ───────────────────────────────────────

    IEnumerator ReturnToIdle()
    {
        yield return StartCoroutine(RotateJoint(arm1Pivot, A1_IDLE, 0.5f));
        yield return StartCoroutine(RotateJoint(arm2Pivot, A2_IDLE, 0.4f));
        yield return StartCoroutine(TurnBase(0f, 0.4f));
        SetGripper(false);
    }

    void SetIdlePose()
    {
        if (arm1Pivot) arm1Pivot.localEulerAngles = new Vector3(A1_IDLE, 0, 0);
        if (arm2Pivot) arm2Pivot.localEulerAngles = new Vector3(A2_IDLE, 0, 0);
        SetGripper(false);
    }

    // ─── 패널 잡기 / 놓기 ────────────────────────────────────

    void GrabPanel(GameObject panel)
    {
        if (panel == null || gripperRoot == null) return;

        var rb = panel.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic     = true;   // ★ 먼저 kinematic 설정 후 velocity 초기화
            rb.linearVelocity  = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        panel.transform.SetParent(gripperRoot, worldPositionStays: true);
        Debug.Log($"[{name}] 잡기: {panel.name}");
    }

    void ReleasePanel(GameObject panel)
    {
        if (panel == null) return;

        panel.transform.SetParent(null, worldPositionStays: true);

        var rb = panel.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic     = false;
            rb.linearVelocity  = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
    }

    // ─── 그리퍼 ──────────────────────────────────────────────

    void SetGripper(bool closed)
    {
        if (gripperL == null || gripperR == null) return;
        float offset = closed ? GRIP_CLOSED : GRIP_OPEN;
        Vector3 lp = gripperL.localPosition;
        Vector3 rp = gripperR.localPosition;
        gripperL.localPosition = new Vector3(-offset, lp.y, lp.z);
        gripperR.localPosition = new Vector3( offset, rp.y, rp.z);
    }

    // ─── 애니메이션 ──────────────────────────────────────────

    IEnumerator TurnBaseToward(Vector3 worldTarget, float duration)
    {
        if (rotationBase == null) yield break;
        Vector3 dir = new Vector3(worldTarget.x - transform.position.x, 0,
                                  worldTarget.z - transform.position.z);
        if (dir.sqrMagnitude < 0.01f) yield break;
        float targetY = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
        yield return StartCoroutine(TurnBase(targetY, duration));
    }

    IEnumerator TurnBase(float targetY, float duration)
    {
        if (rotationBase == null) yield break;
        float startY  = rotationBase.localEulerAngles.y;
        float delta   = Mathf.DeltaAngle(startY, targetY);
        float endY    = startY + delta;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, elapsed / duration);
            rotationBase.localEulerAngles = new Vector3(0, Mathf.Lerp(startY, endY, t), 0);
            yield return null;
        }
        rotationBase.localEulerAngles = new Vector3(0, targetY, 0);
    }

    IEnumerator RotateJoint(Transform joint, float targetX, float duration)
    {
        if (joint == null) yield break;
        float startX = joint.localEulerAngles.x;
        if (startX > 180f) startX -= 360f;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, elapsed / duration);
            float curX = Mathf.Lerp(startX, targetX, t);
            joint.localEulerAngles = new Vector3(curX,
                joint.localEulerAngles.y, joint.localEulerAngles.z);
            yield return null;
        }
        joint.localEulerAngles = new Vector3(targetX,
            joint.localEulerAngles.y, joint.localEulerAngles.z);
    }

    IEnumerator ReworkAnimation(float duration)
    {
        float elapsed = 0f;
        float baseA2  = A2_CARRY;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;

            float wobble = Mathf.Sin(elapsed * 3f) * 4f;
            if (arm2Pivot)
                arm2Pivot.localEulerAngles = new Vector3(baseA2 + wobble,
                    arm2Pivot.localEulerAngles.y, arm2Pivot.localEulerAngles.z);

            float blink = (Mathf.Sin(elapsed * Mathf.PI * 2f) + 1f) * 0.5f;
            SetLightIntensity(1f + blink * 2f);

            yield return null;
        }

        SetLightIntensity(2f);
    }

    // ─── 조명 유틸 ───────────────────────────────────────────

    void SetLight(Color color)
    {
        if (statusLight != null)
        { statusLight.color = color; statusLight.intensity = 2f; }
    }

    void SetLightIntensity(float intensity)
    {
        if (statusLight != null) statusLight.intensity = intensity;
    }
}