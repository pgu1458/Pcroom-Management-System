
# 🖥️ 피씨방 통합 관리 시스템

> C# WinForms + TCP/IP 기반의 피씨방 통합 관리 솔루션  
> 모블융합2기 5인 팀 프로젝트


https://youtu.be/eAQMOYcSDC8
---

## 프로젝트 소개

관리자 PC, 키오스크, 사용자 PC 세 개의 클라이언트가 TCP 소켓으로 연결되어 실시간으로 동작하는 피씩방 통합 관리 시스템입니다.

단순히 기능을 분리하는 것에 그치지 않고, 세 클라이언트가 실시간으로 상태를 공유하는 것에 초점을 뒀습니다. 키오스크에서 시간을 충전하면 사용자 PC의 카운트다운과 관리자 화면의 남은시간 라벨이 동시에 갱신되고, 사용자 PC에서 음식을 주문하면 관리자 화면에 즉시 주문이 추가되며 재고도 자동으로 차감됩니다. 회원 로그인부터 시간 충전, 음식 주문, 관리자 채팅, 매출 관리까지 피씩방 운영에 필요한 기능 전체를 하나의 시스템으로 통합했습니다.

---

## 시스템 구조

### 키오스크 ↔ 관리자

<img width="800" height="300" alt="KakaoTalk_20260420_185357332" src="https://github.com/user-attachments/assets/80ae9f1d-4eab-465c-819a-1920bc3726a8" />


### 사용자 PC ↔ 관리자

<img width="800" height="300" alt="KakaoTalk_20260420_185357332_11" src="https://github.com/user-attachments/assets/7c61bc70-cd06-4047-90b8-a5c7fdf88f8f" />


---

## 핵심 구현 사항

### TCP 소켓 통신
관리자 PC가 9000 / 9001 / 9002 세 포트를 동시에 리슨하는 멀티포트 서버로 동작합니다. 각 포트는 역할이 명확히 분리되어 있으며, 사용자 PC와 키오스크는 독립적인 `TcpClient` 인스턴스를 통해 필요한 포트에만 접속합니다. 서버 측에서는 클라이언트마다 별도 스레드를 생성해 동시 처리를 지원합니다.


### 체크섬 기반 데이터 무결성 검증
모든 TCP 메시지는 4자리 16진수 체크섬으로 감싸서 전송합니다. 수신 측에서는 메시지 파싱 전에 체크섬을 먼저 검증하며, 불일치 시 해당 패킷을 폐기합니다. 이를 통해 네트워크 전송 중 발생할 수 있는 데이터 손상을 감지합니다.


### 싱글톤 패턴
`TcpServer`, `KioskTcpClient`, `TcpClientService` 모두 싱글톤으로 구현되어 있습니다. 여러 폼이 하나의 TCP 연결을 공유하고, 연결 상태를 일관되게 유지할 수 있습니다.


### 실시간 상태 동기화
사용자 PC는 30초마다 서버에 `TIME_REQ` 메시지를 보내 남은시간을 동기화합니다. 덕분에 키오스크에서 충전이 발생하더라도 최대 30초 이내에 사용자 PC 화면에 반영됩니다. 키오스크 좌석 현황은 서버가 연결 즉시 `SEATS` 메시지를 푸시하는 방식으로 초기 상태를 동기화합니다.


### 폴링 기반 채팅
사용자 PC 채팅은 `CHAT_POLL` 메시지를 1.5초 간격으로 서버에 전송해 답장을 수신하는 폴링 방식으로 구현됐습니다. 별도의 상시 연결 없이도 준실시간 채팅이 가능합니다.


---

## TCP 통신 프로토콜


### 9000 포트 — 사용자 PC ↔ 관리자


| 방향 | 메시지 | 설명 |
|------|--------|------|
| → | `LOGIN\|id\|pw` | 로그인 요청 |
| ← | `LOGIN_OK\|name\|remainSec\|seatNum` | 로그인 성공 (이름, 잔여시간(초), 배정좌석) |
| ← | `LOGIN_FAIL\|reason` | 로그인 실패 및 사유 |
| → | `REGISTER\|name\|id\|pw\|birth\|phone\|손님` | 회원가입 요청 |
| ← | `REGISTER_OK` | 회원가입 성공 |
| ← | `REGISTER_FAIL\|reason` | 회원가입 실패 및 사유 |
| → | `TIME_REQ\|id` | 남은시간 동기화 요청 (30초 주기) |
| ← | `TIME_RES\|seconds` | 서버 기준 남은시간 응답 |
| → | `LOGOUT\|id\|remainSec` | 로그아웃 (현재 남은시간 전달, 서버에서 DB 저장) |
| → | `CHAT\|userId\|message` | 관리자에게 채팅 메시지 전송 |
| ← | `CHAT_OK` | 채팅 수신 확인 응답 |
| → | `CHAT_POLL\|userId` | 관리자 답장 폴링 (1.5초 주기) |
| ← | `CHAT_REPLY\|msg` | 관리자 답장 있음 |
| ← | `CHAT_EMPTY` | 관리자 답장 없음 |


