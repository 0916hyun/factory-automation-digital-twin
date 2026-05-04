using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 감지 결과 3D 오버레이 효과 (URP 호환)
/// </summary>
public class DetectionOverlay : MonoBehaviour
{
    [Header("하이라이트 설정")]
    public float highlightDuration = 2.0f;
    public float pulseSpeed = 3.0f;
    public float ringScale = 2.0f;

    private MeshRenderer meshRenderer;
    private Color originalColor;
    private bool isHighlighting = false;
    private GameObject highlightRing;

    void Start()
    {
        meshRenderer = GetComponent<MeshRenderer>();
        if (meshRenderer != null)
        {
            if (meshRenderer.material.HasProperty("_BaseColor"))
                originalColor = meshRenderer.material.GetColor("_BaseColor");
            else
                originalColor = meshRenderer.material.color;
        }
    }

    public void ShowDetectionResult(bool isNormal)
    {
        if (!isHighlighting)
            StartCoroutine(HighlightRoutine(isNormal));
    }

    private IEnumerator HighlightRoutine(bool isNormal)
    {
        isHighlighting = true;
        CreateHighlightRing(isNormal);

        Color highlightColor = isNormal
            ? new Color(0f, 1f, 0f, 1f)
            : new Color(1f, 0f, 0f, 1f);

        float elapsed = 0f;
        while (elapsed < highlightDuration)
        {
            if (meshRenderer != null)
            {
                float t = Mathf.PingPong(Time.time * pulseSpeed, 1f);
                Color blended = Color.Lerp(originalColor, highlightColor, t);

                if (meshRenderer.material.HasProperty("_BaseColor"))
                    meshRenderer.material.SetColor("_BaseColor", blended);
                else
                    meshRenderer.material.color = blended;

                meshRenderer.material.EnableKeyword("_EMISSION");
                meshRenderer.material.SetColor("_EmissionColor", highlightColor * t * 0.5f);
            }
            elapsed += Time.deltaTime;
            yield return null;
        }

        if (meshRenderer != null)
        {
            if (meshRenderer.material.HasProperty("_BaseColor"))
                meshRenderer.material.SetColor("_BaseColor", originalColor);
            else
                meshRenderer.material.color = originalColor;

            meshRenderer.material.SetColor("_EmissionColor", Color.black);
        }

        if (highlightRing != null)
            Destroy(highlightRing);  // ← Destroy 사용 (DestroyImmediate 아님!)

        isHighlighting = false;
    }

    private void CreateHighlightRing(bool isNormal)
    {
        if (highlightRing != null)
            Destroy(highlightRing);  // ← Destroy 사용

        highlightRing = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        highlightRing.name = "HighlightRing";
        highlightRing.transform.SetParent(transform);
        highlightRing.transform.localPosition = Vector3.zero;
        highlightRing.transform.localScale = new Vector3(ringScale, 0.01f, ringScale);

        // Collider 제거 - 코루틴 내에서 안전하게 제거
        Collider col = highlightRing.GetComponent<Collider>();
        if (col != null)
            Destroy(col);  // ← Destroy 사용

        MeshRenderer ringRenderer = highlightRing.GetComponent<MeshRenderer>();
        if (ringRenderer != null)
        {
            Color ringColor = isNormal
                ? new Color(0f, 1f, 0f, 0.3f)
                : new Color(1f, 0f, 0f, 0.3f);

            Shader urpShader = Shader.Find("Universal Render Pipeline/Lit");
            if (urpShader == null) urpShader = Shader.Find("Universal Render Pipeline/Simple Lit");
            if (urpShader == null) urpShader = Shader.Find("Standard");

            Material mat = new Material(urpShader);
            mat.SetColor("_BaseColor", ringColor);
            mat.SetColor("_Color", ringColor);

            mat.SetFloat("_Surface", 1);
            mat.SetFloat("_Blend", 0);
            mat.SetFloat("_AlphaClip", 0);
            mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0);
            mat.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
            mat.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.renderQueue = 3000;
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.EnableKeyword("_ALPHABLEND_ON");

            ringRenderer.material = mat;
        }
    }

    void OnDestroy()
    {
        if (highlightRing != null)
            Destroy(highlightRing);
    }
}
