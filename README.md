# 🏭 Digital Twin : 공장 자동화 시뮬레이션

> Unity 기반의 **디지털 트윈 공장 자동화 시뮬레이션**입니다.
> 컨베이어 벨트에서 투입된 부품을 색상 인식 센서로 양품/불량품을 판별하고,
> 두 대의 모바일 매니퓰레이터(MM1, MM2)가 **EDF 스케줄링** 기반으로 자동 분류·운반하는 시스템입니다.

---

## 📌 1. Project Overview

기존 공장 자동화 시스템은 실제 하드웨어 없이는 테스트와 검증이 어렵습니다.
이 프로젝트는 Unity를 활용해 **실제 공장 환경을 3D로 재현**하고,
센서·로봇·컨베이어 벨트를 오브젝트로 구현하여 **소프트웨어만으로 자동화 파이프라인을 시뮬레이션**합니다.

### ✨ 주요 기능

- 🟦 색상 인식 센서로 양품/불량품 자동 판별
- 🤖 MM1: 컨베이어에서 부품을 집어 검수 테이블 또는 폐기함으로 분류
- 🤖 MM2: 검수 테이블의 양품을 최종 출고함으로 운반
- 📋 EDF(Earliest Deadline First) 스케줄링으로 작업 우선순위 동적 제어
- 🔁 다중 컨베이어(1~3번) 동시 운영 지원

---

## ❓ 2. Why This Project?

세그멘테이션 기반 장면인식 연구를 ROS2 로봇에 탑재하는 메인 프로젝트와 함께,
**디지털 트윈 환경 구축 역량**을 보완하기 위해 진행한 프로젝트입니다.

- 3D 모델링 학습
- ROS2 로봇 제어 학습
- Unity 디지털 트윈 강의

세 가지 학습의 결과물을 통합하여, 세그멘테이션 연구를 **실제 자동화 파이프라인으로 확장**하는 것을 목표로 했습니다.

---

## 🎬 3. Scenario

```text
[컨베이어 1 / 2 / 3]
        ↓ 부품 투입
[Sensor1 / Sensor2 / Sensor3]
  색상 인식으로 양품(파란색) / 불량품(빨간색) 판별
        ↓
     [MM1 출동]
        ├─ 양품 → 검수 테이블로 운반
        └─ 불량품 → 폐기함으로 운반
              ↓ (양품일 때만)
         [Sensor4 감지]
              ↓
           [MM2 출동]
         검수 테이블 → 최종 출고함으로 운반
              ↓
         [ObjectRemover]
          부품 수거 완료
```

---

## 🔄 4. System Pipeline

### MM1 Pipeline

```text
Sensor1~3 충돌 감지
        ↓
색상 판별 (양품 / 불량품)
        ↓
EDF 스케줄링으로 작업 순서 결정
        ↓
MM1 해당 컨베이어로 이동
        ↓
그리퍼로 부품 피킹
        ↓
양품 → 검수 테이블
불량품 → 폐기함
        ↓
부품 내려놓기 → 시작 위치 복귀
```

### MM2 Pipeline

```text
Sensor4 (검수 테이블 위) 감지
        ↓
MM2 검수 테이블로 이동
        ↓
그리퍼로 부품 피킹
        ↓
최종 출고함으로 이동
        ↓
부품 내려놓기 → 시작 위치 복귀
```

---

## 🛠️ 5. Tech Stack

| 분류 | 기술 |
|------|------|
| 시뮬레이션 엔진 | Unity 6 (6000.0.4f1) |
| 개발 언어 | C# |
| 물리 엔진 | Unity Physics (Rigidbody, Collider) |
| 스케줄링 알고리즘 | EDF (Earliest Deadline First) |
| 충돌 감지 방식 | OnCollisionEnter (Sensor1~3), OnTriggerEnter (Sensor4~6) |

---

## 🗂️ 6. Project Structure

```bash
Assets/
└── Scripts/
    ├── MainControl.cs       # 관제 시스템 (EDF 스케줄링, MM1/MM2 제어)
    ├── MM1Moving.cs         # MM1 이동 및 피킹/내려놓기 시퀀스
    ├── MM2Moving.cs         # MM2 이동 및 피킹/내려놓기 시퀀스
    ├── CollisionSensor.cs   # Sensor1~3 충돌 감지 및 색상 판별
    ├── ObjectPicking.cs     # 그리퍼 부품 집기
    ├── ObjectPlace.cs       # Sensor4~6 부품 도착 감지
    └── ObjectRemover.cs     # 부품 수거 완료 처리
```

### 📄 Script Description

- **MainControl.cs**
  → EDF 스케줄링으로 MM1 작업 순서 결정, MM2 자동 출발 트리거 관리

- **MM1Moving.cs**
  → 컨베이어 이동 → 피킹 → 분류 위치 이동 → 내려놓기 → 복귀 시퀀스

- **MM2Moving.cs**
  → 검수 테이블 이동 → 피킹 → 최종 출고함 이동 → 내려놓기 → 복귀 시퀀스

- **CollisionSensor.cs**
  → Sensor1~3에 부착, 부품 충돌 감지 및 색상으로 양품/불량품 판별

- **ObjectPicking.cs**
  → 그리퍼 Trigger 진입 시 부품을 자식으로 설정하여 함께 이동

- **ObjectPlace.cs**
  → Sensor4~6 Trigger 진입 시 isPlace 플래그 설정

- **ObjectRemover.cs**
  → 부품 도착 2초 후 비활성화 처리

---

## ⚠️ 7. Limitations

- 실제 하드웨어(로봇 팔, 컨베이어) 없이 Unity 시뮬레이션 환경에서만 동작합니다.
- 색상 판별은 Material의 RGB 값 기반으로, 조명 환경에 따라 오판 가능성이 있습니다.
- 현재 단일 부품(TestPart) 기준으로 구현되어 있으며, 다품종 동시 처리는 추가 구현이 필요합니다.
- 로봇 이동 경로가 직선 기반이라 장애물 회피 기능은 포함되어 있지 않습니다.

---

## 🔮 8. Future Work

- 📡 ROS2와 Unity 디지털 트윈 연동 (ROS-TCP Connector)
- 🧠 세그멘테이션 모델을 활용한 부품 불량 판별 고도화
- 🗺️ 로봇 경로 계획(Path Planning) 알고리즘 적용
- 📊 공정 현황 실시간 모니터링 대시보드 구현
- 🔢 다품종 부품 동시 처리 지원

---

## ✅ 9. Summary

이 프로젝트는 단순 Unity 학습을 넘어,

**센서 감지 → 양품/불량 판별 → EDF 스케줄링 → 로봇 피킹 → 자동 분류·운반**

까지 이어지는 **공장 자동화 파이프라인**을 디지털 트윈으로 구현한 프로젝트입니다.

특히 3D 모델링·ROS2 로봇 제어·Unity 디지털 트윈 학습을 결합하여,
세그멘테이션 기반 장면인식 연구를 실제 자동화 환경으로 확장하는 데 의의가 있습니다.
