using UnityEngine;

/// <summary>
/// 공장 탑뷰 감시 카메라 - 테이블 3개를 항상 시야에 포함
/// </summary>
public class RobotCamera : MonoBehaviour
{
    [Header("감시 대상 중심점")]
    public Vector3 lookAtTarget = new Vector3(0, 0, 2f);

    [Header("카메라 위치")]
    public Vector3 cameraPosition = new Vector3(0, 8f, 2f);

    void Start()
    {
        transform.position = cameraPosition;
        transform.LookAt(lookAtTarget);
    }
}