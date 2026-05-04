using System.Collections;
using UnityEngine;

/// <summary>
/// 지게차 AGV 컨트롤러 v3
///
/// 수정사항:
/// - 도크 전진 거리: dockPos.z - 2.5f → dockPos.z - 1.0f
///   (기존: 2.5m 앞에서 하차 → 도크 바닥에 팔레트 올려놓음)
///   (수정: 1.0m 앞까지 전진 → 포크 끝이 도크 플랫폼 위에 도달)
/// - 랙 전진 거리: rackPos.z - 2f → rackPos.z - 1.5f (픽업 정확도 향상)
/// </summary>
public class ForkLiftAGV : MonoBehaviour
{
    [Header("ID")]
    public string forkLiftID = "FL_01";
    public float dedicatedRackX = -15f;

    [Header("이동")]
    public float maxSpeed     = 2.0f;
    public float emptySpeed   = 3.0f;
    public float acceleration = 1.5f;

    [Header("포크")]
    public Transform forkCarriage;
    public float forkHighY = 0.9f;
    public float forkLowY  = 0.12f;
    public float liftSpeed = 0.6f;

    [Header("상태")]
    public FLStatus status = FLStatus.Idle;

    [Header("시각 효과")]
    public Light statusLight;

    const float FL_LANE_Z = 60f;

    private float      currentSpeed  = 0f;
    private GameObject currentPallet = null;
    private Vector3    homePos;

    public enum FLStatus { Idle, MovingToRack, Lifting, Carrying, Lowering, Returning }
    public System.Action<ForkLiftAGV> OnTaskComplete;

    void Start()
    {
        homePos = transform.position;
        if (forkCarriage == null)
        {
            Transform t = transform.Find("ForkCarriage");
            if (t != null) forkCarriage = t;
        }
    }

    void Update() => RefreshLight();

    public bool IsAvailable() => status == FLStatus.Idle;

    public IEnumerator ExecuteShipping(GameObject pallet, Vector3 rackPos, Vector3 dockPos)
    {
        currentPallet = pallet;
        Debug.Log($"[{forkLiftID}] ★출하 시작: {pallet?.name} | 랙={rackPos:F1} → 도크={dockPos:F1}");

        // ━━━ 1. 지게차 레인으로 이동 ━━━
        SetStatus(FLStatus.MovingToRack);
        yield return StartCoroutine(MoveTo(
            new Vector3(transform.position.x, 0, FL_LANE_Z), emptySpeed));

        // ━━━ 2. 랙 X 위치로 수평 이동 ━━━
        yield return StartCoroutine(MoveTo(
            new Vector3(rackPos.x, 0, FL_LANE_Z), emptySpeed));

        // ━━━ 3. 포크 내리기 ━━━
        SetStatus(FLStatus.Lowering);
        yield return StartCoroutine(AnimateFork(forkLowY));

        // ━━━ 4. 랙 앞으로 전진 ━━━
        // ★ 수정: rackPos.z - 2f → rackPos.z - 1.5f (픽업 정확도 향상)
        Vector3 pickupPos = new Vector3(rackPos.x, 0, rackPos.z + 1.5f);  // ★ FL_LANE(Z=60)쪽에서 접근
        yield return StartCoroutine(MoveTo(pickupPos, emptySpeed * 0.4f));

        // ━━━ 5. 팔레트 그룹 부착 ━━━
        SetStatus(FLStatus.Lifting);
        AttachPallet();

        // ━━━ 6. 포크 리프트 ━━━
        yield return StartCoroutine(AnimateFork(forkHighY));
        yield return new WaitForSeconds(0.3f);

        // ━━━ 7. 후진 (레인으로) ━━━
        SetStatus(FLStatus.Carrying);
        yield return StartCoroutine(MoveTo(
            new Vector3(rackPos.x, 0, FL_LANE_Z), maxSpeed));

        // ━━━ 8. 도크 X로 수평 이동 ━━━
        yield return StartCoroutine(MoveTo(
            new Vector3(dockPos.x, 0, FL_LANE_Z), maxSpeed));

        // ━━━ 9. 도크로 전진 ━━━
        // ★ 핵심 수정: dockPos.z - 2.5f → dockPos.z - 1.0f
        //   기존: 2.5m 앞에서 하차 → 도크 바닥 앞에 팔레트 놓음
        //   수정: 1.0m 앞까지 진입 → 포크가 도크 플랫폼 위에 닿음
        Vector3 dockApproach = new Vector3(dockPos.x, 0, dockPos.z - 1.5f);  // ★ 포크 끝이 도크 위에 닿도록
        yield return StartCoroutine(MoveTo(dockApproach, maxSpeed * 0.5f));

        // ━━━ 10. 포크 내리기 → 하차 ━━━
        SetStatus(FLStatus.Lowering);
        yield return StartCoroutine(AnimateFork(forkLowY));
        DetachPallet();
        yield return new WaitForSeconds(0.4f);

        // ━━━ 11. 도크에서 후진 ━━━
        yield return StartCoroutine(MoveTo(
            new Vector3(dockPos.x, 0, FL_LANE_Z), emptySpeed));

        // ━━━ 12. 이동 자세 포크 높이 ━━━
        yield return StartCoroutine(AnimateFork(forkHighY * 0.4f));

        // ━━━ 13. 홈으로 복귀 ━━━
        SetStatus(FLStatus.Returning);
        yield return StartCoroutine(MoveTo(
            new Vector3(homePos.x, 0, FL_LANE_Z), emptySpeed));
        yield return StartCoroutine(MoveTo(homePos, emptySpeed));
        yield return StartCoroutine(AnimateFork(forkLowY));

        SetStatus(FLStatus.Idle);
        OnTaskComplete?.Invoke(this);
        Debug.Log($"[{forkLiftID}] ★출하 완료 → 복귀");
    }

