using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// AI Detection 시뮬레이션
/// - 카메라 뷰포트 체크 대신 거리 기반 감지로 변경
/// - 항상 씬의 모든 TargetObject를 감지
/// </summary>
public class PartDetectionSystem : MonoBehaviour
{
    [Header("감지 설정")]
    public float detectionRange = 15f;
    public float detectionInterval = 0.1f;

    [Header("UI 연결")]
    public Camera detectionCamera;
    public RectTransform boundingBoxContainer;
    public GameObject boundingBoxPrefab;

    [Header("통계 UI")]
    public UnityEngine.UI.Text detectedCountText;
    public UnityEngine.UI.Text inferenceText;
    public UnityEngine.UI.Text normalCountText;
    public UnityEngine.UI.Text defectCountText;

    public class DetectionResult
    {
        public GameObject obj;
        public bool isNormal;
        public float confidence;
        public Rect screenRect;
    }

    List<BoundingBoxUI> activeBoxes = new List<BoundingBoxUI>();
    float inferenceTime = 0f;
    int totalDetected = 0;
    int normalCount = 0;
    int defectCount = 0;

    void Start()
    {
        Debug.Log("[Detection] Start() 호출됨");

        if (detectionCamera == null)
            detectionCamera = Camera.main;

        // RobotCamera 찾기
        var rc = FindObjectOfType<RobotCamera>();
        if (rc != null)
        {
            detectionCamera = rc.GetComponent<Camera>();
            Debug.Log("[Detection] detectionCamera = " + detectionCamera?.name + " ✅");
        }

        if (boundingBoxContainer != null)
            Debug.Log("[Detection] boundingBoxContainer = "
                + boundingBoxContainer.name + " ✅");

        StartCoroutine(DetectionLoop());
    }

    IEnumerator DetectionLoop()
    {
        Debug.Log("[Detection] DetectionLoop 시작");
        while (true)
        {
            float t = Time.realtimeSinceStartup;
            var results = ScanForParts();
            inferenceTime = Time.realtimeSinceStartup - t;

            UpdateBoundingBoxUI(results);
            UpdateStatsUI(results);

            yield return new WaitForSeconds(detectionInterval);
        }
    }

    List<DetectionResult> ScanForParts()
    {
        var results = new List<DetectionResult>();

        // TargetObject 태그로 모든 부품 탐색
        var allParts = GameObject.FindGameObjectsWithTag("TargetObject");
        Debug.Log("[Detection] 전체 TargetObject 수: " + allParts.Length);

        foreach (var part in allParts)
        {
            if (part == null) continue;

            // 거리 기반 감지 (카메라 뷰포트 체크 제거)
            bool inRange = true;
            if (detectionCamera != null)
            {
                float dist = Vector3.Distance(
                    detectionCamera.transform.position, part.transform.position);
                inRange = dist <= detectionRange;
            }

            if (!inRange) continue;

            // 색상으로 양품/불량 판단
            bool isNormal = IsNormalPart(part);
            float confidence = Random.Range(0.82f, 0.98f);

            // 스크린 좌표 계산 (UI 바운딩박스용)
            Rect screenRect = GetScreenRect(part);

            results.Add(new DetectionResult
            {
                obj        = part,
                isNormal   = isNormal,
                confidence = confidence,
                screenRect = screenRect,
            });
        }

        Debug.Log("[Detection] 감지된 부품 수: " + results.Count);
        return results;
    }

    bool IsNormalPart(GameObject obj)
    {
        // ★ abnormal 먼저 체크 (normal보다 먼저!)
        if (obj.name.ToLower().Contains("abnormal")) return false;
        if (obj.name.ToLower().Contains("normal")) return true;

        // 색상으로 판단
        var mr = obj.GetComponent<MeshRenderer>();
        if (mr != null && mr.material != null)
        {
            Color c = mr.material.GetColor("_BaseColor");
            if (c == default) c = mr.material.color;
            if (c.r > 0.6f && c.g < 0.4f && c.b < 0.4f) return false;
        }
        return true;
    }

    Rect GetScreenRect(GameObject obj)
    {
        if (detectionCamera == null)
            return new Rect(0.3f, 0.3f, 0.4f, 0.4f);

        // 월드 좌표 → 뷰포트 좌표
        Vector3 vp = detectionCamera.WorldToViewportPoint(obj.transform.position);

        // 부품 크기 추정 (거리 기반)
        float dist = Vector3.Distance(
            detectionCamera.transform.position, obj.transform.position);
        float sizeVP = Mathf.Clamp(1.5f / Mathf.Max(dist, 0.5f), 0.05f, 0.4f);

        return new Rect(
            Mathf.Clamp01(vp.x - sizeVP / 2f),
            Mathf.Clamp01(vp.y - sizeVP / 2f),
            sizeVP, sizeVP);
    }

    void UpdateBoundingBoxUI(List<DetectionResult> results)
    {
        if (boundingBoxContainer == null) return;

        // 기존 박스 숨기기
        foreach (var box in activeBoxes)
            if (box != null) box.gameObject.SetActive(false);

        Rect cr = boundingBoxContainer.rect;
        float w = Mathf.Abs(cr.width);
        float h = Mathf.Abs(cr.height);

        Debug.Log("[Detection] containerRect = " + cr);

        for (int i = 0; i < results.Count; i++)
        {
            BoundingBoxUI box;
            if (i < activeBoxes.Count && activeBoxes[i] != null)
            {
                box = activeBoxes[i];
                box.gameObject.SetActive(true);
            }
            else
            {
                box = CreateBoundingBox();
                if (i < activeBoxes.Count) activeBoxes[i] = box;
                else activeBoxes.Add(box);
            }

            var r = results[i];
            box.UpdateBox(r, cr);
        }
    }

    BoundingBoxUI CreateBoundingBox()
    {
        var go = new GameObject("BoundingBox");
        go.transform.SetParent(boundingBoxContainer, false);
        go.AddComponent<RectTransform>();
        var box = go.AddComponent<BoundingBoxUI>();
        box.Init(); // ★ 반드시 호출
        return box;
    }

    void UpdateStatsUI(List<DetectionResult> results)
    {
        int nc = 0, dc = 0;
        foreach (var r in results)
            if (r.isNormal) nc++; else dc++;

        totalDetected = results.Count;
        normalCount = nc;
        defectCount = dc;

        // ★ FPS 제거, 탐지 개수만 표시
        if (detectedCountText != null)
            detectedCountText.text = totalDetected > 0
                ? $"감지된 부품: {totalDetected}개"
                : "";  // 0개면 아무것도 안 보임

        if (normalCountText != null)
            normalCountText.text = $"◆ NORMAL ×{nc}";
        if (defectCountText != null)
            defectCountText.text = $"◆ DEFECT ×{dc}";

        // ★ inferenceText는 그냥 비워버림
        if (inferenceText != null)
            inferenceText.text = "";
    }
}