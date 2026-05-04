using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Unity.InferenceEngine;

/// <summary>
/// Semantic Segmentation 추론 시스템 (Sentis 2.5.0 / Inference Engine)
/// 
/// [모드 1] AI 모드: ONNX 모델로 실제 세그멘테이션
/// [모드 2] Fallback 모드: 색상 기반 부품 감지 (모델 없이 동작)
/// 
/// Inspector에서 modelAsset을 연결하면 AI 모드, 비워두면 Fallback 모드
/// </summary>
public class SegmentationInference : MonoBehaviour
{
    [Header("=== 카메라 설정 ===")]
    public Camera inspectionCamera;
    public RenderTexture cameraFeedRT;

    [Header("=== 모델 설정 ===")]
    [Tooltip("ONNX 모델 에셋 (비워두면 Fallback 모드)")]
    public ModelAsset modelAsset;

    [Header("=== 추론 설정 ===")]
    public float inferenceInterval = 0.5f;
    public int inputWidth = 513;
    public int inputHeight = 513;

    [Header("=== 마스크 설정 ===")]
    [Range(0f, 1f)]
    public float maskAlpha = 0.4f;
    public Color normalColor = new Color(0f, 1f, 0f, 0.4f);
    public Color abnormalColor = new Color(1f, 0f, 0f, 0.4f);

    [Header("=== 감지 임계값 (Fallback) ===")]
    public float redThreshold = 0.5f;
    public float nonRedThreshold = 0.3f;

    private Texture2D captureTexture;
    private Texture2D maskTexture;
    private bool useSentis = false;
    private float lastInferenceTime = 0f;

    private Model runtimeModel;
    private Worker worker;

    void Start()
    {
        enabled = false;
        return;
        captureTexture = new Texture2D(inputWidth, inputHeight, TextureFormat.RGB24, false);
        maskTexture = new Texture2D(inputWidth, inputHeight, TextureFormat.RGBA32, false);

        InitializeModel();

        Debug.Log("[Segmentation] 초기화 완료. 모드: " + (useSentis ? "Sentis AI 모델" : "Fallback 색상 감지"));
    }

    void Update()
    {
        if (Time.time - lastInferenceTime >= inferenceInterval)
        {
            lastInferenceTime = Time.time;
            StartCoroutine(RunInference());
        }
    }

    private void InitializeModel()
    {
        if (modelAsset == null)
        {
            useSentis = false;
            Debug.Log("[Segmentation] 모델 미지정 → Fallback 색상 감지 모드 사용");
            return;
        }

        try
        {
            runtimeModel = ModelLoader.Load(modelAsset);
            worker = new Worker(runtimeModel, BackendType.GPUCompute);
            useSentis = true;
            Debug.Log("[Segmentation] Sentis 모델 로드 성공!");
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[Segmentation] Sentis 로드 실패: " + e.Message + " → Fallback 모드");
            useSentis = false;
        }
    }

    private IEnumerator RunInference()
    {
        if (inspectionCamera == null || cameraFeedRT == null)
            yield break;

        CaptureFrame();

        if (useSentis)
        {
            try
            {
                RunSentisInference();
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[Segmentation] 추론 에러: " + e.Message);
            }
        }
        else
        {
            RunFallbackColorDetection();
        }

        // if (DashboardUI.Instance != null)
            // DashboardUI.Instance.UpdateSegmentationOverlay(maskTexture);

        yield return null;
    }

    private void CaptureFrame()
    {
        RenderTexture currentRT = RenderTexture.active;

        RenderTexture resizedRT = RenderTexture.GetTemporary(inputWidth, inputHeight, 0);
        Graphics.Blit(cameraFeedRT, resizedRT);
        RenderTexture.active = resizedRT;
        captureTexture.ReadPixels(new Rect(0, 0, inputWidth, inputHeight), 0, 0);
        captureTexture.Apply();

        RenderTexture.active = currentRT;
        RenderTexture.ReleaseTemporary(resizedRT);
    }