### 9001 포트 — 사용자 PC → 관리자 (음식 주문 전용)


| 방향 | 메시지 | 설명 |
|------|--------|------|
| → | `ORDER\|orderId\|items\|totalPrice` | 음식 주문 전송 |
| ← | `ACK\|orderId\|OK` | 주문 접수 확인 응답 |


### 9002 포트 — 키오스크 ↔ 관리자


| 방향 | 메시지 | 설명 |
|------|--------|------|
| ← | `SEATS\|1:1:3600,2:0:0,...` | 전체 좌석 현황 푸시 (연결 즉시 전송) |
| → | `LOGIN\|id\|pw` | 키오스크 로그인 요청 |
| ← | `LOGIN_OK\|name\|remainSec` | 로그인 성공 (이름, 잔여시간) |
| ← | `LOGIN_FAIL\|reason` | 로그인 실패 |
| → | `CHARGE\|id\|addSeconds` | 시간 충전 요청 |
| ← | `CHARGE_OK\|newRemainSec` | 충전 성공 (갱신된 잔여시간 응답) |


---


## DB 구조


### Member.db — 회원 정보


| 컬럼 | 타입 | 설명 |
|------|------|------|
| number | INTEGER | 고유번호 (PK, Auto Increment) |
| name | TEXT | 이름 |
| id | TEXT | 아이디 (UNIQUE) |
| password | TEXT | 비밀번호 |
| time | INTEGER | 남은 이용시간 (초 단위, 로그아웃 시 저장) |
| birth | TEXT | 생년월일 |
| phone | TEXT | 전화번호 |
| seat_number | INTEGER | 현재 좌석번호 (0이면 미이용) |
| role | TEXT | 권한 구분 (관리자 / 알바 / 손님) |


### Food.db — 메뉴 및 재고


| 컬럼 | 타입 | 설명 |
|------|------|------|
| id | INTEGER | 고유번호 (PK) |
| product_name | TEXT | 메뉴명 |
| price | INTEGER | 가격 (원) |
| arrival | INTEGER | 입고량 |
| inventory | INTEGER | 현재 재고량 (주문 시 자동 차감) |
| sale | INTEGER | 누적 판매량 (주문 시 자동 증가) |
| date | TEXT | 날짜 |
| hour | INTEGER | 시간대 |


### sales.db — 매출


주문 발생 시 날짜 / 시간대 / 금액이 함께 저장됩니다. 관리자 화면의 월별·일별·시간대별 매출 차트는 이 테이블을 집계해서 표시합니다.


---


## 실행 순서


> ⚠️ 관리자 PC가 켜져 있지 않으면 키오스크와 사용자 PC에서 연결 실패 메시지가 출력됩니다.


1. **관리자 PC** 먼저 실행 → TCP 서버 자동 시작 (9000 / 9001 / 9002 포트 리슨)
2. **키오스크** 실행 → 9002 포트 접속, 전체 좌석 현황 자동 수신
3. **사용자 PC** 실행 → 9000 포트 접속, 로그인 후 9001 포트 추가 접속


---


## 개발 환경


| 항목 | 내용 |
|------|------|
| Framework | .NET 6.0 (Windows Forms) |
| IDE | Visual Studio 2022 |
| DB | SQLite (System.Data.SQLite NuGet) |
| 차트 | System.Windows.Forms.DataVisualization |
| 통신 | System.Net.Sockets (TcpListener / TcpClient) |
| 기본 서버 IP | 192.168.0.6 (환경에 맞게 수정 필요) |


---


## 클라이언트별 상세 문서


| 클라이언트 | 설명 |
|-----------|------|
| [관리자 PC](README_관리자.md) | TCP 서버 + 좌석/주문/회원/매출/재고/채팅 관리 |
| [키오스크](README_키오스크.md) | 회원 로그인 + 시간 충전 + 좌석 현황 조회 |
| [사용자 PC](README_사용자PC.md) | 로그인 + 게임 런처 + 음식 주문 + 관리자 채팅 |
