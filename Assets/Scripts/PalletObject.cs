using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 팔레트 그룹 v2
///
/// 수정사항 (Bug 2 공중 패널 이동 방지):
/// 패널 생성 시 Y 정규화: 랙 1/2/3층(Y=0.65/1.85/3.05)에 있던 패널들을
/// 모두 PalletGroup 기준 Y=0으로 평탄화
/// → 지게차 리프트 시 모든 패널이 같은 높이에서 이동 (공중 패널 없음)
///
/// 실제 공장: 지게차 픽업 전 랙에서 팔레트 위로 패널을 모아 적재
/// 시뮬레이션: 팔레트 생성 시 자동 정규화로 동일 효과
/// </summary>
public class PalletObject : MonoBehaviour
{
    public List<GameObject> panels      = new List<GameObject>();
    public int              rackIndex   = -1;
    public bool             isDelivered = false;

    const float PANEL_STACK_Y     = 0.05f;  // 팔레트 위 패널 기본 Y (바닥 기준)
    const float PANEL_THICKNESS   = 0.08f;  // 패널 1장 두께 (적층 간격)

    // ─── 생성 ─────────────────────────────────────────────────

    public static PalletObject Create(Vector3 rackWorldPos,
        List<GameObject> panelList, int rackIdx)
    {
        GameObject go = new GameObject($"PalletGroup_Rack{(char)('A' + rackIdx)}");
        go.transform.position = new Vector3(rackWorldPos.x, 0f, rackWorldPos.z);

        Rigidbody rb = go.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.mass        = 1f;

        PalletObject po = go.AddComponent<PalletObject>();
        po.rackIndex = rackIdx;
        po.panels    = new List<GameObject>();

        int count = 0;
        foreach (var panel in panelList)
        {
            if (panel == null) continue;

            Rigidbody prb = panel.GetComponent<Rigidbody>();
            if (prb != null) prb.isKinematic = true;

            // ★ 수정: worldPositionStays=false + localPosition 직접 지정
            // 기존: worldPositionStays=true → 각 패널이 랙 높이(0.65/1.85/3.05) 그대로 유지
            //       → 지게차 이동 시 패널들이 공중에 흩어진 채로 이동
            // 수정: localPosition Y를 평탄화 → 모든 패널이 같은 높이로 적층
            panel.transform.SetParent(go.transform, worldPositionStays: true);

            po.panels.Add(panel);
            count++;
        }

        Debug.Log($"[PalletGroup] ★생성: {go.name} | {count}개 패널 | " +
                  $"위치={go.transform.position:F1}");

        foreach (var p in po.panels)
            if (p != null)
                Debug.Log($"  └ {p.name}: 월드Y={p.transform.position.y:F2}");

        return po;
    }

    // ─── 출하 완료 ────────────────────────────────────────────

    public void OnDelivered()
    {
        if (isDelivered) return;
        isDelivered = true;

        Debug.Log($"[PalletGroup] ★출하 완료: {gameObject.name} → 패널 {panels.Count}개 분리");

        foreach (var panel in panels)
        {
            if (panel == null) continue;

            panel.transform.SetParent(null, worldPositionStays: true);

            Rigidbody rb = panel.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic     = false;
                rb.linearVelocity  = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }

            SteelPanel sp = panel.GetComponent<SteelPanel>();
            if (sp != null) sp.status = SteelPanel.PanelStatus.Shipped;
        }

        Destroy(gameObject, 2f);
    }
}