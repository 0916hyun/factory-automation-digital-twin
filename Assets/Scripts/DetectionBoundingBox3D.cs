using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 3D 씬 공간에 월드스페이스 Canvas로 바운딩박스 + 라벨 표시
/// 부품 오브젝트에 부착
/// </summary>
[RequireComponent(typeof(MeshRenderer))]
public class DetectionBoundingBox3D : MonoBehaviour
{
    [Header("오프셋")]
    public float labelHeight = 0.6f;

    private GameObject canvasObj;
    private Canvas worldCanvas;
    private Text labelText;
    private Text confText;
    private Image panelBg;
    private LineRenderer boxOutline;

    private bool isInitialized = false;
    private Camera mainCamera;

    // 색상
    private static readonly Color normalColor = new Color(0.0f, 1.0f, 0.53f, 1f);
    private static readonly Color defectColor  = new Color(1.0f, 0.27f, 0.27f, 1f);

    void Start()
    {
        mainCamera = Camera.main;
        InitWorldCanvas();
        InitBoxOutline();
        isInitialized = true;

        // 처음에는 숨김
        SetVisible(false);
    }

    void LateUpdate()
    {
        // 라벨이 항상 카메라를 바라보도록
        if (canvasObj != null && mainCamera != null)
        {
            canvasObj.transform.LookAt(
                canvasObj.transform.position + mainCamera.transform.rotation * Vector3.forward,
                mainCamera.transform.rotation * Vector3.up
            );
        }
    }

    void InitWorldCanvas()
    {
        canvasObj = new GameObject("DetectionLabel_" + gameObject.name);
        canvasObj.transform.SetParent(transform);
        canvasObj.transform.localPosition = new Vector3(0, labelHeight, 0);
        canvasObj.transform.localScale = Vector3.one * 0.006f;

        worldCanvas = canvasObj.AddComponent<Canvas>();
        worldCanvas.renderMode = RenderMode.WorldSpace;

        RectTransform crt = canvasObj.GetComponent<RectTransform>();
        crt.sizeDelta = new Vector2(200, 60);

        // 배경 패널
        GameObject panelObj = new GameObject("Panel");
        panelObj.transform.SetParent(canvasObj.transform, false);
        panelBg = panelObj.AddComponent<Image>();
        panelBg.color = new Color(0f, 0.85f, 0.45f, 0.88f);
        RectTransform prt = panelObj.GetComponent<RectTransform>();
        prt.anchorMin = Vector2.zero;
        prt.anchorMax = Vector2.one;
        prt.offsetMin = Vector2.zero;
        prt.offsetMax = Vector2.zero;

        // 라벨 텍스트
        GameObject labelObj = new GameObject("Label");
        labelObj.transform.SetParent(canvasObj.transform, false);
        labelText = labelObj.AddComponent<Text>();
        labelText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        labelText.fontSize = 22;
        labelText.fontStyle = FontStyle.Bold;
        labelText.color = Color.white;
        labelText.alignment = TextAnchor.UpperCenter;
        labelText.text = "NORMAL";
        RectTransform lrt = labelObj.GetComponent<RectTransform>();
        lrt.anchorMin = Vector2.zero;
        lrt.anchorMax = Vector2.one;
        lrt.offsetMin = new Vector2(6, 4);
        lrt.offsetMax = new Vector2(-6, -4);

        // 신뢰도 텍스트 (아래)
        GameObject confObj = new GameObject("Confidence");
        confObj.transform.SetParent(canvasObj.transform, false);
        confText = confObj.AddComponent<Text>();
        confText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        confText.fontSize = 17;
        confText.fontStyle = FontStyle.Normal;
        confText.color = new Color(1f, 1f, 0.8f);
        confText.alignment = TextAnchor.LowerCenter;
        confText.text = "conf: 0.00";
        RectTransform crt2 = confObj.GetComponent<RectTransform>();
        crt2.anchorMin = Vector2.zero;
        crt2.anchorMax = Vector2.one;
        crt2.offsetMin = new Vector2(6, 4);
        crt2.offsetMax = new Vector2(-6, -4);
    }

    void InitBoxOutline()
    {
        // LineRenderer로 3D 바운딩박스 윤곽선
        boxOutline = gameObject.AddComponent<LineRenderer>();
        boxOutline.useWorldSpace = false;
        boxOutline.loop = true;
        boxOutline.widthMultiplier = 0.015f;
        boxOutline.positionCount = 8;

        // 기본 URP 셰이더
        Material mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        if (mat == null) mat = new Material(Shader.Find("Unlit/Color"));
        mat.color = normalColor;
        boxOutline.material = mat;
        boxOutline.startColor = normalColor;
        boxOutline.endColor = normalColor;

        UpdateOutlineVertices();
    }

    void UpdateOutlineVertices()
    {
        if (boxOutline == null) return;

        // 오브젝트 로컬 바운드 기준 (조금 크게)
        float s = 0.55f;
        boxOutline.positionCount = 16;

        // 아래 사각형 → 위 사각형 → 연결
        Vector3[] pts = new Vector3[16]
        {
            new Vector3(-s, -s, -s), new Vector3( s, -s, -s),
            new Vector3( s, -s,  s), new Vector3(-s, -s,  s),
            new Vector3(-s, -s, -s), // 아래 닫기
            new Vector3(-s,  s, -s), // 위로
            new Vector3( s,  s, -s), new Vector3( s, -s, -s),
            new Vector3( s,  s, -s), new Vector3( s,  s,  s),
            new Vector3( s, -s,  s), new Vector3( s,  s,  s),
            new Vector3(-s,  s,  s), new Vector3(-s, -s,  s),
            new Vector3(-s,  s,  s), new Vector3(-s,  s, -s),
        };
        boxOutline.positionCount = pts.Length;
        boxOutline.SetPositions(pts);
    }

    // ─────────────────────────────────────────
    // 외부에서 호출: 감지 결과 업데이트
    // ─────────────────────────────────────────
    public void UpdateDetection(bool isNormal, float confidence)
    {
        if (!isInitialized) return;

        SetVisible(true);

        Color col = isNormal ? normalColor : defectColor;
        Color bgCol = isNormal
            ? new Color(0f, 0.85f, 0.45f, 0.88f)
            : new Color(0.85f, 0.15f, 0.15f, 0.88f);

        // 패널 색상
        if (panelBg != null) panelBg.color = bgCol;

        // 라벨
        if (labelText != null)
            labelText.text = isNormal ? "✓ NORMAL" : "✗ DEFECT";

        // 신뢰도
        if (confText != null)
            confText.text = $"conf: {confidence:F3}";

        // 아웃라인 색상
        if (boxOutline != null)
        {
            boxOutline.startColor = col;
            boxOutline.endColor = col;
            if (boxOutline.material != null)
                boxOutline.material.color = col;
        }

        // 2초 후 자동 숨김
        StopAllCoroutines();
        StartCoroutine(HideAfterDelay(3.0f));
    }

    IEnumerator HideAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        SetVisible(false);
    }

    void SetVisible(bool visible)
    {
        if (canvasObj != null) canvasObj.SetActive(visible);
        if (boxOutline != null) boxOutline.enabled = visible;
    }

    void OnDestroy()
    {
        if (canvasObj != null) Destroy(canvasObj);
    }
}