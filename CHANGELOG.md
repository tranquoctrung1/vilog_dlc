# Nhật ký thay đổi (Changelog)

Toàn bộ thay đổi đáng chú ý giữa VilogLDC v1.2 và v1.3.

Phạm vi diff: chỉ `SerialCom/Form1.vb` và `SerialCom/Form1.Designer.vb` thay đổi. Các file khác của project (`.vbproj`, `Form1.resx`, file solution, v.v.) giống hệt giữa 2 bản.

## [1.3] - 2026-07-09

### Sửa lỗi
- **Treo hàng đợi gửi lệnh (deadlock)** — không có timeout khi chờ ack từ thiết bị, có thể treo vĩnh viễn nếu thiết bị không phản hồi. Thêm timeout 15s (`WaitTimeoutMs`), cũng tự gỡ khi ghi lỗi (write exception).
- **Nhận diện ack sai/thiếu** — trước đây khớp ký tự `"K"` đơn lẻ (dễ khớp nhầm giữa từ, vd `CLOCKLOG`) và không xử lý được phản hồi thiết bị bị chia nhỏ qua nhiều sự kiện `DataReceived` (`"O"` + `"K"`). Nay khớp đầy đủ `"OK"`/`"ERROR"` trên buffer nhận tích lũy, được xóa sau mỗi lệnh.
- **Tranh chấp đa luồng (cross-thread race)** — cờ `isWaiting`/`sending` và lệnh ghi `SerialPort.Write` trước đây bị truy cập từ cả UI thread và `SendLoop` chạy nền mà không đồng bộ. Đã thêm `stateLock`/`serialLock` và các property thread-safe `IsWaitingFlag`/`SendingFlag`; mọi lệnh ghi cổng serial nay bọc trong `SyncLock serialLock`.
- **Crash khi parse dòng dữ liệu thiết bị lỗi** — các dòng đọc về `PUBTOPIC`/`TDC`/`CLOCKLOG` nếu sai định dạng/thiếu trường sẽ ném exception chưa xử lý làm crash app. Đã bọc `Try/Catch`, dòng lỗi sẽ bị bỏ qua thay vì làm sập app.
- **Crash khi lấy địa chỉ MAC (NIC index)** — `nics(1)` ném lỗi trên máy chỉ có 0 hoặc 1 card mạng. Nay tự chuyển sang `nics(0)` hoặc bỏ qua kiểm tra MAC nếu không có card nào.
- **Cờ `isConnecting` bị kẹt / cổng serial không đóng khi thoát app** — khi `SerialPort1.Open()` thất bại, cờ `isConnecting` không được reset, khiến byte "OK" đến sau có thể bị hiểu nhầm là kết nối thành công. Cổng serial cũng chưa từng được đóng khi thoát app. Đã sửa đường reset và thêm `Form1_FormClosing` để đóng cả 3 cổng và dừng `SendLoop`.
- **Lỗi cộng giờ tràn (hour+1 rollover)** — `Hex(TS.Hour + 1)` tạo ra byte hex không hợp lệ (`"18"`) khi giờ chuyển từ 23 sang 24. Đã sửa bằng `Mod 24`.
- **Parse dòng chỉ giả định `vbNewLine`** — thiết bị gửi xuống dòng bằng `vbLf` đơn thuần làm hỏng việc parse tham số. Nay chuẩn hóa `vbCrLf`/`vbCr` về `vbLf` trước khi tách dòng.
- **Mã hóa năm dễ vỡ** — trước đây dựa vào `Hex(Year)` luôn đúng 3 ký tự hex (cắt bằng `Mid`). Đã thay bằng mã hóa hi/lo 2-byte có đệm 0, chắc chắn hơn.
- **Đơ giao diện (UI freeze) khi kết nối/cấu hình/đọc** — các lệnh `Thread.Sleep(1000/5000)` chạy trên UI thread làm đơ toàn bộ app trong lúc bắt tay kết nối và gửi lệnh cấu hình/đọc. Đã chuyển bắt tay kết nối sang `Task.Run`; bỏ các lệnh sleep chặn sau khi gửi lệnh (fire-and-forget), xử lý phản hồi nay là bất đồng bộ qua `DataReceived`.
- **Lỗi chu kỳ gửi 24h** — `Case "24h"` trước đây tính nhầm thành `60 * 12` (trùng với case 12h). Đã sửa thành `60 * 24`.

