using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 컨베이어 벨트 애니메이터 v2
///
/// 수정사항 (Bug 4 패널 날아감 방지):
/// 1. OnPartExit: 벨트 이탈 시 Z 속도 즉시 0 → 패널이 소터 구역 넘어서 날아가지 않음
/// 2. FixedUpdate: Z=30 이후 감속 구간 → 자연스러운 감속 후 정지
///    (소터가 SlideTo로 Z=33까지 부드럽게 이동)
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public class ConveyorBeltAnimator : MonoBehaviour
{
    public float beltSpeed    = 2.5f;

    // ★ 감속 시작 Z 좌표 (소팅 구역 Z=33 기준 3m 앞)
    const float DECEL_START_Z = 30f;
    // ★ 정지 Z 좌표 (소팅 구역 직전)
    const float STOP_Z        = 33.5f;

    private HashSet<Rigidbody> partsOnBelt = new HashSet<Rigidbody>();
    private GameObject         triggerObj;

    void Start()
    {
        // 벨트 표면 마찰 0
        BoxCollider physBelt = GetComponent<BoxCollider>();
        physBelt.isTrigger = false;
        PhysicsMaterial beltMat = new PhysicsMaterial("BeltSurface");
        beltMat.dynamicFriction = 0f;
        beltMat.staticFriction  = 0f;
        beltMat.bounciness      = 0f;
        beltMat.frictionCombine = PhysicsMaterialCombine.Minimum;
        beltMat.bounceCombine   = PhysicsMaterialCombine.Minimum;
        physBelt.material = beltMat;

        // 감지 트리거
        triggerObj = new GameObject("BeltTrigger");
        triggerObj.transform.SetParent(transform.parent);
        triggerObj.transform.position   = transform.position + Vector3.up * 1.0f;
        triggerObj.transform.rotation   = transform.rotation;
        triggerObj.transform.localScale = Vector3.one;
        BoxCollider tc = triggerObj.AddComponent<BoxCollider>();
        tc.isTrigger = true;
        Vector3 ws = transform.lossyScale;
        tc.size   = new Vector3(ws.x * 1.2f, 2.5f, ws.z);
        tc.center = Vector3.zero;

        BeltTriggerReceiver rx = triggerObj.AddComponent<BeltTriggerReceiver>();
        rx.animator = this;

        Debug.Log($"[Belt {transform.parent?.name}] 생성 완료 size={tc.size}");
    }

    void FixedUpdate()
    {
        partsOnBelt.RemoveWhere(rb => rb == null);

        foreach (Rigidbody rb in partsOnBelt)
        {
            if (rb == null || rb.isKinematic) continue;

            Vector3 v   = rb.linearVelocity;
            float   posZ = rb.transform.position.z;

            // ★ 구간별 목표 속도
            float targetZ;
            if (posZ >= STOP_Z)
            {
                // 소팅 구역 도달 → 완전 정지
                targetZ = 0f;
            }
            else if (posZ >= DECEL_START_Z)
            {
                // 감속 구간: DECEL_START_Z ~ STOP_Z 사이에서 선형 감속
                float ratio = (posZ - DECEL_START_Z) / (STOP_Z - DECEL_START_Z);
                targetZ = Mathf.Lerp(beltSpeed, 0f, ratio);
            }
            else
            {
                // 정상 벨트 구간
                targetZ = beltSpeed;
            }

            v.z = Mathf.MoveTowards(v.z, targetZ, 12f * Time.fixedDeltaTime);
            v.x = Mathf.MoveTowards(v.x, 0f, 5f * Time.fixedDeltaTime);

            // Y 위로 튀는 거 방지
            if (rb.transform.position.y < 2.0f && v.y > 0f) v.y = 0f;

            rb.linearVelocity = v;
        }
    }

    static bool IsPanel(Collider other, out Rigidbody rb)
    {
        rb = other.attachedRigidbody;
        if (rb == null) return false;
        return rb.CompareTag("TargetObject") || other.CompareTag("TargetObject");
    }

    public void OnPartEnter(Collider other)
    {
        if (!IsPanel(other, out Rigidbody rb)) return;
        partsOnBelt.Add(rb);
        Debug.Log($"[Belt] 진입: {rb.name} z={rb.transform.position.z:F1} Set={partsOnBelt.Count}");
    }

    public void OnPartStay(Collider other)
    {
        if (!IsPanel(other, out Rigidbody rb)) return;
        partsOnBelt.Add(rb);
    }

    public void OnPartExit(Collider other)
    {
        if (!IsPanel(other, out Rigidbody rb)) return;
        partsOnBelt.Remove(rb);

        // ★ 핵심 수정: 벨트 이탈 시 Z 속도 0으로 감속
        //   기존: 이탈 후 beltSpeed 관성으로 계속 날아감
        //   수정: 이탈 즉시 정지 → 소터가 SlideTo로 부드럽게 이동
        if (!rb.isKinematic)
        {
            Vector3 v = rb.linearVelocity;
            v.z = 0f;
            v.x = 0f;
            rb.linearVelocity = v;
        }

        Debug.Log($"[Belt] 퇴장: {rb.name} z={rb.transform.position.z:F1}");
    }

    void OnDestroy()
    {
        if (triggerObj != null) Destroy(triggerObj);
    }
}

public class BeltTriggerReceiver : MonoBehaviour
{
    public ConveyorBeltAnimator animator;
    void OnTriggerEnter(Collider other) => animator?.OnPartEnter(other);
    void OnTriggerStay(Collider other)  => animator?.OnPartStay(other);
    void OnTriggerExit(Collider other)  => animator?.OnPartExit(other);
}