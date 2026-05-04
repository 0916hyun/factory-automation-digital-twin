using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 자동 분류 게이트 (다이버터)
/// 비전 검사 결과에 따라 부품을 종류별/양불별 레인으로 분기
/// </summary>
public class SortingGate : MonoBehaviour
{
    [Header("분기 레인 위치")]
    public Transform hexNutLane;
    public Transform screwLane;
    public Transform transistorLane;
    public Transform defectLane;

    [Header("게이트 플랩")]
    public Transform gateFlap;           // 회전하는 분기 플랩
    public float flapSpeed = 180f;       // 회전 속도

    [Header("AGV 호출")]
    public AGVFleetManager fleetManager;

    private Queue<(GameObject part, PartData pd)> sortQueue
        = new Queue<(GameObject, PartData)>();
    private bool isSorting = false;

    public void ReceiveInspectionResult(GameObject part, PartData pd)
    {
        sortQueue.Enqueue((part, pd));
        if (!isSorting) StartCoroutine(ProcessQueue());
    }

    IEnumerator ProcessQueue()
    {
        isSorting = true;
        while (sortQueue.Count > 0)
        {
            var (part, pd) = sortQueue.Dequeue();
            if (part == null) continue;

            yield return StartCoroutine(SortPart(part, pd));
            yield return new WaitForSeconds(0.3f);
        }
        isSorting = false;
    }

    IEnumerator SortPart(GameObject part, PartData pd)
    {
        // 목적지 결정
        Transform dest = GetDestination(pd);
        if (dest == null) yield break;

        // 게이트 플랩 작동
        yield return StartCoroutine(ActivateFlap(pd));

        // 부품을 목적지로 순간이동 (실제로는 분기 레인이 밀어줌)
        // 물리적으로는 부품이 벨트에서 레인으로 미끄러짐 시뮬레이션
        yield return StartCoroutine(SlideToDestination(part, dest.position));

        pd.status = pd.isDefective ? PartStatus.Defective : PartStatus.Sorted;

        // AGV 호출 (양품만)
        if (!pd.isDefective && fleetManager != null)
            fleetManager.RequestPickup(part, pd, dest);

        // 불량품은 일정 시간 후 제거
        if (pd.isDefective)
            Destroy(part, 5f);

        Debug.Log($"[Sorter] {part.name} → {(pd.isDefective ? "불량 레인" : $"{pd.partType} 레인")}");
    }

    IEnumerator ActivateFlap(PartData pd)
    {
        if (gateFlap == null) yield break;

        float targetAngle = pd.isDefective ? -45f :
            pd.partType == PartType.HexNut ? 0f :
            pd.partType == PartType.Screw ? 30f : -30f;

        Quaternion target = Quaternion.Euler(0, targetAngle, 0);
        float elapsed = 0f;
        float duration = Mathf.Abs(targetAngle) / flapSpeed;

        Quaternion from = gateFlap.localRotation;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            gateFlap.localRotation = Quaternion.Slerp(from, target, elapsed / duration);
            yield return null;
        }
    }

    IEnumerator SlideToDestination(GameObject part, Vector3 dest)
    {
        if (part == null) yield break;

        Rigidbody rb = part.GetComponent<Rigidbody>();
        Vector3 start = part.transform.position;
        float elapsed = 0f;
        float duration = 1.2f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            if (part == null) yield break;

            float t = elapsed / duration;
            float smooth = t * t * (3f - 2f * t);
            part.transform.position = Vector3.Lerp(start,
                dest + Vector3.up * 0.3f, smooth);

            if (rb != null) rb.isKinematic = true;
            yield return null;
        }

        if (part != null && rb != null)
        {
            rb.isKinematic = false;
            rb.linearVelocity = Vector3.zero;
        }
    }

    Transform GetDestination(PartData pd)
    {
        if (pd.isDefective) return defectLane;
        switch (pd.partType)
        {
            case PartType.HexNut:     return hexNutLane;
            case PartType.Screw:      return screwLane;
            case PartType.Transistor: return transistorLane;
            default: return hexNutLane;
        }
    }
}
