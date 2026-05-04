using UnityEngine;
using UnityEngine.UI;

public class LabelCreator : MonoBehaviour
{
    void Start()
    {
        // 이름, 위치, 오프셋 설정
        CreateLabel("투입 테이블 1", new Vector3(-4, 2.5f, -7.8f));
        CreateLabel("투입 테이블 2", new Vector3(0, 2.5f, -7.8f));
        CreateLabel("투입 테이블 3", new Vector3(4, 2.5f, -7.8f));

        CreateLabel("컨베이어 벨트 1", new Vector3(-4, 1.5f, -4f));
        CreateLabel("컨베이어 벨트 2", new Vector3(0, 1.5f, -4f));
        CreateLabel("컨베이어 벨트 3", new Vector3(4, 1.5f, -4f));

        CreateLabel("양품 적재대", new Vector3(0, 3.0f, 7f));
        CreateLabel("불량품 처리함", new Vector3(-4, 3.0f, 7f));
        CreateLabel("최종 출하대", new Vector3(4, 3.5f, 12f));
    }

    void CreateLabel(string text, Vector3 worldPos)
    {
        // Canvas 생성
        GameObject canvasObj = new GameObject("Label_" + text.Split('\n')[0]);
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvasObj.AddComponent<CanvasScaler>();
        canvasObj.AddComponent<GraphicRaycaster>();

        canvasObj.transform.position = worldPos;
        canvasObj.transform.localScale = Vector3.one * 0.008f;

        // 배경 패널
        GameObject bgObj = new GameObject("Background");
        bgObj.transform.SetParent(canvasObj.transform, false);
        Image bg = bgObj.AddComponent<Image>();
        bg.color = new Color(0, 0, 0, 0.6f);
        RectTransform bgRect = bg.GetComponent<RectTransform>();
        bgRect.sizeDelta = new Vector2(220, 70);

        // 텍스트
        GameObject textObj = new GameObject("Text");
        textObj.transform.SetParent(canvasObj.transform, false);
        Text label = textObj.AddComponent<Text>();
        label.text = text;
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.fontSize = 20;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = Color.white;
        RectTransform textRect = label.GetComponent<RectTransform>();
        textRect.sizeDelta = new Vector2(220, 70);

        // 카메라 방향 바라보기
        canvasObj.AddComponent<LookAtCamera>();
    }
}