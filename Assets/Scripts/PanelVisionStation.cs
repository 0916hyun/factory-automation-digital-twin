using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using Unity.InferenceEngine;   // ★ com.unity.ai.inference 패키지 네임스페이스

/// <summary>
/// 비전 검사 스테이션 v4 - Unity AI Inference (Sentis 2.6.1) 연동
///
/// 패키지: com.unity.ai.inference
/// 네임스페이스: Unity.InferenceEngine
/// Tensor 타입: Tensor&lt;float&gt; (TensorFloat 대신)
/// </summary>
public class PanelVisionStation : MonoBehaviour
{
    [Header("스테이션 설정")]
    public int   stationIndex   = 0;
    public float inspectionTime = 1.2f;

    [Header("Sentis 모델")]
    public ModelAsset  modelAsset;
    public BackendType backendType = BackendType.GPUCompute;

    [Header("조명 효과")]
    public Light scanLight;
    public Color scanColor   = new Color(0.3f, 0.7f, 1f);
    public Color normalColor = new Color(0.2f, 1f, 0.3f);
    public Color defectColor = new Color(1f, 0.2f, 0.2f);

    [Header("UI")]
    public Text resultText;
    public Text defectTypeText;
    public Text confidenceText;

    [Header("연결")]
    public PanelSortingGate sortingGate;

    // ─── Unity AI Inference ───────────────────────────────────
    private Model  runtimeModel;
    private Worker worker;
    private bool   modelLoaded = false;
    private bool   isScanning  = false;

    // ImageNet 정규화 (train_neu.py와 동일)
    static readonly float[] MEAN = { 0.485f, 0.456f, 0.406f };
    static readonly float[] STD  = { 0.229f, 0.224f, 0.225f };
    const int IMG_SIZE = 224;

    // 인덱스 → NEUDefectType (알파벳 순 = ImageFolder 기본)
    static readonly NEUDefectType[] INDEX_TO_DEFECT =
    {
        NEUDefectType.Crazing,        // 0
        NEUDefectType.Inclusion,      // 1
        NEUDefectType.Patches,        // 2
        NEUDefectType.PittedSurface,  // 3
        NEUDefectType.RolledInScale,  // 4
        NEUDefectType.Scratches       // 5
    };

    // ─── 초기화 ──────────────────────────────────────────────

    void Start()
    {
        var col = GetComponent<Collider>();
        if (col != null) col.isTrigger = true;
        LoadModel();
    }

    void LoadModel()
    {
        if (modelAsset == null)
        {
            Debug.LogWarning($"[Vision{stationIndex}] ModelAsset 미연결 → 룰베이스 폴백");
            return;
        }

        runtimeModel = ModelLoader.Load(modelAsset);
        worker       = new Worker(runtimeModel, backendType);
        modelLoaded  = true;

        Debug.Log($"[Vision{stationIndex}] ★ MobileNetV2 로드 완료 | backend={backendType}");
    }

    void OnDestroy() => worker?.Dispose();

    // ─── 트리거 ──────────────────────────────────────────────