### Thêm mới
- **Tự động kết nối lại (auto-reconnect)** khi thiết bị trả về `"Password Incorrect"` — âm thầm dọn dẹp (xóa hàng đợi gửi, join thread gửi, đóng cổng) và tự kết nối lại thay vì hiện hộp thoại `MessageBox` chặn thao tác. Có cờ `isAutoReconnecting` để tránh chạy chồng nhiều chu trình kết nối lại cùng lúc.
- **Bộ lọc phụ thuộc sendTime/logTime** — thêm các hàm `RebuildLogTimeItems`, `SendTimeToMinutes`, `LogTimeToMinutes` để lọc động danh sách logTime (ComboBox4) theo sendTime đã chọn (ComboBox3), đảm bảo chỉ những tổ hợp thỏa giới hạn ~14 mẫu đệm của thiết bị (`sendTime / logTime ≤ 14`) mới được chọn. Gắn vào sự kiện `SelectedIndexChanged` của `ComboBox3`/`ComboBox5`.
- **Thêm tùy chọn sendTime mới** — `"1m"` và `"5m"` (trước đây nhỏ nhất là 10m).
- **Thêm tùy chọn logTime mới** — `"5m"` và `"10m"` với mã hex tương ứng (`00 05`, `00 0A`).
- **Kiểm tra trước khi cấu hình (Config() pre-check)** — `BtnConfigure_Click` nay kiểm tra đã điền đủ ID / Tên trạm / Loại đồng hồ / Chu kỳ gửi / Chu kỳ ghi trước khi gửi cấu hình; hiện hộp thoại cảnh báo tiếng Việt liệt kê trường còn thiếu nếu chưa đủ.
- **Timeout cổng serial** — thêm `WriteTimeout`/`ReadTimeout` = 2000ms cho cả SerialPort1 và SerialPort2, tránh treo vô hạn khi cổng bị đứng.
- **Hiện lỗi ra màn hình** — các lỗi trước đây bị nuốt âm thầm (lỗi mở cổng, lỗi ghi, lỗi gửi lệnh debug, lỗi lưu file) nay được hiện ra ô log (RichTextBox2).
- Các thông báo trạng thái (Connected / Loading parameter / Vilog is Stopped / Load parameters completed) nay **nối thêm** vào log thay vì ghi đè.

### Thay đổi
- **Địa chỉ server** — endpoint `AT+SERVADDR` chuyển từ `157.66.81.22:1883` sang `43.216.183.205:1883` (giá trị cũ giữ lại dạng comment, không xóa).
- **Log phản hồi thô** — mọi đoạn phản hồi từ thiết bị nay được ghi vào RichTextBox2, không chỉ khi đang `isConfiguring`.
- **Đổi công dụng nút Button15** — trước là "Sync Time" (`AT+TIMESTAMP=...`), nay là "Send Data" (`AT+GETSENSORVALUE=1`). Nhãn trong Designer cũng đã cập nhật.
- Thông báo lỗi "Lỗi gửi" đổi sang tiếng Anh "Send error".

### Đổi tên (không đổi hành vi)
- `ReceivedText1/2/3` → `ProcessLoggerResponse` / `ProcessDebugResponse` / `ProcessMonitorResponse`
- `SerialPort1/2/3_DataReceived` → `SerialPortLogger/Debug/Monitor_DataReceived`
- Các hàm xử lý click nút đổi từ tên chung chung (`Button1_Click_1`, `Button9_Click`, ...) sang tên mô tả rõ nghĩa (`BtnConnect_Click`, `BtnConfigure_Click`, `BtnSendDebugCommand_Click`, `BtnRefreshConfigPorts_Click`, `BtnRefreshDebugPorts_Click`, `BtnOpenDebugPort_Click`, `BtnCloseDebugPort_Click`, `BtnBrowseDebugFolder_Click`, `BtnReadDebugFile_Click`, `BtnSaveDebugFile_Click`, `BtnLoadParameters_Click`, `BtnGetSensorValue_Click`, `BtnStopLogger_Click`, `BtnSendData_Click`)
- `RichTextBox1_KeyDown` → `DebugCommandInput_KeyDown`
- Tách hàm `RebrandDeviceNameText` / `RebrandLoggerText` / `RebrandDebugText` (gộp logic thay thế `"AT+"`→`"VILOG_"`/`"Vilog_"` và `"DRAGINO"`→`"BAVITECH"` bị lặp lại trước đó).

## [1.2] - 2026-06-15
Bản gốc trước khi có các thay đổi trên.
