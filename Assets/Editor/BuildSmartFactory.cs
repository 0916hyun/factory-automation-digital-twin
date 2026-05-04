using UnityEngine;
using UnityEditor;
using UnityEngine.UI;

/// <summary>
/// CPS Tools → BUILD SMART FACTORY (로템 맞춤) v5
///
/// ★ v5 변경사항:
/// 1. ReworkRobot / ScrapRobot Z 위치 통일 (Z=42)
///    기존: ReworkRobot Z=46, ScrapRobot Z=40 → 불일치
///    수정: 둘 다 Z=42 (AGV subLaneZ 기준과 정렬)
/// 2. 불량품 처리 구역 데칼/펜스 벽 침범 수정
///    기존: size_x=10 → X=17~27 (벽 X=±26 밖으로 1m 삐짐)
///    수정: X=±21 기준 size_x=7 → X=17.5~24.5 (벽 안)
/// 3. ReworkRobot X=22 → X=21 / ScrapRobot X=-22 → X=-21 (벽 여유 확보)
/// 4. 구역 색상 명확화 및 레이블 위치 정렬
/// 5. 펜스 길이/위치 Z 통일 반영
/// </summary>
public class BuildSmartFactory
{
    const float W = 50f, D = 70f, H = 10f;

    const float Z_INBOUND  = 5f;
    const float Z_CONVEYOR = 15f;
    const float Z_VISION   = 28f;
    const float Z_SORT     = 35f;
    const float Z_AGV_LANE = 45f;
    const float Z_FL_LANE  = 60f;
    const float Z_STORAGE  = 55f;
    const float Z_SHIPPING = 65f;

    // ★ 불량품 처리 구역 좌표 (상수화 → PanelSortingGate와 동기화)
    const float REWORK_X      = 21f;   // 재작업 로봇 X (우측)
    const float SCRAP_X       = -21f;  // 스크랩 로봇 X (좌측)
    const float STATION_Z     = 42f;   // 두 스테이션 공통 Z ★ 통일
    const float REWORK_PROC_Z = 47f;   // 재작업 처리 포인트 Z (테이블)
    const float SCRAP_PROC_Z  = 37f;   // 스크랩 처리 포인트 Z (스크랩함)
    const float ZONE_MIN_Z    = 35.5f; // 불량품 구역 앞쪽 경계 Z
    const float ZONE_MAX_Z    = 49.5f; // 불량품 구역 뒤쪽 경계 Z

    static readonly float[] CONV_X = { -12f, 0f, 12f };

    static Color C(float r, float g, float b) => new Color(r, g, b);
    static readonly Color COL_FLOOR    = C(0.16f, 0.17f, 0.18f);
    static readonly Color COL_WALL     = C(0.72f, 0.73f, 0.75f);
    static readonly Color COL_CEILING  = C(0.80f, 0.81f, 0.83f);
    static readonly Color COL_STEEL    = C(0.55f, 0.58f, 0.62f);
    static readonly Color COL_BELT     = C(0.10f, 0.10f, 0.12f);
    static readonly Color COL_VISION   = C(0.15f, 0.15f, 0.20f);
    static readonly Color COL_PLATE    = C(0.15f, 0.35f, 0.80f);
    static readonly Color COL_SHEET    = C(0.50f, 0.10f, 0.80f);
    static readonly Color COL_DEFECT   = C(0.80f, 0.10f, 0.10f);
    static readonly Color COL_SHIPPING = C(0.10f, 0.65f, 0.25f);
    static readonly Color COL_SAFETY   = C(1.00f, 0.85f, 0.00f);
    static readonly Color COL_REWORK   = C(1.00f, 0.50f, 0.10f);
    static readonly Color COL_FORKLIFT = C(0.95f, 0.75f, 0.05f);
    static readonly Color COL_ROBOT    = C(0.20f, 0.22f, 0.26f);

    [MenuItem("CPS Tools/BUILD SMART FACTORY (로템 맞춤)")]
    static void Build()
    {
        if (!EditorUtility.DisplayDialog("스마트 팩토리 재건",
            "씬을 완전히 초기화합니다.\n계속하시겠습니까?",
            "재건 시작", "취소")) return;

        ClearScene();
        SetupCamera();
        BuildEnvironment();
        BuildFloorMarkings();

        var conveyors      = BuildConveyors();
        var visionStations = BuildVisionStations();
        var sortGate       = BuildSortingGate();

        BuildAutoStorage();
        BuildShippingZone();
        BuildDefectProcessingZone();

        var agvs      = BuildAGVFleet();
        var forkLifts = BuildForkLiftFleet();

        BuildLighting();
        BuildManagers(agvs, forkLifts, visionStations, sortGate);
        SetupPanelSpawners();
        BuildDashboardUI();

        Debug.Log("[SmartFactory] ★ 재건 완료 v5!");
        EditorUtility.DisplayDialog("완료",
            "재건 완료! v5\n\n" +
            "구성:\n" +
            "- 컨베이어 3 (C1·C3=Plate / C2=Sheet)\n" +
            "- 랙 2 (1열×5행 / Plate X=-15 / Sheet X=+15)\n" +
            "- 도크 2 (Plate / Sheet)\n" +
            "- 소형 AGV 5대 / 지게차 2대(전담)\n" +
            "- ReworkRobot (X=+21, Z=42) / ScrapRobot (X=-21, Z=42) ★Z통일\n\n" +
            "플레이!", "확인");
    }