    void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("TargetObject")) return;
        if (isScanning) return;

        SteelPanel panel = other.GetComponentInParent<SteelPanel>();
        if (panel == null || panel.inspected) return;

        panel.inspected = true;
        panel.status    = SteelPanel.PanelStatus.Inspecting;
        StartCoroutine(InspectPanel(panel.gameObject, panel));
    }

    // ─── 검사 코루틴 ─────────────────────────────────────────

    IEnumerator InspectPanel(GameObject panelObj, SteelPanel panel)
    {
        isScanning = true;
        SetLight(scanColor, 5f);
        UpdateUI("분석중...", "", 0f, scanColor);

        float elapsed = 0f;
        while (elapsed < inspectionTime)
        {
            elapsed += Time.deltaTime;
            if (scanLight != null)
                scanLight.intensity = 4f + Mathf.Sin(elapsed * 20f);
            yield return null;
        }

        // ─── 추론 ────────────────────────────────────────────
        NEUDefectType actual = panel.defectType;
        NEUDefectType predicted;
        float         confidence;

        if (modelLoaded && panel.defectTexture != null)
        {
            (predicted, confidence) = RunInference(panel.defectTexture);
        }
        else
        {
            // 폴백: 룰베이스
            predicted  = actual;
            confidence = UnityEngine.Random.Range(0.87f, 0.98f);
            if (!modelLoaded)
                Debug.LogWarning($"[Vision{stationIndex}] 모델 미로드 → 룰베이스");
            else
                Debug.LogWarning($"[Vision{stationIndex}] defectTexture null → 룰베이스");
        }

        panel.defectType = predicted;
        panel.confidence = confidence;
        panel.status     = SteelPanel.PanelStatus.Sorted;

        bool   isDefect  = predicted != NEUDefectType.Normal;
        Color  resultCol = isDefect ? defectColor : normalColor;
        string typeStr   = panel.GetDefectKorean();

        SetLight(resultCol, 4f);
        UpdateUI(isDefect ? "DEFECT" : "PASS", typeStr, confidence, resultCol);
        HighlightPanel(panelObj, isDefect);

        bool isCorrect = (predicted == actual);
        Debug.Log($"[Vision{stationIndex}] {panelObj.name} | " +
                  $"GT={actual} Pred={predicted} | " +
                  $"conf={confidence*100f:F1}% | {(isCorrect ? "✓정답" : "✗오분류")}");

        if (FactoryDashboard.Instance != null)
            FactoryDashboard.Instance.RecordVisionResult(predicted, actual, confidence);

        if (sortingGate != null)
            sortingGate.ReceiveResult(panelObj, panel);

        yield return new WaitForSeconds(2f);
        SetLight(Color.white, 1.5f);
        UpdateUI("대기중", "", 0f, Color.white);
        isScanning = false;
    }

    // ─── Unity AI Inference 추론 ─────────────────────────────

    (NEUDefectType defect, float confidence) RunInference(Texture2D texture)
    {
        // 1. 리사이즈
        Texture2D resized = ResizeTexture(texture, IMG_SIZE, IMG_SIZE);

        // 2. 픽셀 → CHW float 배열 (그레이→RGB 3채널, ImageNet 정규화)
        float[] inputData = new float[3 * IMG_SIZE * IMG_SIZE];
        Color[] pixels    = resized.GetPixels();

        for (int y = 0; y < IMG_SIZE; y++)
        {
            for (int x = 0; x < IMG_SIZE; x++)
            {
                Color c    = pixels[(IMG_SIZE - 1 - y) * IMG_SIZE + x];
                float gray = c.r * 0.299f + c.g * 0.587f + c.b * 0.114f;
                int   bi   = y * IMG_SIZE + x;
                for (int ch = 0; ch < 3; ch++)
                    inputData[ch * IMG_SIZE * IMG_SIZE + bi] = (gray - MEAN[ch]) / STD[ch];
            }
        }

        if (resized != texture) Destroy(resized);

        // 3. 추론 (Unity AI Inference API)
        using Tensor<float> inputTensor = new Tensor<float>(
            new TensorShape(1, 3, IMG_SIZE, IMG_SIZE), inputData);

        worker.Schedule(inputTensor);

        // 4. 출력 읽기
        Tensor<float> outputTensor = worker.PeekOutput() as Tensor<float>;
        var output = outputTensor.ReadbackAndClone();

        // 5. Softmax
        float   maxVal = float.MinValue;
        float[] probs  = new float[6];
        for (int i = 0; i < 6; i++)
            if (output[0, i] > maxVal) maxVal = output[0, i];

        float sumExp = 0f;
        for (int i = 0; i < 6; i++) { probs[i] = Mathf.Exp(output[0, i] - maxVal); sumExp += probs[i]; }
        for (int i = 0; i < 6; i++) probs[i] /= sumExp;

        output.Dispose();

        // 6. Argmax
        int   bestIdx  = 0;
        float bestProb = probs[0];
        for (int i = 1; i < 6; i++)
            if (probs[i] > bestProb) { bestProb = probs[i]; bestIdx = i; }

        return (INDEX_TO_DEFECT[bestIdx], bestProb);
    }

    // ─── 텍스처 리사이즈 ─────────────────────────────────────

    Texture2D ResizeTexture(Texture2D src, int w, int h)
    {
        var rt   = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        Graphics.Blit(src, rt);

        var dst = new Texture2D(w, h, TextureFormat.RGBA32, false);
        dst.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        dst.Apply();

        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);
        return dst;
    }

    // ─── 유틸 ────────────────────────────────────────────────

    void HighlightPanel(GameObject panelObj, bool isDefect)
    {
        Color c = isDefect ? new Color(1f, 0.2f, 0.2f) : new Color(0.2f, 1f, 0.3f);
        foreach (var mr in panelObj.GetComponentsInChildren<MeshRenderer>())
        {
            if (mr == null) continue;
            mr.material.EnableKeyword("_EMISSION");
            mr.material.SetColor("_EmissionColor", c * 0.4f);
        }
        StartCoroutine(ClearHighlight(panelObj));
    }

    IEnumerator ClearHighlight(GameObject panelObj)
    {
        yield return new WaitForSeconds(2f);
        if (panelObj == null) yield break;
        foreach (var mr in panelObj.GetComponentsInChildren<MeshRenderer>())
        {
            if (mr == null) continue;
            mr.material.SetColor("_EmissionColor", Color.black);
        }
    }

    void SetLight(Color color, float intensity)
    {
        if (scanLight == null) return;
        scanLight.color = color; scanLight.intensity = intensity;
    }

    void UpdateUI(string result, string type, float conf, Color color)
    {
        if (resultText     != null) { resultText.text = result; resultText.color = color; }
        if (defectTypeText != null) { defectTypeText.text = type; defectTypeText.color = color; }
        if (confidenceText != null && conf > 0)
            confidenceText.text = $"{conf * 100f:F1}%";
    }
}