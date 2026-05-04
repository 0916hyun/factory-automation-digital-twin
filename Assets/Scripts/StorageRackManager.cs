using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 자동창고 2랙 관리 - v2
///
/// 핵심 수정: TriggerShipping에서 모든 패널이 실제로 랙에 도착(Stored)할 때까지
/// 대기 후 팔레트 생성. AGV 이동 중 출하 트리거 발동으로 인한 패널 낙하 방지.
/// </summary>
public class StorageRackManager : MonoBehaviour
{
    public static StorageRackManager Instance;

    [Header("랙 그리드 설정")]
    public int   cols         = 1;
    public int   rows         = 3;
    public float colSpacing   = 2.0f;
    public float shelf0Y      = 0.65f;
    public float shelfSpacing = 1.2f;

    [Header("랙 위치 (2개)")]
    public Transform rackPlate;
    public Transform rackSheet;

    [Header("출하 도크 (레거시 폴백)")]
    public Transform shippingDock1;
    public Transform shippingDock2;

    [Header("소형 AGV Fleet")]
    public PanelAGVFleetManager fleetManager;

    [Header("출하 설정")]
    public int shippingThreshold = 3;

    // ─── 내부 ─────────────────────────────────────────────

    private class RackSlot
    {
        public Vector3    worldPos;
        public bool       occupied;
        public GameObject panel;
    }

    private List<RackSlot>[] racks;
    private int[]             rackCounts;
    private bool[]            shippingInProgress;
    private int               totalShipped = 0;

    static readonly string[]       RACK_NAMES = { "SteelPlate_Rack", "Sheet_Rack" };
    static readonly PanelModelType[] RACK_TYPES = { PanelModelType.Plate, PanelModelType.Sheet };

    // ─── 초기화 ───────────────────────────────────────────

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    void Start()
    {
        if (rackPlate == null) rackPlate = Find("SteelPlate_Rack");
        if (rackSheet  == null) rackSheet  = Find("Sheet_Rack");
        if (shippingDock1 == null) shippingDock1 = Find("Dock_Plate");
        if (shippingDock2 == null) shippingDock2 = Find("Dock_Sheet");
        if (fleetManager  == null)
            fleetManager = Object.FindFirstObjectByType<PanelAGVFleetManager>();

        InitRacks();
        Debug.Log($"[RackMgr] 초기화 | Plate랙={rackPlate?.name} Sheet랙={rackSheet?.name} " +
                  $"| {cols}열×{rows}행={cols*rows}슬롯/랙 | 임계={shippingThreshold}");
    }

    Transform Find(string n) { var g = GameObject.Find(n); return g?.transform; }

    void InitRacks()
    {
        racks              = new List<RackSlot>[2];
        rackCounts         = new int[2];
        shippingInProgress = new bool[2];

        Transform[] rackTr  = { rackPlate, rackSheet };
        float[]     defaultX = { -15f, 15f };

        for (int r = 0; r < 2; r++)
        {
            racks[r] = new List<RackSlot>();
            Vector3 basePos = rackTr[r] != null
                ? rackTr[r].position
                : new Vector3(defaultX[r], 0f, 55f);

            float[] colOffsets = new float[cols];
            for (int c = 0; c < cols; c++)
                colOffsets[c] = (c - (cols - 1) * 0.5f) * colSpacing;

            for (int row = 0; row < rows; row++)
            {
                float slotY = shelf0Y + row * shelfSpacing;
                for (int col = 0; col < cols; col++)
                {
                    racks[r].Add(new RackSlot
                    {
                        worldPos = new Vector3(basePos.x + colOffsets[col], slotY, basePos.z),
                        occupied = false,
                        panel    = null
                    });
                }
            }
        }
    }

    // ─── 슬롯 배정 ────────────────────────────────────────

    public Vector3 AssignSlot(GameObject panel, out int rackIndex, out int slotIndex)
    {
        rackIndex = -1;
        slotIndex = -1;

        SteelPanel sp = panel.GetComponent<SteelPanel>();
        int targetRack = GetRackIndexForPanel(sp);

        if (rackCounts[targetRack] >= cols * rows)
        {
            Debug.LogWarning($"[RackMgr] {RACK_NAMES[targetRack]} 만석!");
            return Vector3.zero;
        }

        for (int s = 0; s < racks[targetRack].Count; s++)
        {
            if (racks[targetRack][s].occupied) continue;

            racks[targetRack][s].occupied = true;
            racks[targetRack][s].panel    = panel;
            rackCounts[targetRack]++;
            rackIndex = targetRack;
            slotIndex = s;

            int row = s / cols, col = s % cols;
            Debug.Log($"[RackMgr] {RACK_NAMES[targetRack]} {rackCounts[targetRack]:D2}/{cols*rows} " +
                      $"[{row}행{col}열] Y={racks[targetRack][s].worldPos.y:F2}");

            if (rackCounts[targetRack] >= shippingThreshold && !shippingInProgress[targetRack])
                StartCoroutine(TriggerShipping(targetRack));

            return racks[targetRack][s].worldPos;
        }

        return Vector3.zero;
    }

    int GetRackIndexForPanel(SteelPanel sp)
    {
        if (sp == null) return 0;
        return sp.modelType == PanelModelType.Sheet ? 1 : 0;
    }

    // ─── 출하 트리거 ──────────────────────────────────────

