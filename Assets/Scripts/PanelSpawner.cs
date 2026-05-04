using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PanelSpawner : MonoBehaviour
{
    [Header("패널 프리팹")]
    public GameObject panelPrefab_Small;
    public GameObject panelPrefab_Large;

    [Header("스폰 설정")]
    public Transform spawnPoint;
    public int   conveyorIndex = 0;
    public float spawnInterval = 12.0f;

    [Header("결함 확률")]
    [Range(0f,1f)] public float defectRate    = 0.4f;
    [Range(0f,1f)] public float crazingRate   = 0.20f;
    [Range(0f,1f)] public float inclusionRate = 0.15f;
    [Range(0f,1f)] public float patchesRate   = 0.15f;
    [Range(0f,1f)] public float pittedRate    = 0.20f;
    [Range(0f,1f)] public float rolledRate    = 0.15f;
    [Range(0f,1f)] public float scratchesRate = 0.15f;

    const float BELT_TOP_Y    = 0.60f;
    const float COL_W         = 1.0f;
    const float COL_H         = 0.06f;
    const float COL_D         = 0.8f;
    const float AUTO_DESTROY_Z = 70f;
    const int   MAX_ACTIVE    = 2;

    private int panelCounter = 0;
    private List<GameObject> activePanels = new List<GameObject>();

    void Start() => StartCoroutine(SpawnLoop());

    IEnumerator SpawnLoop()
    {
        yield return new WaitForSeconds(2f + conveyorIndex * 2f);
        while (true)
        {
            CleanupInactive();
            if (!IsSpawnPointOccupied() && activePanels.Count < MAX_ACTIVE)
                SpawnPanel();
            yield return new WaitForSeconds(spawnInterval);
        }
    }

    bool IsSpawnPointOccupied()
    {
        if (spawnPoint == null) return false;
        Collider[] hits = Physics.OverlapSphere(spawnPoint.position, 1.5f);
        foreach (var hit in hits)
        {
            if (hit.CompareTag("TargetObject")) return true;
            Rigidbody rb = hit.attachedRigidbody;
            if (rb != null && rb.CompareTag("TargetObject")) return true;
        }
        return false;
    }

    void SpawnPanel()
    {
        if (spawnPoint == null) return;
        panelCounter++;
        NEUDefectType defectType = DetermineDefectType();

        GameObject prefab = Random.value > 0.5f ? panelPrefab_Large : panelPrefab_Small;
        if (prefab == null)
        {
            Debug.LogWarning($"[Spawner {conveyorIndex}] 프리팹 미연결!");
            return;
        }

        Vector3 spawnPos = spawnPoint.position;
        spawnPos.y = BELT_TOP_Y + (COL_H * 0.5f) + 0.01f;

        GameObject panel = Instantiate(prefab, spawnPos, GetVisualRotation(prefab.name));
        panel.name = $"Panel_{defectType}_{panelCounter:000}";
        panel.tag  = "TargetObject";

        // SteelPanel + 텍스처
        SteelPanel sp = panel.AddComponent<SteelPanel>();
        sp.defectType = defectType;
        sp.panelID    = panelCounter;
        sp.modelType  = prefab.name.ToLower().Contains("sheet")
            ? PanelModelType.Sheet : PanelModelType.Plate;

        if (NEUTextureManager.Instance != null)
        {
            NEUTextureManager.Instance.ApplyTexture(panel, defectType);

            // ★ 추론용 텍스처 저장: 렌더러에서 적용된 텍스처를 가져와 defectTexture에 보관
            var mr = panel.GetComponentInChildren<MeshRenderer>();
            if (defectType != NEUDefectType.Normal)
                if (mr != null && mr.material.mainTexture is Texture2D tex)
                    sp.defectTexture = tex;
        }

        // 기존 콜라이더 제거
        foreach (var col in panel.GetComponentsInChildren<Collider>())
        { col.enabled = false; Destroy(col); }

        // 스케일 보정 납작 콜라이더
        GameObject colGO = new GameObject("_FlatCollider");
        colGO.transform.SetParent(panel.transform, false);
        colGO.transform.localPosition = Vector3.zero;
        colGO.transform.rotation      = Quaternion.identity;
        Vector3 ps = panel.transform.lossyScale;
        colGO.transform.localScale = new Vector3(
            1f / Mathf.Max(Mathf.Abs(ps.x), 0.001f),
            1f / Mathf.Max(Mathf.Abs(ps.y), 0.001f),
            1f / Mathf.Max(Mathf.Abs(ps.z), 0.001f));
        colGO.tag = "TargetObject";

        BoxCollider bc = colGO.AddComponent<BoxCollider>();
        bc.size     = new Vector3(COL_W, COL_H, COL_D);
        bc.material = new PhysicsMaterial("PanelSlide")
        {
            dynamicFriction = 0f, staticFriction  = 0f, bounciness      = 0f,
            frictionCombine = PhysicsMaterialCombine.Minimum,
            bounceCombine   = PhysicsMaterialCombine.Minimum
        };

        int layer = LayerMask.NameToLayer("TargetObject");
        if (layer != -1) { colGO.layer = layer; SetLayerAll(panel, layer); }

        Rigidbody rb2 = panel.GetComponent<Rigidbody>();
        if (rb2 == null) rb2 = panel.AddComponent<Rigidbody>();

        rb2.mass           = 5f;
        rb2.linearDamping  = 2f;
        rb2.angularDamping = 10f;
        rb2.constraints    = RigidbodyConstraints.FreezeRotation
                           | RigidbodyConstraints.FreezePositionX;
        rb2.isKinematic    = true;
        StartCoroutine(ReleaseAfterDelay(rb2, 0.15f));

        Debug.Log($"[Spawner {conveyorIndex}] {panel.name} | scale={ps}");
        activePanels.Add(panel);
    }

    IEnumerator ReleaseAfterDelay(Rigidbody rb, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (rb == null) yield break;
        rb.isKinematic     = false;
        rb.linearVelocity  = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
    }

    Quaternion GetVisualRotation(string name)
    {
        if (name.ToLower().Contains("sheet")) return Quaternion.Euler(90f, 0f, 0f);
        return Quaternion.Euler(0f, 0f, 0f);
    }

    NEUDefectType DetermineDefectType()
    {
        if (Random.value > defectRate) return NEUDefectType.Normal;
        float r = Random.value, c = 0f;
        c += crazingRate;   if (r < c) return NEUDefectType.Crazing;
        c += inclusionRate; if (r < c) return NEUDefectType.Inclusion;
        c += patchesRate;   if (r < c) return NEUDefectType.Patches;
        c += pittedRate;    if (r < c) return NEUDefectType.PittedSurface;
        c += rolledRate;    if (r < c) return NEUDefectType.RolledInScale;
        return NEUDefectType.Scratches;
    }

    void SetLayerAll(GameObject obj, int layer)
    { obj.layer = layer; foreach (Transform t in obj.transform) SetLayerAll(t.gameObject, layer); }

    void CleanupInactive()
    {
        activePanels.RemoveAll(p => {
            if (p == null || !p.activeInHierarchy) return true;
            SteelPanel sp = p.GetComponent<SteelPanel>();
            if (sp != null && sp.status != SteelPanel.PanelStatus.OnConveyor
                           && sp.status != SteelPanel.PanelStatus.Inspecting) return true;
            if (p.transform.position.z > AUTO_DESTROY_Z) { Destroy(p); return true; }
            return false;
        });
    }
}