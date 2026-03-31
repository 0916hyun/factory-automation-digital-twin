using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MM1Moving : MonoBehaviour
{
    [Header("목표 위치 포인트")]
    public Transform p1;
    public Transform p2;
    public Transform p3;
    public Transform np;
    public Transform ap;

    [Header("그리퍼 관련")]
    public Transform endEffectorPosition;
    public GameObject gripper;

    [Header("압력 센서 (Sensor4, Sensor5)")]
    public GameObject s4;
    public GameObject s5;

    private Vector3 startPos;
    private Vector3 startEEPosition;
    private Vector3 downPosition;

    private float speed;

    private ObjectPicking op;
    private ObjectPlace nop;
    private ObjectPlace aop;

    void Start()
    {
        startPos = transform.position;
        startEEPosition = endEffectorPosition.localPosition;

        downPosition = startEEPosition;
        downPosition.y -= 1.0f;

        op = gripper.GetComponent<ObjectPicking>();
        nop = s4.GetComponent<ObjectPlace>();
        aop = s5.GetComponent<ObjectPlace>();
    }

    public IEnumerator RobotMoving(int task, bool isNormal)
    {
        switch (task)
        {
            case 1:
                yield return StartCoroutine(Controller(p1.position, isNormal));
                break;
            case 2:
                yield return StartCoroutine(Controller(p2.position, isNormal));
                break;
            case 3:
                yield return StartCoroutine(Controller(p3.position, isNormal));
                break;
        }
    }

    IEnumerator Controller(Vector3 goal, bool isNormal)
    {
        yield return StartCoroutine(MMMoving(goal));
        yield return StartCoroutine(GripperPicking());
        yield return StartCoroutine(ClassificationMoving(isNormal));
        yield return StartCoroutine(GripperPlace(isNormal));
        yield return StartCoroutine(StartPositionMoving());
    }

    IEnumerator MMMoving(Vector3 goal)
    {
        while (transform.position != goal)
        {
            speed = 4.0f * Time.deltaTime;
            transform.position = Vector3.MoveTowards(
                transform.position, goal, speed);
            yield return null;
        }
    }

    IEnumerator GripperPicking()
    {
        // 그리퍼 내리기
        while (!op.isGrip)
        {
            speed = 4.0f * Time.deltaTime;
            endEffectorPosition.localPosition = Vector3.MoveTowards(
                endEffectorPosition.localPosition, downPosition, speed);
            yield return null;
        }

        // 그리퍼 올리기 (물체 든 채로 startEEPosition까지 완전히 올림)
        while (endEffectorPosition.localPosition != startEEPosition)
        {
            speed = 4.0f * Time.deltaTime;
            endEffectorPosition.localPosition = Vector3.MoveTowards(
                endEffectorPosition.localPosition, startEEPosition, speed);
            yield return null;
        }

        op.isGrip = false;
        Debug.Log("[MM1] 피킹 완료");
    }

    IEnumerator ClassificationMoving(bool isNormal)
    {
        Vector3 target = isNormal ? np.position : ap.position;

        // 이동 중 그리퍼는 startEEPosition 유지 (바닥에 안 끌리게)
        while (transform.position != target)
        {
            speed = 4.0f * Time.deltaTime;
            transform.position = Vector3.MoveTowards(
                transform.position, target, speed);
            // 이동 중 그리퍼 높이 startEEPosition으로 고정
            endEffectorPosition.localPosition = Vector3.MoveTowards(
                endEffectorPosition.localPosition, startEEPosition, speed);
            yield return null;
        }

        Debug.Log("[MM1] 분류 위치 도착 - " + (isNormal ? "양품(Normal)" : "불량품(Abnormal)"));
    }

    IEnumerator GripperPlace(bool isNormal)
    {
        ObjectPlace targetPlace = isNormal ? nop : aop;
        Vector3 targetSensorPos = isNormal ? s4.transform.position : s5.transform.position;

        while (Vector3.Distance(endEffectorPosition.position, targetSensorPos) > 0.1f)
        {
            speed = 4.0f * Time.deltaTime;
            endEffectorPosition.position = Vector3.MoveTowards(
                endEffectorPosition.position, targetSensorPos, speed);
            yield return null;
        }

        // Tag로 물체 찾기
        Transform heldObject = null;
        foreach (Transform child in gripper.transform)
        {
            if (child.CompareTag("TargetObject"))
            {
                heldObject = child;
                break;
            }
        }

        if (heldObject != null)
        {
            heldObject.SetParent(null);
            Rigidbody rb = heldObject.GetComponent<Rigidbody>();
            if (rb != null) rb.isKinematic = false;
            Debug.Log("[MM1] 물체 내려놓음: " + heldObject.name);
        }
        else
        {
            Debug.LogWarning("[MM1] 내려놓을 물체가 없습니다!");
        }
        // 내려놓은 후 2초간 MM1 Gripper 감지 비활성화
        op.DisableTemporary(2.0f);

        targetPlace.isPlace = true;
        yield return new WaitForSeconds(0.5f);

        while (endEffectorPosition.localPosition != startEEPosition)
        {
            speed = 4.0f * Time.deltaTime;
            endEffectorPosition.localPosition = Vector3.MoveTowards(
                endEffectorPosition.localPosition, startEEPosition, speed);
            yield return null;
        }

        targetPlace.isPlace = false;
        Debug.Log("[MM1] 내려놓기 완료");
    }

    IEnumerator StartPositionMoving()
    {
        while (transform.position != startPos)
        {
            speed = 4.0f * Time.deltaTime;
            transform.position = Vector3.MoveTowards(
                transform.position, startPos, speed);
            endEffectorPosition.localPosition = Vector3.MoveTowards(
                endEffectorPosition.localPosition, startEEPosition, speed);
            yield return null;
        }

        Debug.Log("[MM1] 시작 위치 복귀 완료");
    }
}