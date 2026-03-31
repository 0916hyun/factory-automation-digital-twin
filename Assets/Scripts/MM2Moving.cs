using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MM2Moving : MonoBehaviour
{
    [Header("센서 오브젝트")]
    public GameObject s6;

    [Header("그리퍼 오브젝트")]
    public GameObject gripper;

    [Header("목표 위치 포인트")]
    public Transform np2;
    public Transform fp;
    public Transform endEffectorPosition;

    private Vector3 startPos;
    private Vector3 startEEPosition;
    private Vector3 downPosition;
    private float speed;
    private ObjectPlace op3;

    void Start()
    {
        startPos = transform.position;
        startEEPosition = endEffectorPosition.localPosition;
        downPosition = startEEPosition;
        downPosition.y -= 1.0f;
        op3 = s6.GetComponent<ObjectPlace>();
    }

    public IEnumerator RobotMoving()
    {
        yield return StartCoroutine(NormalMoving());
        yield return StartCoroutine(GripperPicking());
        yield return StartCoroutine(FinalMoving());
        yield return StartCoroutine(GripperPlace());
        yield return StartCoroutine(StartPositionMoving());
    }

    IEnumerator NormalMoving()
    {
        while (transform.position != np2.position)
        {
            speed = 4.0f * Time.deltaTime;
            transform.position = Vector3.MoveTowards(transform.position, np2.position, speed);
            yield return null;
        }
        Debug.Log("[MM2] NormalTable 도착");
    }

    IEnumerator GripperPicking()
    {
        // 그리퍼 내리기
        while (endEffectorPosition.localPosition != downPosition)
        {
            speed = 4.0f * Time.deltaTime;
            endEffectorPosition.localPosition = Vector3.MoveTowards(
                endEffectorPosition.localPosition, downPosition, speed);
            yield return null;
        }

        // NormalTable 근처(3f 이내) 물체만 집기
        GameObject target = null;
        GameObject[] allTargets = GameObject.FindGameObjectsWithTag("TargetObject");
        float minDist = float.MaxValue;
        foreach (GameObject obj in allTargets)
        {
            if (!obj.activeInHierarchy) continue;
            float dist = Vector3.Distance(obj.transform.position, endEffectorPosition.position);
            if (dist < minDist && dist < 3f)
            {
                minDist = dist;
                target = obj;
            }
        }

        if (target != null)
        {
            Rigidbody rb = target.GetComponent<Rigidbody>();
            if (rb != null) rb.isKinematic = true;
            target.transform.SetParent(gripper.transform);
            Debug.Log("[MM2] 물체 집기: " + target.name);
        }
        else
        {
            Debug.LogWarning("[MM2] 근처에 집을 물체 없음!");
        }

        // 그리퍼 올리기
        while (endEffectorPosition.localPosition != startEEPosition)
        {
            speed = 4.0f * Time.deltaTime;
            endEffectorPosition.localPosition = Vector3.MoveTowards(
                endEffectorPosition.localPosition, startEEPosition, speed);
            yield return null;
        }
        Debug.Log("[MM2] 피킹 완료");
    }

    IEnumerator FinalMoving()
    {
        while (transform.position != fp.position)
        {
            speed = 4.0f * Time.deltaTime;
            transform.position = Vector3.MoveTowards(transform.position, fp.position, speed);
            endEffectorPosition.localPosition = Vector3.MoveTowards(
                endEffectorPosition.localPosition, startEEPosition, speed);
            yield return null;
        }
        Debug.Log("[MM2] FinalStep 도착");
    }

    IEnumerator GripperPlace()
    {
        Vector3 targetPos = s6.transform.position;

        while (Vector3.Distance(endEffectorPosition.position, targetPos) > 0.1f)
        {
            speed = 4.0f * Time.deltaTime;
            endEffectorPosition.position = Vector3.MoveTowards(
                endEffectorPosition.position, targetPos, speed);
            yield return null;
        }

        Transform heldObject = null;
        foreach (Transform child in gripper.transform)
        {
            if (child.CompareTag("TargetObject")) { heldObject = child; break; }
        }
        if (heldObject != null)
        {
            heldObject.SetParent(null);
            Rigidbody rb = heldObject.GetComponent<Rigidbody>();
            if (rb != null) rb.isKinematic = false;
            Debug.Log("[MM2] 물체 내려놓음: " + heldObject.name);
        }

        yield return new WaitForSeconds(0.5f);

        while (endEffectorPosition.localPosition != startEEPosition)
        {
            speed = 4.0f * Time.deltaTime;
            endEffectorPosition.localPosition = Vector3.MoveTowards(
                endEffectorPosition.localPosition, startEEPosition, speed);
            yield return null;
        }
        Debug.Log("[MM2] 내려놓기 완료 → FinalStep 운반 성공!");
    }

    IEnumerator StartPositionMoving()
    {
        while (transform.position != startPos)
        {
            speed = 4.0f * Time.deltaTime;
            transform.position = Vector3.MoveTowards(transform.position, startPos, speed);
            endEffectorPosition.localPosition = Vector3.MoveTowards(
                endEffectorPosition.localPosition, startEEPosition, speed);
            yield return null;
        }
        Debug.Log("[MM2] 원위치 복귀 완료");
    }
}