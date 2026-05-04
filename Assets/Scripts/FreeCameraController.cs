using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 자유 카메라 컨트롤러 (Unity 6 New Input System 전용)
/// 
/// 조작법:
///   WASD / 방향키       - 이동
///   마우스 우클릭 드래그  - 시점 회전
///   Q / E              - 하강 / 상승
///   Shift              - 빠르게 이동
///   스크롤 휠           - 이동 속도 조절
/// </summary>
public class FreeCameraController : MonoBehaviour
{
    [Header("이동 설정")]
    public float moveSpeed = 8f;
    public float shiftMultiplier = 2.5f;
    public float scrollSpeedStep = 2f;

    [Header("회전 설정")]
    public float mouseSensitivity = 3f;

    [Header("속도 범위")]
    public float minSpeed = 1f;
    public float maxSpeed = 50f;

    private float rotX = 0f;
    private float rotY = 0f;

    void Start()
    {
        Vector3 angles = transform.eulerAngles;
        rotX = angles.y;
        rotY = angles.x;
    }

    void Update()
    {
        Keyboard kb = Keyboard.current;
        Mouse mouse = Mouse.current;
        if (kb == null || mouse == null) return;

        // 마우스 우클릭 시점 회전
        if (mouse.rightButton.isPressed)
        {
            Vector2 delta = mouse.delta.ReadValue();
            rotX += delta.x * mouseSensitivity * 0.1f;
            rotY -= delta.y * mouseSensitivity * 0.1f;
            rotY = Mathf.Clamp(rotY, -90f, 90f);
            transform.rotation = Quaternion.Euler(rotY, rotX, 0f);

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
        else
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        // WASD / 방향키 이동
        float speed = moveSpeed;
        if (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed)
            speed *= shiftMultiplier;

        Vector3 move = Vector3.zero;

        if (kb.wKey.isPressed || kb.upArrowKey.isPressed)
            move += transform.forward;
        if (kb.sKey.isPressed || kb.downArrowKey.isPressed)
            move -= transform.forward;
        if (kb.aKey.isPressed || kb.leftArrowKey.isPressed)
            move -= transform.right;
        if (kb.dKey.isPressed || kb.rightArrowKey.isPressed)
            move += transform.right;
        if (kb.eKey.isPressed)
            move += Vector3.up;
        if (kb.qKey.isPressed)
            move -= Vector3.up;

        transform.position += move.normalized * speed * Time.deltaTime;

        // 스크롤 휠 속도 조절
        float scroll = mouse.scroll.ReadValue().y;
        if (scroll != 0f)
        {
            moveSpeed += Mathf.Sign(scroll) * scrollSpeedStep;
            moveSpeed = Mathf.Clamp(moveSpeed, minSpeed, maxSpeed);
        }
    }
}
