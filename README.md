# 🏭 CPS Smart Factory : 스마트 물류 디지털 트윈

Unity 기반의 사이버-물리 시스템(CPS) 스마트 팩토리 디지털 트윈입니다.  
NEU Surface Defect Database 기반 MobileNetV2 딥러닝 모델로 강판 결함을 실시간 분류하고,  
AGV 5대(EDF 스케줄링)와 지게차 2대가 컨베이어→비전검사→분류→창고→출하까지 완전 자동화 파이프라인을 구성합니다.

---

## 📌 1. Project Overview

실제 스마트 공장 환경을 Unity 6 LTS(URP)로 재현한 **50×70m 규모의 물류 팩토리 디지털 트윈**입니다.  
NEU 강판 결함 데이터셋으로 학습한 MobileNetV2 ONNX 모델을 Unity AI Inference(Sentis)에 연동하여  
비전 검사 → 3단계 분류 → AGV 군집 운용 → 자동창고 → 지게차 출하까지의 전체 공정을 시뮬레이션합니다.

### ✨ 주요 기능

- 🔍 **컴퓨터 비전** : NEU 데이터셋 기반 강판 결함 6종 실시간 분류 (MobileNetV2 + Unity Sentis)
- 📦 **컨베이어 3라인** : Plate(C1·C3) / Sheet(C2) 동시 이송, AddForce 물리 기반
- 🤖 **AGV 군집 제어** : 5대 EDF 스케줄링, LiDAR 충돌 회피, 배터리 관리
- 🏗️ **자동창고** : Plate/Sheet 전용 2랙, 슬롯 자동 배정 및 출하 트리거
- 🚜 **지게차 2대** : FL_01(Plate 전담) / FL_02(Sheet 전담) 팔레트 출하
- 🦾 **3DoF 로봇암** : 재작업/스크랩 처리 스테이션
- 📊 **실시간 대시보드** : AGV 상태·배터리·처리량 모니터링

---

## ❓ 2. Why This Project?

공장 자동화 시스템은 실제 하드웨어 없이 테스트와 검증이 어렵습니다.  
이 프로젝트는 Unity를 활용해 실제 스마트 공장 환경을 3D로 재현하고,  
**딥러닝 기반 비전 검사 · AGV 군집 제어 · 자동창고 물류**를 하나의 통합 파이프라인으로 구현합니다.

- 산업 현장에서 실제로 사용되는 NEU 강판 결함 데이터셋을 활용한 AI 모델 학습
- ONNX 변환 → Unity Sentis 엣지 배포까지의 딥러닝 모델 서빙 파이프라인 구현
- EDF 스케줄링 기반 AGV 군집 운용으로 실시간 물류 자동화 시뮬레이션

---

## 🎬 3. Scenario

```
패널 스폰 (컨베이어 진입, Z=5)
        ↓
컨베이어 3라인 이송 (Z=5 → Z=28)
  C1(X=-12) · C3(X=+12) : Steel Plate
  C2(X=0)               : Steel Sheet
        ↓
비전 스테이션 (Z=28, 갠트리 고정 카메라)
  MobileNetV2 ONNX 추론 → 결함 신뢰도 판별
        ↓
소팅 게이트 (Z=33)
  ├─ 정상(Normal)                    → AGV → 자동창고 랙
  ├─ 경미(Scratches·Patches·Pitted)  → AGV → 재작업 로봇 (X=+21, Z=42)
  └─ 심각(Crazing·Inclusion·Rolled)  → AGV → 스크랩 로봇 (X=-21, Z=42)
        ↓
자동창고 적재 (Z=55)
  Plate랙 (X=-15) / Sheet랙 (X=+15) 전용 슬롯
        ↓
3개 적재 시 팔레트 생성 → 지게차 배차
        ↓
출하 도크 (Z=67)
```

---

## 🔄 4. System Pipeline

### Vision Pipeline
```
NEU 텍스처 로드 (60장, 6클래스)
        ↓
갠트리 카메라 감지 (DetectionZone)
        ↓
MobileNetV2 ONNX 추론 (Unity AI Inference)
  입력: (1, 3, 224, 224) / 출력: (1, 6)
        ↓
confidence 기준 분류
  Normal(<0.7) / Minor(≥0.7) / Major(≥0.7)
        ↓
PanelSortingGate 결과 전달
```

### AGV Pipeline
```
소터존 패널 대기 (Z=33)
        ↓
EDF 스케줄링 → 최근접 Idle AGV 배차
        ↓
픽업 이동 → LiDAR 충돌 회피 → 리프트
        ↓
목적지 이동 (랙 / 재작업 / 스크랩)
        ↓
하강 드롭 → 복귀
```

### Forklift Pipeline
```
랙 3개 적재 → TriggerShipping
        ↓
PalletObject 생성 (패널 그룹화)
        ↓
ForkLiftFleetManager 전담 배차
  FL_01 → Plate랙 / FL_02 → Sheet랙
        ↓
홈(Z=60) → 랙앞(Z=51.5) → 팔레트 픽업
        ↓
도크(Z=67) → 하차 → 복귀
```

---

## 🛠️ 5. Tech Stack

| 분류 | 기술 |
|---|---|
| 시뮬레이션 엔진 | Unity 6.3 LTS (URP) |
| 개발 언어 | C# |
| 딥러닝 프레임워크 | Python (PyTorch) → ONNX |
| 모델 추론 | Unity AI Inference (com.unity.ai.inference 2.6.1) |
| 딥러닝 모델 | MobileNetV2 (검증 정확도 100%) |
| 데이터셋 | NEU Surface Defect Database (6클래스, 60장) |
| 스케줄링 알고리즘 | EDF (Earliest Deadline First) |
| 물리 엔진 | Unity Physics (Rigidbody, AddForce) |
| 충돌 회피 | LiDAR 시뮬레이션 (AGVTrafficManager) |

