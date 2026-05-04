using System.Collections;
using UnityEngine;

/// <summary>
/// 적재대/처리함에 올려진 부품을 일정 시간 후 제거
/// 부품이 무한 쌓이지 않도록 관리
/// </summary>
public class ObjectRemover : MonoBehaviour
{
    [Header("제거 설정")]
    public float removeDelay = 4.0f;    // 올려진 후 제거까지 대기시간
    public float checkRadius = 1.5f;    // 부품 감지 반경

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("TargetObject")) return;
        if (other.transform.parent != null) return;
        StartCoroutine(RemoveAfterDelay(other.gameObject));
    }

    IEnumerator RemoveAfterDelay(GameObject obj)
    {
        yield return new WaitForSeconds(removeDelay);
        if (obj != null && obj.transform.parent == null)
        {
            Debug.Log($"[Remover] 제거: {obj.name}");
            Destroy(obj);
        }
    }
}