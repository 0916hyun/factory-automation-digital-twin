using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ObjectPicking : MonoBehaviour
{
    [Header("물체를 잡고 있으면 true")]
    public bool isGrip = false;

    private bool disabled = false; // 일시적 감지 비활성화

    public void DisableTemporary(float seconds)
    {
        StartCoroutine(DisableRoutine(seconds));
    }

    private IEnumerator DisableRoutine(float seconds)
    {
        disabled = true;
        yield return new WaitForSeconds(seconds);
        disabled = false;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (disabled) return;
        if (!other.gameObject.CompareTag("TargetObject")) return;
        if (isGrip) return;

        Rigidbody rb = other.gameObject.GetComponent<Rigidbody>();
        if (rb != null) rb.isKinematic = true;

        isGrip = true;
        other.gameObject.transform.SetParent(this.transform);
        Debug.Log("[Gripper] 물체 집기: " + other.gameObject.name);
    }
}