    /// <summary>
    /// Sentis AI 모델 추론
    /// </summary>
    private void RunSentisInference()
    {
        // 텍스처 → 텐서 변환 (NHWC 레이아웃으로 변환)
        var inputTensor = new Tensor<float>(new TensorShape(1, inputHeight, inputWidth, 3));
        var pixels = captureTexture.GetPixels();
        for (int y = 0; y < inputHeight; y++)
        {
            for (int x = 0; x < inputWidth; x++)
            {
                Color p = pixels[y * inputWidth + x];
                inputTensor[0, y, x, 0] = p.r;
                inputTensor[0, y, x, 1] = p.g;
                inputTensor[0, y, x, 2] = p.b;
            }
        }

        // 추론 실행
        worker.Schedule(inputTensor);

        // 출력 텐서 가져오기
        var outputTensor = worker.PeekOutput() as Tensor<float>;
        var outputData = outputTensor.ReadbackAndClone();

        // 세그멘테이션 마스크 생성
        Color[] maskPixels = new Color[inputWidth * inputHeight];

        // 모델 출력 형태 확인 후 처리
        // DeepLabV3: 출력이 [1, num_classes, H, W] 또는 [1, H, W, num_classes] 일 수 있음
        int dim0 = outputData.shape[0];
        int dim1 = outputData.shape[1];
        int dim2 = outputData.shape[2];
        int dim3 = outputData.shape[3];

        // 출력 형태 판별: dim1이 작으면 NCHW, dim3이 작으면 NHWC
        bool outputIsNCHW = (dim1 < dim2 && dim1 < dim3);

        int numClasses, outH, outW;
        if (outputIsNCHW)
        {
            numClasses = dim1;
            outH = dim2;
            outW = dim3;
        }
        else
        {
            outH = dim1;
            outW = dim2;
            numClasses = dim3;
        }

        for (int y = 0; y < Mathf.Min(outH, inputHeight); y++)
        {
            for (int x = 0; x < Mathf.Min(outW, inputWidth); x++)
            {
                // argmax: 가장 높은 확률의 클래스 찾기
                int bestClass = 0;
                float bestScore = float.MinValue;
                for (int c = 0; c < numClasses; c++)
                {
                    float score;
                    if (outputIsNCHW)
                        score = outputData[0, c, y, x];
                    else
                        score = outputData[0, y, x, c];

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestClass = c;
                    }
                }

                int idx = y * inputWidth + x;
                if (idx < maskPixels.Length)
                {
                    if (bestClass == 0)
                    {
                        maskPixels[idx] = Color.clear;
                    }
                    else
                    {
                        // 물체 감지됨 → 색상으로 양품/불량 판별
                        Color pixelColor = captureTexture.GetPixel(x, y);
                        bool isAbnormal = (pixelColor.r > redThreshold &&
                                           pixelColor.g < nonRedThreshold &&
                                           pixelColor.b < nonRedThreshold);
                        maskPixels[idx] = isAbnormal ? abnormalColor : normalColor;
                    }
                }
            }
        }

        maskTexture.SetPixels(maskPixels);
        maskTexture.Apply();

        outputData.Dispose();
    }

    /// <summary>
    /// Fallback: 색상 기반 부품 감지
    /// </summary>
    private void RunFallbackColorDetection()
    {
        Color[] pixels = captureTexture.GetPixels();
        Color[] maskPixels = new Color[pixels.Length];

        for (int i = 0; i < pixels.Length; i++)
        {
            Color p = pixels[i];

            if (p.r > redThreshold && p.g < nonRedThreshold && p.b < nonRedThreshold)
                maskPixels[i] = abnormalColor;
            else if (p.b > 0.4f && p.r < 0.3f && p.g < 0.3f)
                maskPixels[i] = normalColor;
            else
                maskPixels[i] = Color.clear;
        }

        maskTexture.SetPixels(maskPixels);
        maskTexture.Apply();
    }

    void OnDestroy()
    {
        if (worker != null)
        {
            worker.Dispose();
            worker = null;
        }
        if (captureTexture != null) Destroy(captureTexture);
        if (maskTexture != null) Destroy(maskTexture);
    }
}