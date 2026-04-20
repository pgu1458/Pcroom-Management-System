# 피씨방 통합 관리 시스템

C# WinForms 기반의 TCP/IP 피씨방 통합 관리 시스템입니다.
관리자 PC, 키오스크, 사용자 PC 세 개의 클라이언트가 TCP 소켓으로 연결되어 실시간으로 동작합니다.
5인 팀 프로젝트입니다.

---

## 시스템 구성

```
┌─────────────────────────────────────────────┐
│              관리자 PC (서버)               │
│  9000 포트 ← 사용자 PC (로그인/주문/채팅)    │
│  9001 포트 ← 사용자 PC (음식 주문)           │
│  9002 포트 ← 키오스크 (충전/좌석현황)        │
└─────────────────────────────────────────────┘
```

모든 메시지는 체크섬(4자리 16진수)으로 감싸서 전송하여 데이터 손상을 감지합니다.

------------------------------------------------------------------------------

## TCP 통신 프로토콜

### 9000 포트 (사용자 PC ↔ 관리자)

| 방향 | 메시지 | 설명 |
|------|--------|------|
| → | `LOGIN\|id\|pw` | 로그인 |
| ← | `LOGIN_OK\|name\|remainSec\|seatNum` | 로그인 성공 |
| ← | `LOGIN_FAIL\|reason` | 로그인 실패 |
| → | `REGISTER\|name\|id\|pw\|birth\|phone\|손님` | 회원가입 |
| ← | `REGISTER_OK` / `REGISTER_FAIL\|reason` | 회원가입 결과 |
| → | `TIME_REQ\|id` | 남은시간 요청 |
| ← | `TIME_RES\|seconds` | 남은시간 응답 |
| → | `LOGOUT\|id\|remainSec` | 로그아웃 (남은시간 저장) |
| → | `CHAT\|userId\|message` | 채팅 전송 |
| ← | `CHAT_OK` | 채팅 수신 확인 |
| → | `CHAT_POLL\|userId` | 관리자 답장 확인 |
| ← | `CHAT_REPLY\|msg` / `CHAT_EMPTY` | 답장 유무 |

### 9001 포트 (사용자 PC → 관리자)

| 방향 | 메시지 | 설명 |
|------|--------|------|
| → | `ORDER\|orderId\|items\|totalPrice` | 음식 주문 |
| ← | `ACK\|orderId\|OK` | 주문 접수 확인 |

### 9002 포트 (키오스크 ↔ 관리자)

| 방향 | 메시지 | 설명 |
|------|--------|------|
| ← | `SEATS\|1:1:3600,2:0:0,...` | 좌석 현황 푸시 |
| → | `LOGIN\|id\|pw` | 키오스크 로그인 |
| ← | `LOGIN_OK\|name\|remainSec` | 로그인 성공 |
| → | `CHARGE\|id\|addSeconds` | 충전 요청 |
| ← | `CHARGE_OK\|newRemainSec` | 충전 성공 |

---

## DB 구조

### Member.db

| 컬럼 | 설명 |
|------|------|
| number | 고유번호 |
| name | 이름 |
| id | 아이디 |
| password | 비밀번호 |
| time | 남은 이용시간 (초) |
| birth | 생년월일 |
| phone | 전화번호 |
| seat_number | 현재 좌석번호 (0이면 미이용) |
| role | 관리자 / 알바 / 손님 |

### Food.db

| 컬럼 | 설명 |
|------|------|
| id | 고유번호 |
| product_name | 메뉴명 |
| price | 가격 |
| arrival | 입고량 |
| inventory | 재고량 |
| sale | 판매량 |
| date | 날짜 |
| hour | 시간대 |

### sales.db

매출 데이터 저장. 월별/일별/시간대별 차트 조회에 사용됩니다.

---

## 프로젝트별 상세 내용

각 파트의 상세 README는 아래를 참고하세요.

- [관리자 PC README](README_관리자.md)
- [키오스크 README](README_키오스크.md)
- [사용자 PC README](README_사용자PC.md)

---

## 실행 순서

1. **관리자 PC** 먼저 실행 (TCP 서버 자동 시작)
2. **키오스크** 실행
3. **사용자 PC** 실행

관리자 PC가 켜져 있지 않으면 키오스크와 사용자 PC에서 연결 실패 메시지가 출력됩니다.

---

## 개발 환경

- .NET 6.0 (Windows Forms)
- Visual Studio 2022
- SQLite (System.Data.SQLite)
- 서버 IP: 192.168.0.6 (환경에 맞게 수정 필요)

---

남서울대학교 전자공학과 팀 프로젝트 (5인)
