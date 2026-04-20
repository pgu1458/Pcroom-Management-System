# 키오스크 (Kiosk Client)

피씨방 통합 관리 시스템의 **키오스크 클라이언트**입니다.
회원이 직접 로그인하고 이용 시간을 충전하는 무인 단말기 역할을 합니다.

---

## 주요 기능

### 메인 화면 (Form1)

<img width="600" height="400" alt="키오스크로그인" src="https://github.com/user-attachments/assets/7a838165-7a7a-4e55-ba3a-4a378291acde" />





- 충전 버튼 → 로그인 화면으로 전환
- 좌석 현황 버튼 → 전체 50석 실시간 현황 화면으로 전환
- 전체 화면 고정, ESC로 종료



### 좌석 현황 (KioskSeat)


<img width="600" height="400" alt="키오스크 좌석" src="https://github.com/user-attachments/assets/2caccc04-8b5f-47fe-a55a-4cc2081e2b46" />



- 50개 좌석 상태 실시간 표시
  - 사용 중: 파란색 + 남은시간 카운트다운
  - 비어있음: 검은색
- 현재 이용 중인 좌석 수 표시 (예: 12/50석)
- 서버 연결 끊기면 5초마다 자동 재연결
- 로그아웃 버튼 → TCP 연결 해제 후 프로그램 재시작



### 로그인 (KioskLogin)
- 아이디 / 비밀번호 입력 후 9002 포트로 인증 요청
- 엔터 키 로그인 지원
- 로그인 성공 시 Member_Charge 화면으로 자동 전환


### 회원 충전 (Member_Charge)

<img width="600" height="400" alt="키오스크 결제" src="https://github.com/user-attachments/assets/3a1ce59d-8061-4afb-9080-7be9bfaa0df3" />


- 로그인한 회원의 아이디, 이름, 현재 남은시간 표시
- 충전 시간 5가지 선택

| 금액 | 충전 시간 |
|------|-----------|
| 1,000원 | 1시간 |
| 3,000원 | 3시간 30분 |
| 10,000원 | 10시간 |
| 30,000원 | 35시간 |
| 50,000원 | 60시간 |

- 결제 수단 선택 후 결제 폼(Pay) 실행
- 결제 완료 후 서버에 CHARGE 요청 → 충전 성공 시 프로그램 재시작


### 결제 (Pay / Credit / KaKao / Npay)

<img width="600" height="400" alt="키오스크 결제확인" src="https://github.com/user-attachments/assets/f8ff7b49-60fa-42af-b094-508f9286a038" />


- 신용카드, 카카오페이, 네이버페이 지원
- 30초 카운트다운 타이머
  - 15초 이후: 노란색 경고
  - 25초 이후: 빨간색 경고
  - 30초 초과: 자동 취소

---

## 프로젝트 구조

```
Kiosk/
├── Program.cs            # 진입점
├── Form1.cs              # 메인 화면
├── KioskLogin.cs         # 로그인
├── KioskSeat.cs          # 50석 실시간 좌석 현황
├── Member_Charge.cs      # 회원 정보 + 충전 시간 선택
├── Pay.cs                # 결제 수단 선택 폼
├── Credit.cs             # 신용카드 결제 (30초 타이머)
├── KaKao.cs              # 카카오페이 결제 (30초 타이머)
├── Npay.cs               # 네이버페이 결제 (30초 타이머)
└── KioskTcpClient.cs     # TCP 클라이언트 싱글톤 (9002 포트)
```

---

## 서버 IP 설정

`KioskTcpClient.cs`에서 수정합니다.

```csharp
public string ServerIp { get; set; } = "192.168.0.6";
public int ServerPort { get; set; } = 9002;
```
