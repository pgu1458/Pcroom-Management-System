using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Kiosk_Sum
{
    /// <summary>
    /// 키오스크 전용 TCP 클라이언트 (관리자 서버 9002 포트)
    /// - 연결 시 서버가 즉시 SEATS 데이터를 보내줌
    /// - 사용자 로그인/로그아웃 시 서버가 SEATS 데이터를 다시 보내줌
    /// - LOGIN, CHARGE 요청/응답 처리
    /// - 로그인 세션 정보 보관 (CurrentUserId, CurrentUserName 등)
    /// </summary>
    public class KioskTcpClient
    {
        // ── 싱글톤 ──
        private static KioskTcpClient _instance;
        public static KioskTcpClient Instance
        {
            get { if (_instance == null) _instance = new KioskTcpClient(); return _instance; }
        }
        private KioskTcpClient() { }

        // ── 설정 ──
        public string ServerIp { get; set; } = "192.168.0.6";
        public int ServerPort { get; set; } = 9002;

        // ── 로그인 세션 정보 (Member_Charge 등에서 사용) ──
        public string CurrentUserId { get; set; }
        public string CurrentUserName { get; set; }
        public int CurrentRemainTime { get; set; }
        public int CurrentSeatNumber { get; set; }
        public bool IsLoggedIn { get; private set; }

        // ── 연결 상태 ──
        private TcpClient _client;
        private StreamReader _reader;
        private StreamWriter _writer;
        private CancellationTokenSource _cts;
        private bool _connected;

        // ── 요청-응답 동기화 ──
        private TaskCompletionSource<string> _pendingLoginTcs;
        private TaskCompletionSource<string> _pendingChargeTcs;
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);

        // ── 이벤트 ──
        /// <summary>
        /// SEATS 데이터 수신 시 발생.
        /// Dictionary(좌석번호, SeatInfo) — 사용중 여부 + 남은시간(초)
        /// </summary>
        public event Action<Dictionary<int, SeatInfo>> OnSeatsUpdated;
        /// <summary>로그 메시지</summary>
        public event Action<string> OnLog;

        private void Log(string msg) { OnLog?.Invoke("[KioskTcp " + DateTime.Now.ToString("HH:mm:ss") + "] " + msg); }

        // ═══════════════════════════════════════
        //  연결
        // ═══════════════════════════════════════
        public async Task<bool> ConnectAsync()
        {
            if (_connected) return true;
            try
            {
                _client = new TcpClient();
                await _client.ConnectAsync(ServerIp, ServerPort);
                var ns = _client.GetStream();
                _reader = new StreamReader(ns, Encoding.UTF8);
                _writer = new StreamWriter(ns, Encoding.UTF8) { AutoFlush = true };
                _connected = true;

                // 백그라운드 수신 루프 시작
                _cts = new CancellationTokenSource();
                _ = Task.Run(() => ReaderLoopAsync(_cts.Token));

                Log("9002 연결 성공");
                return true;
            }
            catch (Exception ex)
            {
                Log("9002 연결 실패: " + ex.Message);
                _connected = false;
                return false;
            }
        }

        public void Disconnect()
        {
            _connected = false;
            try { _cts?.Cancel(); } catch { }
            try { _writer?.Dispose(); } catch { }
            try { _reader?.Dispose(); } catch { }
            try { _client?.Close(); } catch { }
            Log("9002 연결 해제");
        }

        // ═══════════════════════════════════════
        //  로그인 세션 관리
        // ═══════════════════════════════════════

        /// <summary>로그인 성공 시 세션 정보 저장 (KioskLogin 등에서 호출)</summary>
        public void SetLoginSession(string userId, string userName, int remainTime, int seatNumber = 0)
        {
            CurrentUserId = userId;
            CurrentUserName = userName;
            CurrentRemainTime = remainTime;
            CurrentSeatNumber = seatNumber;
            IsLoggedIn = true;
        }

        /// <summary>로컬 로그아웃 (서버 통신 없이 세션만 초기화)</summary>
        public void LogoutLocal()
        {
            IsLoggedIn = false;
            CurrentUserId = null;
            CurrentUserName = null;
            CurrentRemainTime = 0;
            CurrentSeatNumber = 0;
        }

        // ═══════════════════════════════════════
        //  백그라운드 수신 루프
        //  서버가 보내는 모든 메시지를 여기서 분류 처리
        // ═══════════════════════════════════════
        private async Task ReaderLoopAsync(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested && _connected)
                {
                    string line = await _reader.ReadLineAsync();
                    if (line == null) break;
                    line = line.Trim();
                    if (line.Length == 0) continue;

                    Log("수신: " + line);

                    // ── SEATS 푸시 ──
                    if (line.StartsWith("SEATS|"))
                    {
                        var seats = ParseSeats(line);
                        OnSeatsUpdated?.Invoke(seats);
                    }
                    // ── LOGIN 응답 ──
                    else if (line.StartsWith("LOGIN_OK|") || line == "LOGIN_FAIL")
                    {
                        _pendingLoginTcs?.TrySetResult(line);
                    }
                    // ── CHARGE 응답 ──
                    else if (line.StartsWith("CHARGE_OK|"))
                    {
                        _pendingChargeTcs?.TrySetResult(line);
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Log("수신 루프 오류: " + ex.Message); }
            finally
            {
                _connected = false;
                Log("수신 루프 종료");
            }
        }

        // ═══════════════════════════════════════
        //  SEATS 파싱
        //  새 포맷: "SEATS|1:1:3600,2:0:0,...,50:0:0"
        //           좌석번호:사용중(1/0):남은시간(초)
        //  하위호환: "SEATS|1:1,2:0,..." (시간 없는 경우도 처리)
        // ═══════════════════════════════════════
        private Dictionary<int, SeatInfo> ParseSeats(string line)
        {
            var dict = new Dictionary<int, SeatInfo>();
            try
            {
                string data = line.Substring("SEATS|".Length);
                string[] pairs = data.Split(',');
                foreach (string pair in pairs)
                {
                    string[] parts = pair.Split(':');
                    if (parts.Length >= 2)
                    {
                        int seatNum;
                        int status;
                        int remainSec = 0;

                        if (int.TryParse(parts[0].Trim(), out seatNum) &&
                            int.TryParse(parts[1].Trim(), out status))
                        {
                            // 3번째 값이 있으면 남은시간
                            if (parts.Length >= 3)
                                int.TryParse(parts[2].Trim(), out remainSec);

                            dict[seatNum] = new SeatInfo(status == 1, remainSec);
                        }
                    }
                }
            }
            catch (Exception ex) { Log("SEATS 파싱 오류: " + ex.Message); }
            return dict;
        }

        // ═══════════════════════════════════════
        //  LOGIN 요청 (키오스크에서 로그인)
        //  성공 시 세션 정보도 자동 저장
        // ═══════════════════════════════════════
        public async Task<KioskLoginResult> LoginAsync(string id, string pw)
        {
            if (!_connected) { bool ok = await ConnectAsync(); if (!ok) return new KioskLoginResult(false, "서버 연결 실패", "", 0); }
            await _sendLock.WaitAsync();
            try
            {
                _pendingLoginTcs = new TaskCompletionSource<string>();
                await _writer.WriteLineAsync("LOGIN|" + id + "|" + pw);
                Log("송신: LOGIN|" + id + "|***");

                var completed = await Task.WhenAny(_pendingLoginTcs.Task, Task.Delay(5000));
                if (completed != _pendingLoginTcs.Task)
                    return new KioskLoginResult(false, "서버 응답 타임아웃", "", 0);

                string res = _pendingLoginTcs.Task.Result;
                if (res.StartsWith("LOGIN_OK|"))
                {
                    string[] p = res.Split('|');
                    if (p.Length >= 3)
                    {
                        string name = p[1];
                        int remain;
                        int.TryParse(p[2], out remain);

                        // ★ 세션 정보 자동 저장
                        SetLoginSession(id, name, remain);

                        return new KioskLoginResult(true, "로그인 성공", name, remain);
                    }
                }
                return new KioskLoginResult(false, "로그인 실패", "", 0);
            }
            catch (Exception ex) { Log("LOGIN 오류: " + ex.Message); return new KioskLoginResult(false, ex.Message, "", 0); }
            finally { _sendLock.Release(); }
        }

        // ═══════════════════════════════════════
        //  CHARGE 요청 (키오스크에서 충전)
        //  성공 시 CurrentRemainTime 자동 갱신
        // ═══════════════════════════════════════
        public async Task<KioskChargeResult> ChargeAsync(string id, int addSeconds)
        {
            if (!_connected) { bool ok = await ConnectAsync(); if (!ok) return new KioskChargeResult(false, "서버 연결 실패", 0); }
            await _sendLock.WaitAsync();
            try
            {
                _pendingChargeTcs = new TaskCompletionSource<string>();
                await _writer.WriteLineAsync("CHARGE|" + id + "|" + addSeconds);
                Log("송신: CHARGE|" + id + "|" + addSeconds);

                var completed = await Task.WhenAny(_pendingChargeTcs.Task, Task.Delay(5000));
                if (completed != _pendingChargeTcs.Task)
                    return new KioskChargeResult(false, "서버 응답 타임아웃", 0);

                string res = _pendingChargeTcs.Task.Result;
                if (res.StartsWith("CHARGE_OK|"))
                {
                    int newRemain;
                    int.TryParse(res.Substring("CHARGE_OK|".Length), out newRemain);

                    // ★ 남은시간 자동 갱신
                    CurrentRemainTime = newRemain;

                    return new KioskChargeResult(true, "충전 성공", newRemain);
                }
                return new KioskChargeResult(false, "충전 실패", 0);
            }
            catch (Exception ex) { Log("CHARGE 오류: " + ex.Message); return new KioskChargeResult(false, ex.Message, 0); }
            finally { _sendLock.Release(); }
        }

        // ═══════════════════════════════════════
        //  연결 상태 확인
        // ═══════════════════════════════════════
        public bool IsConnected => _connected;
    }

    // ── 좌석 정보 구조체 ──
    public class SeatInfo
    {
        public bool Active;
        public int RemainSeconds;
        public SeatInfo(bool active, int remainSec)
        { Active = active; RemainSeconds = remainSec; }
    }

    // ── 결과 구조체 ──
    public class KioskLoginResult
    {
        public bool Success;
        public string Message;
        public string Name;
        public int RemainTime;
        public KioskLoginResult(bool s, string m, string n, int t)
        { Success = s; Message = m; Name = n; RemainTime = t; }
    }

    public class KioskChargeResult
    {
        public bool Success;
        public string Message;
        public int NewRemainTime;
        public KioskChargeResult(bool s, string m, int t)
        { Success = s; Message = m; NewRemainTime = t; }
    }
}