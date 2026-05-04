using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// AGV 중앙 교통 관제 시스템
/// Time-Space Reservation (시간-공간 예약) 방식
/// 
/// 실제 로보틱스 참고:
/// - CBS (Conflict-Based Search): 경로 충돌 감지 후 제약 추가
/// - MAPF (Multi-Agent Path Finding): 전체 경로를 중앙에서 계산
/// - 본 구현: 실용적 단순화 → 셀 예약으로 동시 점유 차단
/// 
/// 동작 원리:
/// 1. 맵을 CELL_SIZE × CELL_SIZE 격자로 분할
/// 2. AGV가 셀 진입 전 예약 요청
/// 3. 이미 예약된 셀 → 예약 만료까지 대기
/// 4. 예약 만료 후 진입 → 충돌 구조적 차단
/// </summary>
public class AGVTrafficManager : MonoBehaviour
{
    public static AGVTrafficManager Instance;

    [Header("격자 설정")]
    public float cellSize = 3f;         // 셀 크기 (m)
    public bool showDebugGrid = true;   // Scene 뷰 디버그

    // 예약 정보
    private class Reservation
    {
        public string agvID;
        public float  expireTime;  // 예약 만료 시각
        public Reservation(string id, float expire) { agvID = id; expireTime = expire; }
    }

    // 셀 키 → 예약 정보
    private Dictionary<Vector2Int, Reservation> reservations
        = new Dictionary<Vector2Int, Reservation>();

    // 디버그용 최근 예약 이력
    private List<string> log = new List<string>();

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    void Update()
    {
        // 만료된 예약 자동 해제
        float now = Time.time;
        var toRemove = new List<Vector2Int>();
        foreach (var kv in reservations)
            if (kv.Value.expireTime < now) toRemove.Add(kv.Key);
        foreach (var key in toRemove) reservations.Remove(key);
    }

    // ─── 공개 API ────────────────────────────────────────

    /// <summary>
    /// 지정 위치 셀을 예약하고 진입 허가를 기다림 (Coroutine)
    /// duration: 이 셀에 머무는 예상 시간 (초)
    /// </summary>
    public IEnumerator ReserveAndWait(string agvID, Vector3 worldPos, float duration = 3f)
    {
        Vector2Int cell = WorldToCell(worldPos);
        float waitTimer = 0f;

        // 다른 AGV의 예약이 만료될 때까지 대기
        while (IsReservedByOther(cell, agvID))
        {
            waitTimer += 0.2f;
            if (waitTimer > 15f)
            {
                Debug.LogWarning($"[Traffic] {agvID} 교착 감지 → 강제 진입");
                break;
            }
            yield return new WaitForSeconds(0.2f);
        }

        // 예약
        reservations[cell] = new Reservation(agvID, Time.time + duration);
        AddLog($"[{agvID}] 셀 {cell} 예약 ({duration:F1}s)");
    }

    /// <summary>
    /// 경로 상의 모든 셀을 순차 예약 (이동 전 호출)
    /// </summary>
    public IEnumerator ReservePath(string agvID, Vector3 from, Vector3 to, float speedMps)
    {
        var cells = GetPathCells(from, to);
        float dist = Vector3.Distance(from, to);
        float travelTime = dist / Mathf.Max(speedMps, 0.1f);
        float perCellTime = travelTime / Mathf.Max(cells.Count, 1);

        for (int i = 0; i < cells.Count; i++)
        {
            Vector2Int cell = cells[i];
            float waitTimer = 0f;

            while (IsReservedByOther(cell, agvID))
            {
                waitTimer += 0.2f;
                if (waitTimer > 15f) { Debug.LogWarning($"[Traffic] {agvID} 강제진입 {cell}"); break; }
                yield return new WaitForSeconds(0.2f);
            }

            // 이 셀 예약 (남은 이동시간 + 여유)
            float remainTime = (cells.Count - i) * perCellTime + 1f;
            reservations[cell] = new Reservation(agvID, Time.time + remainTime);
        }
    }

    /// <summary>
    /// AGV의 모든 예약 해제
    /// </summary>
    public void ReleaseAll(string agvID)
    {
        var toRemove = new List<Vector2Int>();
        foreach (var kv in reservations)
            if (kv.Value.agvID == agvID) toRemove.Add(kv.Key);
        foreach (var key in toRemove) reservations.Remove(key);
        AddLog($"[{agvID}] 모든 예약 해제");
    }

    /// <summary>특정 위치가 다른 AGV에 예약됐는지 확인</summary>
    public bool IsOccupied(string agvID, Vector3 worldPos)
        => IsReservedByOther(WorldToCell(worldPos), agvID);

    // ─── 내부 유틸 ────────────────────────────────────────

    bool IsReservedByOther(Vector2Int cell, string agvID)
    {
        if (!reservations.TryGetValue(cell, out Reservation r)) return false;
        if (r.expireTime < Time.time) return false; // 만료
        return r.agvID != agvID; // 다른 AGV의 예약
    }

    Vector2Int WorldToCell(Vector3 worldPos)
        => new Vector2Int(
            Mathf.FloorToInt(worldPos.x / cellSize),
            Mathf.FloorToInt(worldPos.z / cellSize));

    /// <summary>두 점 사이의 셀 목록 (Bresenham line)</summary>
    List<Vector2Int> GetPathCells(Vector3 from, Vector3 to)
    {
        var cells = new List<Vector2Int>();
        Vector2Int a = WorldToCell(from);
        Vector2Int b = WorldToCell(to);

        int dx = Mathf.Abs(b.x - a.x), sx = a.x < b.x ? 1 : -1;
        int dz = Mathf.Abs(b.y - a.y), sz = a.y < b.y ? 1 : -1;
        int err = dx - dz;

        Vector2Int cur = a;
        while (true)
        {
            if (!cells.Contains(cur)) cells.Add(cur);
            if (cur == b) break;
            int e2 = 2 * err;
            if (e2 > -dz) { err -= dz; cur.x += sx; }
            if (e2 <  dx) { err += dx; cur.y += sz; }
        }
        return cells;
    }

    void AddLog(string msg)
    {
        log.Add($"[{Time.time:F1}s] {msg}");
        if (log.Count > 30) log.RemoveAt(0);
    }

    public List<string> GetLog() => log;

    // ─── 디버그 시각화 ────────────────────────────────────

    void OnDrawGizmos()
    {
        if (!showDebugGrid || !Application.isPlaying) return;

        foreach (var kv in reservations)
        {
            if (kv.Value.expireTime < Time.time) continue;

            Vector3 center = new Vector3(
                (kv.Key.x + 0.5f) * cellSize,
                0.1f,
                (kv.Key.y + 0.5f) * cellSize);

            // 예약된 셀을 색상으로 표시
            Gizmos.color = GetAGVColor(kv.Value.agvID);
            Gizmos.DrawWireCube(center, new Vector3(cellSize * 0.9f, 0.1f, cellSize * 0.9f));
        }
    }

    Color GetAGVColor(string agvID)
    {
        return agvID switch
        {
            "AGV_01" => new Color(1f, 0.9f, 0f, 0.8f),
            "AGV_02" => new Color(0f, 0.6f, 1f, 0.8f),
            "AGV_03" => new Color(1f, 0.4f, 0f, 0.8f),
            "AGV_04" => new Color(0.3f, 1f, 0.2f, 0.8f),
            "AGV_05" => new Color(0.8f, 0.2f, 0.9f, 0.8f),
            _ => Color.white
        };
    }
}