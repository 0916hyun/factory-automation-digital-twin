using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 대시보드 카메라 피드 위에 그려지는 바운딩박스 UI (단순화 버전)
/// </summary>
public class BoundingBoxUI : MonoBehaviour
{
    private RectTransform rectTransform;
    private Image bgImage;
    private Image[] corners = new Image[8]; // 코너당 H+V = 2개씩 × 4코너
    private Image labelBg;
    private Text labelText;

    private static readonly Color normalColor   = new Color(0.0f, 1.0f, 0.53f, 1f);
    private static readonly Color defectColor   = new Color(1.0f, 0.27f, 0.27f, 1f);
    private static readonly Color normalColorBg = new Color(0.0f, 1.0f, 0.53f, 0.08f);
    private static readonly Color defectColorBg = new Color(1.0f, 0.27f, 0.27f, 0.08f);

    public void Init()
    {
        rectTransform = gameObject.GetComponent<RectTransform>();
        if (rectTransform == null)
            rectTransform = gameObject.AddComponent<RectTransform>();

        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.zero;
        rectTransform.pivot = Vector2.zero;

        // 배경
        bgImage = CreateImageChild("BG", rectTransform);
        bgImage.color = normalColorBg;
        FillParent(bgImage.rectTransform);

        // 코너 마커 4개 (각각 가로선 + 세로선)
        float len = 14f, thick = 2.5f;
        // 0=좌하, 1=우하, 2=좌상, 3=우상
        Vector2[] anchors = { Vector2.zero, Vector2.right, Vector2.up, Vector2.one };

        for (int i = 0; i < 4; i++)
        {
            // 가로선
            Image h = CreateImageChild("C" + i + "H", rectTransform);
            h.color = normalColor;
            RectTransform rh = h.rectTransform;
            rh.anchorMin = anchors[i]; rh.anchorMax = anchors[i]; rh.pivot = anchors[i];
            rh.sizeDelta = new Vector2(len, thick);
            rh.anchoredPosition = Vector2.zero;

            // 세로선
            Image v = CreateImageChild("C" + i + "V", rectTransform);
            v.color = normalColor;
            RectTransform rv = v.rectTransform;
            rv.anchorMin = anchors[i]; rv.anchorMax = anchors[i]; rv.pivot = anchors[i];
            rv.sizeDelta = new Vector2(thick, len);
            rv.anchoredPosition = Vector2.zero;

            corners[i * 2]     = h;
            corners[i * 2 + 1] = v;
        }

        // 라벨 배경
        labelBg = CreateImageChild("LabelBg", rectTransform);
        labelBg.color = new Color(0f, 0.85f, 0.45f, 0.9f);
        RectTransform lbr = labelBg.rectTransform;
        lbr.anchorMin = new Vector2(0, 1);
        lbr.anchorMax = new Vector2(0, 1);
        lbr.pivot = new Vector2(0, 0);
        lbr.anchoredPosition = new Vector2(0, 3f);
        lbr.sizeDelta = new Vector2(150f, 22f);

        // 라벨 텍스트
        GameObject ltObj = new GameObject("LabelText");
        ltObj.transform.SetParent(labelBg.transform, false);
        labelText = ltObj.AddComponent<Text>();
        labelText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        labelText.fontSize = 11;
        labelText.fontStyle = FontStyle.Bold;
        labelText.color = Color.white;
        labelText.alignment = TextAnchor.MiddleCenter;
        labelText.text = "NORMAL";
        FillParent(ltObj.GetComponent<RectTransform>());
    }

    public void UpdateBox(PartDetectionSystem.DetectionResult result, Rect containerRect)
    {
        if (rectTransform == null) return;

        // 뷰포트 UV → 컨테이너 픽셀 좌표 (Y축: 뷰포트 아래=0, UI 아래=0 → 동일)
        float w = Mathf.Abs(containerRect.width);
        float h = Mathf.Abs(containerRect.height);

        float px = Mathf.Clamp01(result.screenRect.x) * w;
        float py = Mathf.Clamp01(result.screenRect.y) * h; // 0=아래, h=위
        float pw = Mathf.Max(result.screenRect.width  * w, 50f);
        float ph = Mathf.Max(result.screenRect.height * h, 50f);

        rectTransform.anchoredPosition = new Vector2(px, py);
        rectTransform.sizeDelta = new Vector2(pw, ph);

        Color col   = result.isNormal ? normalColor   : defectColor;
        Color bgCol = result.isNormal ? normalColorBg : defectColorBg;

        if (bgImage != null) bgImage.color = bgCol;

        foreach (var c in corners)
            if (c != null) c.color = col;

        if (labelBg != null)
            labelBg.color = result.isNormal
                ? new Color(0f, 0.75f, 0.4f, 0.92f)
                : new Color(0.85f, 0.15f, 0.15f, 0.92f);

        if (labelText != null)
        {
            string conf = (result.confidence * 100f).ToString("F1");
            labelText.text = result.isNormal ? $"NORMAL {conf}%" : $"DEFECT {conf}%";
        }
    }

    // ── 헬퍼 ──
    Image CreateImageChild(string name, Transform parent)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        return obj.AddComponent<Image>();
    }

    void FillParent(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}