    IEnumerator TriggerShipping(int rackIdx)
    {
        shippingInProgress[rackIdx] = true;
        Debug.Log($"[RackMgr] ★출하 트리거: {RACK_NAMES[rackIdx]} ({rackCounts[rackIdx]}개)");

        // ★ 슬롯 수집: Stored 패널만 팔레트로 묶음
        // OnAGV 상태(운반중) 패널은 슬롯 유지 → AGV가 정상 드롭 후 다음 사이클에 출하
        var panels = new List<GameObject>();
        for (int s = 0; s < racks[rackIdx].Count; s++)
        {
            if (!racks[rackIdx][s].occupied || racks[rackIdx][s].panel == null) continue;
            SteelPanel sp = racks[rackIdx][s].panel.GetComponent<SteelPanel>();

            // OnAGV = 아직 AGV가 운반 중 → 슬롯 그대로 유지
            if (sp != null && sp.status == SteelPanel.PanelStatus.OnAGV)
            {
                Debug.Log($"[RackMgr] {racks[rackIdx][s].panel.name} OnAGV 운반중 → 슬롯 유지");
                continue;
            }

            panels.Add(racks[rackIdx][s].panel);
            racks[rackIdx][s].occupied = false;
            racks[rackIdx][s].panel    = null;
        }

        // 실제 남은 점유 슬롯 수 재계산
        rackCounts[rackIdx] = 0;
        for (int s = 0; s < racks[rackIdx].Count; s++)
            if (racks[rackIdx][s].occupied) rackCounts[rackIdx]++;

        // Sorted 상태 패널이 있으면 Stored 될 때까지 대기
        float waitTime = 0f;
        bool allStored = false;
        while (!allStored && waitTime < 30f)
        {
            allStored = true;
            foreach (var p in panels)
            {
                if (p == null) continue;
                SteelPanel sp = p.GetComponent<SteelPanel>();
                if (sp != null && sp.status != SteelPanel.PanelStatus.Stored)
                {
                    allStored = false;
                    break;
                }
            }
            if (!allStored)
            {
                yield return new WaitForSeconds(0.5f);
                waitTime += 0.5f;
            }
        }

        if (!allStored)
            Debug.LogWarning($"[RackMgr] 30초 대기 초과 → 강제 출하");

        if (panels.Count == 0) { shippingInProgress[rackIdx] = false; yield break; }

        Transform[] rackTr = { rackPlate, rackSheet };
        Vector3 rackPos = rackTr[rackIdx] != null
            ? rackTr[rackIdx].position
            : new Vector3(rackIdx == 0 ? -15f : 15f, 0f, 55f);

        PalletObject pallet = PalletObject.Create(rackPos, panels, rackIdx);
        if (fleetManager != null)
            fleetManager.CancelTasksForPanels(panels);

        ForkLiftFleetManager flFleet = ForkLiftFleetManager.Instance;
        if (flFleet != null)
        {
            flFleet.RequestShipping(pallet.gameObject, rackPos);
            Debug.Log($"[RackMgr] {pallet.name} ({panels.Count}개) → 지게차 배차");

            // ★ 배차 직후 즉시 플래그 해제 (랙은 이미 비워졌으므로 새 패널 즉시 배정 가능)
            // 기존: 120초 대기 → 그 동안 shippingInProgress=true → 슬롯 배정 실패 → X=0 fallback
        }
        else
        {
            Debug.LogWarning("[RackMgr] ForkLiftFleetManager 없음 → 폴백");
            Transform dock = rackIdx == 0 ? shippingDock1 : shippingDock2;
            Vector3 dropP  = dock != null ? dock.position
                           : new Vector3(rackIdx == 0 ? -15f : 15f, 0, 67f);
            if (fleetManager != null)
                fleetManager.RequestShipping(pallet.gameObject, null, rackPos, dropP);
        }

        totalShipped += panels.Count;
        shippingInProgress[rackIdx] = false;
        Debug.Log($"[RackMgr] 출하 플래그 해제: {RACK_NAMES[rackIdx]}");

        // ★ 대기 중 채워진 새 패널이 임계값 도달 시 즉시 재트리거
        // 기존: 플래그 해제 후 다음 패널이 올 때까지 대기 → 그 패널이 만석 판정 → X=0 fallback
        if (rackCounts[rackIdx] >= shippingThreshold)
        {
            Debug.Log($"[RackMgr] 재적재 감지 → 즉시 재출하 트리거: {RACK_NAMES[rackIdx]} ({rackCounts[rackIdx]}개)");
            StartCoroutine(TriggerShipping(rackIdx));
        }
    }

    // ─── 슬롯 해제 ────────────────────────────────────────

    public void ReleaseSlot(int rackIdx, int slotIdx)
    {
        if (rackIdx < 0 || rackIdx >= 2) return;
        if (slotIdx < 0 || slotIdx >= racks[rackIdx].Count) return;
        racks[rackIdx][slotIdx].occupied = false;
        racks[rackIdx][slotIdx].panel    = null;
        if (rackCounts[rackIdx] > 0) rackCounts[rackIdx]--;
    }

    public int GetTotalCapacity() => 2 * cols * rows;
    public int GetTotalOccupied() => rackCounts[0] + rackCounts[1];
    public int GetTotalShipped()  => totalShipped;
}