---

## 🗂️ 6. Project Structure

```
Assets/
└── Scripts/
    ├── SteelPanel.cs              # 강판 컴포넌트, NEUDefectType enum
    ├── NEUTextureManager.cs       # NEU 텍스처 싱글톤 로더
    ├── PanelSpawner.cs            # 컨베이어별 패널 스폰
    ├── ConveyorBeltAnimator.cs    # AddForce 컨베이어 이송
    ├── PanelVisionStation.cs      # 갠트리 비전 검사 (Sentis 추론)
    ├── PanelSortingGate.cs        # 정상/경미/심각 3분류 + AGV 배차
    ├── PanelAGVFleetManager.cs    # EDF 스케줄링, AGV 배차 관리
    ├── AGVController.cs           # AGV 이동·리프트·배터리·LiDAR
    ├── AGVTrafficManager.cs       # 격자 기반 충돌 회피
    ├── StorageRackManager.cs      # 2랙 슬롯 관리, 출하 트리거
    ├── PalletObject.cs            # 팔레트 그룹 오브젝트
    ├── ForkLiftFleetManager.cs    # 지게차 2대 전담 배차
    ├── ForkLiftAGV.cs             # 지게차 이동·리프트·팔레트 부착
    ├── DefectProcessingStation.cs # 재작업/스크랩 3DoF 로봇암
    ├── FactoryDashboard.cs        # 실시간 관제 대시보드
    └── BuildSmartFactory.cs       # Editor: CPS Tools → BUILD
```

---

## 📄 7. Script Description

**PanelVisionStation.cs** → 갠트리 카메라 감지 → MobileNetV2 ONNX 추론 → 결함 분류 결과를 SortingGate에 전달

**PanelSortingGate.cs** → 비전 결과에 따라 정상/경미/심각 3분류 → 소터존 대기 → AGV 배차 요청 (슬롯 만석 시 재시도 루프)

**PanelAGVFleetManager.cs** → EDF 기반 태스크 큐 관리 → 최근접 Idle AGV 배차 → 팔레트 생성 시 중복 태스크 취소

**AGVController.cs** → 픽업/재작업/스크랩 태스크 실행 → LiDAR 충돌 회피 → 중복 픽업 방지 (OnAGV 즉시 예약)

**StorageRackManager.cs** → Plate/Sheet 전용 랙 슬롯 배정 → 임계값 도달 시 TriggerShipping → OnAGV 패널 슬롯 유지 후 Stored 패널만 팔레트화

**ForkLiftAGV.cs** → 홈↔랙↔도크 경로 이동 → 포크 리프트 → 팔레트 부착/분리

---

## 🗺️ 8. Factory Layout

```
Z=67  ┌──────────────────────────────────────┐  출하 도크
      │  Dock_Plate(X=-15)   Dock_Sheet(X=+15)  │
Z=60  │    FL_01(X=-20)        FL_02(X=+20)     │  지게차 홈
      │                                         │
Z=55  │   Plate랙(X=-15)    Sheet랙(X=+15)      │  자동창고
      │                                         │
Z=42  │ ScrapRobot(X=-21)  ReworkRobot(X=+21)   │  처리 스테이션
      │                                         │
Z=33  │             소팅 게이트                  │
Z=28  │             비전 스테이션                │
      │                                         │
Z=5   │  C1(X=-12)   C2(X=0)   C3(X=+12)       │  컨베이어 입구
      └──────────────────────────────────────┘
               W = 50m  /  D = 70m  /  H = 10m
```

---

## ⚠️ 9. Limitations

- 실제 하드웨어 없이 Unity 시뮬레이션 환경에서만 동작합니다.
- NEU 데이터셋 특성상 대형 스케일 패널에서 간헐적 오분류가 발생할 수 있습니다.
- 컨베이어 벨트는 AddForce 물리 기반으로 이상적인 벨트와 속도 제어 차이가 있습니다.
- AGV 경로 계획은 격자 기반 충돌 회피로 복잡한 동적 장애물 대응은 제한적입니다.

---

## 🔮 10. Future Work

- 📡 **ROS2 연동** : ROS-TCP Connector를 통한 실제 AGV 제어 연결
- 🧠 **모델 고도화** : 추가 데이터 증강 및 실시간 추론 정확도 개선
- 🗺️ **경로 계획** : A* / DWA 기반 동적 장애물 회피 알고리즘 적용
- 📊 **대시보드 완성** : 공정 KPI 실시간 시각화 (OEE, 처리량, 불량률)
- 🏭 **공장 확장** : 다품종 혼류 생산, 다층 랙 구조 지원

---

## ✅ 11. Summary

**강판 투입 → 컨베이어 이송 → AI 비전 검사 → 3단계 분류 → AGV 군집 운반 → 자동창고 적재 → 지게차 출하**

까지 이어지는 스마트 팩토리 전체 공정을 디지털 트윈으로 구현한 프로젝트입니다.

**NEU 데이터셋 기반 MobileNetV2 학습 → ONNX 변환 → Unity Sentis 실시간 추론**으로 이어지는  
딥러닝 모델의 엣지 배포 파이프라인과,  
**EDF 스케줄링 기반 AGV 군집 제어 + LiDAR 충돌 회피**를 통합한 자율 물류 시스템 구현에 초점을 맞췄습니다.
