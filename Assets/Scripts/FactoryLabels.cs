using UnityEngine;

/// <summary>
/// 공장 각 구역 3D 라벨 자동 생성
/// Play 시작 시 자동 실행
/// </summary>
public class FactoryLabels : MonoBehaviour
{
    void Start()
    {
        CreateLabel("📥 투입 라인 1",  new Vector3(-4, 2.5f, 0),   new Color(1f, 0.8f, 0.1f));
        CreateLabel("📥 투입 라인 2",  new Vector3( 0, 2.5f, 0),   new Color(1f, 0.8f, 0.1f));
        CreateLabel("📥 투입 라인 3",  new Vector3( 4, 2.5f, 0),   new Color(1f, 0.8f, 0.1f));
        CreateLabel("✅ 양품 대기",     new Vector3( 0, 2.5f, 6),   new Color(0.2f, 1f, 0.4f));
        CreateLabel("❌ 불량 수거",     new Vector3(-4, 2.5f, 6),   new Color(1f, 0.3f, 0.3f));
        CreateLabel("🏁 최종 적재",     new Vector3( 4, 2.5f, 10),  new Color(0.3f, 0.6f, 1f));
    }

    void CreateLabel(string text, Vector3 position, Color color)
    {
        GameObject textObj = new GameObject("Label_" + text);
        textObj.transform.position = position;

        TextMesh tm = textObj.AddComponent<TextMesh>();
        tm.text = text;
        tm.fontSize = 14;          // ★ 작게
        tm.characterSize = 0.15f;  // ★ 핵심: 크기 조절
        tm.alignment = TextAlignment.Center;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.color = color;
        tm.fontStyle = FontStyle.Bold;
    }
}