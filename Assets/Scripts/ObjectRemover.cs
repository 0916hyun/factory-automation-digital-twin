using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ObjectRemover : MonoBehaviour
{
    private void OnTriggerEnter(Collider other)
    {
        if (!other.gameObject.CompareTag("TargetObject")) return;
        StartCoroutine(Remover(other));
        Debug.Log("[" + gameObject.name + "] 부품 수거 예약: " + other.gameObject.name);
    }

    IEnumerator Remover(Collider other)
    {
        yield return new WaitForSeconds(2f);
        if (other != null && other.gameObject != null)
        {
            other.gameObject.SetActive(false);
            Debug.Log("[" + gameObject.name + "] 부품 제거 완료");

            // ObjectPlace isPlace 초기화
            ObjectPlace op = GetComponent<ObjectPlace>();
            if (op != null) op.isPlace = false;
        }
    }
}