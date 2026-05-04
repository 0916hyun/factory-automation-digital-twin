using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 지게차 2대 전담 관제
///   FL_01 (dedicatedRackX=-15) → SteelPlate_Rack 전담
///   FL_02 (dedicatedRackX=+15) → Sheet_Rack 전담
/// 교차 배차 없음 → 동선 충돌 원천 차단
/// </summary>
public class ForkLiftFleetManager : MonoBehaviour
{
    public static ForkLiftFleetManager Instance;

    [Header("지게차 목록 (순서: FL_01, FL_02)")]
    public List<ForkLiftAGV> fleet = new List<ForkLiftAGV>();

    [Header("도크 (자동 탐색)")]
    public Transform dockPlate;   // Dock_Plate  X=-15 → FL_01 전용
    public Transform dockSheet;   // Dock_Sheet  X=+15 → FL_02 전용

    private Queue<ShippingOrder> orderQueue = new Queue<ShippingOrder>();

    private class ShippingOrder
    {
        public GameObject pallet;
        public Vector3    rackPos;
        public Vector3    dockPos;
        public float      dedicatedX; // 이 주문을 처리할 지게차 전담 X

        public ShippingOrder(GameObject p, Vector3 r, Vector3 d, float dx)
        { pallet = p; rackPos = r; dockPos = d; dedicatedX = dx; }
    }

    // ─── 초기화 ───────────────────────────────────────────

    void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }
    }

    void Start()
    {
        AutoFindDocks();
        AutoFindForkLifts();   // ★ fleet 참조 날아간 경우 자동 재탐색
        StartCoroutine(DispatchLoop());
        Debug.Log($"[FLFleet] 초기화: 지게차 {fleet.Count}대 | " +
                  $"Plate도크={dockPlate?.name} Sheet도크={dockSheet?.name}");

        foreach (var fl in fleet)
            if (fl != null)
                Debug.Log($"  └ {fl.forkLiftID}: 전담랙X={fl.dedicatedRackX:F0} | 가용={fl.IsAvailable()}");
    }

    void AutoFindDocks()
    {
        if (dockPlate == null) { var g = GameObject.Find("Dock_Plate"); if (g) dockPlate = g.transform; }
        if (dockSheet  == null) { var g = GameObject.Find("Dock_Sheet");  if (g) dockSheet  = g.transform; }
    }

    // ★ Editor 스크립트에서 Add한 fleet 참조가 Play 시 날아가는 Unity 직렬화 문제 대응
    void AutoFindForkLifts()
    {
        fleet.RemoveAll(fl => fl == null);
        if (fleet.Count == 0)
        {
            var all = Object.FindObjectsByType<ForkLiftAGV>(FindObjectsSortMode.None);
            foreach (var fl in all)
                if (!fleet.Contains(fl)) fleet.Add(fl);
            Debug.Log($"[FLFleet] AutoFind: {fleet.Count}대 자동 탐색");
        }
    }

    // ─── 출하 요청 ────────────────────────────────────────

    /// <summary>
    /// StorageRackManager.TriggerShipping()에서 호출
    /// rackPos.x로 어느 전담 지게차인지 자동 판단
    /// </summary>
    public void RequestShipping(GameObject palletGO, Vector3 rackPos)
    {
        // 랙 X가 0보다 작으면 Plate(FL_01), 크면 Sheet(FL_02)
        float dedicatedX = rackPos.x < 0f ? -15f : 15f;
        Vector3 dockPos  = dedicatedX < 0f
            ? (dockPlate != null ? dockPlate.position : new Vector3(-15f, 0.3f, 67f))
            : (dockSheet  != null ? dockSheet.position  : new Vector3( 15f, 0.3f, 67f));

        orderQueue.Enqueue(new ShippingOrder(palletGO, rackPos, dockPos, dedicatedX));

        Debug.Log($"[FLFleet] ★출하 요청: {palletGO?.name} | " +
                  $"랙X={rackPos.x:F0} → 도크={dockPos:F1} | 전담X={dedicatedX:F0} | 대기={orderQueue.Count}");
    }

    // ─── 배차 루프 ────────────────────────────────────────

    IEnumerator DispatchLoop()
    {
        while (true)
        {
            yield return new WaitForSeconds(0.4f);
            if (orderQueue.Count == 0) continue;

            CleanNullOrders();
            if (orderQueue.Count == 0) continue;

            // 큐에서 처리 가능한 주문 탐색 (전담 지게차가 가용한 주문)
            ShippingOrder dispatched = null;
            var temp = new List<ShippingOrder>(orderQueue);

            foreach (var order in temp)
            {
                ForkLiftAGV fl = GetDedicatedAvailable(order.dedicatedX);
                if (fl == null) continue; // 전담 지게차 바쁨 → 대기

                // 큐에서 제거
                var rebuild = new Queue<ShippingOrder>();
                bool removed = false;
                while (orderQueue.Count > 0)
                {
                    var o = orderQueue.Dequeue();
                    if (!removed && o == order) { removed = true; continue; }
                    rebuild.Enqueue(o);
                }
                orderQueue = rebuild;

                Debug.Log($"[FLFleet] 배차: {fl.forkLiftID} (전담X={fl.dedicatedRackX:F0}) " +
                          $"→ {order.pallet?.name}");
                StartCoroutine(fl.ExecuteShipping(order.pallet, order.rackPos, order.dockPos));
                dispatched = order;
                break;
            }

            // 모든 주문이 전담 지게차 바쁨 상태면 다음 사이클 대기
        }
    }

    // ─── 전담 지게차 탐색 ────────────────────────────────

    /// <summary>
    /// dedicatedRackX가 targetX와 일치하는 가용 지게차 반환
    /// 전담 지게차가 바쁘면 null (교차 배차 없음)
    /// </summary>
    ForkLiftAGV GetDedicatedAvailable(float targetX)
    {
        foreach (var fl in fleet)
        {
            if (fl == null) continue;
            if (Mathf.Abs(fl.dedicatedRackX - targetX) > 1f) continue; // 전담 아님
            if (fl.IsAvailable()) return fl;
        }
        return null; // 전담 지게차 바쁨
    }

    void CleanNullOrders()
    {
        var temp = new Queue<ShippingOrder>();
        while (orderQueue.Count > 0)
        {
            var o = orderQueue.Dequeue();
            if (o.pallet != null) temp.Enqueue(o);
        }
        orderQueue = temp;
    }

    public int GetQueueCount()  => orderQueue.Count;
    public int GetIdleCount()
    {
        int n = 0;
        foreach (var fl in fleet) if (fl != null && fl.IsAvailable()) n++;
        return n;
    }
}