    void AttachPallet()
    {
        if (currentPallet == null) return;

        Rigidbody rb = currentPallet.GetComponent<Rigidbody>();
        if (rb != null) rb.isKinematic = true;

        Transform parent = forkCarriage != null ? forkCarriage : transform;
        currentPallet.transform.SetParent(parent, worldPositionStays: true);

        Debug.Log($"[{forkLiftID}] ▲팔레트 부착: {currentPallet.name} | " +
                  $"부착 위치={currentPallet.transform.position:F1}");
    }

    void DetachPallet()
    {
        if (currentPallet == null) return;

        currentPallet.transform.SetParent(null, worldPositionStays: true);

        Rigidbody rb = currentPallet.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic     = false;
            rb.linearVelocity  = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        PalletObject po = currentPallet.GetComponent<PalletObject>();
        if (po != null) po.OnDelivered();

        Debug.Log($"[{forkLiftID}] ▼팔레트 하차: {currentPallet.name}");
        currentPallet = null;
    }

    IEnumerator AnimateFork(float targetLocalY)
    {
        if (forkCarriage == null) yield break;

        float startY = forkCarriage.localPosition.y;
        float dist   = Mathf.Abs(targetLocalY - startY);
        if (dist < 0.005f) yield break;

        float elapsed  = 0f;
        float duration = dist / liftSpeed;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            t = t * t * (3f - 2f * t);

            Vector3 lp = forkCarriage.localPosition;
            forkCarriage.localPosition = new Vector3(lp.x, Mathf.Lerp(startY, targetLocalY, t), lp.z);
            yield return null;
        }

        Vector3 fp = forkCarriage.localPosition;
        forkCarriage.localPosition = new Vector3(fp.x, targetLocalY, fp.z);
    }

    IEnumerator MoveTo(Vector3 goal, float speed = -1f)
    {
        if (speed < 0) speed = maxSpeed;
        goal.y = 0f;

        while (Vector3.Distance(transform.position, goal) > 0.12f)
        {
            float dist        = Vector3.Distance(transform.position, goal);
            float targetSpeed = dist < 2.5f ? Mathf.Max(0.3f, dist * 0.5f) : speed;
            currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, acceleration * Time.deltaTime);

            Vector3 dir = (goal - transform.position).normalized;
            transform.position += dir * currentSpeed * Time.deltaTime;

            if (dir.sqrMagnitude > 0.001f)
                transform.rotation = Quaternion.Slerp(
                    transform.rotation, Quaternion.LookRotation(dir), 4f * Time.deltaTime);

            yield return null;
        }

        currentSpeed = 0f;
        transform.position = new Vector3(goal.x, 0f, goal.z);
    }

    void SetStatus(FLStatus s)
    {
        status = s;
        RefreshLight();
        Debug.Log($"[{forkLiftID}] 상태: {s}");
    }

    void RefreshLight()
    {
        if (statusLight == null) return;
        statusLight.color = status switch
        {
            FLStatus.Idle         => Color.green,
            FLStatus.MovingToRack => Color.yellow,
            FLStatus.Lifting      => new Color(0f, 0.8f, 1f),
            FLStatus.Carrying     => new Color(1f, 0.5f, 0f),
            FLStatus.Lowering     => Color.cyan,
            FLStatus.Returning    => new Color(0.7f, 0.7f, 0.7f),
            _                     => Color.red
        };
        statusLight.intensity = status == FLStatus.Idle ? 1f : 2f;
    }

    void OnDestroy()
    {
        if (currentPallet != null) DetachPallet();
    }
}