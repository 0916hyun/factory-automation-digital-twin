using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ObjectPlace : MonoBehaviour
{
    [Header("부품이 이 위에 있으면 true")]
    public bool isPlace = false;

    private void OnTriggerEnter(Collider other)
    {
        if (!other.gameObject.CompareTag("TargetObject")) return;

        // 이미 isPlace면 무시 (중복 감지 방지)
        if (isPlace) return;

        isPlace = true;
        Debug.Log("[" + gameObject.name + "] 부품 도착: " + other.gameObject.name);
        // SetParent(null)은 MM1Moving의 GripperPlace()에서
        // isPlace=true 확인 후 그리퍼가 올라갈 때 처리하도록 제거
    }
}