    // ─── 씬 초기화 ───────────────────────────────────────────
    static void ClearScene()
    {
        var all = Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None);
        var del = new System.Collections.Generic.List<GameObject>();
        foreach (var go in all)
        {
            if (go == null) continue;
            try
            {
                string n = go.name;
                if (n == "Main Camera" || n == "Directional Light" || n == "NEUTextureManager") continue;
                if (go.transform.parent == null) del.Add(go);
            }
            catch { }
        }
        foreach (var go in del) if (go != null) Object.DestroyImmediate(go);
    }

    static void SetupCamera()
    {
        Camera cam = Camera.main;
        if (cam == null) return;
        cam.transform.position = new Vector3(0, 28f, -10f);
        cam.transform.rotation = Quaternion.Euler(48f, 0, 0);
        cam.fieldOfView = 60f; cam.farClipPlane = 300f;
        cam.backgroundColor = new Color(0.07f, 0.07f, 0.09f);
        if (cam.GetComponent<FreeCameraController>() == null)
            cam.gameObject.AddComponent<FreeCameraController>();
    }

    // ─── 환경 ────────────────────────────────────────────────
    static void BuildEnvironment()
    {
        var env = new GameObject("Environment");
        CreateBox("Floor",      env, new Vector3(0,-0.1f,D/2f),       new Vector3(W+4f,0.2f,D+4f),  Mat(COL_FLOOR));
        CreateBox("Wall_Back",  env, new Vector3(0,H/2f,D+1f),        new Vector3(W+4f,H,0.4f),      Mat(COL_WALL));
        CreateBox("Wall_Front", env, new Vector3(0,H/2f,-1f),         new Vector3(W+4f,H,0.4f),      Mat(COL_WALL));
        CreateBox("Wall_Left",  env, new Vector3(-W/2f-1f,H/2f,D/2f), new Vector3(0.4f,H,D+4f),      Mat(COL_WALL));
        CreateBox("Wall_Right", env, new Vector3( W/2f+1f,H/2f,D/2f), new Vector3(0.4f,H,D+4f),      Mat(COL_WALL));
        CreateBox("Ceiling",    env, new Vector3(0,H,D/2f),            new Vector3(W+4f,0.3f,D+4f),  Mat(COL_CEILING));

        for (int z = 15; z <= (int)D; z += 15)
            CreateBox($"Truss_{z}", env, new Vector3(0,H-0.3f,z), new Vector3(W+2f,0.35f,0.35f), Mat(COL_STEEL,0.8f));

        foreach (float pz in new[]{ 5f, D-5f })
        {
            CreateBox($"Pillar_L_{pz}", env, new Vector3(-W/2f+1f,H/2f,pz), new Vector3(0.8f,H,0.8f), Mat(COL_STEEL,0.7f));
            CreateBox($"Pillar_R_{pz}", env, new Vector3( W/2f-1f,H/2f,pz), new Vector3(0.8f,H,0.8f), Mat(COL_STEEL,0.7f));
        }

        foreach (float dz in new[]{ Z_VISION-1f, Z_SORT-1f, Z_AGV_LANE-1f })
            CreateSemiWall(env, new Vector3(0,H/2f,dz), new Vector3(W,H,0.15f));
    }

    static void CreateSemiWall(GameObject p, Vector3 pos, Vector3 scale)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "Divider"; go.transform.SetParent(p.transform);
        go.transform.position = pos; go.transform.localScale = scale;
        var mat = Mat(new Color(0.6f,0.8f,1f,0.25f)); SetTransparent(mat);
        go.GetComponent<MeshRenderer>().material = mat;
        var col = go.GetComponent<BoxCollider>(); if (col) col.enabled = false;
    }

    // ─── 바닥 마킹 ───────────────────────────────────────────
    static void BuildFloorMarkings()
    {
        var marks = new GameObject("FloorMarkings");

        // AGV 레인 라인
        float[] laneX = { -18f,-9f,0f,9f,18f };
        foreach (float x in laneX)
        {
            CreateLine(marks,$"AGVLane_{x}",new Vector3(x,0.02f,Z_SORT),new Vector3(x,0.02f,Z_FL_LANE),COL_SAFETY,0.15f);
            for (float z = Z_SORT+5f; z < Z_FL_LANE; z += 8f)
                CreateArrow(marks, new Vector3(x,0.02f,z), 0f);
        }

        // 지게차 레인 라인
        CreateLine(marks,"FLLane_H",new Vector3(-W/2f,0.02f,Z_FL_LANE),new Vector3(W/2f,0.02f,Z_FL_LANE),COL_FORKLIFT,0.20f);
        foreach (float rx in new[]{-15f,15f})
            CreateLine(marks,$"FLLane_{rx}",new Vector3(rx,0.02f,Z_FL_LANE),new Vector3(rx,0.02f,Z_SHIPPING),COL_FORKLIFT,0.15f);

        // 구역 경계선
        foreach (float bz in new[]{ Z_INBOUND,Z_CONVEYOR,Z_VISION,Z_SORT,Z_AGV_LANE,Z_FL_LANE,Z_STORAGE,Z_SHIPPING })
            CreateLine(marks,$"Bnd_{bz}",new Vector3(-W/2f,0.02f,bz),new Vector3(W/2f,0.02f,bz),COL_SAFETY,0.10f);

        // ─── 구역 데칼 ─────────────────────────────────────────
        // 인바운드/컨베이어
        CreateZoneDecal(marks,"Zone_Conv",
            new Vector3(0,0.01f,(Z_CONVEYOR+Z_INBOUND)/2f),
            new Vector3(W,0.01f,Z_CONVEYOR-Z_INBOUND),
            new Color(0.10f,0.10f,0.12f,0.6f));

        // 비전 검사
        CreateZoneDecal(marks,"Zone_Vision",
            new Vector3(0,0.01f,(Z_VISION+Z_CONVEYOR)/2f),
            new Vector3(W,0.01f,Z_VISION-Z_CONVEYOR),
            new Color(0.05f,0.15f,0.35f,0.4f));

        // 분류 게이트
        CreateZoneDecal(marks,"Zone_Sort",
            new Vector3(0,0.01f,(Z_SORT+Z_VISION)/2f),
            new Vector3(W,0.01f,Z_SORT-Z_VISION),
            new Color(0.25f,0.15f,0.05f,0.4f));

        // AGV 레인 (중앙부만: 불량품 구역 X=±18~25는 별도 색상)
        //   X=-18~+18 범위만 표시하여 불량품 구역과 색상 충돌 방지
        CreateZoneDecal(marks,"Zone_AGV",
            new Vector3(0,0.01f,(Z_AGV_LANE+Z_SORT)/2f),
            new Vector3(36f,0.01f,Z_AGV_LANE-Z_SORT),
            new Color(0.05f,0.20f,0.05f,0.4f));

        // 지게차 레인
        CreateZoneDecal(marks,"Zone_FL",
            new Vector3(0,0.01f,(Z_FL_LANE+Z_AGV_LANE)/2f),
            new Vector3(W,0.01f,Z_FL_LANE-Z_AGV_LANE),
            new Color(0.80f,0.65f,0.02f,0.25f));

        // Plate 창고 (X=-15 기준)
        CreateZoneDecal(marks,"Zone_Plate",
            new Vector3(-15f,0.01f,(Z_STORAGE+Z_FL_LANE)/2f),
            new Vector3(16f,0.01f,Z_STORAGE-Z_FL_LANE),
            new Color(0.10f,0.20f,0.55f,0.5f));

        // Sheet 창고 (X=+15 기준)
        CreateZoneDecal(marks,"Zone_Sheet",
            new Vector3(15f,0.01f,(Z_STORAGE+Z_FL_LANE)/2f),
            new Vector3(16f,0.01f,Z_STORAGE-Z_FL_LANE),
            new Color(0.35f,0.05f,0.55f,0.5f));

        // 출하 구역
        CreateZoneDecal(marks,"Zone_Ship",
            new Vector3(0,0.01f,(Z_SHIPPING+Z_STORAGE)/2f),
            new Vector3(W,0.01f,Z_SHIPPING-Z_STORAGE),
            new Color(0.05f,0.40f,0.15f,0.5f));

        // ★ 불량품 구역 데칼 (v5: 벽 침범 수정)
        // X 범위: REWORK_X=21 ± 3.5 → 17.5~24.5 (벽 X=25.8 안)
        // Z 범위: ZONE_MIN_Z~ZONE_MAX_Z = 35.5~49.5
        float zoneW  = 7f;   // X 방향 폭
        float zoneD  = (ZONE_MAX_Z - ZONE_MIN_Z); // Z 방향 깊이 = 14f
        float zoneCZ = (ZONE_MAX_Z + ZONE_MIN_Z) / 2f; // Z 중심 = 42.5f

        CreateZoneDecal(marks,"Zone_Rework",
            new Vector3(REWORK_X, 0.02f, zoneCZ),
            new Vector3(zoneW, 0.02f, zoneD),
            new Color(1f,0.5f,0.1f,0.45f));

        CreateZoneDecal(marks,"Zone_Scrap",
            new Vector3(Mathf.Abs(SCRAP_X)*-1f, 0.02f, zoneCZ),
            new Vector3(zoneW, 0.02f, zoneD),
            new Color(0.8f,0.1f,0.1f,0.45f));

        // ─── 구역 레이블 ───────────────────────────────────────
        CreateZoneLabel(marks,"인바운드",   new Vector3(-W/2f+3f,0.5f,Z_INBOUND+2f));
        CreateZoneLabel(marks,"비전검사",   new Vector3(-W/2f+3f,0.5f,Z_VISION-3f));
        CreateZoneLabel(marks,"AGV레인",    new Vector3(-W/2f+3f,0.5f,Z_AGV_LANE-2f));
        CreateZoneLabel(marks,"지게차레인", new Vector3(-W/2f+3f,0.5f,Z_FL_LANE+1f));
        CreateZoneLabel(marks,"Plate창고",  new Vector3(-15f,0.5f,Z_STORAGE-3f));
        CreateZoneLabel(marks,"Sheet창고",  new Vector3( 15f,0.5f,Z_STORAGE-3f));
        CreateZoneLabel(marks,"출하구역",   new Vector3(-W/2f+3f,0.5f,Z_SHIPPING-2f));
        // ★ 레이블 Z 통일 (STATION_Z 기준)
        CreateZoneLabel(marks,"재작업존",   new Vector3(REWORK_X,  0.5f, STATION_Z));
        CreateZoneLabel(marks,"스크랩존",   new Vector3(SCRAP_X,   0.5f, STATION_Z));
    }

    static void CreateZoneLabel(GameObject p, string text, Vector3 pos)
        => CreateBox("Label_"+text, p, pos, new Vector3(5f,0.02f,2f), Mat(new Color(0.1f,0.1f,0.15f,0.8f)));

    // ─── 컨베이어 ────────────────────────────────────────────
    static GameObject[] BuildConveyors()
    {
        var parent = new GameObject("ConveyorSystem");
        var conveyors = new GameObject[3];

        for (int i = 0; i < 3; i++)
        {
            float x = CONV_X[i];
            var conv = new GameObject($"Conveyor_{i+1}");
            conv.transform.SetParent(parent.transform);

            float convZ = (Z_INBOUND+Z_VISION)/2f, convLen = Z_VISION-Z_INBOUND;

            var belt = CreateBox("Belt", conv, new Vector3(x,0.55f,convZ), new Vector3(1.8f,0.1f,convLen), Mat(COL_BELT,0.1f,0.3f));
            var trig = belt.AddComponent<BoxCollider>();
            trig.isTrigger = true; trig.size = new Vector3(1f,0.5f,1f);

            Color fc = new Color(0.5f,0.52f,0.55f);
            CreateBox("Frame_L", conv, new Vector3(x-1.0f,0.5f,convZ), new Vector3(0.1f,0.2f,convLen+0.2f), Mat(fc,0.7f));
            CreateBox("Frame_R", conv, new Vector3(x+1.0f,0.5f,convZ), new Vector3(0.1f,0.2f,convLen+0.2f), Mat(fc,0.7f));

            foreach (float lz in new[]{ Z_INBOUND+2f,Z_INBOUND+6f,Z_VISION-6f,Z_VISION-2f })
            {
                CreateBox("Leg_L", conv, new Vector3(x-0.8f,0.25f,lz), new Vector3(0.1f,0.5f,0.1f), Mat(fc,0.7f));
                CreateBox("Leg_R", conv, new Vector3(x+0.8f,0.25f,lz), new Vector3(0.1f,0.5f,0.1f), Mat(fc,0.7f));
            }

            for (int r = 0; r < 10; r++)
            {
                float rz = Z_INBOUND+1f+r*(convLen-2f)/9f;
                var roller = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                roller.name = "Roller"; roller.transform.SetParent(conv.transform);
                roller.transform.position = new Vector3(x,0.62f,rz);
                roller.transform.rotation = Quaternion.Euler(0,0,90f);
                roller.transform.localScale = new Vector3(0.08f,0.92f,0.08f);
                roller.GetComponent<MeshRenderer>().material = Mat(fc,0.9f,0.8f);
                Object.DestroyImmediate(roller.GetComponent<CapsuleCollider>());
            }

            var spawn = new GameObject("SpawnPoint");
            spawn.transform.SetParent(conv.transform);
            spawn.transform.position = new Vector3(x,0.85f,Z_INBOUND+2f);

            var ps = conv.AddComponent<PanelSpawner>();
            ps.conveyorIndex = i;
            ps.spawnPoint    = spawn.transform;
            ps.spawnInterval = 12f;
            ps.defectRate    = 0.4f;

            var cba = belt.AddComponent<ConveyorBeltAnimator>();
            cba.beltSpeed = 1.8f;

            conveyors[i] = conv;
        }
        return conveyors;
    }

    // ─── 비전 스테이션 ───────────────────────────────────────
    static GameObject[] BuildVisionStations()
    {
        var parent = new GameObject("VisionStations");
        var stations = new GameObject[3];

        for (int i = 0; i < 3; i++)
        {
            float x = CONV_X[i], z = Z_VISION-2f;
            var station = new GameObject($"VisionStation_{i+1}");
            station.transform.SetParent(parent.transform);

            CreateBox("Gantry_L",   station, new Vector3(x-1.2f,2f,z),  new Vector3(0.2f,4f,0.2f),    Mat(COL_VISION,0.5f,0.6f));
            CreateBox("Gantry_R",   station, new Vector3(x+1.2f,2f,z),  new Vector3(0.2f,4f,0.2f),    Mat(COL_VISION,0.5f,0.6f));
            CreateBox("Gantry_Top", station, new Vector3(x,4.1f,z),     new Vector3(2.6f,0.2f,0.3f),  Mat(COL_VISION,0.5f,0.6f));
            CreateBox("CamHousing", station, new Vector3(x,3.8f,z),     new Vector3(0.4f,0.3f,0.4f),  Mat(new Color(0.1f,0.1f,0.12f),0.7f,0.8f));

            var lens = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            lens.name = "Lens"; lens.transform.SetParent(station.transform);
            lens.transform.position = new Vector3(x,3.5f,z);
            lens.transform.localScale = new Vector3(0.15f,0.05f,0.15f);
            lens.GetComponent<MeshRenderer>().material = Mat(new Color(0.1f,0.4f,1f),0f,1f);
            Object.DestroyImmediate(lens.GetComponent<CapsuleCollider>());

            var slGO = new GameObject("ScanLight"); slGO.transform.SetParent(station.transform);
            slGO.transform.position = new Vector3(x,3.4f,z);
            var sl = slGO.AddComponent<Light>();
            sl.type = LightType.Spot; sl.spotAngle = 40f; sl.range = 4f;
            sl.intensity = 2f; sl.color = new Color(0.3f,0.7f,1f);
            sl.transform.rotation = Quaternion.Euler(90f,0,0);

            var trigGO = new GameObject("DetectionZone"); trigGO.transform.SetParent(station.transform);
            trigGO.transform.position = new Vector3(x,0.8f,z);
            var col = trigGO.AddComponent<BoxCollider>();
            col.size = new Vector3(2f,1f,1.5f); col.isTrigger = true;

            var vs = trigGO.AddComponent<PanelVisionStation>();
            vs.stationIndex = i; vs.scanLight = sl;

            stations[i] = station;
        }
        return stations;
    }

    // ─── 분류 게이트 ─────────────────────────────────────────
    static GameObject BuildSortingGate()
    {
        var gate = new GameObject("SortingGate");
        gate.transform.position = new Vector3(0,0,Z_SORT);

        float[] slotX = {-12f,0f,12f};
        Color[] slotC = {COL_PLATE,new Color(0.5f,0.5f,0.5f),COL_SHEET};
        for (int i = 0; i < 3; i++)
            CreateBox($"ParkSlot_{i}", gate, new Vector3(slotX[i],0.01f,Z_SORT+2f),
                new Vector3(2.5f,0.01f,2.5f), Mat(new Color(slotC[i].r,slotC[i].g,slotC[i].b,0.4f)));

        CreateBox("GateFlap", gate, new Vector3(0,0.8f,Z_SORT-0.5f), new Vector3(0.15f,0.5f,1.5f), Mat(COL_STEEL,0.6f,0.7f));
        gate.AddComponent<PanelSortingGate>();
        return gate;
    }

    // ─── 자동창고 (2랙, 1열×5행) ────────────────────────────
    static void BuildAutoStorage()
    {
        var storage = new GameObject("AutoStorage");
        BuildStorageRack(storage, "SteelPlate_Rack", new Vector3(-15f,0,Z_STORAGE), COL_PLATE, 1, 5);
        BuildStorageRack(storage, "Sheet_Rack",      new Vector3( 15f,0,Z_STORAGE), COL_SHEET, 1, 5);
    }

    static void BuildStorageRack(GameObject parent, string name, Vector3 pos,
        Color color, int cols, int rows)
    {
        var rack = new GameObject(name);
        rack.transform.SetParent(parent.transform);
        rack.transform.position = pos;

        var frameMat = Mat(COL_STEEL,0.6f,0.6f);
        var shelfMat = Mat(new Color(color.r*0.4f,color.g*0.4f,color.b*0.4f),0.3f);
        float slotW = 2.5f, slotH = 1.2f;

        for (int row = 0; row < rows; row++)
            CreateBox($"Shelf_{row}", rack, pos+new Vector3(0, 0.5f+row*slotH, 0),
                new Vector3(slotW*cols+0.2f, 0.08f, 2.5f), shelfMat);

        for (int c = 0; c <= cols; c++)
        {
            float cx = pos.x - slotW*cols/2f + c*slotW;
            CreateBox($"Post_{c}", rack, new Vector3(cx, rows*slotH/2f, pos.z),
                new Vector3(0.1f, rows*slotH, 0.1f), frameMat);
        }

        CreateBox("LED", rack, pos+new Vector3(0, rows*slotH+0.2f, 0),
            new Vector3(slotW*cols, 0.05f, 0.05f),
            Mat(new Color(Mathf.Min(color.r*1.8f,1f), Mathf.Min(color.g*1.8f,1f), Mathf.Min(color.b*1.8f,1f))));

        CreateBox("NamePlate", rack, pos+new Vector3(0, rows*slotH+0.5f, 0),
            new Vector3(slotW*cols, 0.3f, 0.05f), Mat(color,0.2f));
    }

    // ─── 출하 구역 (2도크) ───────────────────────────────────
    static void BuildShippingZone()
    {
        var zone = new GameObject("ShippingZone");
        zone.transform.position = new Vector3(0,0,Z_SHIPPING);

        float[] dockX     = { -15f, 15f };
        string[] dockNames  = { "Dock_Plate", "Dock_Sheet" };
        Color[]  dockColors = { COL_PLATE, COL_SHEET };

        for (int i = 0; i < 2; i++)
        {
            float x = dockX[i];
            CreateBox(dockNames[i], zone, new Vector3(x,0.3f,Z_SHIPPING+2f),  new Vector3(9f,0.6f,4f),    Mat(dockColors[i],0.2f,0.5f));
            CreateBox($"DockMarker_{i}", zone, new Vector3(x,0.01f,Z_SHIPPING+2f), new Vector3(9.5f,0.01f,4.5f),Mat(new Color(dockColors[i].r,dockColors[i].g,dockColors[i].b,0.4f)));
            CreateBox($"DockLED_{i}", zone, new Vector3(x,1f,Z_SHIPPING+4.5f),  new Vector3(2f,0.5f,0.1f),  Mat(new Color(0.2f,1f,0.4f)));
            CreateBox($"DockSign_{i}",zone, new Vector3(x,1.8f,Z_SHIPPING+4.7f),new Vector3(4f,0.6f,0.1f),  Mat(dockColors[i],0.1f));
        }
    }

    // ─── 불량품 처리 구역 (★ v5: Z 통일 + 벽 침범 수정) ─────
    static void BuildDefectProcessingZone()
    {
        var zone = new GameObject("DefectProcessingZone");

        // ════════════════════════════════════════════════════
        // 재작업존 (우측 X=+21, Z=42): 경미한 결함
        // 펜스 범위: X=18~25, Z=35.5~49.5 (벽 안)
        // ════════════════════════════════════════════════════
        var reworkZone = new GameObject("ReworkZone");
        reworkZone.transform.SetParent(zone.transform);

        // 처리 테이블 (로봇 Z+ 방향)
        CreateBox("ReworkTable", reworkZone,
            new Vector3(REWORK_X, 0.5f, REWORK_PROC_Z),
            new Vector3(4f, 1f, 3f),
            Mat(new Color(0.55f,0.45f,0.28f),0.3f,0.4f));

        // 합격 패드 (초록)
        CreateBox("ApprovalPad", reworkZone,
            new Vector3(REWORK_X, 0.02f, REWORK_PROC_Z),
            new Vector3(4.5f, 0.02f, 3.5f),
            Mat(new Color(0.1f,0.8f,0.3f,0.5f)));

        // 안전 펜스
        // ★ 좌측 경계: X=18.2 (AGV 레인 X=18과 0.2m 간격)
        //   Z=35.5~49.5 → length=14, center Z=(35.5+49.5)/2=42.5
        CreateFence(reworkZone,
            new Vector3(18.2f, 0.9f, (ZONE_MIN_Z+ZONE_MAX_Z)/2f),
            new Vector3(0.12f, 1.8f, ZONE_MAX_Z-ZONE_MIN_Z));

        // ★ 앞쪽 경계: Z=ZONE_MIN_Z=35.5
        //   X=18.2~24.8 → length=6.6, center X=21.5
        CreateFence(reworkZone,
            new Vector3(REWORK_X + 0.5f, 0.9f, ZONE_MIN_Z),
            new Vector3(6.6f, 1.8f, 0.12f));

        // 경고 사인 (앞쪽 펜스 위)
        CreateBox("ReworkSign", reworkZone,
            new Vector3(REWORK_X, 2.8f, ZONE_MIN_Z+0.1f),
            new Vector3(5f, 0.9f, 0.1f),
            Mat(COL_REWORK, 0.15f));

        // ★ 재작업 로봇암 (Z=42로 통일)
        BuildRobotArm(reworkZone, "ReworkRobot",
            new Vector3(REWORK_X, 0f, STATION_Z),
            new Color(1f, 0.55f, 0.1f),
            DefectProcessingStation.StationType.Rework,
            new Vector3(REWORK_X, 1f, REWORK_PROC_Z));

        // ════════════════════════════════════════════════════
        // 스크랩존 (좌측 X=-21, Z=42): 심각한 결함
        // 펜스 범위: X=-25~-18, Z=35.5~49.5 (벽 안)
        // ════════════════════════════════════════════════════
        var scrapZone = new GameObject("ScrapZone");
        scrapZone.transform.SetParent(zone.transform);

        // 스크랩함 (로봇 Z- 방향)
        BuildScrapBox(scrapZone, new Vector3(SCRAP_X, 0f, SCRAP_PROC_Z));

        // 안전 펜스
        // ★ 오른쪽 경계: X=-18.2 (AGV 레인과 간격)
        CreateFence(scrapZone,
            new Vector3(-18.2f, 0.9f, (ZONE_MIN_Z+ZONE_MAX_Z)/2f),
            new Vector3(0.12f, 1.8f, ZONE_MAX_Z-ZONE_MIN_Z));

        // ★ 앞쪽 경계: Z=ZONE_MIN_Z=35.5
        CreateFence(scrapZone,
            new Vector3(SCRAP_X - 0.5f, 0.9f, ZONE_MIN_Z),
            new Vector3(6.6f, 1.8f, 0.12f));

        // 경고 사인
        CreateBox("ScrapSign", scrapZone,
            new Vector3(SCRAP_X, 2.8f, ZONE_MIN_Z+0.1f),
            new Vector3(5f, 0.9f, 0.1f),
            Mat(COL_DEFECT, 0.15f));

        // ★ 스크랩 로봇암 (Z=42로 통일)
        BuildRobotArm(scrapZone, "ScrapRobot",
            new Vector3(SCRAP_X, 0f, STATION_Z),
            new Color(0.85f, 0.15f, 0.1f),
            DefectProcessingStation.StationType.Scrap,
            new Vector3(SCRAP_X, 0.6f, SCRAP_PROC_Z));

        Debug.Log($"[SmartFactory] DefectProcessingZone 생성 | " +
                  $"ReworkRobot ({REWORK_X},{STATION_Z}) / ScrapRobot ({SCRAP_X},{STATION_Z})");
    }

    static void CreateFence(GameObject parent, Vector3 pos, Vector3 scale)
    {
        var fence = CreateBox("Fence", parent, pos, scale, Mat(COL_SAFETY, 0.5f, 0.6f));
        var col = fence.GetComponent<BoxCollider>();
        if (col) Object.DestroyImmediate(col);
    }

    static void BuildScrapBox(GameObject parent, Vector3 pos)
    {
        var box = new GameObject("DefectBox");
        box.transform.SetParent(parent.transform);
        CreateBox("Body",    box, pos+new Vector3(0,0.7f,0),  new Vector3(3.5f,1.4f,3f),   Mat(COL_DEFECT,0.2f));
        CreateBox("Lid",     box, pos+new Vector3(0,1.5f,0),  new Vector3(3.7f,0.1f,3.2f), Mat(new Color(0.6f,0.05f,0.05f),0.3f));
        CreateBox("Warning", box, pos+new Vector3(0,1.7f,0),  new Vector3(3.8f,0.12f,0.12f), Mat(new Color(1f,0.85f,0f)));
        var wl = new GameObject("WarnLight"); wl.transform.SetParent(box.transform);
        wl.transform.position = pos+new Vector3(0,2.5f,0);
        var l = wl.AddComponent<Light>();
        l.type = LightType.Point; l.color = COL_DEFECT;
        l.intensity = 2.5f; l.range = 7f;
    }

    // ─── 로봇암 생성 ─────────────────────────────────────────
    static void BuildRobotArm(GameObject parent, string robotName, Vector3 baseWorldPos,
        Color armColor, DefectProcessingStation.StationType stationType, Vector3 procWorldPos)
    {
        var root = new GameObject(robotName);
        root.transform.SetParent(parent.transform);
        root.transform.position = baseWorldPos;

        var metalMat = Mat(COL_ROBOT, 0.7f, 0.8f);
        var armMat   = Mat(armColor,  0.5f, 0.7f);
        var jointMat = Mat(new Color(0.12f,0.12f,0.15f), 0.8f, 0.9f);
        var gripMat  = Mat(new Color(0.62f,0.65f,0.70f), 0.4f, 0.7f);

        // 받침대
        var basePlat = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        basePlat.name = "BasePlatform";
        basePlat.transform.SetParent(root.transform);
        basePlat.transform.localPosition = Vector3.zero;
        basePlat.transform.localScale    = new Vector3(1.1f, 0.1f, 1.1f);
        basePlat.GetComponent<MeshRenderer>().material = metalMat;
        Object.DestroyImmediate(basePlat.GetComponent<CapsuleCollider>());

        // RotationBase (Y축 선회)
        var rotBase = new GameObject("RotationBase");
        rotBase.transform.SetParent(root.transform);
        rotBase.transform.localPosition = new Vector3(0, 0.1f, 0);

        var baseBody = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        baseBody.name = "BaseBody";
        baseBody.transform.SetParent(rotBase.transform);
        baseBody.transform.localPosition = new Vector3(0, 0.2f, 0);
        baseBody.transform.localScale    = new Vector3(0.65f, 0.2f, 0.65f);
        baseBody.GetComponent<MeshRenderer>().material = armMat;
        Object.DestroyImmediate(baseBody.GetComponent<CapsuleCollider>());

        // ShoulderPivot (arm1Pivot)
        var shoulderPivot = new GameObject("ShoulderPivot");
        shoulderPivot.transform.SetParent(rotBase.transform);
        shoulderPivot.transform.localPosition = new Vector3(0, 0.42f, 0);

        var jt0 = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        jt0.name = "JointShoulder";
        jt0.transform.SetParent(shoulderPivot.transform);
        jt0.transform.localPosition = Vector3.zero;
        jt0.transform.localScale    = new Vector3(0.24f, 0.24f, 0.24f);
        jt0.GetComponent<MeshRenderer>().material = jointMat;
        Object.DestroyImmediate(jt0.GetComponent<SphereCollider>());

        var arm1Body = GameObject.CreatePrimitive(PrimitiveType.Cube);
        arm1Body.name = "Arm1Body";
        arm1Body.transform.SetParent(shoulderPivot.transform);
        arm1Body.transform.localPosition = new Vector3(0, 0.55f, 0);
        arm1Body.transform.localScale    = new Vector3(0.19f, 1.1f, 0.19f);
        arm1Body.GetComponent<MeshRenderer>().material = armMat;
        Object.DestroyImmediate(arm1Body.GetComponent<BoxCollider>());

        // ElbowPivot (arm2Pivot)
        var elbowPivot = new GameObject("ElbowPivot");
        elbowPivot.transform.SetParent(arm1Body.transform);
        elbowPivot.transform.localPosition = new Vector3(0, 0.5f, 0);

        var jt1 = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        jt1.name = "JointElbow";
        jt1.transform.SetParent(elbowPivot.transform);
        jt1.transform.localPosition = Vector3.zero;
        jt1.transform.localScale    = new Vector3(0.20f, 0.20f, 0.20f);
        jt1.GetComponent<MeshRenderer>().material = jointMat;
        Object.DestroyImmediate(jt1.GetComponent<SphereCollider>());

        var arm2Body = GameObject.CreatePrimitive(PrimitiveType.Cube);
        arm2Body.name = "Arm2Body";
        arm2Body.transform.SetParent(elbowPivot.transform);
        arm2Body.transform.localPosition = new Vector3(0, 0.38f, 0);
        arm2Body.transform.localScale    = new Vector3(0.14f, 0.76f, 0.14f);
        arm2Body.GetComponent<MeshRenderer>().material = armMat;
        Object.DestroyImmediate(arm2Body.GetComponent<BoxCollider>());

        // WristPivot & GripperRoot
        var wristPivot = new GameObject("WristPivot");
        wristPivot.transform.SetParent(arm2Body.transform);
        wristPivot.transform.localPosition = new Vector3(0, 0.5f, 0);

        var jt2 = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        jt2.name = "JointWrist";
        jt2.transform.SetParent(wristPivot.transform);
        jt2.transform.localPosition = Vector3.zero;
        jt2.transform.localScale    = new Vector3(0.15f, 0.15f, 0.15f);
        jt2.GetComponent<MeshRenderer>().material = jointMat;
        Object.DestroyImmediate(jt2.GetComponent<SphereCollider>());

        var gripperRoot = new GameObject("GripperRoot");
        gripperRoot.transform.SetParent(wristPivot.transform);
        gripperRoot.transform.localPosition = new Vector3(0, 0.06f, 0);

        var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
        bar.name = "GripBar";
        bar.transform.SetParent(gripperRoot.transform);
        bar.transform.localPosition = Vector3.zero;
        bar.transform.localScale    = new Vector3(0.48f, 0.06f, 0.11f);
        bar.GetComponent<MeshRenderer>().material = jointMat;
        Object.DestroyImmediate(bar.GetComponent<BoxCollider>());

        var gripL = GameObject.CreatePrimitive(PrimitiveType.Cube);
        gripL.name = "GripperL";
        gripL.transform.SetParent(gripperRoot.transform);
        gripL.transform.localPosition = new Vector3(-0.22f, -0.12f, 0);
        gripL.transform.localScale    = new Vector3(0.06f, 0.26f, 0.09f);
        gripL.GetComponent<MeshRenderer>().material = gripMat;
        Object.DestroyImmediate(gripL.GetComponent<BoxCollider>());

        var gripR = GameObject.CreatePrimitive(PrimitiveType.Cube);
        gripR.name = "GripperR";
        gripR.transform.SetParent(gripperRoot.transform);
        gripR.transform.localPosition = new Vector3( 0.22f, -0.12f, 0);
        gripR.transform.localScale    = new Vector3(0.06f, 0.26f, 0.09f);
        gripR.GetComponent<MeshRenderer>().material = gripMat;
        Object.DestroyImmediate(gripR.GetComponent<BoxCollider>());

        // 상태 표시등
        var lightGO = new GameObject("StatusLight");
        lightGO.transform.SetParent(root.transform);
        lightGO.transform.localPosition = new Vector3(0, 3.2f, 0);
        var sLight = lightGO.AddComponent<Light>();
        sLight.type = LightType.Point; sLight.range = 6f;
        sLight.intensity = 2f; sLight.color = Color.green;

        // 처리 포인트 마커
        var procPoint = new GameObject("ProcessingPoint");
        procPoint.transform.SetParent(root.transform);
        procPoint.transform.position = procWorldPos;

        // DefectProcessingStation 컴포넌트 연결
        var station          = root.AddComponent<DefectProcessingStation>();
        station.stationType  = stationType;
        station.rotationBase = rotBase.transform;
        station.arm1Pivot    = shoulderPivot.transform;
        station.arm2Pivot    = elbowPivot.transform;
        station.gripperRoot  = gripperRoot.transform;
        station.gripperL     = gripL.transform;
        station.gripperR     = gripR.transform;
        station.processingPoint = procPoint.transform;

        Debug.Log($"[SmartFactory] 로봇암: {robotName} @ {baseWorldPos:F1} | {stationType}");
    }

    // ─── 소형 AGV 5대 ────────────────────────────────────────
    static GameObject[] BuildAGVFleet()
    {
        var parent = new GameObject("AGV_Fleet");
        var agvs   = new GameObject[5];

        Color[] colors = {
            new Color(0.9f,0.8f,0.05f), new Color(0.05f,0.6f,0.9f),
            new Color(0.9f,0.4f,0.05f), new Color(0.5f,0.9f,0.1f), new Color(0.8f,0.1f,0.8f)
        };
        float[] startX = { -18f,-9f,0f,9f,18f };

        for (int i = 0; i < 5; i++)
        {
            var pos = new Vector3(startX[i],0,Z_AGV_LANE-3f);
            var agv = BuildAGVVehicle($"AGV_{i+1:00}", pos, colors[i]);
            agv.transform.SetParent(parent.transform);

            var ctrl = agv.AddComponent<AGVController>();
            ctrl.agvID        = $"AGV_{i+1:00}";
            ctrl.maxSpeed     = 7.0f + i*0.3f;
            ctrl.acceleration = 4.0f;

            var ledGO = new GameObject("StatusLight"); ledGO.transform.SetParent(agv.transform);
            ledGO.transform.localPosition = new Vector3(0,0.5f,0);
            var led = ledGO.AddComponent<Light>();
            led.type = LightType.Point; led.range = 3f; led.intensity = 1.5f;
            ctrl.statusLight = led;

            var cargo = CreateBox("CargoPlatform", agv, pos+new Vector3(0,0.35f,0),
                new Vector3(1.6f,0.05f,1.6f), Mat(new Color(0.25f,0.25f,0.28f),0.5f));
            ctrl.cargoPlate = cargo.transform;

            agvs[i] = agv;
        }
        return agvs;
    }

    static GameObject BuildAGVVehicle(string name, Vector3 pos, Color color)
    {
        var agv = new GameObject(name); agv.transform.position = pos;
        var bm = Mat(color,0.3f,0.5f); var dm = Mat(new Color(0.12f,0.12f,0.14f),0.5f,0.4f);
        var wm = Mat(new Color(0.10f,0.10f,0.12f),0.2f,0.3f);

        CreateBox("Body",    agv, pos+new Vector3(0,0.2f,0),     new Vector3(2.0f,0.35f,2.0f), bm);
        CreateBox("Bumper_F",agv, pos+new Vector3(0,0.2f,1.1f),  new Vector3(2.1f,0.3f,0.1f),  dm);
        CreateBox("Bumper_B",agv, pos+new Vector3(0,0.2f,-1.1f), new Vector3(2.1f,0.3f,0.1f),  dm);

        foreach (var wp in new[]{ new Vector3(-0.8f,0.1f,0.7f),new Vector3(0.8f,0.1f,0.7f),
                                   new Vector3(-0.8f,0.1f,-0.7f),new Vector3(0.8f,0.1f,-0.7f) })
        {
            var w = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            w.name = "Wheel"; w.transform.SetParent(agv.transform);
            w.transform.position = pos+wp; w.transform.rotation = Quaternion.Euler(0,0,90f);
            w.transform.localScale = new Vector3(0.22f,0.12f,0.22f);
            w.GetComponent<MeshRenderer>().material = wm;
            Object.DestroyImmediate(w.GetComponent<CapsuleCollider>());
        }

        var lidar = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        lidar.name = "LiDAR"; lidar.transform.SetParent(agv.transform);
        lidar.transform.position = pos+new Vector3(0,0.45f,0.5f);
        lidar.transform.localScale = new Vector3(0.15f,0.08f,0.15f);
        lidar.GetComponent<MeshRenderer>().material = Mat(new Color(0.1f,0.1f,0.12f),0.8f,0.9f);
        Object.DestroyImmediate(lidar.GetComponent<CapsuleCollider>());

        return agv;
    }

    // ─── 지게차 2대 ──────────────────────────────────────────
    static GameObject[] BuildForkLiftFleet()
    {
        var parent = new GameObject("ForkLift_Fleet");
        var result = new GameObject[2];

        float[] startX     = { -20f, 20f };
        float[] dedicatedX = { -15f, 15f };
        Color[] bodyColors = { new Color(0.95f,0.75f,0.05f), new Color(0.95f,0.55f,0.05f) };

        for (int i = 0; i < 2; i++)
        {
            string id  = $"FL_{i+1:00}";
            var pos    = new Vector3(startX[i], 0f, Z_FL_LANE);
            var fl     = BuildForkLiftVehicle(id, pos, bodyColors[i]);
            fl.transform.SetParent(parent.transform);

            var ctrl = fl.AddComponent<ForkLiftAGV>();
            ctrl.forkLiftID     = id;
            ctrl.maxSpeed       = 2.0f;
            ctrl.emptySpeed     = 3.0f;
            ctrl.forkHighY      = 0.9f;
            ctrl.forkLowY       = 0.12f;
            ctrl.liftSpeed      = 0.6f;
            ctrl.dedicatedRackX = dedicatedX[i];

            var carriage = fl.transform.Find("ForkCarriage");
            if (carriage != null) ctrl.forkCarriage = carriage;

            var lightGO = new GameObject("StatusLight"); lightGO.transform.SetParent(fl.transform);
            lightGO.transform.position = pos+new Vector3(0,1.2f,0);
            var l = lightGO.AddComponent<Light>();
            l.type = LightType.Point; l.range = 4f; l.intensity = 2f; l.color = Color.green;
            ctrl.statusLight = l;

            result[i] = fl;
            Debug.Log($"[SmartFactory] 지게차: {id} | pos={pos:F1} | 전담랙X={dedicatedX[i]:F0}");
        }
        return result;
    }

    static GameObject BuildForkLiftVehicle(string name, Vector3 pos, Color bodyColor)
    {
        var fl = new GameObject(name); fl.transform.position = pos;
        var bm = Mat(bodyColor,0.3f,0.5f);
        var dm = Mat(new Color(0.12f,0.12f,0.14f),0.4f,0.3f);
        var sm = Mat(new Color(0.55f,0.58f,0.62f),0.6f,0.6f);
        var fm = Mat(new Color(0.80f,0.65f,0.02f),0.5f,0.7f);
        var wm = Mat(new Color(0.08f,0.08f,0.10f),0.1f,0.2f);

        CreateBox("Body",          fl, pos+new Vector3(0,0.22f,0),     new Vector3(1.4f,0.35f,2.5f), bm);
        CreateBox("Counterweight", fl, pos+new Vector3(0,0.55f,-1.0f), new Vector3(1.3f,0.60f,0.6f), dm);
        CreateBox("Cab",           fl, pos+new Vector3(0,0.70f,-0.4f), new Vector3(0.9f,0.45f,0.7f), dm);
        CreateBox("MastL",  fl, pos+new Vector3(-0.42f,1.5f,1.0f), new Vector3(0.10f,2.8f,0.10f), sm);
        CreateBox("MastR",  fl, pos+new Vector3( 0.42f,1.5f,1.0f), new Vector3(0.10f,2.8f,0.10f), sm);
        CreateBox("MastTop",fl, pos+new Vector3(0,2.85f,1.0f),     new Vector3(1.00f,0.10f,0.10f),sm);

        var carriage = new GameObject("ForkCarriage");
        carriage.transform.SetParent(fl.transform);
        carriage.transform.position = new Vector3(pos.x, pos.y+0.12f, pos.z);
        var cp = carriage.transform.position;
        CreateBox("BackPlate",carriage, cp+new Vector3(0,0.20f,1.00f),    new Vector3(1.00f,0.45f,0.08f), sm);
        CreateBox("ForkArmL", carriage, cp+new Vector3(-0.32f,0.05f,1.65f),new Vector3(0.10f,0.07f,1.20f), fm);
        CreateBox("ForkArmR", carriage, cp+new Vector3( 0.32f,0.05f,1.65f),new Vector3(0.10f,0.07f,1.20f), fm);

        foreach (var wp in new[]{ new Vector3(-0.65f,0.12f,0.85f),new Vector3(0.65f,0.12f,0.85f),
                                   new Vector3(-0.65f,0.12f,-0.85f),new Vector3(0.65f,0.12f,-0.85f) })
        {
            var w = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            w.name = "Wheel"; w.transform.SetParent(fl.transform);
            w.transform.position = pos+wp; w.transform.rotation = Quaternion.Euler(0,0,90f);
            w.transform.localScale = new Vector3(0.24f,0.13f,0.24f);
            w.GetComponent<MeshRenderer>().material = wm;
            Object.DestroyImmediate(w.GetComponent<CapsuleCollider>());
        }

        var beacon = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        beacon.name = "Beacon"; beacon.transform.SetParent(fl.transform);
        beacon.transform.position = pos+new Vector3(0,0.55f,-0.6f);
        beacon.transform.localScale = Vector3.one*0.08f;
        beacon.GetComponent<MeshRenderer>().material = Mat(new Color(1f,0.3f,0f),0f,1f);
        Object.DestroyImmediate(beacon.GetComponent<SphereCollider>());

        foreach (var col in fl.GetComponentsInChildren<BoxCollider>())
            Object.DestroyImmediate(col);

        return fl;
    }

    // ─── 조명 ────────────────────────────────────────────────
    static void BuildLighting()
    {
        foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            if (l.type == LightType.Directional)
            {
                l.intensity = 0.6f; l.color = new Color(1f,0.97f,0.92f);
                l.shadows = LightShadows.Soft; l.shadowStrength = 0.5f;
            }

        var lights = new GameObject("FactoryLights");
        for (float x = -W/2f+5f; x <= W/2f-5f; x += 10f)
            for (float z = 5f; z <= D-5f; z += 12f)
                AddLight(lights,$"Ceil_{x:F0}_{z:F0}",new Vector3(x,H-0.5f,z),new Color(0.95f,0.97f,1f),2f,20f);

        foreach (float x in CONV_X)
            AddLight(lights,$"VisionL_{x:F0}",new Vector3(x,5f,Z_VISION-2f),new Color(0.5f,0.8f,1f),3f,8f);

        for (float x=-18f; x<=18f; x+=9f)
            AddLight(lights,$"AGVLane_{x:F0}",new Vector3(x,3f,Z_AGV_LANE),new Color(0.8f,1f,0.7f),2f,15f);

        AddLight(lights,"FLLane_L", new Vector3(-15f,3f,Z_FL_LANE),  new Color(1f,0.9f,0.5f),2f,12f);
        AddLight(lights,"FLLane_R", new Vector3( 15f,3f,Z_FL_LANE),  new Color(1f,0.9f,0.5f),2f,12f);

        AddLight(lights,"StorePlate",new Vector3(-15f,5f,Z_STORAGE),  new Color(0.7f,0.85f,1f),2.5f,18f);
        AddLight(lights,"StoreSheet",new Vector3( 15f,5f,Z_STORAGE),  new Color(0.85f,0.7f,1f),2.5f,18f);

        AddLight(lights,"DockPlate", new Vector3(-15f,5f,Z_SHIPPING), new Color(0.5f,1f,0.6f),3f,15f);
        AddLight(lights,"DockSheet", new Vector3( 15f,5f,Z_SHIPPING), new Color(0.5f,1f,0.6f),3f,15f);

        // ★ 불량품 구역 조명 (Z=42 통일 기준)
        AddLight(lights,"Rework_L1", new Vector3(REWORK_X,4f,STATION_Z),   new Color(1f,0.65f,0.1f),3f,10f);
        AddLight(lights,"Rework_L2", new Vector3(REWORK_X,4f,REWORK_PROC_Z),new Color(1f,0.65f,0.1f),2f,8f);
        AddLight(lights,"Scrap_L1",  new Vector3(SCRAP_X,4f,STATION_Z),    new Color(1f,0.2f,0.15f),3f,10f);
        AddLight(lights,"Scrap_L2",  new Vector3(SCRAP_X,4f,SCRAP_PROC_Z), new Color(1f,0.2f,0.15f),2f,8f);
    }

    // ─── 매니저 연결 ─────────────────────────────────────────
    static void BuildManagers(GameObject[] agvs, GameObject[] forkLifts,
        GameObject[] visionStations, GameObject sortGate)
    {
        var fleetGO = new GameObject("PanelAGVFleetManager");
        var fleet   = fleetGO.AddComponent<PanelAGVFleetManager>();
        foreach (var agv in agvs)
        {
            var c = agv.GetComponent<AGVController>();
            if (c) fleet.agvFleet.Add(c);
        }

        fleet.shippingDock1 = GameObject.Find("Dock_Plate")?.transform;
        fleet.shippingDock2 = GameObject.Find("Dock_Sheet")?.transform;
        fleet.shippingDock3 = null;

        fleet.reworkZone = GameObject.Find("ReworkRobot")?.transform;
        fleet.scrapBox   = GameObject.Find("ScrapRobot")?.transform;

        var sg = sortGate?.GetComponent<PanelSortingGate>();
        if (sg != null)
        {
            sg.fleetManager = fleet;
            sg.reworkZone   = fleet.reworkZone;
            sg.scrapBox     = fleet.scrapBox;
        }

        foreach (var vs_go in visionStations)
        {
            var vs = vs_go.GetComponentInChildren<PanelVisionStation>();
            if (vs != null) vs.sortingGate = sg;
        }

        var rackMgrGO = new GameObject("StorageRackManager");
        var rackMgr   = rackMgrGO.AddComponent<StorageRackManager>();
        rackMgr.fleetManager  = fleet;
        rackMgr.shippingDock1 = fleet.shippingDock1;
        rackMgr.shippingDock2 = fleet.shippingDock2;

        var tmGO = new GameObject("AGVTrafficManager");
        var tm   = tmGO.AddComponent<AGVTrafficManager>();
        tm.cellSize = 3f; tm.showDebugGrid = true;

        var flFleetGO = new GameObject("ForkLiftFleetManager");
        var flFleet   = flFleetGO.AddComponent<ForkLiftFleetManager>();
        foreach (var fl in forkLifts)
        {
            var c = fl?.GetComponent<ForkLiftAGV>();
            if (c) flFleet.fleet.Add(c);
        }

        var stations = Object.FindObjectsByType<DefectProcessingStation>(FindObjectsSortMode.None);
        foreach (var st in stations)
        {
            st.fleetManager = fleet;
            Debug.Log($"[SmartFactory] DefectStation 연결: {st.name} | {st.stationType}");
        }

        new GameObject("FactoryDashboardManager").AddComponent<FactoryDashboard>();

        Debug.Log($"[SmartFactory] 매니저 연결 완료 | " +
                  $"ReworkRobot={fleet.reworkZone?.position:F1} | ScrapRobot={fleet.scrapBox?.position:F1}");
    }

    // ─── 프리팹 연결 ─────────────────────────────────────────
    static void SetupPanelSpawners()
    {
        var prefabPlate = FindPrefab("Steel_Plate");
        var prefabSheet = FindPrefab("Sheet");

        if (prefabPlate == null) Debug.LogWarning("[Setup] 'Steel_Plate' 못 찾음!");
        if (prefabSheet == null) Debug.LogWarning("[Setup] 'Sheet' 못 찾음!");

        var sets = new[]
        {
            new[]{ prefabPlate, prefabPlate },
            new[]{ prefabSheet, prefabSheet },
            new[]{ prefabPlate, prefabPlate },
        };

        for (int i = 0; i < 3; i++)
        {
            var conv = GameObject.Find($"Conveyor_{i+1}");
            if (conv == null) continue;
            var ps = conv.GetComponent<PanelSpawner>();
            if (ps == null) continue;
            ps.panelPrefab_Small = sets[i][0];
            ps.panelPrefab_Large = sets[i][1];
            EditorUtility.SetDirty(conv);
        }

        Debug.Log("[Setup] 프리팹 연결: C1·C3=Plate / C2=Sheet");
    }

    static GameObject FindPrefab(string name)
    {
        foreach (var guid in AssetDatabase.FindAssets($"t:Prefab {name}"))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var p    = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (p != null && p.name == name) return p;
        }
        return null;
    }

    static void BuildDashboardUI()
    {
        // ── Canvas ────────────────────────────────────────────────
        var canvasGO = new GameObject("DashboardCanvas");
        var canvas   = canvasGO.AddComponent<Canvas>();
        canvas.renderMode  = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;
        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        canvasGO.AddComponent<GraphicRaycaster>();
    
        // ── 우측 패널 (340px 고정폭) ──────────────────────────────
        var panel = MakePanel("DashboardPanel", canvasGO,
            new Vector2(1f, 0f), new Vector2(1f, 1f),       // 앵커: 우측 full-height
            new Vector2(-340f, 0f), new Vector2(0f, 0f),    // offset
            new Color(0.04f, 0.04f, 0.07f, 0.92f));
    
        float y = -10f;  // 현재 Y 커서 (위→아래)
    
        // ── 헤더 ─────────────────────────────────────────────────
        MakeLabel(panel, "🏭  CPS 스마트 팩토리 관제", ref y, 16, Color.white, bold: true);
        var dash = Object.FindFirstObjectByType<FactoryDashboard>();
    
        dash.txt_SystemTime = MakeText(panel, "00:00:00", ref y, 13, new Color(0.6f,0.9f,1f));
        dash.txt_Uptime     = MakeText(panel, "가동: 00:00", ref y, 12, new Color(0.5f,0.8f,0.5f));
        MakeDivider(panel, ref y);
    
        // ── 공정 현황 ────────────────────────────────────────────
        MakeLabel(panel, "[ 공정 현황 ]", ref y, 13, new Color(0.9f,0.85f,0.3f), bold: true);
        dash.txt_TotalInspected = MakeText(panel, "총 검사:  0 장",        ref y);
        dash.txt_Normal         = MakeText(panel, "정  상:  0 장  (0%)",   ref y, color: new Color(0.3f,1f,0.4f));
        dash.txt_Defect         = MakeText(panel, "결  함:  0 장  (0%)",   ref y, color: new Color(1f,0.4f,0.4f));
        dash.txt_DefectRate     = MakeText(panel, "불량률:  0.0%",         ref y, color: new Color(1f,0.6f,0.3f));
        dash.txt_Throughput     = MakeText(panel, "처리량:  0.0 장/min",   ref y);
        MakeDivider(panel, ref y);
    
        // ── 비전 모델 성능 ────────────────────────────────────────
        MakeLabel(panel, "[ 비전 모델 성능 ]", ref y, 13, new Color(0.4f,0.8f,1f), bold: true);
        dash.txt_VisionAccuracy = MakeText(panel, "전체 정확도:  N/A", ref y, 12, new Color(0.4f,1f,0.8f));
        dash.txt_AvgConfidence  = MakeText(panel, "평균 Confidence:  N/A", ref y);
        y -= 4f;
    
        string[] classLabels = {
            "균열(Crazing)    ", "개재물(Inclusion)",
            "패치(Patches)    ", "피팅(PittedSurf) ",
            "압입(RolledScale)", "스크래치(Scratch)"
        };
        Color[] classColors = {
            new Color(1f,0.4f,0.4f),   // crazing   - Major
            new Color(1f,0.5f,0.3f),   // inclusion - Major
            new Color(0.4f,0.9f,0.4f), // patches   - Minor
            new Color(0.5f,0.9f,0.5f), // pitted    - Minor
            new Color(1f,0.4f,0.4f),   // rolled    - Major
            new Color(0.5f,0.9f,0.5f)  // scratches - Minor
        };
        for (int i = 0; i < 6; i++)
        {
            dash.txt_ClassAccuracy[i] = MakeText(panel,
                $"{classLabels[i]} ░░░░░░░░  N/A", ref y, 11, classColors[i]);
        }
        MakeDivider(panel, ref y);
    
        // ── 불량품 처리 ───────────────────────────────────────────
        MakeLabel(panel, "[ 불량품 처리 ]", ref y, 13, new Color(1f,0.6f,0.2f), bold: true);
        dash.txt_ReworkTotal = MakeText(panel, "재작업:  0 장",          ref y, color: new Color(1f,0.7f,0.2f));
        dash.txt_ReworkPass  = MakeText(panel, "  통과:  0 장  (0%)",    ref y, color: new Color(0.4f,1f,0.5f));
        dash.txt_ReworkFail  = MakeText(panel, "  실패:  0 장",          ref y, color: new Color(1f,0.4f,0.4f));
        dash.txt_ScrapTotal  = MakeText(panel, "스크랩:  0 장",          ref y, color: new Color(1f,0.25f,0.25f));
        MakeDivider(panel, ref y);
    
        // ── AGV 상태 (5대) ────────────────────────────────────────
        MakeLabel(panel, "[ AGV 상태 ]", ref y, 13, new Color(0.5f,1f,0.7f), bold: true);
        Color[] agvColors = {
            new Color(1f,0.9f,0.1f),
            new Color(0.1f,0.7f,1f),
            new Color(1f,0.5f,0.1f),
            new Color(0.5f,1f,0.2f),
            new Color(0.9f,0.2f,0.9f)
        };
        for (int i = 0; i < 5; i++)
        {
            dash.txt_AGVStatus[i]  = MakeText(panel,
                $"AGV_{i+1:00}  ● Idle    ", ref y, 12, agvColors[i]);
            dash.txt_AGVBattery[i] = MakeText(panel,
                $"         ████████ 100%", ref y, 11, new Color(0.6f,0.8f,0.6f));
            y -= 3f;
        }
    
        Debug.Log("[SmartFactory] DashboardUI 생성 완료");
    }

    // ─── 유틸 ────────────────────────────────────────────────

    static GameObject CreateBox(string name, GameObject parent, Vector3 pos, Vector3 scale, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name; go.transform.SetParent(parent?.transform);
        go.transform.position = pos; go.transform.localScale = scale;
        go.GetComponent<MeshRenderer>().material = mat;
        return go;
    }

    static void CreateLine(GameObject p, string name, Vector3 from, Vector3 to, Color color, float w)
    {
        var go = new GameObject(name); go.transform.SetParent(p.transform);
        var lr = go.AddComponent<LineRenderer>();
        lr.positionCount = 2; lr.SetPosition(0,from); lr.SetPosition(1,to);
        lr.startWidth = lr.endWidth = w; lr.useWorldSpace = true;
        lr.material = Mat(color);
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    static void CreateZoneDecal(GameObject p, string name, Vector3 pos, Vector3 scale, Color color)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name; go.transform.SetParent(p.transform);
        go.transform.position = pos; go.transform.localScale = scale;
        Object.DestroyImmediate(go.GetComponent<BoxCollider>());
        var mat = Mat(color); SetTransparent(mat);
        go.GetComponent<MeshRenderer>().material = mat;
        go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    static void CreateArrow(GameObject p, Vector3 pos, float yRot)
    {
        var go = new GameObject("Arrow"); go.transform.SetParent(p.transform);
        go.transform.position = pos; go.transform.rotation = Quaternion.Euler(0,yRot,0);
        var mf = go.AddComponent<MeshFilter>(); var mr = go.AddComponent<MeshRenderer>();
        mr.material = Mat(COL_SAFETY);
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var mesh = new Mesh();
        mesh.vertices  = new[]{ new Vector3(0,0,0.7f),new Vector3(-0.4f,0,-0.3f),new Vector3(0.4f,0,-0.3f) };
        mesh.triangles = new[]{ 0,1,2 }; mesh.RecalculateNormals(); mf.mesh = mesh;
    }

    static void AddLight(GameObject p, string name, Vector3 pos, Color color, float intensity, float range)
    {
        var go = new GameObject(name); go.transform.SetParent(p.transform); go.transform.position = pos;
        var l = go.AddComponent<Light>(); l.type = LightType.Point; l.color = color;
        l.intensity = intensity; l.range = range; l.shadows = LightShadows.None;
    }

    static Material Mat(Color color, float metallic = 0f, float smoothness = 0.4f)
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var mat    = new Material(shader);
        mat.SetColor("_BaseColor", color); mat.color = color;
        mat.SetFloat("_Metallic", metallic); mat.SetFloat("_Smoothness", smoothness);
        return mat;
    }

    static void SetTransparent(Material mat)
    {
        mat.SetFloat("_Surface", 1);
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0); mat.renderQueue = 3000; mat.EnableKeyword("_ALPHABLEND_ON");
    }

    static GameObject MakePanel(string name, GameObject parent,
        Vector2 anchorMin, Vector2 anchorMax,
        Vector2 offsetMin, Vector2 offsetMax, Color color)
    {
        var go  = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        var img = go.AddComponent<Image>();
        img.color = color;
        var rt  = go.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
        rt.offsetMin = offsetMin; rt.offsetMax = offsetMax;
        return go;
    }
    
    static Text MakeText(GameObject parent, string content, ref float y,
        int fontSize = 12, Color color = default, bool bold = false)
    {
        if (color == default) color = new Color(0.85f, 0.87f, 0.9f);
        var go = new GameObject("Txt");
        go.transform.SetParent(parent.transform, false);
        var t  = go.AddComponent<Text>();
        t.text      = content;
        t.fontSize  = fontSize;
        t.color     = color;
        t.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
        t.supportRichText = true;
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, 1); rt.anchorMax = new Vector2(1, 1);
        rt.pivot     = new Vector2(0, 1);
        rt.offsetMin = new Vector2(10, y - 20);
        rt.offsetMax = new Vector2(-10, y);
        y -= 20f;
        return t;
    }
    
    static void MakeLabel(GameObject parent, string text, ref float y,
        int fontSize = 13, Color color = default, bool bold = false)
    {
        if (color == default) color = Color.white;
        y -= 6f;
        MakeText(parent, text, ref y, fontSize, color, bold);
        y -= 2f;
    }
    
    static void MakeDivider(GameObject parent, ref float y)
    {
        y -= 4f;
        var go  = new GameObject("Divider");
        go.transform.SetParent(parent.transform, false);
        var img = go.AddComponent<Image>();
        img.color = new Color(0.3f, 0.35f, 0.4f, 0.8f);
        var rt  = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, 1); rt.anchorMax = new Vector2(1, 1);
        rt.pivot     = new Vector2(0, 1);
        rt.offsetMin = new Vector2(8, y - 1);
        rt.offsetMax = new Vector2(-8, y);
        y -= 8f;
    }

}