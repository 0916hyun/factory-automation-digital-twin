using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 컨베이어 시스템
/// - 부품을 Z+ 방향으로 이송
/// - 비전 스테이션 통과 후 소터로 전달
/// </summary>
public class ConveyorSystem : MonoBehaviour
{
    [Header("컨베이어 설정")]
    public float beltSpeed = 2.0f;
    public int conveyorIndex = 0;      // 0,1,2

    [Header("연결")]
    public VisionStation visionStation;
    public SortingGate sortingGate;

    [Header("스폰 설정")]
    public Transform spawnPoint;
    public float spawnInterval = 5.0f;
    public float abnormalRate = 0.3f;

    [Header("프리팹")]
    public GameObject hexNutPrefab;
    public GameObject screwPrefab;
    public GameObject transistorPrefab;

    private List<Rigidbody> partsOnBelt = new List<Rigidbody>();
    private int partCounter = 0;

    // 텍스처 애니메이션용
    private Material beltMaterial;
    private float textureOffset = 0f;

    void Start()
    {
        // 벨트 머티리얼 캐시
        MeshRenderer mr = GetComponentInChildren<MeshRenderer>();
        if (mr != null) beltMaterial = mr.material;

        StartCoroutine(SpawnLoop());
    }

    void FixedUpdate()
    {
        // 벨트 위 부품 이송
        partsOnBelt.RemoveAll(rb => rb == null || rb.isKinematic);
        foreach (Rigidbody rb in partsOnBelt)
        {
            if (rb == null) continue;
            rb.linearVelocity = new Vector3(
                rb.linearVelocity.x * 0.5f,
                rb.linearVelocity.y,
                Mathf.Lerp(rb.linearVelocity.z, beltSpeed, 0.4f));
        }

        // 벨트 텍스처 스크롤
        if (beltMaterial != null)
        {
            textureOffset += beltSpeed * Time.fixedDeltaTime * 0.1f;
            beltMaterial.SetTextureOffset("_BaseMap", new Vector2(0, textureOffset));
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("TargetObject")) return;
        Rigidbody rb = other.attachedRigidbody;
        if (rb != null && !partsOnBelt.Contains(rb))
            partsOnBelt.Add(rb);
    }

    void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("TargetObject")) return;
        Rigidbody rb = other.attachedRigidbody;
        if (rb != null) partsOnBelt.Remove(rb);
    }

    IEnumerator SpawnLoop()
    {
        yield return new WaitForSeconds(1f + conveyorIndex * 0.8f);

        while (true)
        {
            SpawnPart();
            yield return new WaitForSeconds(spawnInterval);
        }
    }

    void SpawnPart()
    {
        if (spawnPoint == null) return;

        partCounter++;
        bool isDefective = Random.value < abnormalRate;
        PartType type = (PartType)Random.Range(0, 3);

        GameObject prefab = GetPrefab(type);
        if (prefab == null) return;

        GameObject part = Instantiate(prefab,
            spawnPoint.position, GetSpawnRot(type));
        part.name = $"Part_{(isDefective ? "DEF" : "OK")}_{type}_{partCounter:000}";
        part.tag = "TargetObject";

        // PartData
        PartData pd = part.AddComponent<PartData>();
        pd.partType = type;
        pd.isDefective = isDefective;

        // Rigidbody
        Rigidbody rb = part.GetComponent<Rigidbody>();
        if (rb == null) rb = part.AddComponent<Rigidbody>();
        rb.mass = 0.3f;
        rb.constraints = RigidbodyConstraints.FreezeRotation;
        rb.linearDamping = 1.5f;

        // Collider 보장
        if (part.GetComponentInChildren<Collider>() == null)
            part.AddComponent<BoxCollider>();

        // 불량품 빨간 틴트
        if (isDefective) ApplyDefectTint(part);

        // 레이어
        int layer = LayerMask.NameToLayer("TargetObject");
        if (layer != -1) SetLayerAll(part, layer);

        Debug.Log($"[Conveyor{conveyorIndex}] 생성: {part.name}");
    }

    Quaternion GetSpawnRot(PartType type)
    {
        switch(type)
        {
            case PartType.Screw: return Quaternion.Euler(90, 0, 0);
            case PartType.HexNut: return Quaternion.Euler(0, Random.Range(0,6)*60f, 0);
            default: return Quaternion.identity;
        }
    }

    GameObject GetPrefab(PartType type)
    {
        switch(type)
        {
            case PartType.HexNut: return hexNutPrefab;
            case PartType.Screw: return screwPrefab;
            case PartType.Transistor: return transistorPrefab;
            default: return null;
        }
    }

    void ApplyDefectTint(GameObject obj)
    {
        foreach (var mr in obj.GetComponentsInChildren<MeshRenderer>())
        {
            Material mat = new Material(mr.sharedMaterial ??
                new Material(Shader.Find("Universal Render Pipeline/Lit")));
            Color c = mat.HasProperty("_BaseColor") ?
                mat.GetColor("_BaseColor") : mat.color;
            mat.SetColor("_BaseColor", c * new Color(1f, 0.3f, 0.3f));
            mr.material = mat;
        }
    }

    void SetLayerAll(GameObject obj, int layer)
    {
        obj.layer = layer;
        foreach (Transform t in obj.transform) SetLayerAll(t.gameObject, layer);
    }
}
