using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace test
{
    
    public class TcpServer
    {
        private static TcpServer _instance;
        public static TcpServer Instance
        {
            get { if (_instance == null) _instance = new TcpServer(); return _instance; }
        }
        private TcpServer() { }

        // ── TCP 변수 ──
        private TcpListener _listener9000, _listener9001, _listener9002;
        private TcpClient _client9000, _client9001, _client9002;
        private StreamReader _reader9000, _reader9001, _reader9002;
        private StreamWriter _writer9000, _writer9001, _writer9002;
        private CancellationTokenSource _cts = new CancellationTokenSource();
        private int _orderIdCounter = 100;

        private string MemberDbPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Member.db");
        private string FoodDbPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Food.db");
        private string SalesDbPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sales.db");

        // ── 채팅 저장소 ──
        private readonly Dictionary<string, List<ChatMessage>> _chatHistory = new Dictionary<string, List<ChatMessage>>();
        private readonly Dictionary<string, List<string>> _pendingAdminReply = new Dictionary<string, List<string>>();
        private readonly object _chatLock = new object();

        // ── 이벤트 ──
        public event Action<int, string, int, DateTime> OnSeatLogin;
        public event Action<int> OnSeatLogout;
        public event Action<int, string, int> OnOrderReceived;
        public event Action<string> OnLog;
        public event Action<string> OnMemberRegistered;
        public event Action<string, string, string> OnChatReceived; // userId, userName, message
        public event Action<string, int, int> OnChargeCompleted;   // userId, seatNumber, newRemainSec

        public void Start()
        {
            _ = Loop9000Async(_cts.Token);
            _ = Loop9001Async(_cts.Token);
            _ = Loop9002Async(_cts.Token);
        }

        public void Stop()
        {
            _cts.Cancel();
            SafeDispose(_writer9000, _reader9000, _client9000, _listener9000);
            SafeDispose(_writer9001, _reader9001, _client9001, _listener9001);
            SafeDispose(_writer9002, _reader9002, _client9002, _listener9002);
        }

        private void SafeDispose(params IDisposable[] items)
        {
            foreach (var item in items) try { item?.Dispose(); } catch { }
        }
        private void SafeDispose(IDisposable a, IDisposable b, TcpClient c, TcpListener d)
        {
            try { a?.Dispose(); } catch { }
            try { b?.Dispose(); } catch { }
            try { c?.Close(); } catch { }
            try { d?.Stop(); } catch { }
        }

        private void AddLog(string msg) { OnLog?.Invoke(msg); }

        // ═══════════════════════════════════════════════
        //  9000 포트 (재연결 루프)
        //  LOGIN|id|pw → LOGIN_OK|name|time|seat / LOGIN_FAIL|reason
        //  REGISTER|name|id|pw|birth|phone|role → REGISTER_OK / REGISTER_FAIL|reason
        //  TIME_REQ|id → TIME_RES|time
        //  LOGOUT|id → LOGOUT_OK
        //  CHAT|userId|message → CHAT_OK
        //  CHAT_POLL|userId → CHAT_REPLY|msg / CHAT_EMPTY
        // ═══════════════════════════════════════════════
        private async Task Loop9000Async(CancellationToken token)
        {
            _listener9000 = new TcpListener(IPAddress.Any, 9000);
            _listener9000.Start();
            while (!token.IsCancellationRequested)
            {
                try
                {
                    AddLog("[9000] 대기 중...");
                    _client9000 = await _listener9000.AcceptTcpClientAsync();
                    AddLog("[9000] 클라이언트 연결됨");
                    var ns = _client9000.GetStream();
                    _reader9000 = new StreamReader(ns, Encoding.UTF8);
                    _writer9000 = new StreamWriter(ns, Encoding.UTF8) { AutoFlush = true };
                    await Recv9000Async(token);
                    AddLog("[9000] 연결 끊김 → 재대기");
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) { AddLog("[9000 오류] " + ex.Message); await Task.Delay(1000); }
                finally
                {
                    try { _writer9000?.Dispose(); } catch { }
                    try { _reader9000?.Dispose(); } catch { }
                    try { _client9000?.Close(); } catch { }
                }
            }
        }

        private async Task Recv9000Async(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    string raw = await _reader9000.ReadLineAsync();
                    if (raw == null) break;
                    raw = raw.Trim();
                    if (raw.Length == 0) continue;

                    string line = Checksum.Unwrap(raw);
                    if (line == null) { AddLog("[9000] 체크섬 오류: " + raw); continue; }
                    AddLog("[9000 수신] " + line);

                    string resp = null;
                    if (line.StartsWith("LOGIN|")) resp = DoLogin(line);
                    else if (line.StartsWith("REGISTER|")) resp = DoRegister(line);
                    else if (line.StartsWith("TIME_REQ|")) resp = DoTimeReq(line);
                    else if (line.StartsWith("LOGOUT|")) resp = DoLogout(line);
                    else if (line.StartsWith("CHAT|")) resp = DoChat(line);
                    else if (line.StartsWith("CHAT_POLL|")) resp = DoChatPoll(line);

                    if (resp != null)
                    {
                        await _writer9000.WriteLineAsync(Checksum.Wrap(resp));
                        AddLog("[9000 송신] " + resp);
                    }
                }
            }
            catch (Exception ex) { AddLog("[9000 수신 오류] " + ex.Message); }
        }

        // ── LOGIN ── 좌석 자동배정 포함
        private string DoLogin(string line)
        {
            string[] p = line.Split('|');
            if (p.Length != 3) return "LOGIN_FAIL|잘못된 요청";
            string id = p[1].Trim(), pw = p[2].Trim();

            string cs = "Data Source=" + MemberDbPath + ";Version=3;";
            using (var conn = new SQLiteConnection(cs))
            {
                conn.Open();
                string name; int remainTime; int seat;
                using (var cmd = new SQLiteCommand("SELECT name, time, seat_number FROM Member WHERE id=@id AND password=@pw", conn))
                {
                    cmd.Parameters.AddWithValue("@id", id);
                    cmd.Parameters.AddWithValue("@pw", pw);
                    using (var rd = cmd.ExecuteReader())
                    {
                        if (!rd.Read()) return "LOGIN_FAIL|아이디 또는 비밀번호가 틀립니다";
                        name = rd["name"].ToString();
                        remainTime = Convert.ToInt32(rd["time"]);
                        seat = Convert.ToInt32(rd["seat_number"]);
                    }
                }

                // ★ 좌석 자동 배정 (0이면 빈 좌석 찾기)
                if (seat == 0)
                {
                    seat = FindEmptySeat(conn, id);
                    if (seat == 0) return "LOGIN_FAIL|빈 좌석이 없습니다";
                }

                AddLog("[9000] 로그인 성공: " + id + " → " + seat + "번");
                OnSeatLogin?.Invoke(seat, name, remainTime, DateTime.Now);
                _ = SendSeatInfoToKioskAsync();
                return "LOGIN_OK|" + name + "|" + remainTime + "|" + seat;
            }
        }

        private int FindEmptySeat(SQLiteConnection conn, string userId)
        {
            for (int s = 1; s <= 50; s++)
            {
                using (var cmd = new SQLiteCommand("SELECT COUNT(*) FROM Member WHERE seat_number=@s", conn))
                {
                    cmd.Parameters.AddWithValue("@s", s);
                    if (Convert.ToInt32(cmd.ExecuteScalar()) == 0)
                    {
                        using (var upd = new SQLiteCommand("UPDATE Member SET seat_number=@s WHERE id=@id", conn))
                        {
                            upd.Parameters.AddWithValue("@s", s); upd.Parameters.AddWithValue("@id", userId);
                            upd.ExecuteNonQuery();
                        }
                        return s;
                    }
                }
            }
            return 0;
        }

        // ── REGISTER ──
        private string DoRegister(string line)
        {
            string[] p = line.Split('|');
            if (p.Length != 7) return "REGISTER_FAIL|잘못된 요청";
            string cs = "Data Source=" + MemberDbPath + ";Version=3;";
            using (var conn = new SQLiteConnection(cs))
            {
                conn.Open();
                using (var chk = new SQLiteCommand("SELECT COUNT(*) FROM Member WHERE id=@id", conn))
                {
                    chk.Parameters.AddWithValue("@id", p[2]);
                    if (Convert.ToInt32(chk.ExecuteScalar()) > 0) return "REGISTER_FAIL|이미 존재하는 아이디입니다";
                }
                using (var ins = new SQLiteCommand("INSERT INTO Member(name,id,password,birth,phone,role,time,seat_number)VALUES(@n,@i,@p,@b,@ph,@r,0,0)", conn))
                {
                    ins.Parameters.AddWithValue("@n", p[1]); ins.Parameters.AddWithValue("@i", p[2]);
                    ins.Parameters.AddWithValue("@p", p[3]); ins.Parameters.AddWithValue("@b", p[4]);
                    ins.Parameters.AddWithValue("@ph", p[5]); ins.Parameters.AddWithValue("@r", p[6]);
                    ins.ExecuteNonQuery();
                }
                OnMemberRegistered?.Invoke(p[2]);
                return "REGISTER_OK";
            }
        }

        // ── TIME_REQ ──
        private string DoTimeReq(string line)
        {
            string[] p = line.Split('|');
            return p.Length == 2 ? "TIME_RES|" + GetRemainById(p[1].Trim()) : "TIME_RES|0";
        }

        // ── LOGOUT|id|remainSeconds ──
        private string DoLogout(string line)
        {
            string[] p = line.Split('|');
            if (p.Length < 2) return "LOGOUT_OK";
            string id = p[1].Trim();

            // ★ 남은시간 파싱 (out은 실패 시 0으로 덮어쓰므로 별도 bool 체크)
            int remainSeconds = 0;
            bool hasTime = false;
            if (p.Length >= 3)
            {
                hasTime = int.TryParse(p[2].Trim(), out remainSeconds);
            }

            AddLog("[9000 LOGOUT] id=" + id + " hasTime=" + hasTime + " remain=" + remainSeconds);

            string cs = "Data Source=" + MemberDbPath + ";Version=3;";
            using (var conn = new SQLiteConnection(cs))
            {
                conn.Open();

                // 현재 좌석번호 조회
                int seat = 0;
                using (var cmd = new SQLiteCommand("SELECT seat_number FROM Member WHERE id=@id", conn))
                {
                    cmd.Parameters.AddWithValue("@id", id);
                    object r = cmd.ExecuteScalar();
                    if (r != null) int.TryParse(r.ToString(), out seat);
                }

                // ★ 좌석 초기화 + 남은시간 항상 저장
                if (hasTime)
                {
                    using (var cmd = new SQLiteCommand("UPDATE Member SET seat_number=0, time=@time WHERE id=@id", conn))
                    {
                        cmd.Parameters.AddWithValue("@time", remainSeconds);
                        cmd.Parameters.AddWithValue("@id", id);
                        int affected = cmd.ExecuteNonQuery();
                        AddLog("[9000 LOGOUT] DB 업데이트 완료: time=" + remainSeconds + " affected=" + affected);
                    }
                }
                else
                {
                    using (var cmd = new SQLiteCommand("UPDATE Member SET seat_number=0 WHERE id=@id", conn))
                    {
                        cmd.Parameters.AddWithValue("@id", id);
                        cmd.ExecuteNonQuery();
                    }
                    AddLog("[9000 LOGOUT] 좌석만 초기화 (시간 데이터 없음)");
                }

                if (seat > 0) { OnSeatLogout?.Invoke(seat); _ = SendSeatInfoToKioskAsync(); }
            }
            return "LOGOUT_OK";
        }

        // ── CHAT (이용자→관리자) ──
        private string DoChat(string line)
        {
            string[] p = line.Split(new char[] { '|' }, 3);
            if (p.Length != 3) return "CHAT_OK";
            string userId = p[1].Trim(), message = p[2];
            string userName = GetNameById(userId);
            lock (_chatLock)
            {
                if (!_chatHistory.ContainsKey(userId)) _chatHistory[userId] = new List<ChatMessage>();
                _chatHistory[userId].Add(new ChatMessage("user", message, DateTime.Now));
            }
            OnChatReceived?.Invoke(userId, userName, message);
            return "CHAT_OK";
        }

        // ── CHAT_POLL (이용자가 관리자 답장 확인) ──
        private string DoChatPoll(string line)
        {
            string[] p = line.Split('|');
            if (p.Length != 2) return "CHAT_EMPTY";
            string userId = p[1].Trim();
            lock (_chatLock)
            {
                if (_pendingAdminReply.ContainsKey(userId) && _pendingAdminReply[userId].Count > 0)
                {
                    string all = string.Join("\n", _pendingAdminReply[userId]);
                    _pendingAdminReply[userId].Clear();
                    return "CHAT_REPLY|" + all;
                }
            }
            return "CHAT_EMPTY";
        }

        // ── 관리자→이용자 채팅 (CounterAl에서 호출) ──
        public void SendChatToUser(string userId, string message)
        {
            lock (_chatLock)
            {
                if (!_chatHistory.ContainsKey(userId)) _chatHistory[userId] = new List<ChatMessage>();
                _chatHistory[userId].Add(new ChatMessage("admin", message, DateTime.Now));
                if (!_pendingAdminReply.ContainsKey(userId)) _pendingAdminReply[userId] = new List<string>();
                _pendingAdminReply[userId].Add(message);
            }
        }

        public List<ChatMessage> GetChatHistory(string userId)
        {
            lock (_chatLock)
            {
                if (_chatHistory.ContainsKey(userId)) return new List<ChatMessage>(_chatHistory[userId]);
                return new List<ChatMessage>();
            }
        }

        // ═══════════════════════════════════════════════
        //  9001 포트 (주문 - 재연결)
        //  수신: ORDER|주문번호|내역|총액 (체크섬)
        //  송신: ACK|주문번호|OK (체크섬)
        // ═══════════════════════════════════════════════
        private async Task Loop9001Async(CancellationToken token)
        {
            _listener9001 = new TcpListener(IPAddress.Any, 9001);
            _listener9001.Start();
            while (!token.IsCancellationRequested)
            {
                try
                {
                    AddLog("[9001] 대기 중...");
                    _client9001 = await _listener9001.AcceptTcpClientAsync();
                    AddLog("[9001] 클라이언트 연결됨");
                    var ns = _client9001.GetStream();
                    _reader9001 = new StreamReader(ns, Encoding.UTF8);
                    _writer9001 = new StreamWriter(ns, Encoding.UTF8) { AutoFlush = true };
                    await Recv9001Async(token);
                    AddLog("[9001] 연결 끊김 → 재대기");
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) { AddLog("[9001 오류] " + ex.Message); await Task.Delay(1000); }
                finally
                {
                    try { _writer9001?.Dispose(); } catch { }
                    try { _reader9001?.Dispose(); } catch { }
                    try { _client9001?.Close(); } catch { }
                }
            }
        }

        private async Task Recv9001Async(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                string raw = await _reader9001.ReadLineAsync();
                if (raw == null) break;
                raw = raw.Trim(); if (raw.Length == 0) continue;

                string line = Checksum.Unwrap(raw);
                if (line == null) { AddLog("[9001] 체크섬 오류"); continue; }

                if (!line.StartsWith("ORDER|")) continue;
                string[] parts = line.Split('|');
                if (parts.Length != 4) continue;

                int orderId; if (!int.TryParse(parts[1], out orderId)) continue;
                string items = parts[2];
                int total; if (!int.TryParse(parts[3], out total)) continue;

                // ★ Menu.cs가 기대하는 ACK 형식 그대로 전송
                await _writer9001.WriteLineAsync(Checksum.Wrap("ACK|" + orderId + "|OK"));

                // ★ Food.db 판매량/재고량/시간 업데이트
                UpdateFoodOnOrder(items);

                OnOrderReceived?.Invoke(_orderIdCounter++, items, total);
                AddLog("[9001] 주문: " + items + " / " + total + "원");
            }
        }

        // ★ 주문 시 Food.db 업데이트 (판매량↑ 재고↓ 시간기록 월일시)
        private void UpdateFoodOnOrder(string itemsStr)
        {
            try
            {
                string[] pairs = itemsStr.Split(',');
                string cs = "Data Source=" + FoodDbPath + ";Version=3;";
                DateTime now = DateTime.Now;
                string dateStr = now.ToString("MM-dd");
                int hour = now.Hour;

                using (var conn = new SQLiteConnection(cs))
                {
                    conn.Open();
                    foreach (string pair in pairs)
                    {
                        string[] kv = pair.Split(':');
                        if (kv.Length != 2) continue;
                        string productName = kv[0].Trim();
                        int qty; if (!int.TryParse(kv[1].Trim(), out qty)) continue;

                        int foodId = -1, curSale = 0, curInv = 0, price = 0;
                        using (var cmd = new SQLiteCommand("SELECT id, sale, inventory, price FROM Food WHERE product_name=@n LIMIT 1", conn))
                        {
                            cmd.Parameters.AddWithValue("@n", productName);
                            using (var r = cmd.ExecuteReader())
                            {
                                if (!r.Read()) continue;
                                foodId = Convert.ToInt32(r["id"]);
                                curSale = Convert.ToInt32(r["sale"]);
                                curInv = Convert.ToInt32(r["inventory"]);
                                price = Convert.ToInt32(r["price"]);
                            }
                        }
                        using (var cmd = new SQLiteCommand("UPDATE Food SET sale=@s, inventory=@i, date=@d, hour=@h WHERE id=@id", conn))
                        {
                            cmd.Parameters.AddWithValue("@s", curSale + qty);
                            cmd.Parameters.AddWithValue("@i", Math.Max(0, curInv - qty));
                            cmd.Parameters.AddWithValue("@d", dateStr);
                            cmd.Parameters.AddWithValue("@h", hour);
                            cmd.Parameters.AddWithValue("@id", foodId);
                            cmd.ExecuteNonQuery();
                        }

                        // ★ sales.db에도 판매 기록 삽입 (차트용)
                        try
                        {
                            string salesCs = "Data Source=" + SalesDbPath + ";Version=3;";
                            using (var sConn = new SQLiteConnection(salesCs))
                            {
                                sConn.Open();
                                using (var sCmd = new SQLiteCommand("INSERT INTO sales_data(Product_name, sale, price, date, hour) VALUES(@n,@s,@p,@d,@h)", sConn))
                                {
                                    sCmd.Parameters.AddWithValue("@n", productName);
                                    sCmd.Parameters.AddWithValue("@s", qty);
                                    sCmd.Parameters.AddWithValue("@p", price);
                                    sCmd.Parameters.AddWithValue("@d", dateStr);
                                    sCmd.Parameters.AddWithValue("@h", hour);
                                    sCmd.ExecuteNonQuery();
                                }
                            }
                        }
                        catch (Exception sex) { AddLog("[Sales 오류] " + sex.Message); }

                        AddLog("[Food] " + productName + " 판매+" + qty + " 재고:" + Math.Max(0, curInv - qty) + " @" + dateStr + " " + hour + "시");
                    }
                }
            }
            catch (Exception ex) { AddLog("[Food 오류] " + ex.Message); }
        }

        // ═══════════════════════════════════════════════
        //  9002 포트 (키오스크 - 재연결)
        // ═══════════════════════════════════════════════
        private async Task Loop9002Async(CancellationToken token)
        {
            _listener9002 = new TcpListener(IPAddress.Any, 9002);
            _listener9002.Start();
            while (!token.IsCancellationRequested)
            {
                try
                {
                    AddLog("[9002] 대기 중...");
                    _client9002 = await _listener9002.AcceptTcpClientAsync();
                    AddLog("[9002] 클라이언트 연결됨");
                    var ns = _client9002.GetStream();
                    _reader9002 = new StreamReader(ns, Encoding.UTF8);
                    _writer9002 = new StreamWriter(ns, Encoding.UTF8) { AutoFlush = true };
                    await SendSeatInfoToKioskAsync();
                    await Recv9002Async(token);
                    AddLog("[9002] 연결 끊김 → 재대기");
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) { AddLog("[9002 오류] " + ex.Message); await Task.Delay(1000); }
                finally
                {
                    try { _writer9002?.Dispose(); } catch { }
                    try { _reader9002?.Dispose(); } catch { }
                    try { _client9002?.Close(); } catch { }
                }
            }
        }

        private async Task Recv9002Async(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                string line = await _reader9002.ReadLineAsync();
                if (line == null) break;
                line = line.Trim(); if (line.Length == 0) continue;

                if (line.StartsWith("LOGIN|"))
                {
                    string[] p = line.Split('|');
                    if (p.Length != 3) continue;
                    if (ValidateUser(p[1].Trim(), p[2].Trim()))
                    {
                        string name = GetNameById(p[1].Trim());
                        int remain = GetRemainById(p[1].Trim());
                        await _writer9002.WriteLineAsync("LOGIN_OK|" + name + "|" + remain);
                    }
                    else await _writer9002.WriteLineAsync("LOGIN_FAIL");
                }
                else if (line.StartsWith("CHARGE|"))
                {
                    string[] p = line.Split('|');
                    if (p.Length != 3) continue;
                    string chargeId = p[1].Trim();
                    int add; if (!int.TryParse(p[2].Trim(), out add)) continue;

                    // DB에서 현재 남은시간 + 충전시간 = 새 남은시간
                    int newR = GetRemainById(chargeId) + add;
                    UpdateRemainById(chargeId, newR);

                    // 응답 전송
                    await _writer9002.WriteLineAsync("CHARGE_OK|" + newR);
                    AddLog("[9002 CHARGE] id=" + chargeId + " add=" + add + " newRemain=" + newR);

                    // ★ 이용 중인지 판별 (seat_number > 0이면 이용 중)
                    int seatNum = GetSeatById(chargeId);
                    if (seatNum > 0)
                    {
                        // 이용 중 → 관리자 좌석표 remain 라벨 + DB 동시 업데이트
                        AddLog("[9002 CHARGE] 이용중 좌석=" + seatNum + " → remain 업데이트");
                        OnChargeCompleted?.Invoke(chargeId, seatNum, newR);
                    }
                    else
                    {
                        // 이용 중 아님 → DB만 업데이트 완료 (위에서 이미 처리됨)
                        AddLog("[9002 CHARGE] 미이용 회원 → DB만 업데이트");
                    }

                    await SendSeatInfoToKioskAsync();
                }
            }
        }

        private async Task SendSeatInfoToKioskAsync()
        {
            if (_writer9002 == null) return;
            try
            {
                var sb = new StringBuilder("SEATS|");
                string cs = "Data Source=" + MemberDbPath + ";Version=3;";
                using (var conn = new SQLiteConnection(cs))
                {
                    conn.Open();
                    for (int i = 1; i <= 50; i++)
                    {
                        int active = 0;
                        int remainSec = 0;
                        using (var cmd = new SQLiteCommand("SELECT time FROM Member WHERE seat_number=@s", conn))
                        {
                            cmd.Parameters.AddWithValue("@s", i);
                            object r = cmd.ExecuteScalar();
                            if (r != null)
                            {
                                active = 1;
                                int.TryParse(r.ToString(), out remainSec);
                            }
                        }
                        sb.Append(i + ":" + active + ":" + remainSec);
                        if (i < 50) sb.Append(",");
                    }
                }
                await _writer9002.WriteLineAsync(sb.ToString());
            }
            catch (Exception ex) { AddLog("[9002 좌석전송 오류] " + ex.Message); }
        }

        // ── DB 헬퍼 ──
        private bool ValidateUser(string id, string pw)
        {
            string cs = "Data Source=" + MemberDbPath + ";Version=3;";
            using (var conn = new SQLiteConnection(cs))
            { conn.Open(); using (var cmd = new SQLiteCommand("SELECT COUNT(*) FROM Member WHERE id=@id AND password=@pw", conn)) { cmd.Parameters.AddWithValue("@id", id); cmd.Parameters.AddWithValue("@pw", pw); return Convert.ToInt32(cmd.ExecuteScalar()) > 0; } }
        }

        private string GetNameById(string id)
        {
            string cs = "Data Source=" + MemberDbPath + ";Version=3;";
            using (var conn = new SQLiteConnection(cs))
            { conn.Open(); using (var cmd = new SQLiteCommand("SELECT name FROM Member WHERE id=@id", conn)) { cmd.Parameters.AddWithValue("@id", id); object r = cmd.ExecuteScalar(); return r != null ? r.ToString() : ""; } }
        }

        private int GetRemainById(string id)
        {
            string cs = "Data Source=" + MemberDbPath + ";Version=3;";
            using (var conn = new SQLiteConnection(cs))
            { conn.Open(); using (var cmd = new SQLiteCommand("SELECT time FROM Member WHERE id=@id", conn)) { cmd.Parameters.AddWithValue("@id", id); object r = cmd.ExecuteScalar(); int v; return (r != null && int.TryParse(r.ToString(), out v)) ? v : 0; } }
        }

        private void UpdateRemainById(string id, int sec)
        {
            string cs = "Data Source=" + MemberDbPath + ";Version=3;";
            using (var conn = new SQLiteConnection(cs))
            { conn.Open(); using (var cmd = new SQLiteCommand("UPDATE Member SET time=@t WHERE id=@id", conn)) { cmd.Parameters.AddWithValue("@t", sec); cmd.Parameters.AddWithValue("@id", id); cmd.ExecuteNonQuery(); } }
        }

        /// <summary>회원 아이디로 현재 좌석번호 조회 (0이면 미이용)</summary>
        private int GetSeatById(string id)
        {
            string cs = "Data Source=" + MemberDbPath + ";Version=3;";
            using (var conn = new SQLiteConnection(cs))
            {
                conn.Open();
                using (var cmd = new SQLiteCommand("SELECT seat_number FROM Member WHERE id=@id", conn))
                {
                    cmd.Parameters.AddWithValue("@id", id);
                    object r = cmd.ExecuteScalar();
                    int v;
                    return (r != null && int.TryParse(r.ToString(), out v)) ? v : 0;
                }
            }
        }
    }

    // ── 채팅 메시지 구조 ──
    public class ChatMessage
    {
        public string Sender;   // "user" 또는 "admin"
        public string Message;
        public DateTime Time;
        public ChatMessage(string s, string m, DateTime t) { Sender = s; Message = m; Time = t; }
    }
}