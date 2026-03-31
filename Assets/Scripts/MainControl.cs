using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MainControl : MonoBehaviour
{
    [Header("센서 부모 오브젝트")]
    public Transform sensors;

    [Header("로봇 오브젝트")]
    public GameObject mm1;
    public GameObject mm2;

    [Header("센서 오브젝트")]
    public GameObject s4;

    private MM1Moving mm;
    private MM2Moving mmm;
    private ObjectPlace op;
    private CollisionSensor[] cs = new CollisionSensor[3];

    private List<int> tasks = new List<int>
    {
        1, 2, 1, 1, 1, 1, 1, 1, 1, 1
    };

    private bool isNormal;

    void Start()
    {
        if (mm1 != null) mm = mm1.GetComponent<MM1Moving>();
        if (mm2 != null) mmm = mm2.GetComponent<MM2Moving>();
        if (s4 != null) op = s4.GetComponent<ObjectPlace>();

        for (int i = 0; i < cs.Length; i++)
        {
            GameObject sensorObj = sensors.GetChild(i).gameObject;
            cs[i] = sensorObj.GetComponent<CollisionSensor>();
        }

        StartCoroutine(StartTasks());
        StartCoroutine(StartMM2());
    }

    IEnumerator StartTasks()
    {
        const float MaxTime = 180f;

        for (int i = 0; i < tasks.Count; i++)
        {
            if (Time.time > MaxTime) { Debug.Log("시뮬레이션 완료"); yield break; }

            bool canProceed = false;

            switch (tasks[i])
            {
                case 1:
                    if (cs[0] != null) { canProceed = cs[0].p1; isNormal = cs[0].n1; }
                    break;
                case 2:
                    if (cs[1] != null) { canProceed = cs[1].p2; isNormal = cs[1].n2; }
                    break;
                case 3:
                    if (cs[2] != null) { canProceed = cs[2].p3; isNormal = cs[2].n3; }
                    break;
            }

            if (canProceed)
            {
                Debug.Log("작업 " + tasks[i] + " 시작, 양품=" + isNormal);
                // 센서 플래그 즉시 초기화 (다음 물체 감지 준비)
                if (cs[0] != null) { cs[0].p1 = false; }
                if (cs[1] != null) { cs[1].p2 = false; }
                if (cs[2] != null) { cs[2].p3 = false; }
                yield return StartCoroutine(mm.RobotMoving(tasks[i], isNormal));
            }
            else
            {
                yield return null;
                i--;
            }
        }

        Debug.Log("모든 스케줄 작업 완료");
    }

    IEnumerator StartMM2()
    {
        const float MaxTime = 190f;

        while (Time.time <= MaxTime)
        {
            if (op == null) { yield return null; continue; }

            // isPlace가 true이고 실제 물체가 NormalTable 위에 있을 때만 출발
            while (!op.isPlace)
            {
                yield return null;
            }

            // 실제 물체 존재 확인
            GameObject target = null;
            GameObject[] allTargets = GameObject.FindGameObjectsWithTag("TargetObject");
            foreach (GameObject obj in allTargets)
            {
                if (!obj.activeInHierarchy) continue;
                float dist = Vector3.Distance(obj.transform.position, s4.transform.position);
                if (dist < 2f) { target = obj; break; }
            }

            if (target == null)
            {
                // 물체 없으면 isPlace 초기화 후 다시 대기
                op.isPlace = false;
                yield return null;
                continue;
            }

            Debug.Log("MM2 작업 시작");
            yield return StartCoroutine(mmm.RobotMoving());

            // 작업 후 isPlace 초기화
            op.isPlace = false;
        }
    }
}